using System;
using System.Collections.Generic;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// The read result of one worksheet.
    /// Cells hold values already resolved to "what Excel displays" (no raw
    /// values are kept). Row and column indices are 0-based.
    /// </summary>
    public sealed class SheetModel
    {
        private readonly string[,] _displayTexts;

        public SheetModel(
            string sheetName,
            string[,] displayTexts,
            IReadOnlyList<MergedRange> mergedRanges,
            IReadOnlyList<ColumnInfo> columns)
            : this(sheetName, displayTexts, mergedRanges, columns, 0, 0)
        {
        }

        /// <summary>
        /// Creates a bounded display model while retaining the source window
        /// dimensions used for the renderer's truncation notice.
        /// </summary>
        public SheetModel(
            string sheetName,
            string[,] displayTexts,
            IReadOnlyList<MergedRange> mergedRanges,
            IReadOnlyList<ColumnInfo> columns,
            int sourceRowCount,
            int sourceColumnCount)
        {
            if (displayTexts == null) throw new ArgumentNullException(nameof(displayTexts));
            SheetName = sheetName ?? string.Empty;
            _displayTexts = displayTexts;
            MergedRanges = mergedRanges ?? new List<MergedRange>();
            Columns = columns ?? new List<ColumnInfo>();
            SourceRowCount = sourceRowCount > 0 ? sourceRowCount : RowCount;
            SourceColumnCount = sourceColumnCount > 0 ? sourceColumnCount : ColumnCount;
        }

        public string SheetName { get; }

        public int RowCount
        {
            get { return _displayTexts.GetLength(0); }
        }

        public int ColumnCount
        {
            get { return _displayTexts.GetLength(1); }
        }

        /// <summary>
        /// Number of rows in the selected source window before the bounded
        /// display array was applied. This is used only for truncation notes.
        /// </summary>
        public int SourceRowCount { get; }

        /// <summary>Number of source columns before display-array truncation.</summary>
        public int SourceColumnCount { get; }

        /// <summary>Merged cell ranges (0-based, inclusive).</summary>
        public IReadOnlyList<MergedRange> MergedRanges { get; }

        /// <summary>Column information (kept in Excel width units; converting
        /// to Revit feet is the renderer's responsibility).</summary>
        public IReadOnlyList<ColumnInfo> Columns { get; }

        /// <summary>Returns the display text. Empty cells yield string.Empty.</summary>
        public string GetText(int row, int column)
        {
            return _displayTexts[row, column] ?? string.Empty;
        }
    }

    /// <summary>A merged cell range (0-based, inclusive).</summary>
    public sealed class MergedRange
    {
        public MergedRange(int firstRow, int firstColumn, int lastRow, int lastColumn)
        {
            FirstRow = firstRow;
            FirstColumn = firstColumn;
            LastRow = lastRow;
            LastColumn = lastColumn;
        }

        public int FirstRow { get; }
        public int FirstColumn { get; }
        public int LastRow { get; }
        public int LastColumn { get; }
    }

    /// <summary>Column information. Width is in Excel column-width units.
    /// Columns without a custom width are not included.</summary>
    public sealed class ColumnInfo
    {
        public ColumnInfo(int columnIndex, double width)
        {
            ColumnIndex = columnIndex;
            Width = width;
        }

        /// <summary>0-based column index.</summary>
        public int ColumnIndex { get; }

        /// <summary>Excel column-width units.</summary>
        public double Width { get; }
    }
}
