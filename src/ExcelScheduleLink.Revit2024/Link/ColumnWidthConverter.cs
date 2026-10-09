using System.Collections.Generic;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Adapts a <see cref="SheetModel"/> to the pure paper-space width math in
    /// <see cref="ColumnWidthMath"/>. For every column it takes the larger of the
    /// Excel column width and a content width derived from the longest visible
    /// string -- measured in half-width units (<see cref="CharWidth"/>) so CJK
    /// text is not under-sized -- then scales by WidthScale and clamps into the
    /// range Revit accepts. Cells inside a horizontal merge (spanning more than
    /// one column) are excluded from the content estimate so a long merged title
    /// does not blow out a single column.
    ///
    /// All lengths are feet (paper space). Every tuning constant lives in
    /// <see cref="LayoutMetrics"/>; this class only bridges the Revit-side model
    /// to the math.
    /// </summary>
    internal static class ColumnWidthConverter
    {
        /// <summary>Computes the width in feet for every column of the sheet.</summary>
        public static double[] ComputeColumnFeet(SheetModel sheet, double widthScale, int columnCount)
        {
            // Excel widths by column index.
            Dictionary<int, double> excelWidths = new Dictionary<int, double>();
            foreach (ColumnInfo column in sheet.Columns)
            {
                excelWidths[column.ColumnIndex] = column.Width;
            }

            // Longest content per column in half-width units, ignoring cells that
            // belong to a horizontal merge.
            double[] maxUnits = new double[columnCount];
            for (int r = 0; r < sheet.RowCount; r++)
            {
                for (int c = 0; c < columnCount; c++)
                {
                    if (IsInHorizontalMerge(sheet, r, c))
                    {
                        continue;
                    }
                    double units = CharWidth.MeasureUnits(sheet.GetText(r, c));
                    if (units > maxUnits[c])
                    {
                        maxUnits[c] = units;
                    }
                }
            }

            return ColumnWidthMath.ComputeColumnFeet(maxUnits, excelWidths, widthScale);
        }

        /// <summary>Clamps a width in feet into the accepted range.</summary>
        public static double Clamp(double feet)
        {
            return ColumnWidthMath.Clamp(feet);
        }

        private static bool IsInHorizontalMerge(SheetModel sheet, int row, int column)
        {
            foreach (MergedRange m in sheet.MergedRanges)
            {
                if (m.LastColumn > m.FirstColumn
                    && row >= m.FirstRow && row <= m.LastRow
                    && column >= m.FirstColumn && column <= m.LastColumn)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
