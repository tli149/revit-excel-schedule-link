using System;
using System.Collections.Generic;

namespace ExcelScheduleLink.Layout
{
    /// <summary>
    /// Pure paper-space row-height math (feet). Kept separate from the width
    /// math so both can be tuned independently through <see cref="LayoutMetrics"/>.
    /// </summary>
    public static class RowHeightMath
    {
        /// <summary>
        /// Estimated one-line minimum for fitted dimensions. This helper does
        /// not apply a height to a Revit row. Calibrated by
        /// <see cref="LayoutMetrics.DefaultRowInches"/>.
        /// </summary>
        public static double DefaultRowFeet()
        {
            return LayoutMetrics.DefaultRowInches / LayoutMetrics.InchesPerFoot;
        }

        public static double RequiredFeetForLines(long lines,
            double textSizeFeet)
        {
            return RequiredFeetForLines(lines, textSizeFeet,
                LayoutMetrics.RowFitLinePitchFactor);
        }

        public static double RequiredFeetForLines(long lines,
            double textSizeFeet, double linePitchFactor)
        {
            if (lines < 0)
            {
                throw new ArgumentException(
                    "The line count cannot be negative.", "lines");
            }
            ValidatePositiveFinite(textSizeFeet, "textSizeFeet");
            ValidatePositiveFinite(linePitchFactor, "linePitchFactor");

            long effectiveLines = Math.Max(1L, lines);
            double pitchedLineFeet = textSizeFeet * linePitchFactor;
            EnsurePositiveFinite(pitchedLineFeet,
                "The line pitch is too large.");
            double textHeightFeet = effectiveLines * pitchedLineFeet;
            EnsurePositiveFinite(textHeightFeet,
                "The required text height is too large.");
            double verticalPaddingFeet = LayoutMetrics.RowFitVerticalPaddingInches
                / LayoutMetrics.InchesPerFoot;
            EnsurePositiveFinite(verticalPaddingFeet,
                "The vertical padding is invalid.");
            double requiredFeet = textHeightFeet + verticalPaddingFeet;
            EnsurePositiveFinite(requiredFeet,
                "The required row height is too large.");
            return Math.Max(DefaultRowFeet(), requiredFeet);
        }

        public static RowFitResult FitRows(
            FitContentMeasurements measurements,
            IReadOnlyList<double> columnFeet,
            IReadOnlyList<RowFitTextMetrics> textMetrics)
        {
            if (measurements == null)
            {
                throw new ArgumentNullException("measurements");
            }
            if (columnFeet == null)
            {
                throw new ArgumentNullException("columnFeet");
            }
            if (textMetrics == null)
            {
                throw new ArgumentNullException("textMetrics");
            }

            try
            {
                return FitRowsCore(measurements, columnFeet, textMetrics);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    "The row-fit input exceeds supported numeric bounds.",
                    "measurements", exception);
            }
        }

