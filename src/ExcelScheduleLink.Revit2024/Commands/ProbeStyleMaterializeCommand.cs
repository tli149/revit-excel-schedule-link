using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Probe-manifest-only diagnostic for the style-materialization regression.
    /// The render-variants
    /// probe showed that cells created by InsertRow/InsertColumn have no native
    /// style right after the clear/insert/write steps, even with the exact
    /// v0.9.1 sequence. This probe repeats those steps on the active linked
    /// schedule and then applies one candidate "materializer" per variant
    /// (Regenerate, section/schedule RefreshData, re-applying column widths,
    /// re-applying row heights), each inside its own rolled-back transaction,
    /// and records the style verdict. The last variant commits the bare steps
    /// and scans again in a new rolled-back transaction, to learn whether a
    /// commit alone gives the cells their style; that leaves the model changed
    /// in memory, so the session must be closed without saving.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeStyleMaterializeCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Style Materialize";

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
            ExcelLinkStorage storage = schedule == null ? null : ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                TaskDialog.Show(Title, "Open an Excel-linked schedule view first. Nothing was changed.");
                return Result.Cancelled;
            }

            List<string> lines = new List<string>();
            lines.Add("schedule=" + schedule.Name + " id=" + schedule.Id.Value);
            ExcelLinkInfo info = storage.Info;
            string xlsxPath = PathResolver.Resolve(doc, info);
            if (xlsxPath == null)
            {
                lines.Add("workbook=(not found)");
                return Finish(commandData, lines, "Nothing was changed.");
            }
            SheetModel sheet;
            try
            {
                string range = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null;
                sheet = new OpenXmlExcelReadService().ReadSheetSnapshot(xlsxPath, info.SheetName, range).Sheet;
            }
            catch (Exception ex)
            {
                lines.Add("read_failed=" + ex.GetType().Name + ": " + ex.Message);
                return Finish(commandData, lines, "Nothing was changed.");
            }
            lines.Add("sheet rows=" + sheet.RowCount + " columns=" + sheet.ColumnCount);

            KeyValuePair<string, Action<Document, ViewSchedule>>[] materializers =
            {
                Pair("M0 none", delegate { }),
                Pair("M1 doc.Regenerate()", (d, s) => d.Regenerate()),
                Pair("M2 header TableSectionData.RefreshData()", (d, s) =>
                    s.GetTableData().GetSectionData(SectionType.Header).RefreshData()),
                Pair("M3 ViewSchedule.RefreshData()", (d, s) => s.RefreshData()),
                Pair("M4 re-apply every column width", (d, s) =>
                {
                    TableSectionData t = s.GetTableData().GetSectionData(SectionType.Header);
                    for (int c = t.FirstColumnNumber; c <= t.LastColumnNumber; c++)
                    {
                        t.SetColumnWidth(c, t.GetColumnWidth(c));
                    }
                }),
                Pair("M5 re-apply every row height", (d, s) =>
                {
                    TableSectionData t = s.GetTableData().GetSectionData(SectionType.Header);
                    for (int r = t.FirstRowNumber; r <= t.LastRowNumber; r++)
                    {
                        t.SetRowHeight(r, t.GetRowHeight(r));
                    }
                }),
                Pair("M6 Regenerate() then header RefreshData()", (d, s) =>
                {
                    d.Regenerate();
                    s.GetTableData().GetSectionData(SectionType.Header).RefreshData();
                }),
            };

            bool allRolledBack = true;
            foreach (KeyValuePair<string, Action<Document, ViewSchedule>> m in materializers)
            {
                Transaction transaction = new Transaction(doc, "ESL Probe - materialize (rolled back)");
                try
                {
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        lines.Add(m.Key + ": transaction did not start");
                        continue;
                    }
                    WriteBareRender(schedule, sheet);
                    string step;
                    try
                    {
                        m.Value(doc, schedule);
                        step = "ok";
                    }
                    catch (Exception ex)
                    {
                        step = "exception " + ex.GetType().Name + ": " + ex.Message;
                    }
                    StyleIntegrityResult scan = ScheduleStyleIntegrityScanner.ScanInOpenTransaction(doc, schedule, false);
                    lines.Add(m.Key + " [" + step + "]: " + Summarize(scan));
                }
                catch (Exception ex)
                {
                    lines.Add(m.Key + ": exception " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    TransactionStatus? final = TransactionRollback.RollBackIfStarted(transaction);
                    transaction.Dispose();
                    if (!final.HasValue || final.Value != TransactionStatus.RolledBack)
                    {
                        allRolledBack = false;
                    }
                    lines.Add("  rollback=" + (final.HasValue ? final.Value.ToString() : "unreadable"));
                }
            }

            // Last: commit the bare steps, then scan in a new rolled-back read.
            string committedNote = "M7 not run";
            Transaction commit = new Transaction(doc, "ESL Probe - materialize M7 (COMMITTED, do not save)");
            try
            {
                if (commit.Start() == TransactionStatus.Started)
                {
                    WriteBareRender(schedule, sheet);
                    TransactionStatus status = commit.Commit();
                    committedNote = "M7 bare steps committed: " + status;
                    StyleIntegrityResult after = ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(doc, schedule);
                    committedNote += " -> scan after commit: " + Summarize(after);
                }
            }
            catch (Exception ex)
            {
                committedNote = "M7 exception " + ex.GetType().Name + ": " + ex.Message;
                TransactionRollback.RollBackIfStarted(commit);
            }
            finally
            {
                commit.Dispose();
            }
            lines.Add(committedNote);
            lines.Add("variants_M0_M6_rolled_back=" + allRolledBack);

            return Finish(commandData, lines,
                "Variants M0-M6 were rolled back. Variant M7 COMMITTED a bare re-render in this session: "
                + "do not open this schedule again and close Revit WITHOUT saving.");
        }

        private static KeyValuePair<string, Action<Document, ViewSchedule>> Pair(
            string name, Action<Document, ViewSchedule> action)
        {
            return new KeyValuePair<string, Action<Document, ViewSchedule>>(name, action);
        }

        /// <summary>The exact v0.9.1 clear/insert/write steps, no widths or merges.</summary>
        private static void WriteBareRender(ViewSchedule schedule, SheetModel sheet)
        {
            TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
            while (tsd.NumberOfRows > 1)
            {
                tsd.RemoveRow(tsd.LastRowNumber);
            }
            while (tsd.NumberOfColumns > 1)
            {
                tsd.RemoveColumn(tsd.LastColumnNumber);
            }
            tsd.SetCellText(tsd.FirstRowNumber, tsd.FirstColumnNumber, string.Empty);
            StyleIntegrityLimits.RenderedSize renderedSize =
                StyleIntegrityLimits.GetEffectiveRenderedSize(
                    sheet.RowCount, sheet.ColumnCount);
            int rows = renderedSize.Rows;
            int columns = renderedSize.Columns;
            while (tsd.NumberOfRows < rows)
            {
                tsd.InsertRow(tsd.FirstRowNumber + 1);
            }
            while (tsd.NumberOfColumns < columns)
            {
                tsd.InsertColumn(tsd.FirstColumnNumber + 1);
            }
            int r0 = tsd.FirstRowNumber;
            int c0 = tsd.FirstColumnNumber;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    string text = sheet.GetText(r, c) ?? string.Empty;
                    if (text.Length > 0)
                    {
                        tsd.SetCellText(r0 + r, c0 + c, text);
                    }
                }
            }
        }

        private static string Summarize(StyleIntegrityResult scan)
        {
            if (scan == null)
            {
                return "(no scan)";
            }
            string text = scan.State + "/" + scan.ReasonCode + " styleless=" + scan.StylelessCellCount;
            ScheduleStyleFacts facts = scan.Facts;
            if (facts != null && facts.Cells != null && facts.Cells.Count > 1)
            {
                CellStyleFacts sample = facts.Cells[1];
                text += " sample(" + sample.Row + "," + sample.Column + ")=" + sample.FontName + " "
                    + sample.TextSize.ToString("R", CultureInfo.InvariantCulture) + " b=" + sample.BorderTopLineStyle;
            }
            return text;
        }

        private static Result Finish(ExternalCommandData commandData, List<string> lines, string summary)
        {
            string source;
            string directory = Probing.WorksharingStatusReport.ResolveEvidenceDirectory(out source);
            System.IO.Directory.CreateDirectory(directory);
            string user = commandData.Application.Application.Username ?? "user";
            string path = System.IO.Path.Combine(directory,
                "ESL_StyleMaterialize_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                + "_" + user + ".txt");
            System.IO.File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            TaskDialog.Show(Title, summary + "\n\nEvidence file:\n" + path);
            return Result.Succeeded;
        }
    }
}
