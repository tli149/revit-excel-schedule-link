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
        public bool FatalRegenerationFailure;
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
                return Failed(UserMessages.ManagerLinkedScheduleUnavailable, false, false);
            }
            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return Failed(UserMessages.LinkStorageNotAvailable, false, false);
            }

            ModelMutationResult<bool> mutation = ModelMutation.Run<bool>(
                doc,
                "Edit Excel Link Settings",
                delegate (ModelMutationContext context)
                {
                    ModelMutationLinkLoadResult loaded =
                        context.LoadV2Link(schedule, allowLegacyMigration);
                    if (!loaded.IsLoaded)
                    {
                        if (loaded.Status == ModelMutationLinkLoadStatus.LegacyMigrationRefused
                            || loaded.Status == ModelMutationLinkLoadStatus.LegacyMigrationFailed)
                        {
                            context.Abort(UserMessages.LegacyLinkUpgradeFailed);
                            return false;
                        }
                        if (loaded.Status == ModelMutationLinkLoadStatus.StorageUnavailable)
                        {
                            context.Abort(UserMessages.LinkStorageNotAvailable);
                            return false;
                        }
                        throw new InvalidOperationException(
                            UserMessages.SchemaV2UnavailableAfterMigration);
                    }

                    ExcelLinkInfo info = loaded.Info;
                    info.ColumnWidthMode = columnWidthMode;
                    info.RowHeightMode = rowHeightMode;
                    info.WidthScale = widthScale;
                    ExcelLinkSchema.SaveV2(schedule, info);
                    return true;
                });

            if (mutation.Status == ModelMutationStatus.Committed)
            {
                return new SettingsSaveOutcome
                {
                    Success = true,
                    TransactionStarted = mutation.TransactionStarted
                };
            }
            if (mutation.Status == ModelMutationStatus.FatalRegeneration)
            {
                return Failed(
                    UserMessages.RegenerationFailure,
                    mutation.RollbackVerified,
                    mutation.TransactionStarted,
                    true);
            }
            if (mutation.Status == ModelMutationStatus.NotStarted)
            {
                return Failed(
                    UserMessages.SettingsTransactionNotStarted,
                    mutation.RollbackVerified,
                    mutation.TransactionStarted);
            }
            if (mutation.Status == ModelMutationStatus.StartThrew)
            {
                Exception exception = mutation.Exception ?? new InvalidOperationException();
                return Failed(
                    exception.ToString(),
                    mutation.RollbackVerified,
                    mutation.TransactionStarted);
            }
            if (mutation.Status == ModelMutationStatus.Aborted)
            {
                return Failed(
                    mutation.AbortReason,
                    mutation.RollbackVerified,
                    mutation.TransactionStarted);
            }
            if (mutation.Status == ModelMutationStatus.CommitNotCommitted)
            {
                return Failed(
                    UserMessages.SettingsTransactionReturnedStatus(
                        mutation.CommitStatus.Value.ToString()),
                    mutation.RollbackVerified,
                    mutation.TransactionStarted);
            }

            Exception failure = mutation.Exception ?? new InvalidOperationException();
            return Failed(
                failure.ToString(),
                mutation.RollbackVerified,
                mutation.TransactionStarted);
        }

        private static SettingsSaveOutcome Failed(
            string detail, bool rollbackVerified, bool transactionStarted,
            bool fatalRegenerationFailure = false)
        {
            return new SettingsSaveOutcome
            {
                Success = false,
                TransactionStarted = transactionStarted,
                RollbackVerified = rollbackVerified,
                FatalRegenerationFailure = fatalRegenerationFailure,
                Detail = detail ?? string.Empty
            };
        }
    }
}
