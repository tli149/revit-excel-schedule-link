using System.Collections.Generic;
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
        public StyleIntegrityResult IntegrityResult;
    }

    /// <summary>
    /// UI-free operations for the Link Manager: enumerating linked schedules and
    /// their resolved state.
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
                    RangeText = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : "(used range)"
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
    }
}
