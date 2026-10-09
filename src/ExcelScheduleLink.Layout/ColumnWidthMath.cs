using System.Collections.Generic;

namespace ExcelScheduleLink.Layout
{
    /// <summary>
    /// Pure paper-space column-width math -- no Revit and no Excel types, only
    /// primitives -- so it is unit-testable. For each column it takes the larger
    /// of a content-based width (from half-width units) and the Excel column
    /// width, multiplies by the user width scale, and clamps into Revit's
    /// accepted range. All results are feet.
    /// </summary>
    public static class ColumnWidthMath
    {
        /// <summary>Excel column-width units to feet, scaled and clamped (content ignored).</summary>
        public static double FromExcelWidth(double excelWidth, double widthScale)
        {
            double feet = ExcelWidthToFeet(excelWidth);
            return Clamp(feet * NormalizeScale(widthScale));
        }

        /// <summary>
        /// Width in feet for each column from its maximum content (in half-width
        /// units) and, when present, its Excel width. The larger of the two is
        /// scaled and clamped. Content units are capped at
        /// <see cref="LayoutMetrics.MaxContentUnits"/>.
        /// </summary>
        /// <param name="maxUnitsPerColumn">Max half-width units seen in each column.</param>
        /// <param name="excelWidthsByColumn">Excel widths keyed by column index; may be null.</param>
        /// <param name="widthScale">User multiplier; values &lt;= 0 are treated as 1.</param>
        public static double[] ComputeColumnFeet(
            IList<double> maxUnitsPerColumn,
            IDictionary<int, double> excelWidthsByColumn,
            double widthScale)
        {
            double scale = NormalizeScale(widthScale);
            int count = maxUnitsPerColumn.Count;
            double[] feet = new double[count];

            for (int c = 0; c < count; c++)
            {
                double units = maxUnitsPerColumn[c];
                if (units > LayoutMetrics.MaxContentUnits)
                {
                    units = LayoutMetrics.MaxContentUnits;
                }

                double contentInches = units * LayoutMetrics.HalfWidthInchesPerUnit
                                       + LayoutMetrics.CellPaddingInches;
                double contentFeet = contentInches / LayoutMetrics.InchesPerFoot;

                double excelFeet = 0.0;
                double w;
                if (excelWidthsByColumn != null && excelWidthsByColumn.TryGetValue(c, out w))
                {
                    excelFeet = ExcelWidthToFeet(w);
                }

                double baseFeet = contentFeet > excelFeet ? contentFeet : excelFeet;
                feet[c] = Clamp(baseFeet * scale);
            }

            return feet;
        }

        /// <summary>
        /// Content width in feet for a number of half-width units (cell padding
        /// included, units capped at MaxContentUnits). NOT clamped, so callers can
        /// use it as a merged-span requirement that spreads over several columns.
        /// Non-finite or negative units are treated as zero.
        /// </summary>
        public static double ContentFeetForUnits(double units)
        {
            if (double.IsNaN(units) || double.IsInfinity(units) || units < 0.0)
            {
                units = 0.0;
            }
            if (units > LayoutMetrics.MaxContentUnits)
            {
                units = LayoutMetrics.MaxContentUnits;
            }
            return (units * LayoutMetrics.HalfWidthInchesPerUnit + LayoutMetrics.CellPaddingInches)
                / LayoutMetrics.InchesPerFoot;
        }

        /// <summary>Clamps a width in feet into [MinColumnFeet, MaxColumnFeet].</summary>
        public static double Clamp(double feet)
        {
            if (double.IsNaN(feet) || feet < LayoutMetrics.MinColumnFeet)
            {
                return LayoutMetrics.MinColumnFeet;
            }
            if (feet > LayoutMetrics.MaxColumnFeet)
            {
                return LayoutMetrics.MaxColumnFeet;
            }
            return feet;
        }

        private static double ExcelWidthToFeet(double excelWidth)
        {
            double pixels = excelWidth * LayoutMetrics.ExcelPixelsPerWidthUnit + LayoutMetrics.ExcelPixelPadding;
            return pixels / LayoutMetrics.PixelsPerInch / LayoutMetrics.InchesPerFoot;
        }

        private static double NormalizeScale(double widthScale)
        {
            return widthScale <= 0 ? 1.0 : widthScale;
        }
    }
}
