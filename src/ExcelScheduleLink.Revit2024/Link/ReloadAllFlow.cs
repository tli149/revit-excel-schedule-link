using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal sealed class ReloadAllRowResult
    {
        public ElementId ScheduleId;
        public ReloadOutcome Reload;
        public StyleIntegrityResult After;
    }

    internal sealed class ReloadAllFlowResult
    {
        public int Success;
        public int Unchanged;
        public int Missing;
        public int Failed;
        public int Repaired;
        public int ConfirmationRequired;
        public readonly List<string> TruncatedSchedules = new List<string>();
        public readonly List<string> Details = new List<string>();
        public readonly List<ReloadAllRowResult> Rows =
            new List<ReloadAllRowResult>();
        public bool FatalRegenerationFailure;
        public string FatalDetail = string.Empty;
    }

    /// <summary>
    /// Reloads each Link Manager row independently without offering the
    /// current-table fallback. Every nonfatal row receives a fresh cached scan.
    /// </summary>
    internal static class ReloadAllFlow
    {
        public static ReloadAllFlowResult Run(
            Document doc, IList<LinkRow> rows)
        {
            ReloadAllFlowResult result = new ReloadAllFlowResult();
            if (rows == null)
            {
                return result;
            }

            foreach (LinkRow row in new List<LinkRow>(rows))
            {
                ViewSchedule schedule = doc.GetElement(row.ScheduleId)
                    as ViewSchedule;
                if (schedule == null)
                {
                    result.Failed++;
                    result.Details.Add(
                        UserMessages.ManagerReloadAllUnavailable(row.ScheduleName));
                    result.Rows.Add(new ReloadAllRowResult
                    {
                        ScheduleId = row.ScheduleId
                    });
                    continue;
                }

                ReloadOutcome outcome = ReloadService.Reload(
                    doc, schedule, ReloadScope.AllLinks,
                    row.IntegrityResult);
                ReloadAllRowResult rowResult = new ReloadAllRowResult
                {
                    ScheduleId = row.ScheduleId,
                    Reload = outcome
                };
                result.Rows.Add(rowResult);
                if (outcome.IsFatalRegenerationFailure)
                {
                    result.FatalRegenerationFailure = true;
                    result.FatalDetail = outcome.Detail;
                    return result;
                }

                switch (outcome.Kind)
                {
                    case ReloadResultKind.Success:
                        if (outcome.Truncated)
                        {
                            result.TruncatedSchedules.Add(row.ScheduleName);
                        }
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        if (ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                                row.IntegrityResult, true, rowResult.After))
                        {
                            result.Repaired++;
                        }
                        if (rowResult.After == null
                            || rowResult.After.State != StyleIntegrityState.Healthy)
                        {
                            result.Failed++;
                            result.Details.Add(
                                UserMessages.ManagerReloadAllPostRepairNotHealthy(
                                    row.ScheduleName));
                            break;
                        }
                        string details = UserMessages.AppendRenderNotes(
                            UserMessages.ReloadColumnCountNote(outcome),
                            0, 0, false, false, 0,
                            outcome.RowHeightsRejected, outcome.ScheduleName,
                            rowsMayHideText: outcome.RowsMayHideText);
                        if (!string.IsNullOrEmpty(details))
                        {
                            result.Details.Add(details);
                        }
                        result.Success++;
                        if (outcome.Identity
                            == ExcelScheduleLink.Contracts.SourceIdentityState.Match)
                        {
                            result.Unchanged++;
                        }
                        break;

                    case ReloadResultKind.SourceMissing:
                        result.Missing++;
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        break;

                    case ReloadResultKind.NetworkAccessCancelled:
                        result.ConfirmationRequired++;
                        result.Details.Add(outcome.Detail);
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        break;

                    case ReloadResultKind.ConfirmationRequired:
                        result.ConfirmationRequired++;
                        result.Details.Add(outcome.Detail);
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        break;

                    case ReloadResultKind.Cancelled:
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        break;

                    default:
                        rowResult.After = ScanAndCache(doc, row.ScheduleId);
                        result.Failed++;
                        result.Details.Add(
                            UserMessages.ManagerReloadAllFailure(
                                row.ScheduleName, outcome.Detail));
                        break;
                }
            }
            return result;
        }

        private static StyleIntegrityResult ScanAndCache(
            Document doc, ElementId scheduleId)
        {
            ViewSchedule schedule = doc.GetElement(scheduleId) as ViewSchedule;
            if (schedule == null)
            {
                return null;
            }
            return ScheduleIntegritySessionCache.Rescan(doc, schedule);
        }
    }
}
