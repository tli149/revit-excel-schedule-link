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
    /// The targeted reload flow lives in <see cref="TargetedReloadFlow"/> so
    /// it is shared with the Link Manager and repair coordinator.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReloadExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return FatalRegenerationBoundary.RunCommand(ref message,
                delegate { return Run(commandData); });
        }

        private static Result Run(ExternalCommandData commandData)
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
            TargetedReloadFlowResult flow = TargetedReloadFlow.Run(
                uidoc.Document, schedule, ReloadScope.SingleLink,
                preReloadIntegrityHint);
            if (flow.FatalRegeneration)
            {
                if (flow.Reload.IsFatalRegenerationFailure)
                {
                    TaskDialog.Show(UserMessages.DialogTitle, flow.Reload.Detail);
                }
                // A fatal in the fallback was already shown by
                // ScheduleRepairCoordinator.TryFallback.
                return Result.Failed;
            }
            ReloadOutcome outcome = flow.Reload;
            bool fallbackSucceeded = flow.Fallback != null
                && flow.Fallback.Success;
            if (flow.MutationSucceeded && !flow.FinalHealthy)
            {
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.FallbackPostRepairFailed);
                return Result.Failed;
            }

            if (fallbackSucceeded)
            {
                return Result.Succeeded;
            }

            switch (outcome.Kind)
            {
                case ReloadResultKind.Success:
                    TargetedReloadFlow.ShowReloadOutcome(
                        flow, UserMessages.DialogTitle);
                    return Result.Succeeded;

                case ReloadResultKind.SourceMissing:
                case ReloadResultKind.NetworkAccessCancelled:
                case ReloadResultKind.ReadFailed:
                    TargetedReloadFlow.ShowReloadOutcome(
                        flow, UserMessages.DialogTitle);
                    return Result.Cancelled; // nothing changed, or TryFallback reported

                case ReloadResultKind.ConfirmationRequired:
                case ReloadResultKind.Cancelled:
                    TargetedReloadFlow.ShowReloadOutcome(
                        flow, UserMessages.DialogTitle);
                    return Result.Cancelled; // no bulk or canceled mutation

                case ReloadResultKind.RenderFailed:
                    TargetedReloadFlow.ShowReloadOutcome(
                        flow, UserMessages.DialogTitle);
                    return Result.Failed;

                default:
                    TaskDialog.Show(UserMessages.DialogTitle, UserMessages.ReloadNoTarget);
                    return Result.Cancelled;
            }
        }
    }
}
