using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// Reads raw cell values and style information from an .xlsx file.
    /// Does not generate display text (that is CellDisplayFormatter's job).
    /// </summary>
    internal static class RawCellReader
    {
        /// <summary>First numFmtId reserved for custom number formats (ECMA-376).</summary>
        private const int CustomNumFmtFirstId = 164;

        public static IReadOnlyList<string> GetSheetNames(SpreadsheetDocument doc)
        {
            WorkbookPart wbPart = RequireWorkbookPart(doc);
            return wbPart.Workbook.Descendants<Sheet>()
                .Select(s => s.Name != null ? s.Name.Value : string.Empty)
                .ToList();
        }

        /// <summary>
        /// Reads the given worksheet. When rangeAddress is null/empty the used
        /// range (A1 through the last used cell) is read; otherwise the output
        /// is fixed to the given range (its top-left becomes output [0,0]).
        /// </summary>
        public static RawSheetData Read(SpreadsheetDocument doc, string sheetName, string rangeAddress)
        {
            WorkbookPart wbPart = RequireWorkbookPart(doc);

            Sheet sheet = wbPart.Workbook.Descendants<Sheet>()
                .FirstOrDefault(s => s.Name != null && s.Name.Value == sheetName);
            if (sheet == null || sheet.Id == null)
            {
                throw new ExcelReadException(Messages.SheetNotFound(sheetName));
            }
            WorksheetPart wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);

            bool date1904 = IsDate1904(wbPart);
            StyleResolver styles = StyleResolver.Create(wbPart);
            SharedStringTablePart sst = wbPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();

            // ---- Determine the output range ----
            int originRow;
            int originCol;
            int sourceRowCount;
            int sourceColumnCount;
            int rowCount;
            int colCount;
            if (!string.IsNullOrEmpty(rangeAddress))
            {
                int lastRow;
                int lastCol;
                CellReference.ParseRange(rangeAddress, out originRow, out originCol, out lastRow, out lastCol);
                sourceRowCount = lastRow - originRow + 1;
                sourceColumnCount = lastCol - originCol + 1;
                WorkbookReadLimits.GetBoundedOutputDimensions(
                    sourceRowCount, sourceColumnCount, "the requested cell range",
                    out rowCount, out colCount);
            }
            else
            {
                int maxRow;
                int maxCol;
                FindUsedDimensions(wsPart, sheetName, out maxRow, out maxCol);
                originRow = 0;
                originCol = 0;
                sourceRowCount = Math.Max(maxRow + 1, 1);
                sourceColumnCount = Math.Max(maxCol + 1, 1);
                WorkbookReadLimits.GetBoundedOutputDimensions(
                    sourceRowCount, sourceColumnCount, "the used worksheet dimensions",
                    out rowCount, out colCount);
            }

            RawSheetData data = new RawSheetData(
                rowCount, colCount, sourceRowCount, sourceColumnCount);
            data.SheetName = sheetName;
            data.Date1904 = date1904;

            // Parse directly into the bounded output array. In particular, an
            // explicit range never retains cells outside that range in a list.
            int runningRow = -1;
            foreach (Row xmlRow in wsPart.Worksheet.Descendants<Row>())
            {
                runningRow = GetRowIndex(xmlRow, runningRow, sheetName);
                int runningCol = -1;
                foreach (Cell xmlCell in xmlRow.Elements<Cell>())
                {
                    int r;
                    int c;
                    if (xmlCell.CellReference != null)
                    {
                        CellReference.Parse(xmlCell.CellReference.Value, out r, out c);
                    }
                    else
                    {
                        r = runningRow;
                        c = runningCol + 1;
                    }
                    runningCol = c;
                    ValidateCellCoordinate(r, c, sheetName);

                    int relativeRow = r - originRow;
                    int relativeColumn = c - originCol;
                    if (relativeRow < 0 || relativeRow >= rowCount
                        || relativeColumn < 0 || relativeColumn >= colCount)
                    {
                        continue;
                    }

                    RawCell cell = ParseCell(xmlCell, sst, styles);
                    if (cell.Kind != RawCellKind.Empty)
                    {
                        data.Cells[relativeRow, relativeColumn] = cell;
                    }
                }
            }

            ReadColumnWidths(wsPart, originCol, colCount, data);
            ReadMerges(wsPart, originRow, originCol, rowCount, colCount, data);
            return data;
        }

        // ------------------------------------------------------------------

        private static void FindUsedDimensions(
            WorksheetPart wsPart, string sheetName, out int maxRow, out int maxColumn)
        {
            maxRow = -1;
            maxColumn = -1;
            int runningRow = -1;
            foreach (Row xmlRow in wsPart.Worksheet.Descendants<Row>())
            {
                runningRow = GetRowIndex(xmlRow, runningRow, sheetName);
                int runningColumn = -1;
                foreach (Cell xmlCell in xmlRow.Elements<Cell>())
                {
                    int row;
                    int column;
                    if (xmlCell.CellReference != null)
                    {
                        CellReference.Parse(xmlCell.CellReference.Value, out row, out column);
                    }
                    else
                    {
                        row = runningRow;
                        column = runningColumn + 1;
                    }
                    runningColumn = column;
                    ValidateCellCoordinate(row, column, sheetName);
                    maxRow = Math.Max(maxRow, row);
                    maxColumn = Math.Max(maxColumn, column);
                }
            }
        }

        private static int GetRowIndex(Row row, int previousRow, string sheetName)
        {
            if (row.RowIndex != null)
            {
                ulong rowIndex = row.RowIndex.Value;
                if (rowIndex < 1UL || rowIndex > WorkbookReadLimits.ExcelMaximumRows)
                {
                    throw new ExcelReadException(Messages.ExcelCoordinateExceeded(
                        "row", (long)rowIndex, WorkbookReadLimits.ExcelMaximumRows, sheetName));
                }
                return checked((int)rowIndex - 1);
            }

            int nextRow = checked(previousRow + 1);
            if (nextRow < 0 || nextRow >= WorkbookReadLimits.ExcelMaximumRows)
            {
                throw new ExcelReadException(Messages.ExcelCoordinateExceeded(
                    "row", (long)nextRow + 1L,
                    WorkbookReadLimits.ExcelMaximumRows, sheetName));
            }
            return nextRow;
        }

        private static void ValidateCellCoordinate(int row, int column, string sheetName)
        {
            if (row < 0 || row >= WorkbookReadLimits.ExcelMaximumRows)
            {
                throw new ExcelReadException(Messages.ExcelCoordinateExceeded(
                    "row", (long)row + 1L,
                    WorkbookReadLimits.ExcelMaximumRows, sheetName));
            }
            if (column < 0 || column >= WorkbookReadLimits.ExcelMaximumColumns)
            {
                throw new ExcelReadException(Messages.ExcelCoordinateExceeded(
                    "column", (long)column + 1L,
                    WorkbookReadLimits.ExcelMaximumColumns, sheetName));
            }
        }

        private static WorkbookPart RequireWorkbookPart(SpreadsheetDocument doc)
        {
            WorkbookPart wbPart = doc.WorkbookPart;
            if (wbPart == null)
            {
                throw new ExcelReadException(Messages.WorkbookPartMissing);
            }
            return wbPart;
        }

        private static bool IsDate1904(WorkbookPart wbPart)
        {
            WorkbookProperties props = wbPart.Workbook.WorkbookProperties;
            return props != null && props.Date1904 != null && props.Date1904.Value;
        }

        private static RawCell ParseCell(Cell xmlCell, SharedStringTablePart sst, StyleResolver styles)
        {
            RawCell cell = new RawCell();
            styles.Resolve(xmlCell.StyleIndex != null ? (int)xmlCell.StyleIndex.Value : -1,
                out cell.NumFmtId, out cell.FormatCode);

            string cellValue = xmlCell.CellValue != null ? xmlCell.CellValue.InnerText : null;

            if (xmlCell.DataType == null)
            {
                // Numeric (including date/time serials). For formula cells the
                // cached value (CellValue) is used; formulas are never evaluated.
                if (string.IsNullOrEmpty(cellValue))
                {
                    cell.Kind = RawCellKind.Empty;
                    return cell;
                }
                cell.Kind = RawCellKind.Number;
                cell.Raw = cellValue;
                return cell;
            }

            CellValues type = xmlCell.DataType.Value;
            if (type == CellValues.SharedString)
            {
                cell.Kind = RawCellKind.Text;
                cell.Raw = ResolveSharedString(cellValue, sst);
            }
            else if (type == CellValues.InlineString)
            {
                cell.Kind = RawCellKind.Text;
                cell.Raw = xmlCell.InlineString != null ? xmlCell.InlineString.InnerText : xmlCell.InnerText;
            }
            else if (type == CellValues.String)
            {
                // String result of a formula
                cell.Kind = RawCellKind.Text;
                cell.Raw = cellValue ?? string.Empty;
            }
            else if (type == CellValues.Boolean)
            {
                cell.Kind = RawCellKind.Boolean;
                cell.Raw = cellValue ?? "0";
            }
            else if (type == CellValues.Error)
            {
                cell.Kind = RawCellKind.Error;
                cell.Raw = cellValue ?? "#ERROR";
            }
            else if (type == CellValues.Date)
            {
                // Strict mode (ISO 8601 string). Rare in transitional files.
                cell.Kind = RawCellKind.IsoDate;
                cell.Raw = cellValue ?? string.Empty;
            }
            else
            {
                cell.Kind = string.IsNullOrEmpty(cellValue) ? RawCellKind.Empty : RawCellKind.Number;
                cell.Raw = cellValue;
            }

            if (cell.Kind == RawCellKind.Text && string.IsNullOrEmpty(cell.Raw))
            {
                cell.Kind = RawCellKind.Empty;
            }
            WorkbookReadLimits.ValidateString(cell.Raw, "cell value");
            return cell;
        }

        private static string ResolveSharedString(string indexText, SharedStringTablePart sst)
        {
            int index;
            if (sst == null || !int.TryParse(indexText, out index))
            {
                return indexText ?? string.Empty;
            }
            SharedStringTable table = sst.SharedStringTable;
            if (table == null || index < 0 || index >= table.ChildElements.Count)
            {
                return indexText ?? string.Empty;
            }
            return table.ChildElements[index].InnerText;
        }

        private static void ReadColumnWidths(WorksheetPart wsPart, int originCol, int colCount, RawSheetData data)
        {
            foreach (Column col in wsPart.Worksheet.Descendants<Column>())
            {
                if (col.Width == null || col.Min == null || col.Max == null)
                {
                    continue;
                }
                bool custom = col.CustomWidth != null && col.CustomWidth.Value;
                if (!custom)
                {
                    continue; // default widths are not part of the model (left to the Revit defaults)
                }
                int first;
                int last;
                if (!WorkbookReadLimits.TryGetAcceptedColumnIntersection(
                    col.Min.Value, col.Max.Value, originCol, colCount,
                    out first, out last))
                {
                    continue;
                }
                for (int c = first; c <= last; c++)
                {
                    data.ColumnWidths[c - originCol] = col.Width.Value;
                }
            }
        }

        private static void ReadMerges(
            WorksheetPart wsPart, int originRow, int originCol, int rowCount, int colCount, RawSheetData data)
        {
            foreach (MergeCell merge in wsPart.Worksheet.Descendants<MergeCell>())
            {
                if (merge.Reference == null)
                {
                    continue;
                }
                int r1;
                int c1;
                int r2;
                int c2;
                CellReference.ParseRange(merge.Reference.Value, out r1, out c1, out r2, out c2);
                r1 -= originRow; c1 -= originCol; r2 -= originRow; c2 -= originCol;
                // Only merges fully inside the output range are kept; merges
                // partially crossing the range boundary are ignored.
                if (r1 < 0 || c1 < 0 || r2 >= rowCount || c2 >= colCount)
                {
                    continue;
                }
                data.Merges.Add(new MergeInfo { FirstRow = r1, FirstColumn = c1, LastRow = r2, LastColumn = c2 });
            }
        }

        // ------------------------------------------------------------------

        /// <summary>Resolves styleIndex → (numFmtId, custom format code).</summary>
        private sealed class StyleResolver
        {
            private readonly List<int> _numFmtIdsByXfIndex = new List<int>();
            private readonly Dictionary<int, string> _customFormats = new Dictionary<int, string>();

            public static StyleResolver Create(WorkbookPart wbPart)
            {
                StyleResolver resolver = new StyleResolver();
                WorkbookStylesPart stylesPart = wbPart.GetPartsOfType<WorkbookStylesPart>().FirstOrDefault();
                Stylesheet stylesheet = stylesPart != null ? stylesPart.Stylesheet : null;
                if (stylesheet == null)
                {
                    return resolver;
                }

                if (stylesheet.NumberingFormats != null)
                {
                    foreach (NumberingFormat nf in stylesheet.NumberingFormats.Elements<NumberingFormat>())
                    {
                        if (nf.NumberFormatId != null && nf.FormatCode != null)
                        {
                            resolver._customFormats[(int)nf.NumberFormatId.Value] = nf.FormatCode.Value;
                        }
                    }
                }

                if (stylesheet.CellFormats != null)
                {
                    foreach (CellFormat xf in stylesheet.CellFormats.Elements<CellFormat>())
                    {
                        int id = xf.NumberFormatId != null ? (int)xf.NumberFormatId.Value : 0;
                        resolver._numFmtIdsByXfIndex.Add(id);
                    }
                }
                return resolver;
            }

            public void Resolve(int styleIndex, out int numFmtId, out string formatCode)
            {
                numFmtId = 0;
                formatCode = null;
                if (styleIndex >= 0 && styleIndex < _numFmtIdsByXfIndex.Count)
                {
                    numFmtId = _numFmtIdsByXfIndex[styleIndex];
                }
                string custom;
                if (_customFormats.TryGetValue(numFmtId, out custom))
                {
                    formatCode = custom;
                }
                else if (numFmtId >= CustomNumFmtFirstId)
                {
                    formatCode = null; // custom id without a definition → treat as General
                    numFmtId = 0;
                }
            }
        }
    }
}
