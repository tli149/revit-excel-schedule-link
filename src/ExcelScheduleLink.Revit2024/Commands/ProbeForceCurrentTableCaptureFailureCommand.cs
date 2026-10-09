using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Arms the one-shot current-table capture failure.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeForceCurrentTableCaptureFailureCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Current-Table Capture Failure";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DebugCurrentTableCaptureFailureInjector.ArmNextCaptureFailure();
            TaskDialog.Show(
                Title,
                "The next no-workbook current-table capture will report one unreadable cell.\n\n"
                + "The fallback will stop before its repair mutation transaction.\n"
                + "This probe-manifest-only hook changes no model state.");
            return Result.Succeeded;
        }
    }
}
