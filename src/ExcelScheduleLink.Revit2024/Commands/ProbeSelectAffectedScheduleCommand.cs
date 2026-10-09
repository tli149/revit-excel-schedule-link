using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Probe-manifest-only helper for selecting the first Excel-linked
    /// schedule whose fresh style scan is Affected, without opening it, so the
    /// operator can reach the layout dialog's "Open Schedule View" guard
    /// without clicking a crash-prone schedule in the Project Browser (a click
    /// there can register as a double-click, which Revit opens and the add-in
    /// cannot intercept). It changes only the selection; every scan is a
    /// rolled-back read.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeSelectAffectedScheduleCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Select Affected Schedule";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            foreach (ViewSchedule schedule in LinkedScheduleFinder.FindAll(doc))
            {
                StyleIntegrityResult result =
                    ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(doc, schedule);
                if (result == null || result.State != StyleIntegrityState.Affected)
                {
                    continue;
                }

                uidoc.Selection.SetElementIds(new List<ElementId> { schedule.Id });
                TaskDialog.Show(Title,
                    "Selected \"" + schedule.Name + "\" (element id " + schedule.Id.Value
                    + ", " + result.StylelessCellCount + " styleless cell(s)).\n\n"
                    + "The schedule view was not opened and the model was not changed.");
                return Result.Succeeded;
            }

            TaskDialog.Show(Title,
                "No Excel-linked schedule with unsafe cell styles was found. Nothing was selected.");
            return Result.Cancelled;
        }
    }
}
