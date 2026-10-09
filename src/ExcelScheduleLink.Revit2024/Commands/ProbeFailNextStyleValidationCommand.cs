using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Arms the one-shot post-render style-validation failure injector.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeFailNextStyleValidationCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DebugStyleIntegrityFailureInjector.ArmNextValidationFailure();
            TaskDialog.Show(
                "ESL Probe - Render Guard",
                "The next in-transaction style validation will report Affected.\n\n"
                + "This probe-manifest-only hook changes no model state.");
            return Result.Succeeded;
        }
    }
}
