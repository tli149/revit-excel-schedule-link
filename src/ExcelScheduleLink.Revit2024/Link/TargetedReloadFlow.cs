using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal sealed class TargetedReloadFlowResult
    {
        public ReloadOutcome Reload;
        public bool FallbackOffered;
        public RepairAttemptOutcome Fallback;
        public bool FatalRegeneration;
        public StyleIntegrityResult After;
        public bool MutationSucceeded;
        public bool FinalHealthy;
    }

    /// <summary>
    /// Runs one reload attempt, its explicitly consented current-table fallback,
    /// and the final integrity check used to qualify a successful mutation.
    /// </summary>
    internal static class TargetedReloadFlow
    {
        public static TargetedReloadFlowResult Run(
            Document doc,
            ViewSchedule schedule,
            ReloadScope scope,
            PendingLinkSettings pendingSettings,
            StyleIntegrityResult priorHint,
            bool rescanAfterUnsuccessfulFallback = true)
        {
            TargetedReloadFlowResult result = new TargetedReloadFlowResult();
            result.Reload = ReloadService.Reload(
                doc, schedule, scope, pendingSettings, priorHint);
            if (result.Reload.IsFatalRegenerationFailure)
            {
                result.FatalRegeneration = true;
                return result;
            }

            result.FallbackOffered =
                ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                    priorHint == null
                        ? (StyleIntegrityState?)null : priorHint.State,
                    !result.Reload.IsWorkbookUnavailable);
            if (result.FallbackOffered)
            {
                result.Fallback =
                    ScheduleRepairCoordinator.TryFallback(doc, schedule);
                if (result.Fallback != null
                    && result.Fallback.FatalRegenerationFailure)
                {
                    result.FatalRegeneration = true;
                    return result;
                }
                if (!rescanAfterUnsuccessfulFallback
                    && (result.Fallback == null || !result.Fallback.Success))
                {
                    return result;
                }
            }

            result.After = ScanAndCache(doc, schedule);
            bool fallbackSucceeded = result.Fallback != null
                && result.Fallback.Success;
            result.MutationSucceeded = result.Reload.IsSuccess
                || fallbackSucceeded;
            result.FinalHealthy =
                ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                    result.Reload.IsSuccess, fallbackSucceeded, result.After);
            return result;
        }

        private static StyleIntegrityResult ScanAndCache(
            Document doc, ViewSchedule schedule)
        {
            return ScheduleIntegritySessionCache.Rescan(doc, schedule);
        }
    }
}
