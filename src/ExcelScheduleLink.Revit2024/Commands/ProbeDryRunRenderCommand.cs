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
    /// Probe-manifest-only regression check for a re-rendered active linked
    /// schedule from its workbook exactly as a reload does, inside one
    /// transaction that is always rolled back, and writes the post-render
    /// style-integrity verdict with every header cell's facts. It never saves
    /// link storage, never commits and never opens another view, so the
    /// reason a real reload is rolled back can be read without changing the
    /// model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeDryRunRenderCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Dry-Run Render";

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
                TaskDialog.Show(Title,
                    "Open an Excel-linked schedule view first. Nothing was changed.");
                return Result.Cancelled;
            }

            List<string> lines = new List<string>();
            lines.Add("schedule=" + schedule.Name + " id=" + schedule.Id.Value);
            ExcelLinkInfo info = storage.Info;
            string xlsxPath = PathResolver.Resolve(doc, info);
            lines.Add("workbook=" + (xlsxPath ?? "(not found)"));
            if (xlsxPath == null)
            {
                return Finish(commandData, lines, null);
            }

            SheetModel sheet;
            try
            {
                string range = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null;
                sheet = new OpenXmlExcelReadService().ReadSheetSnapshot(
                    xlsxPath, info.SheetName, range).Sheet;
                lines.Add("sheet rows=" + sheet.RowCount + " columns=" + sheet.ColumnCount
                    + " merges=" + sheet.MergedRanges.Count);
                for (int r = 0; r < sheet.RowCount && r < 5; r++)
                {
                    List<string> texts = new List<string>();
                    for (int c = 0; c < sheet.ColumnCount && c < 5; c++)
                    {
                        texts.Add("\"" + sheet.GetText(r, c) + "\"");
                    }
                    lines.Add("  source row " + r + ": " + string.Join(", ", texts.ToArray()));
                }
            }
            catch (Exception ex)
            {
                lines.Add("read_failed=" + ex.GetType().Name + ": " + ex.Message);
                return Finish(commandData, lines, null);
            }

            Transaction transaction = new Transaction(doc, "ESL Probe - dry-run render (rolled back)");
            TransactionStatus? finalStatus = null;
            try
            {
                TransactionStatus start = transaction.Start();
                lines.Add("transaction_start=" + start);
                if (start == TransactionStatus.Started)
                {
                    StyleIntegrityResult verdict;
                    try
                    {
                        HeaderTableRenderer.RenderResult render = HeaderTableRenderer.Render(
                            schedule, sheet, info.WidthScale, info.ColumnWidthMode, info.RowHeightMode);
                        verdict = render.IntegrityResult;
                        lines.Add("render=completed (guard passed)");
                    }
                    catch (UnsafeScheduleStyleException ex)
                    {
                        verdict = ex.Result;
                        lines.Add("render=guard_failed");
                    }
                    Dump(lines, verdict);
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
            return Finish(commandData, lines, finalStatus);
        }

        private static void Dump(List<string> lines, StyleIntegrityResult verdict)
        {
            if (verdict == null)
            {
                lines.Add("verdict=(none)");
                return;
            }
            lines.Add("verdict=" + verdict.State + "/" + verdict.ReasonCode
                + " styleless=" + verdict.StylelessCellCount
                + " first_error=" + (verdict.FirstReadError ?? string.Empty));
            ScheduleStyleFacts facts = verdict.Facts;
            if (facts == null)
            {
                lines.Add("facts=(none)");
                return;
            }
            lines.Add("dimensions rows=" + facts.NumberOfRows + " columns=" + facts.NumberOfColumns
                + " expected=" + facts.ExpectedHeaderCellCount + " read=" + (facts.Cells == null ? 0 : facts.Cells.Count));
            lines.Add("title=" + Describe(facts.TitleTextStyle));
            lines.Add("header=" + Describe(facts.HeaderTextStyle));
            if (facts.Cells == null)
            {
                return;
            }
            foreach (CellStyleFacts cell in facts.Cells)
            {
                lines.Add("cell (" + cell.Row + "," + cell.Column + ")"
                    + " read=" + cell.ReadComplete
                    + " type_read=" + cell.CellTypeReadComplete
                    + " text_cell=" + cell.IsTextCell
                    + " font=\"" + cell.FontName + "\""
                    + " size=" + cell.TextSize.ToString("R", CultureInfo.InvariantCulture)
                    + " borders=" + cell.BorderTopLineStyle + "/" + cell.BorderBottomLineStyle
                    + "/" + cell.BorderLeftLineStyle + "/" + cell.BorderRightLineStyle
                    + " overrides=" + cell.IsCellOverridden + "/" + cell.IsStyleOverridden
                    + "/" + cell.HasAnyOverrideOption
                    + (string.IsNullOrEmpty(cell.ReadError) ? string.Empty : " error=" + cell.ReadError));
            }
        }

        private static string Describe(TextStyleFacts style)
        {
            if (style == null)
            {
                return "(none)";
            }
            return "resolvable=" + style.IsResolvable + " font=\"" + style.FontName + "\" table_units="
                + style.SizeTableUnits.ToString("R", CultureInfo.InvariantCulture)
                + (string.IsNullOrEmpty(style.ReadError) ? string.Empty : " error=" + style.ReadError);
        }

        private static Result Finish(
            ExternalCommandData commandData, List<string> lines, TransactionStatus? finalStatus)
        {
            lines.Add("final_rollback_status=" + (finalStatus.HasValue ? finalStatus.Value.ToString() : "not started"));
            string source;
            string directory = Probing.WorksharingStatusReport.ResolveEvidenceDirectory(out source);
            System.IO.Directory.CreateDirectory(directory);
            string user = commandData.Application.Application.Username ?? "user";
            string path = System.IO.Path.Combine(directory,
                "ESL_DryRunRender_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                + "_" + user + ".txt");
            System.IO.File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            TaskDialog.Show(Title,
                (finalStatus == null || finalStatus == TransactionStatus.RolledBack
                    ? "Nothing was changed; the dry-run transaction was rolled back or never started."
                    : "The rollback could not be verified.")
                + "\n\nEvidence file:\n" + path);
            return Result.Succeeded;
        }
    }
}
