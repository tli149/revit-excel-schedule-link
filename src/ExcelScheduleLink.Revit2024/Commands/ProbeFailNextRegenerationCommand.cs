using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Arms the one-shot regeneration-failure equivalent.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeFailNextRegenerationCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DebugRegenerationFailureInjector.ArmNextRegenerationFailure();
            TaskDialog.Show(
                "ESL Probe - Regeneration Failure",
                "The next in-transaction style scan will throw a private marker treated "
                + "identically to Revit's RegenerationFailedException (the Revit exception "
                + "cannot be constructed by the probe).\n\n"
                + "This probe-manifest-only hook changes no model state.");
            return Result.Succeeded;
        }
    }
}
