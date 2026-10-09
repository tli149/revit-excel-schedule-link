using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Debug probe (not part of production): builds the N-dummy-body verification
    /// schedule (3 body fields + 3 header columns with distinct widths), runs the
    /// API coupling tests and the 50-field feasibility check, and places the
    /// schedule on a new sheet. See <see cref="NDummyBodyProbe"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeNDummySetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return NDummyBodyProbe.RunSetup(commandData, ref message);
        }
    }
}
