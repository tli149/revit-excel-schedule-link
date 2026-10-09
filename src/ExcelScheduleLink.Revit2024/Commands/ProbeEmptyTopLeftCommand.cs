using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Probe-manifest-only regression check for a range whose
    /// top-left cell is empty fails the post-render guard. On the active linked
    /// schedule, inside one transaction that is always rolled back, this tries
    /// three ways of writing an empty header (0,0) cell and records the cell
    /// type and the in-transaction style-integrity verdict after each, so the
    /// renderer fix can be chosen from evidence. Nothing is committed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeEmptyTopLeftCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Empty Top-Left";

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
            ViewSchedule schedule = uidoc.ActiveView as ViewSchedule;
            if (schedule == null || !LinkedScheduleFinder.IsLinked(schedule))
            {
                TaskDialog.Show(Title,
                    "Open an Excel-linked schedule view first. Nothing was changed.");
                return Result.Cancelled;
            }

            List<string> lines = new List<string>();
            lines.Add("schedule=" + schedule.Name + " id=" + schedule.Id.Value);
            Transaction transaction = new Transaction(doc, "ESL Probe - empty top-left (rolled back)");
            TransactionStatus? finalStatus = null;
            try
            {
                TransactionStatus start = transaction.Start();
                lines.Add("transaction_start=" + start);
                if (start == TransactionStatus.Started)
                {
                    TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
                    int r0 = tsd.FirstRowNumber;
                    int c0 = tsd.FirstColumnNumber;
                    lines.Add("before: " + Describe(doc, schedule, tsd, r0, c0));

                    Try(lines, "A type-then-empty-text", delegate
                    {
                        tsd.SetCellType(r0, c0, CellType.Text);
                        tsd.SetCellText(r0, c0, string.Empty);
                    }, doc, schedule, tsd, r0, c0);

                    Try(lines, "B empty-text-then-type", delegate
                    {
                        tsd.SetCellText(r0, c0, string.Empty);
                        tsd.SetCellType(r0, c0, CellType.Text);
                    }, doc, schedule, tsd, r0, c0);

                    Try(lines, "C type-then-space", delegate
                    {
                        tsd.SetCellType(r0, c0, CellType.Text);
                        tsd.SetCellText(r0, c0, " ");
                    }, doc, schedule, tsd, r0, c0);
                }
            }
            catch (Exception ex)
            {
                lines.Add("exception=" + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                finalStatus = TransactionRollback.RollBackIfStarted(transaction);
                transaction.Dispose();
            }
            lines.Add("final_rollback_status=" + (finalStatus.HasValue ? finalStatus.Value.ToString() : "unreadable"));

            string path = WriteEvidence(commandData, lines);
            TaskDialog.Show(Title,
                (finalStatus == TransactionStatus.RolledBack
                    ? "Nothing was changed; the test transaction was rolled back."
                    : "The rollback could not be verified.")
                + "\n\nEvidence file:\n" + path);
            return Result.Succeeded;
        }

        private static void Try(
            List<string> lines, string label, Action action,
            Document doc, ViewSchedule schedule, TableSectionData tsd, int r0, int c0)
        {
            try
            {
                action();
                lines.Add(label + ": " + Describe(doc, schedule, tsd, r0, c0));
            }
            catch (Exception ex)
            {
                lines.Add(label + ": exception " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string Describe(
            Document doc, ViewSchedule schedule, TableSectionData tsd, int r0, int c0)
        {
            string type;
            string text;
            try { type = tsd.GetCellType(r0, c0).ToString(); }
            catch (Exception ex) { type = "(failed: " + ex.GetType().Name + ")"; }
            try { text = "\"" + tsd.GetCellText(r0, c0) + "\""; }
            catch (Exception ex) { text = "(failed: " + ex.GetType().Name + ")"; }
            StyleIntegrityResult scan = ScheduleStyleIntegrityScanner.ScanInOpenTransaction(
                doc, schedule, true);
            string verdict = scan == null
                ? "(no scan)"
                : scan.State + "/" + scan.ReasonCode + " styleless=" + scan.StylelessCellCount
                    + (string.IsNullOrEmpty(scan.FirstReadError) ? string.Empty
                        : " first_error=" + scan.FirstReadError);
            return "cell_type=" + type + " text=" + text + " scan=" + verdict;
        }

        private static string WriteEvidence(ExternalCommandData commandData, List<string> lines)
        {
            string source;
            string directory = Probing.WorksharingStatusReport.ResolveEvidenceDirectory(out source);
            System.IO.Directory.CreateDirectory(directory);
            string user = commandData.Application.Application.Username ?? "user";
            string path = System.IO.Path.Combine(directory,
                "ESL_EmptyTopLeft_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")
                + "_" + user + ".txt");
            System.IO.File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            return path;
        }
    }
}