        private static RowFitResult FitRowsCore(
            FitContentMeasurements measurements,
            IReadOnlyList<double> columnFeet,
            IReadOnlyList<RowFitTextMetrics> textMetrics)
        {
            // Measurements come only from FitContentText.Prepare, which owns
            // shape and anchor validation; only the new inputs are checked.
            int rowCount = measurements.RowCount;
            int columnCount = measurements.ColumnCount;
            if (columnFeet.Count != columnCount)
            {
                throw new ArgumentException(
                    "The column width count does not match the measurements.",
                    "columnFeet");
            }
            if (textMetrics.Count != measurements.Cells.Count)
            {
                throw new ArgumentException(
                    "The text metric count does not match the measured anchors.",
                    "textMetrics");
            }

            if (rowCount == 0)
            {
                return new RowFitResult(
                    new double[0], new long[0],
                    new FitContentCellPosition[0], new int[0], true);
            }

            double[] columnPrefix = new double[checked(columnCount + 1)];
            for (int column = 0; column < columnCount; column++)
            {
                double width = columnFeet[column];
                ValidatePositiveFinite(width, "columnFeet");
                columnPrefix[column + 1] = columnPrefix[column] + width;
                EnsurePositiveFinite(columnPrefix[column + 1],
                    "The column width total is too large.");
            }

            double horizontalPaddingFeet = LayoutMetrics.CellPaddingInches
                / LayoutMetrics.InchesPerFoot;
            EnsureFiniteNonnegative(horizontalPaddingFeet,
                "The horizontal padding is invalid.");

            double[] requiredFeet = new double[rowCount];
            long[] requiredLines = new long[rowCount];
            for (int row = 0; row < rowCount; row++)
            {
                requiredFeet[row] = DefaultRowFeet();
                requiredLines[row] = 1L;
            }

            List<FitContentCellPosition> tooNarrowCells =
                new List<FitContentCellPosition>();
            bool[] tooNarrowRows = new bool[rowCount];

            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                RowFitTextMetrics metrics = textMetrics[i];
                if (metrics == null)
                {
                    throw new ArgumentException(
                        "The text metrics cannot contain a null entry.",
                        "textMetrics");
                }
                ValidatePositiveFinite(metrics.TextSizeFeet,
                    "textMetrics");

                double halfUnitFeet = metrics.TextSizeFeet
                    * LayoutMetrics.RowFitHalfUnitEm;
                EnsurePositiveFinite(halfUnitFeet,
                    "The physical half-unit size is too large.");
                double spanFeet = columnPrefix[cell.Column + cell.ColumnSpan]
                    - columnPrefix[cell.Column];
                EnsurePositiveFinite(spanFeet,
                    "The cell span width is invalid.");

                double availableFeet = spanFeet - horizontalPaddingFeet;
                EnsureFinite(availableFeet,
                    "The cell span capacity is invalid.");
                double widestGlyphFeet = cell.MaxGlyphUnits * halfUnitFeet;
                EnsureFiniteNonnegative(widestGlyphFeet,
                    "The widest glyph demand is too large.");
                if (cell.MaxGlyphUnits > 0.0
                    && availableFeet < widestGlyphFeet - LayoutOps.Tolerance)
                {
                    tooNarrowCells.Add(new FitContentCellPosition(
                        cell.Row, cell.Column));
                    int rowEnd = checked(cell.Row + cell.RowSpan);
                    for (int row = cell.Row; row < rowEnd; row++)
                    {
                        tooNarrowRows[row] = true;
                    }
                }

                long fullLineCount = FitContentText.EstimateRequiredLineCount(
                    cell, spanFeet, halfUnitFeet, horizontalPaddingFeet);
                long explicitLineCount =
                    FitContentText.CountExplicitLines(cell.FullLines);
                long cellLineCount = fullLineCount;
                if (fullLineCount > explicitLineCount)
                {
                    cellLineCount = checked(cellLineCount
                        + LayoutMetrics.RowFitWrappedLineAllowance);
                }
                long distributedLineCount = CeilingDivide(
                    cellLineCount, cell.RowSpan);
                if (distributedLineCount < 1L)
                {
                    distributedLineCount = 1L;
                }
                double cellRequiredFeet = RequiredFeetForLines(
                    distributedLineCount, metrics.TextSizeFeet);
                int cellRowEnd = checked(cell.Row + cell.RowSpan);
                for (int row = cell.Row; row < cellRowEnd; row++)
                {
                    if (cellRequiredFeet > requiredFeet[row])
                    {
                        requiredFeet[row] = cellRequiredFeet;
                    }
                    if (distributedLineCount > requiredLines[row])
                    {
                        requiredLines[row] = distributedLineCount;
                    }
                }
            }

            List<int> tooNarrowRowList = new List<int>();
            for (int row = 0; row < tooNarrowRows.Length; row++)
            {
                if (tooNarrowRows[row])
                {
                    tooNarrowRowList.Add(row);
                }
            }

            return new RowFitResult(requiredFeet, requiredLines,
                tooNarrowCells, tooNarrowRowList,
                tooNarrowCells.Count == 0);
        }

        private static long CeilingDivide(long value, int divisor)
        {
            if (value < 0L || divisor <= 0)
            {
                throw new ArgumentException(
                    "The distributed line count is invalid.",
                    "value");
            }
            long quotient = value / divisor;
            return value % divisor == 0L
                ? quotient : checked(quotient + 1L);
        }

        private static void ValidatePositiveFinite(double value,
            string parameterName)
        {
            if (value <= 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(
                    "The value must be finite and positive.", parameterName);
            }
        }

        private static void EnsurePositiveFinite(double value,
            string message)
        {
            if (value <= 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }

        private static void EnsureFiniteNonnegative(double value,
            string message)
        {
            if (value < 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }

        private static void EnsureFinite(double value, string message)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }
    }
}
