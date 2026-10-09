using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Step 3 production command: reloads the Excel content of a linked
    /// schedule (the active view, or a single selected linked schedule).
    /// The actual reload flow lives in <see cref="ReloadService"/> so it is
    /// shared with the Link Manager.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReloadExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            ViewSchedule schedule = LinkedScheduleFinder.PickTarget(uidoc);
            if (schedule == null)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.ReloadNoTarget);
                return Result.Cancelled;
            }

            StyleIntegrityResult preReloadIntegrityHint =
                ScheduleIntegritySessionCache.GetResult(uidoc.Document, schedule.Id);
            ReloadOutcome outcome = ReloadService.Reload(
                uidoc.Document, schedule, ReloadScope.SingleLink, null,
                preReloadIntegrityHint);
            if (outcome.IsFatalRegenerationFailure)
            {
                TaskDialog.Show(UserMessages.DialogTitle, outcome.Detail);
                message = outcome.Detail;
                return Result.Failed;
            }
            bool fallbackSucceeded = false;
            bool offersFallback =
                ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                    preReloadIntegrityHint == null
                        ? (StyleIntegrityState?)null : preReloadIntegrityHint.State,
                    outcome.Kind != ReloadResultKind.SourceMissing
                        && outcome.Kind != ReloadResultKind.NetworkAccessCancelled
                        && outcome.Kind != ReloadResultKind.ReadFailed);
            if (offersFallback)
            {
                RepairAttemptOutcome fallback =
                    ScheduleRepairCoordinator.TryFallback(uidoc.Document, schedule);
                if (fallback != null && fallback.FatalRegenerationFailure)
                {
                    message = fallback.Detail;
                    return Result.Failed;
                }
                fallbackSucceeded = fallback != null && fallback.Success;
            }

            StyleIntegrityResult after =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(
                    uidoc.Document, schedule);
            ScheduleIntegritySessionCache.SetResult(uidoc.Document, schedule.Id, after);

            bool mutationSucceeded = outcome.IsSuccess || fallbackSucceeded;
            bool finalRescanHealthy =
                ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                    outcome.IsSuccess, fallbackSucceeded, after);
            if (mutationSucceeded && !finalRescanHealthy)
            {
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.FallbackPostRepairFailed);
                message = UserMessages.FallbackPostRepairFailed;
                return Result.Failed;
            }

            if (fallbackSucceeded)
            {
                return Result.Succeeded;
            }

            switch (outcome.Kind)
            {
                case ReloadResultKind.Success:
                    string summary = UserMessages.ReloadSucceeded(outcome);
                    summary = UserMessages.AppendRenderNotes(
                        summary, outcome.SourceRows, outcome.SourceColumns,
                        outcome.Truncated, outcome.ViewWidthProblem,
                        outcome.ColumnWidthsRejected);
                    TaskDialog.Show(UserMessages.DialogTitle, summary);
                    return Result.Succeeded;

                case ReloadResultKind.SourceMissing:
                case ReloadResultKind.NetworkAccessCancelled:
                case ReloadResultKind.ReadFailed:
                    TaskDialog.Show(UserMessages.DialogTitle, outcome.Detail);
                    return Result.Cancelled; // nothing changed

                case ReloadResultKind.ConfirmationRequired:
                case ReloadResultKind.Cancelled:
                    if (outcome.Kind == ReloadResultKind.ConfirmationRequired)
                    {
                        TaskDialog.Show(UserMessages.DialogTitle, outcome.Detail);
                    }
                    return Result.Cancelled; // no bulk or canceled mutation

                case ReloadResultKind.RenderFailed:
                    TaskDialog.Show(UserMessages.DialogTitle, outcome.Detail);
                    message = outcome.Detail;
                    return Result.Failed;

                default:
                    TaskDialog.Show(UserMessages.DialogTitle, UserMessages.ReloadNoTarget);
                    return Result.Cancelled;
            }
        }
    }
}
