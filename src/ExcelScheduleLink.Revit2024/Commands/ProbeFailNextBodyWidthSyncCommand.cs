using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Arms the one-shot body-width synchronization failure.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeFailNextBodyWidthSyncCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DebugBodyWidthSyncFailureInjector.ArmNextBodyWidthSyncFailure();
            TaskDialog.Show(
                "ESL Probe - Body Width Sync",
                "The next layout body-width synchronization will fail inside its transaction.\n\n"
                + "This probe-manifest-only hook changes no model state.");
            return Result.Succeeded;
        }
    }
}
