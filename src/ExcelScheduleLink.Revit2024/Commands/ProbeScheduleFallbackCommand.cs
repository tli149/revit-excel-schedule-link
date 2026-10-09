using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Diagnostic variant B: creates a test table in the header of a schedule
    /// whose body is emptied with one dummy field and two contradictory filters.
    /// Schedule name: ESL_Step0_B_DummyFilter. The dummy field is selected from
    /// GetSchedulableFields and recorded in the report. Run it together with
    /// variant A for comparison.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeScheduleFallbackCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ScheduleProbeRunner.Run(
                commandData,
                "Probe_Schedule_B_Fallback",
                HeaderTableProbe.ScheduleNameB,
                true);
        }
    }
}
