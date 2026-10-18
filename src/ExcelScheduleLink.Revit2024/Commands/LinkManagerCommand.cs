using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Step 4 production command: opens the Link Manager — a modal dialog that
    /// lists all linked schedules with their source path, status and integrity,
    /// reloads the selected link or all links, and checks and repairs unsafe
    /// tables. Reload policies are edited in Column Widths, not here. The dialog
    /// runs in this command's API context, so the transactions it opens (reload,
    /// repair) are valid.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LinkManagerCommand : IExternalCommand
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

            // Link Manager owns the explicit bounded Manual-scope scan. The
            // modal dialog only consumes these current results; it does not read
            // table data opportunistically while constructing rows.
            ScheduleIntegritySessionCache.ScanManual(uidoc.Document);

            using (LinkManagerDialog dialog = new LinkManagerDialog(uidoc.Document))
            {
                dialog.ShowDialog();
            }
            return Result.Succeeded;
        }
    }
}
