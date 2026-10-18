using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Debug probe (not part of production): records the current body/header
    /// column widths of the N-dummy-body probe schedule. Run after manually
    /// dragging the middle sheet column grip, and again after save/reopen to
    /// check persistence. See <see cref="NDummyBodyProbe"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeNDummyMeasureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return NDummyBodyProbe.RunMeasure(commandData);
        }
    }
}
