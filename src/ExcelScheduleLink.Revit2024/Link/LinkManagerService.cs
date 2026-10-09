using System.Collections.Generic;
using System;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>One row of the Link Manager list: a linked schedule plus its
    /// resolved state. Pure data, computed outside any transaction.</summary>
    internal sealed class LinkRow
    {
        public ElementId ScheduleId;
        public string ScheduleName = string.Empty;
        public string SheetName = string.Empty;
        public string SourceDisplayPath = string.Empty;
        public bool SourceFound;
        public bool IsV2;
        public string StatusDisplay = string.Empty;
        public string RangeText = string.Empty;
        public double WidthScale = 1.0;
        public ColumnWidthMode ColumnWidthMode = ColumnWidthMode.ApplyExcelWidths;
        public RowHeightMode RowHeightMode = RowHeightMode.ApplyExcelHeights;
        public StyleIntegrityResult IntegrityResult;
    }

    internal sealed class SettingsSaveOutcome
    {
        public bool Success;
        public bool TransactionStarted;
        public bool RollbackVerified;
        public string Detail = string.Empty;
    }

    /// <summary>
    /// UI-free operations for the Link Manager: enumerating links and persisting
    /// per-link settings. Settings are written to extensible storage only; they
    /// take effect on the NEXT reload (no re-render here), keeping this fast and
    /// the model untouched apart from the stored entity.
    /// </summary>
    internal static class LinkManagerService
    {
        public static StyleIntegrityResult GetIntegrity(
            IEnumerable<LinkRow> rows, ElementId scheduleId)
        {
            if (rows == null || scheduleId == null)
            {
                return null;
            }
            foreach (LinkRow row in rows)
            {
                if (row != null && row.ScheduleId == scheduleId)
                {
                    return row.IntegrityResult;
                }
            }
            return null;
        }

        public static List<LinkRow> GetLinks(
            Document doc, IDictionary<long, StyleIntegrityResult> integrityResults)
        {
            List<LinkRow> rows = new List<LinkRow>();
            foreach (ViewSchedule schedule in LinkedScheduleFinder.FindAll(doc))
            {
                ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
                if (storage == null || storage.Info == null)
                {
                    continue;
                }
                ExcelLinkInfo info = storage.Info;
                PathResolutionResult resolution = PathResolver.Inspect(doc, info);
                LinkRow row = new LinkRow
                {
                    ScheduleId = schedule.Id,
                    ScheduleName = schedule.Name,
                    SheetName = info.SheetName,
                    SourceDisplayPath = !string.IsNullOrEmpty(info.AbsolutePath)
                        ? info.AbsolutePath
                        : info.RelativePath,
                    SourceFound = resolution != null && resolution.IsResolved,
                    RangeText = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : "(used range)",
                    WidthScale = info.WidthScale,
                    ColumnWidthMode = info.ColumnWidthMode,
                    RowHeightMode = info.RowHeightMode
                };
                if (integrityResults != null)
                {
                    StyleIntegrityResult integrity;
                    if (integrityResults.TryGetValue(schedule.Id.Value, out integrity))
                    {
                        row.IntegrityResult = integrity;
                    }
                }
                if (row.IntegrityResult == null)
                {
                    row.IntegrityResult = new StyleIntegrityResult
                    {
                        State = StyleIntegrityState.Indeterminate,
                        ReasonCode = StyleIntegrityReasonCode.IncompleteScan,
                        FirstReadError = "No session scan result was available."
                    };
                }
                row.IsV2 = storage.IsV2;
                if (resolution != null
                    && resolution.Status == PathResolutionStatus.NetworkNotApproved)
                {
                    row.StatusDisplay = UserMessages.ManagerStatusNetworkNotApproved;
                }
                else if (resolution != null
                    && resolution.Status == PathResolutionStatus.Invalid)
                {
                    row.StatusDisplay = UserMessages.ManagerStatusInvalidPath;
                }
                else if (!row.SourceFound)
                {
                    row.StatusDisplay = UserMessages.ManagerStatusMissing
                        + "; " + (storage.IsV2
                            ? UserMessages.ManagerStatusProtected
                            : UserMessages.ManagerStatusLegacy);
                }
                else if (!storage.IsV2)
                {
                    row.StatusDisplay = UserMessages.ManagerStatusLegacy;
                }
                else
                {
                    row.StatusDisplay = string.IsNullOrEmpty(info.LastAcceptedSourceSha256)
                        ? UserMessages.ManagerStatusProtectedBaselineUnknown
                        : UserMessages.ManagerStatusProtected;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Persists the layout settings for one link. Loads the current entity,
        /// updates only the three settings fields, and saves it back inside a
        /// transaction. Other fields and the schedule content are untouched.
        /// </summary>
        public static SettingsSaveOutcome SaveSettings(
            Document doc, ElementId scheduleId,
            ColumnWidthMode columnWidthMode, RowHeightMode rowHeightMode, double widthScale,
            bool allowLegacyMigration)
        {
            ViewSchedule schedule = doc.GetElement(scheduleId) as ViewSchedule;
            if (schedule == null)
            {
                return Failed("The linked schedule was not available.", false, false);
            }
            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return Failed("The link storage was not available.", false, false);
            }

            using (Transaction transaction = new Transaction(doc, "Edit Excel Link Settings"))
            {
                bool transactionStarted = false;
                try
                {
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        return Failed("The settings transaction did not start.", false, false);
                    }
                    transactionStarted = true;
                    ExcelLinkInfo info = storage.Info;
                    if (!storage.IsV2)
                    {
                        if (!allowLegacyMigration
                            || !ExcelLinkMigration.MigrateInOpenTransaction(schedule))
                        {
                            bool rollbackVerified = TransactionRollback.RollBackIfStarted(
                                transaction) == TransactionStatus.RolledBack;
                            return Failed("The legacy link could not be upgraded.",
                                rollbackVerified, true);
                        }
                        info = ExcelLinkSchema.LoadV2(schedule);
                        if (info == null)
                        {
                            throw new InvalidOperationException(
                                "Schema v2 was not available after legacy-link migration.");
                        }
                    }
                    info.ColumnWidthMode = columnWidthMode;
                    info.RowHeightMode = rowHeightMode;
                    info.WidthScale = widthScale;
                    ExcelLinkSchema.SaveV2(schedule, info);
                    TransactionStatus committed = transaction.Commit();
                    if (committed != TransactionStatus.Committed)
                    {
                        bool rollbackVerified = TransactionRollback.RollBackIfStarted(
                            transaction) == TransactionStatus.RolledBack;
                        return Failed("The settings transaction returned status "
                            + committed + ".", rollbackVerified, true);
                    }
                    return new SettingsSaveOutcome
                    {
                        Success = true,
                        TransactionStarted = transactionStarted
                    };
                }
                catch (System.Exception ex)
                {
                    bool rollbackVerified = TransactionRollback.RollBackIfStarted(
                        transaction) == TransactionStatus.RolledBack;
                    return Failed(ex.ToString(), rollbackVerified, transactionStarted);
                }
            }
        }

        private static SettingsSaveOutcome Failed(
            string detail, bool rollbackVerified, bool transactionStarted)
        {
            return new SettingsSaveOutcome
            {
                Success = false,
                TransactionStarted = transactionStarted,
                RollbackVerified = rollbackVerified,
                Detail = detail ?? string.Empty
            };
        }
    }
}
