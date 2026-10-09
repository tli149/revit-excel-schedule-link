using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Probe-only explicit no-border mutation of the named header cell. The command is
    /// intentionally not available from the production ribbon or manifest.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeApplyExplicitNoBorderStyleCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Apply Explicit No-Border Style";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp == null ? null : uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "No active document is available. Nothing was changed.");
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            ViewSchedule schedule = uidoc.ActiveView as ViewSchedule;
            if (schedule == null || !LinkedScheduleFinder.IsLinked(schedule))
            {
                TaskDialog.Show(
                    Title,
                    "The active view is not an Excel-linked schedule. Nothing was changed.");
                return Result.Cancelled;
            }

            using (Transaction transaction = new Transaction(
                doc, "ESL Probe - Apply Explicit No-Border Style"))
            {
                bool started = false;
                string transactionStatus = "Not started";
                try
                {
                    TransactionStatus startStatus = transaction.Start();
                    transactionStatus = startStatus.ToString();
                    if (startStatus != TransactionStatus.Started)
                    {
                        TaskDialog.Show(
                            Title,
                            "The no-border style was not applied.\n\n"
                            + "Transaction status: " + transactionStatus
                            + ". Nothing was changed in the Revit model.");
                        return Result.Failed;
                    }
                    started = true;

                    TableSectionData section = schedule.GetTableData()
                        .GetSectionData(SectionType.Header);
                    int row = section.FirstRowNumber + 1;
                    int column = section.FirstColumnNumber + 1;
                    if (section.NumberOfRows < 2 || section.NumberOfColumns < 2)
                    {
                        throw new InvalidOperationException(
                            "The header does not contain the requested row 2, column 2 cell.");
                    }

                    TableCellStyle current = section.GetTableCellStyle(row, column);
                    if (current == null)
                    {
                        throw new InvalidOperationException(
                            "Revit returned no TableCellStyle for the requested header cell.");
                    }

                    TableCellStyle style = new TableCellStyle(current);
                    style.BorderTopLineStyle = ElementId.InvalidElementId;
                    style.BorderBottomLineStyle = ElementId.InvalidElementId;
                    style.BorderLeftLineStyle = ElementId.InvalidElementId;
                    style.BorderRightLineStyle = ElementId.InvalidElementId;

                    TableCellStyleOverrideOptions options =
                        style.GetCellStyleOverrideOptions();
                    if (options == null)
                    {
                        options = new TableCellStyleOverrideOptions();
                    }
                    options.BorderTopLineStyle = true;
                    options.BorderBottomLineStyle = true;
                    options.BorderLeftLineStyle = true;
                    options.BorderRightLineStyle = true;
                    style.SetCellStyleOverrideOptions(options);

                    section.SetCellStyle(row, column, style);

                    TransactionStatus commitStatus = transaction.Commit();
                    transactionStatus = commitStatus.ToString();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        bool rollbackVerified =
                            TransactionRollback.RollBackIfStarted(transaction)
                                == TransactionStatus.RolledBack;
                        TaskDialog.Show(
                            Title,
                            "The no-border style was not committed.\n\n"
                            + "Transaction status: " + transactionStatus + ".\n"
                            + (rollbackVerified
                                ? "The transaction was rolled back; nothing was changed in the Revit model."
                                : "Rollback completion could not be verified."));
                        return Result.Failed;
                    }

                    TaskDialog.Show(
                        Title,
                        "Applied the explicit no-border style to the active linked schedule.\n\n"
                        + "Header cell: row 2, column 2 (Revit row " + row
                        + ", column " + column + ").\n"
                        + "BorderTopLineStyle = InvalidElementId\n"
                        + "BorderBottomLineStyle = InvalidElementId\n"
                        + "BorderLeftLineStyle = InvalidElementId\n"
                        + "BorderRightLineStyle = InvalidElementId\n"
                        + "Override.BorderTopLineStyle = true\n"
                        + "Override.BorderBottomLineStyle = true\n"
                        + "Override.BorderLeftLineStyle = true\n"
                        + "Override.BorderRightLineStyle = true\n"
                        + "Transaction status: " + transactionStatus + ".");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    bool rollbackVerified = started
                        && TransactionRollback.RollBackIfStarted(transaction)
                            == TransactionStatus.RolledBack;
                    message = ex.Message;
                    TaskDialog.Show(
                        Title,
                        "The explicit no-border style could not be applied.\n\n"
                        + "Transaction status: " + transactionStatus + ".\n"
                        + (rollbackVerified
                            ? "The transaction was rolled back; nothing was changed in the Revit model.\n\n"
                            : "Rollback completion could not be verified.\n\n")
                        + ex.GetType().Name + ": " + ex.Message);
                    return Result.Failed;
                }
            }
        }
    }
}
