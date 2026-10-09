using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Debug-only read-only evidence command for inspecting persisted schedule
    /// table state without opening a schedule view. In particular, it must be
    /// safe to run while the suspected schedule is still closed.
    ///
    /// Manual rather than ReadOnly: ViewSchedule.GetTableData refuses to run on
    /// a document that is not modifiable, so the report reads each table inside
    /// a transaction that it always rolls back. Nothing is committed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeTableStateCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Table State";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc;
            try
            {
                uidoc = uiapp.ActiveUIDocument;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(Title, "The active document could not be read.\n\n" + ex);
                message = ex.Message;
                return Result.Failed;
            }
            if (uidoc == null)
            {
                TaskDialog.Show(Title, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            TableStateReportResult report;
            try
            {
                // TableStateReport only reads the document and writes an evidence
                // file. It does not assign or request a view; its only transactions
                // are rolled back.
                report = TableStateReport.Run(uiapp, uidoc.Document);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(Title,
                    "The table state report could not be written.\n\n"
                    + "Record this failure with the diagnostic run; a missing or partial\n"
                    + "file must not be interpreted as a clean table state.\n\n"
                    + ex);
                message = ex.Message;
                return Result.Failed;
            }

            TaskDialog dialog = new TaskDialog(Title);
            dialog.MainInstruction = "Table state recorded.";
            dialog.MainContent =
                "Evidence file:\n" + report.Path + "\n\n"
                + "Schedules dumped: " + report.SchedulesDumped;
            dialog.ExpandedContent =
                "No view was opened and the command makes no intentional model changes.\n"
                + "Each table-read block records its observed final transaction status;\n"
                + "the table-read transaction is never committed.\n"
                + "The report uses the same evidence directory as the worksharing probe.\n"
                + "Each schedule block is flushed as it is read so a native failure leaves\n"
                + "the last schedule, section, and cell progress marker on disk.";
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
