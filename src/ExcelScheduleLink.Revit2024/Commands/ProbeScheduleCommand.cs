using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Diagnostic variant A: creates a test table in the header of a
    /// multi-category schedule without fields. Schedule name:
    /// ESL_Step0_A_NoField. Run it together with variant B for comparison.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ScheduleProbeRunner.Run(
                commandData,
                "Probe_Schedule_A",
                HeaderTableProbe.ScheduleNameA,
                false);
        }
    }
}
