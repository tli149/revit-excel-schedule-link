using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using ExcelScheduleLink.Layout;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Reads the column widths of the CURRENT linked worksheet for "Restore Excel
    /// Widths" (design v0.8.0 5.2.1). This is not "go back to the widths used when
    /// the link was created": it re-reads whatever the linked workbook has now,
    /// using the stored path / worksheet / range and the link's current WidthScale.
    ///
    /// Reading happens outside any transaction. On any failure - link info missing,
    /// path unresolvable, read error, or a column count that no longer matches the
    /// schedule - nothing is returned to apply, so the caller leaves the candidate
    /// widths untouched. Cell contents, merges and the stored link info are never
    /// modified here; only widths are produced.
    /// </summary>
    internal static class ExcelWidthReader
    {
        public static ExcelWidthResult ReadColumnWidths(
            Document doc, ViewSchedule schedule, int scheduleColumns)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            if (info == null)
            {
                return ExcelWidthResult.Failed(UserMessages.LayoutNotLinked);
            }

            PathResolutionResult pathResolution = PathResolver.ResolveForAction(doc, info);
            if (pathResolution == null || !pathResolution.IsResolved)
            {
                return ExcelWidthResult.Failed(
                    PathResolver.DescribeFailure(info, pathResolution));
            }
            string xlsxPath = pathResolution.ResolvedPath;

            SheetReadResult snapshot;
            try
            {
                string range = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null;
                snapshot = new OpenXmlExcelReadService().ReadSheetSnapshot(
                    xlsxPath, info.SheetName, range);
            }
            catch (ExcelReadException ex)
            {
                return ExcelWidthResult.Failed(UserMessages.LayoutRestoreReadFailed(ex.Message));
            }
            catch (Exception ex)
            {
                return ExcelWidthResult.Failed(UserMessages.LayoutRestoreReadFailed(ex.Message));
            }

            // Compare against what the renderer would produce (same truncation rule).
            SheetModel sheet = snapshot.Sheet;
            StyleIntegrityLimits.RenderedSize renderedSize =
                StyleIntegrityLimits.GetEffectiveRenderedSize(
                    sheet.RowCount, sheet.ColumnCount);
            int worksheetColumns = renderedSize.Columns;
            if (worksheetColumns != scheduleColumns)
            {
                // No truncating and no min-filling: tell the user to reload first.
                return ExcelWidthResult.Failed(
                    UserMessages.LayoutRestoreColumnMismatch(worksheetColumns, scheduleColumns));
            }

            Dictionary<int, double> excelWidths = new Dictionary<int, double>();
            foreach (ColumnInfo column in sheet.Columns)
            {
                excelWidths[column.ColumnIndex] = column.Width;
            }

            return new ExcelWidthResult
            {
                Success = true,
                Feet = LayoutOps.ResetToExcelFeet(excelWidths, scheduleColumns, info.WidthScale),
                SourceSnapshot = new LayoutSourceSnapshot
                {
                    Fingerprint = snapshot.Fingerprint,
                    ResolvedPath = xlsxPath
                }
            };
        }
    }
}
