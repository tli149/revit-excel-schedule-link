using System;
using System.Collections.Generic;
using ExcelScheduleLink.Layout;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class RowHeightMathTests
    {
        [Fact]
        public void RequiredHeight_UsesOneLineMinimumPitchAndPadding()
        {
            Assert.Equal(0.0208333333333333,
                RowHeightMath.RequiredFeetForLines(0, 0.01), 12);
            Assert.Equal(0.0208333333333333,
                RowHeightMath.RequiredFeetForLines(1, 0.01), 12);
            Assert.Equal(0.0303333333333333,
                RowHeightMath.RequiredFeetForLines(2, 0.01), 12);
            Assert.Equal(0.0438333333333333,
                RowHeightMath.RequiredFeetForLines(3, 0.01), 12);
            Assert.Equal(0.0433333333333333,
                RowHeightMath.RequiredFeetForLines(2, 0.01, 2.0), 12);

            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(-1, 0.01));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, 0.0));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, -0.01));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, double.NaN));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, 0.01, -1.0));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, 0.01, double.NaN));
            Assert.Throws<ArgumentException>(() =>
                RowHeightMath.RequiredFeetForLines(1, 0.01, double.PositiveInfinity));
        }

        [Fact]
        public void FitRows_UsesFullLinesBeyondWidthEstimateCap()
        {
            FitContentMeasurements measurements = Prepare(1, 1,
                new FitContentCell(0, 0, 1, 1, new string('A', 61)));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.37 },
                new[] { new RowFitTextMetrics(0.01) });

            Assert.Equal(3L, result.RequiredLines[0]);
            Assert.Equal(0.0438333333333333, result.RequiredFeet[0], 12);
            Assert.True(result.CanFit);
        }

        [Fact]
        public void FitRows_UsesMaximumHeightNotMaximumLineCount()
        {
            FitContentMeasurements measurements = Prepare(1, 2,
                new FitContentCell(0, 0, 1, 1, "A\nB\nC"),
                new FitContentCell(0, 1, 1, 1, "A\nB"));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.30, 0.30 },
                new[] { new RowFitTextMetrics(0.005),
                    new RowFitTextMetrics(0.02) });

            Assert.Equal(3L, result.RequiredLines[0]);
            Assert.Equal(0.0573333333333333, result.RequiredFeet[0], 12);
        }

        [Fact]
        public void FitRows_HorizontalMergeUsesOneSpanAndOnePadding()
        {
            FitContentMeasurements measurements = Prepare(1, 2,
                new FitContentCell(0, 0, 1, 2, new string('A', 30)));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.095, 0.095 },
                new[] { new RowFitTextMetrics(0.01) });

            Assert.Equal(1L, result.RequiredLines[0]);
            Assert.Equal(0.0208333333333333, result.RequiredFeet[0], 12);
            Assert.True(result.CanFit);
        }

        [Fact]
        public void FitRows_VerticalMergeDistributesWithCeiling()
        {
            FitContentMeasurements measurements = Prepare(3, 1,
                new FitContentCell(0, 0, 3, 1, "A\nB\nC\nD\nE"));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.50 },
                new[] { new RowFitTextMetrics(0.01) });

            for (int row = 0; row < 3; row++)
            {
                Assert.Equal(2L, result.RequiredLines[row]);
                Assert.Equal(0.0303333333333333,
                    result.RequiredFeet[row], 12);
            }
        }

        [Fact]
        public void FitRows_PreservesExplicitAndBlankLines()
        {
            FitContentMeasurements measurements = Prepare(3, 1,
                new FitContentCell(0, 0, 1, 1, string.Empty),
                new FitContentCell(1, 0, 1, 1, "A\n\n"),
                new FitContentCell(2, 0, 1, 1, "A\r\nB"));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.50 },
                new[] { new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.01) });

            Assert.Equal(1L, result.RequiredLines[0]);
            Assert.Equal(3L, result.RequiredLines[1]);
            Assert.Equal(2L, result.RequiredLines[2]);
            Assert.Equal(0.0208333333333333,
                result.RequiredFeet[0], 12);
            Assert.Equal(0.0438333333333333,
                result.RequiredFeet[1], 12);
            Assert.Equal(0.0303333333333333,
                result.RequiredFeet[2], 12);
        }

        [Fact]
        public void FitRows_AddsSoftWrapAllowanceOncePerAnchor()
        {
            // Capacity is 60 units: (0.37 - 0.01) / (0.01 * 0.60).
            // Two 61-unit explicit lines wrap to four estimated lines; one
            // allowance for the anchor gives five, not one per wrapped line.
            string text = new string('A', 61) + "\n" + new string('A', 61);
            FitContentMeasurements measurements = Prepare(1, 1,
                new FitContentCell(0, 0, 1, 1, text));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.37 },
                new[] { new RowFitTextMetrics(0.01) });

            Assert.Equal(5L, result.RequiredLines[0]);
            Assert.Equal(0.0708333333333333, result.RequiredFeet[0], 12);
        }

        [Fact]
        public void FitRows_ReportsGlyphWidthShortageWithoutClampingWidths()
        {
            FitContentMeasurements measurements = Prepare(1, 1,
                new FitContentCell(0, 0, 1, 1, "A"));
            double[] widths = { 0.01 };

            RowFitResult result = RowHeightMath.FitRows(
                measurements, widths,
                new[] { new RowFitTextMetrics(0.10) });

            Assert.False(result.CanFit);
            Assert.Single(result.TooNarrowCells);
            Assert.Equal(0, result.TooNarrowCells[0].Row);
            Assert.Equal(0, result.TooNarrowCells[0].Column);
            Assert.Equal(0.01, widths[0], 12);
            Assert.Equal(0.4083333333333333,
                result.RequiredFeet[0], 12);
            Assert.Equal(3L, result.RequiredLines[0]);
        }

        [Fact]
        public void FitRows_TooNarrowRowsAreUniqueAcrossMergedAndOrdinaryCells()
        {
            FitContentMeasurements measurements = Prepare(3, 2,
                new FitContentCell(0, 0, 3, 1, "A"),
                new FitContentCell(0, 1, 1, 1, string.Empty),
                new FitContentCell(1, 1, 1, 1, "A"),
                new FitContentCell(2, 1, 1, 1, string.Empty));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.01, 0.01 },
                new[] { new RowFitTextMetrics(0.10),
                    new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.10),
                    new RowFitTextMetrics(0.01) });

            Assert.False(result.CanFit);
            Assert.Equal(2, result.TooNarrowCells.Count);
            Assert.Equal(0, result.TooNarrowCells[0].Row);
            Assert.Equal(0, result.TooNarrowCells[0].Column);
            Assert.Equal(1, result.TooNarrowCells[1].Row);
            Assert.Equal(1, result.TooNarrowCells[1].Column);
            Assert.Equal(3, result.TooNarrowRows.Count);
            Assert.Equal(0, result.TooNarrowRows[0]);
            Assert.Equal(1, result.TooNarrowRows[1]);
            Assert.Equal(2, result.TooNarrowRows[2]);
        }

        [Fact]
        public void FitRows_TooNarrowDiagnosticsKeepCompleteHeightVector()
        {
            FitContentMeasurements measurements = Prepare(2, 2,
                new FitContentCell(0, 0, 1, 1, "A"),
                new FitContentCell(0, 1, 1, 1, string.Empty),
                new FitContentCell(1, 0, 1, 1, string.Empty),
                new FitContentCell(1, 1, 1, 1, "A\nB"));

            RowFitResult result = RowHeightMath.FitRows(
                measurements, new[] { 0.01, 0.50 },
                new[] { new RowFitTextMetrics(0.10),
                    new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.02) });

            Assert.False(result.CanFit);
            Assert.Equal(2, result.RequiredFeet.Count);
            Assert.Equal(0.4083333333333333,
                result.RequiredFeet[0], 12);
            Assert.Equal(0.0573333333333333,
                result.RequiredFeet[1], 12);
            Assert.True(result.RequiredFeet[0] > 0.0);
            Assert.True(result.RequiredFeet[1] > 0.0);
        }

        [Fact]
        public void FitRows_DoesNotTreatWidthScaleAsFontSize()
        {
            FitContentTable table = new FitContentTable(1, 1,
                new[] { new FitContentCell(0, 0, 1, 1, "A\nB\nC") });
            FitContentMeasurements scaleOne = FitContentText.Prepare(
                table, 1.5, 1, LayoutMetrics.MinColumnFeet,
                LayoutMetrics.MaxColumnFeet);
            FitContentMeasurements scaleTwo = FitContentText.Prepare(
                table, 2.0, 1, LayoutMetrics.MinColumnFeet,
                LayoutMetrics.MaxColumnFeet);

            RowFitResult first = RowHeightMath.FitRows(scaleOne,
                new[] { 0.30 }, new[] { new RowFitTextMetrics(0.01) });
            RowFitResult second = RowHeightMath.FitRows(scaleTwo,
                new[] { 0.30 }, new[] { new RowFitTextMetrics(0.01) });

            Assert.Equal(first.RequiredLines[0], second.RequiredLines[0]);
            Assert.Equal(first.RequiredFeet[0], second.RequiredFeet[0], 12);
        }

        [Fact]
        public void FitRows_RejectsMalformedWidthsMetricsAndOverflow()
        {
            FitContentMeasurements one = Prepare(1, 1,
                new FitContentCell(0, 0, 1, 1, "A"));
            RowFitTextMetrics[] validMetrics =
                { new RowFitTextMetrics(0.01) };

            Assert.Throws<ArgumentNullException>(() => RowHeightMath.FitRows(
                one, null, validMetrics));
            Assert.Throws<ArgumentNullException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 }, null));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1, 0.1 }, validMetrics));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 }, new RowFitTextMetrics[0]));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.0 }, validMetrics));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { double.NaN }, validMetrics));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { double.PositiveInfinity }, validMetrics));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 }, new RowFitTextMetrics[] { null }));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 },
                new[] { new RowFitTextMetrics(0.0) }));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 },
                new[] { new RowFitTextMetrics(double.NaN) }));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 },
                new[] { new RowFitTextMetrics(double.PositiveInfinity) }));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                one, new[] { 0.1 },
                new[] { new RowFitTextMetrics(double.MaxValue) }));

            FitContentMeasurements two = Prepare(1, 2,
                new FitContentCell(0, 0, 1, 1, string.Empty),
                new FitContentCell(0, 1, 1, 1, string.Empty));
            Assert.Throws<ArgumentException>(() => RowHeightMath.FitRows(
                two, new[] { double.MaxValue, double.MaxValue },
                new[] { new RowFitTextMetrics(0.01),
                    new RowFitTextMetrics(0.01) }));
        }

        [Fact]
        public void FitRows_IsDeterministicAndDoesNotMutateInputs()
        {
            FitContentCell first = new FitContentCell(1, 0, 1, 1, "A\nB");
            FitContentCell second = new FitContentCell(0, 0, 1, 1, "A");
            FitContentMeasurements measurements = Prepare(2, 1, first, second);
            double[] widths = { 0.30 };
            RowFitTextMetrics[] metrics = { new RowFitTextMetrics(0.01),
                new RowFitTextMetrics(0.01) };

            RowFitResult firstResult = RowHeightMath.FitRows(
                measurements, widths, metrics);
            RowFitResult secondResult = RowHeightMath.FitRows(
                measurements, widths, metrics);

            Assert.Equal(firstResult.RequiredLines[0],
                secondResult.RequiredLines[0]);
            Assert.Equal(firstResult.RequiredLines[1],
                secondResult.RequiredLines[1]);
            Assert.Equal(firstResult.RequiredFeet[0],
                secondResult.RequiredFeet[0], 12);
            Assert.Equal(firstResult.RequiredFeet[1],
                secondResult.RequiredFeet[1], 12);
            Assert.Equal(0.30, widths[0], 12);
            Assert.Equal(0.01, metrics[0].TextSizeFeet, 12);
            Assert.Equal(0.01, metrics[1].TextSizeFeet, 12);

            IList<double> requiredFeet =
                (IList<double>)firstResult.RequiredFeet;
            Assert.Throws<NotSupportedException>(() => requiredFeet.Clear());
            IList<long> requiredLines =
                (IList<long>)firstResult.RequiredLines;
            Assert.Throws<NotSupportedException>(() => requiredLines.Clear());
        }

        [Fact]
        public void FitRows_HandlesEmptyAndFiveHundredByFifty()
        {
            FitContentMeasurements empty = FitContentText.Prepare(
                new FitContentTable(0, 0, new FitContentCell[0]),
                1.0, 1, LayoutMetrics.MinColumnFeet,
                LayoutMetrics.MaxColumnFeet);
            RowFitResult emptyResult = RowHeightMath.FitRows(
                empty, new double[0], new RowFitTextMetrics[0]);
            Assert.Empty(emptyResult.RequiredFeet);
            Assert.Empty(emptyResult.RequiredLines);
            Assert.True(emptyResult.CanFit);

            List<FitContentCell> cells =
                new List<FitContentCell>(500 * 50);
            for (int row = 0; row < 500; row++)
            {
                for (int column = 0; column < 50; column++)
                {
                    cells.Add(new FitContentCell(
                        row, column, 1, 1, string.Empty));
                }
            }
            FitContentMeasurements largest = FitContentText.Prepare(
                new FitContentTable(500, 50, cells), 1.0, 1,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            RowFitTextMetrics[] metrics =
                new RowFitTextMetrics[largest.Cells.Count];
            for (int i = 0; i < metrics.Length; i++)
            {
                metrics[i] = new RowFitTextMetrics(0.01);
            }

            double[] widths = new double[50];
            for (int column = 0; column < widths.Length; column++)
            {
                widths[column] = 0.10;
            }
            RowFitResult largestResult = RowHeightMath.FitRows(
                largest, widths, metrics);
            Assert.Equal(500, largestResult.RequiredFeet.Count);
            Assert.Equal(500, largestResult.RequiredLines.Count);
            Assert.Equal(0.0208333333333333,
                largestResult.RequiredFeet[0], 12);
            Assert.Equal(0.0208333333333333,
                largestResult.RequiredFeet[499], 12);
            Assert.Equal(1L, largestResult.RequiredLines[0]);
            Assert.Empty(largestResult.TooNarrowRows);
            Assert.True(largestResult.CanFit);
        }

        private static FitContentMeasurements Prepare(int rows, int columns,
            params FitContentCell[] cells)
        {
            return FitContentText.Prepare(
                new FitContentTable(rows, columns, cells), 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
        }
    }
}
