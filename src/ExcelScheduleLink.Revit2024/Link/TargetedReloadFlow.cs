using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
        /// <summary>
        /// Shows the reload outcome a caller still owes the user. Once the flow
        /// dispatched to the current-table fallback, TryFallback has put the
        /// workbook reason in its offer or refusal details and reported any
        /// failure, or the user cancelled, so the original workbook outcome is
        /// not shown again. Final-qualification warnings and fatal reporting
        /// keep their existing handling.
        /// </summary>
        public static void ShowReloadOutcome(
            TargetedReloadFlowResult flow, string title)
        {
            if (flow == null || flow.FallbackOffered)
            {
                return;
            }
            string text = UserMessages.ReloadOutcomeMessage(flow.Reload);
            if (!string.IsNullOrEmpty(text))
            {
                TaskDialog.Show(title, text);
            }
        }

        public static TargetedReloadFlowResult Run(
            Document doc,
            ViewSchedule schedule,
            ReloadScope scope,
            StyleIntegrityResult priorHint,
            bool rescanAfterUnsuccessfulFallback = true)
        {
            TargetedReloadFlowResult result = new TargetedReloadFlowResult();
            result.Reload = ReloadService.Reload(
                doc, schedule, scope, priorHint);
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
                    ScheduleRepairCoordinator.TryFallback(
                        doc, schedule, result.Reload.Detail);
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
