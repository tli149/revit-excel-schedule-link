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
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(doc, schedule);
            ScheduleIntegritySessionCache.SetResult(doc, schedule.Id, initial);

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

            ReloadOutcome reload = ReloadService.Reload(
                uidoc.Document, schedule, ReloadScope.SingleLink, null, initial);
            if (reload.IsFatalRegenerationFailure)
            {
                TaskDialog.Show(UserMessages.DialogTitle, reload.Detail);
                return false;
            }
            if (reload.IsSuccess)
            {
                if (!ActivateOnlyAfterHealthyRescan(uidoc.Document, schedule))
                {
                    return false;
                }
                string summary = UserMessages.AppendRenderNotes(
                    UserMessages.ReloadSucceeded(reload),
                    reload.SourceRows, reload.SourceColumns,
                    reload.Truncated, reload.ViewWidthProblem,
                    reload.ColumnWidthsRejected);
                TaskDialog.Show(UserMessages.DialogTitle, summary);
                return true;
            }

            if (ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                initial == null ? (StyleIntegrityState?)null : initial.State,
                reload.Kind != ReloadResultKind.SourceMissing
                    && reload.Kind != ReloadResultKind.NetworkAccessCancelled
                    && reload.Kind != ReloadResultKind.ReadFailed))
            {
                RepairAttemptOutcome fallback =
                    ScheduleRepairCoordinator.TryFallback(uidoc.Document, schedule);
                if (fallback != null && fallback.FatalRegenerationFailure)
                {
                    return false;
                }
                if (fallback != null && fallback.Success)
                {
                    return ActivateOnlyAfterHealthyRescan(uidoc.Document, schedule);
                }
                return false;
            }

            // Keep the session hint truthful after cancellation or any failed
            // ordinary reload, even though activation remains blocked.
            StyleIntegrityResult after =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(
                    uidoc.Document, schedule);
            ScheduleIntegritySessionCache.SetResult(
                uidoc.Document, schedule.Id, after);

            if (reload.Kind != ReloadResultKind.Cancelled
                && !string.IsNullOrEmpty(reload.Detail))
            {
                TaskDialog.Show(UserMessages.DialogTitle, reload.Detail);
            }
            return false;
        }

        private static bool ActivateOnlyAfterHealthyRescan(
            Document doc, ViewSchedule schedule)
        {
            StyleIntegrityResult after =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(doc, schedule);
            ScheduleIntegritySessionCache.SetResult(doc, schedule.Id, after);
            if (after != null && after.State == StyleIntegrityState.Healthy)
            {
                return true;
            }

            TaskDialog.Show(UserMessages.DialogTitle,
                UserMessages.FallbackPostRepairFailed);
            return false;
        }
    }
}
