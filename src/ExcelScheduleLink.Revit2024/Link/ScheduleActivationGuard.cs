using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Freshly checks an existing ESL-linked schedule immediately before an
    /// ESL-owned ActiveView assignment. The guard never activates an affected or
    /// unverifiable schedule, and it never repairs without the corresponding
    /// user choice.
    /// </summary>
    internal static class ScheduleActivationGuard
    {
        public static bool CheckAndOfferRepair(UIDocument uidoc, ViewSchedule schedule)
        {
            if (uidoc == null || schedule == null || !schedule.IsValidObject)
            {
                return false;
            }

            Document doc = uidoc.Document;
            StyleIntegrityResult initial =
                ScheduleIntegritySessionCache.Rescan(doc, schedule);

            switch (ScheduleIntegrityFlowDecisions.ForActivation(initial))
            {
                case ScheduleActivationDecision.Activate:
                    return true;
                case ScheduleActivationDecision.RefuseUnverified:
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.ActivationUnverified(schedule.Name));
                    return false;
                case ScheduleActivationDecision.OfferWorkbookRepair:
                    return OfferRepair(uidoc, schedule, initial);
                default:
                    return false;
            }
        }

        private static bool OfferRepair(
            UIDocument uidoc, ViewSchedule schedule, StyleIntegrityResult initial)
        {
            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = UserMessages.ActivationUnsafe(schedule.Name);
            dialog.MainContent = string.Empty;
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UserMessages.ActivationReloadFromWorkbook,
                UserMessages.ActivationReloadFromWorkbookHint);
            dialog.DefaultButton = TaskDialogResult.Cancel;

            if (dialog.Show() != TaskDialogResult.CommandLink1)
            {
                return false;
            }

            TargetedReloadFlowResult flow = TargetedReloadFlow.Run(
                uidoc.Document, schedule, ReloadScope.SingleLink, null, initial,
                false);
            ReloadOutcome reload = flow.Reload;
            if (flow.FatalRegeneration)
            {
                if (reload.IsFatalRegenerationFailure)
                {
                    TaskDialog.Show(UserMessages.DialogTitle, reload.Detail);
                }
                return false;
            }
            if (reload.IsSuccess)
            {
                if (!flow.FinalHealthy)
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.FallbackPostRepairFailed);
                    return false;
                }
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.ReloadOutcomeMessage(reload));
                return true;
            }

            if (flow.Fallback != null && flow.Fallback.Success)
            {
                if (!flow.FinalHealthy)
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.FallbackPostRepairFailed);
                }
                return flow.FinalHealthy;
            }

            if (!flow.FallbackOffered
                && reload.Kind != ReloadResultKind.Cancelled
                && !string.IsNullOrEmpty(reload.Detail))
            {
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.ReloadOutcomeMessage(reload));
            }
            return false;
        }
    }
}
