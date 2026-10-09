using System.Collections.Generic;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>Kind of a raw cell value.</summary>
    internal enum RawCellKind
    {
        Empty,
        /// <summary>Numeric value (including date/time serial values). Format
        /// interpretation is the responsibility of CellDisplayFormatter.</summary>
        Number,
        /// <summary>Text (shared string / inline string / string formula result,
        /// already resolved).</summary>
        Text,
        Boolean,
        Error,
        /// <summary>ISO 8601 date string (strict mode, t="d").</summary>
        IsoDate
    }

    /// <summary>Raw value and style information of a single cell.</summary>
    internal struct RawCell
    {
        public RawCellKind Kind;
        /// <summary>Raw value string (shared strings resolved to text; numbers
        /// kept in their original notation).</summary>
        public string Raw;
        /// <summary>Resolved number format id (0 = General when unstyled).</summary>
        public int NumFmtId;
        /// <summary>Format code for custom formats. Null for built-in formats
        /// and General.</summary>
        public string FormatCode;
    }

    /// <summary>Raw data of one worksheet (before display-text generation).</summary>
    internal sealed class RawSheetData
    {
        public RawSheetData(
            int rowCount, int columnCount, int sourceRowCount, int sourceColumnCount)
        {
            RowCount = rowCount;
            ColumnCount = columnCount;
            SourceRowCount = sourceRowCount;
            SourceColumnCount = sourceColumnCount;
            Cells = new RawCell[rowCount, columnCount];
            Merges = new List<MergeInfo>();
            ColumnWidths = new Dictionary<int, double>();
        }

        public string SheetName;
        public int RowCount { get; }
        public int ColumnCount { get; }
        public int SourceRowCount { get; }
        public int SourceColumnCount { get; }
        public RawCell[,] Cells { get; }
        /// <summary>0-based, inclusive, relative to the output range origin.</summary>
        public List<MergeInfo> Merges { get; }
        /// <summary>Column index (relative to the output range origin) → Excel
        /// width units. Only columns with a custom width are included.</summary>
        public Dictionary<int, double> ColumnWidths { get; }
        /// <summary>Whether the workbook uses the 1904 date system.</summary>
        public bool Date1904;
    }

    internal struct MergeInfo
    {
        public int FirstRow;
        public int FirstColumn;
        public int LastRow;
        public int LastColumn;
    }
}
