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
    /// Probe-manifest-only diagnostic for the v0.9.2 render of a range
    /// whose top-left cell is empty leaves the inserted cells without a native
    /// style, while the v0.9.1 render of the same schedule did not. v0.9.2
    /// changed three cell writes: (0,0) is converted to text, every cell gets
    /// SetCellType(Text), and empty cells get SetCellText(""). This probe
    /// replays the clear/insert/write part of the render on the active linked
    /// schedule once per variant, each inside its own transaction that is
    /// always rolled back, and records the style verdict per variant.
    /// Nothing is committed.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeRenderVariantsCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Render Variants";

        private sealed class Variant
        {
            public string Name;
            public bool TopLeftTypeBeforeInsert;
            public bool TopLeftTypeAfterWrite;
            public bool TypeEveryCell;
            public bool WriteEmptyText;
        }

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
                return Finish(commandData, lines, true);
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
                return Finish(commandData, lines, true);
            }
            lines.Add("sheet rows=" + sheet.RowCount + " columns=" + sheet.ColumnCount);

            Variant[] variants =
            {
                new Variant { Name = "V1 v0.9.1 (no SetCellType, skip empty text)" },
                new Variant { Name = "V2 V1 + (0,0) SetCellType(Text) after writes", TopLeftTypeAfterWrite = true },
                new Variant { Name = "V3 V1 + (0,0) SetCellType(Text) before inserts", TopLeftTypeBeforeInsert = true },
                new Variant { Name = "V4 V1 + SetCellType(Text) on every cell", TypeEveryCell = true },
                new Variant { Name = "V5 V1 + SetCellText(\"\") on empty cells", WriteEmptyText = true },
                new Variant { Name = "V6 v0.9.2 current (all three)", TopLeftTypeBeforeInsert = true, TypeEveryCell = true, WriteEmptyText = true },
            };

            bool allRolledBack = true;
            foreach (Variant variant in variants)
            {
                Transaction transaction = new Transaction(doc, "ESL Probe - render variant (rolled back)");
                TransactionStatus? final = null;
                try
                {
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        lines.Add(variant.Name + ": transaction did not start");
                        continue;
                    }
                    Apply(schedule, sheet, variant);
                    StyleIntegrityResult scan = ScheduleStyleIntegrityScanner.ScanInOpenTransaction(doc, schedule, false);
                    lines.Add(variant.Name + ": " + Summarize(scan));
                }
                catch (Exception ex)
                {
                    lines.Add(variant.Name + ": exception " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    final = TransactionRollback.RollBackIfStarted(transaction);
                    transaction.Dispose();
                    if (final.HasValue && final.Value != TransactionStatus.RolledBack)
                    {
                        allRolledBack = false;
                    }
                    lines.Add("  rollback=" + (final.HasValue ? final.Value.ToString() : "unreadable"));
                }
            }
            return Finish(commandData, lines, allRolledBack);
        }

        private static void Apply(ViewSchedule schedule, SheetModel sheet, Variant variant)
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
            if (variant.TopLeftTypeBeforeInsert)
            {
                tsd.SetCellType(tsd.FirstRowNumber, tsd.FirstColumnNumber, CellType.Text);
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
                    if (variant.TypeEveryCell)
                    {
                        tsd.SetCellType(r0 + r, c0 + c, CellType.Text);
                    }
                    if (text.Length > 0 || variant.WriteEmptyText)
                    {
                        tsd.SetCellText(r0 + r, c0 + c, text);
                    }
                }
            }
            if (variant.TopLeftTypeAfterWrite)
            {
                tsd.SetCellType(r0, c0, CellType.Text);
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
            if (facts != null && facts.Cells != null)
            {
                foreach (CellStyleFacts cell in facts.Cells)
                {
                    text += " | (" + cell.Row + "," + cell.Column + ") "
                        + (cell.IsTextCell ? "Text" : "NonText") + " " + cell.FontName + " "
                        + cell.TextSize.ToString("R", CultureInfo.InvariantCulture)
                        + " b=" + cell.BorderTopLineStyle;
                }
            }
            return text;
        }

        private static Result Finish(ExternalCommandData commandData, List<string> lines, bool rolledBack)
        {
            string source;
            string directory = Probing.WorksharingStatusReport.ResolveEvidenceDirectory(out source);
            System.IO.Directory.CreateDirectory(directory);
            string user = commandData.Application.Application.Username ?? "user";
            string path = System.IO.Path.Combine(directory,
                "ESL_RenderVariants_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                + "_" + user + ".txt");
            System.IO.File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            TaskDialog.Show(Title,
                (rolledBack ? "Nothing was changed; every variant was rolled back."
                    : "A rollback could not be verified.")
                + "\n\nEvidence file:\n" + path);
            return Result.Succeeded;
        }
    }
}
