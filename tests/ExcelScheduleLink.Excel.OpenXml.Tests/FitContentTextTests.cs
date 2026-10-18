using System;
using System.Collections.Generic;
using ExcelScheduleLink.Layout;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    [Collection("CultureSensitive")]
    public class FitContentTextTests
    {
        private const char FullwidthA = (char)0xFF21;
        private const char FullwidthOne = (char)0xFF11;
        private const char FullwidthComma = (char)0xFF0C;
        private const char FullwidthPeriod = (char)0xFF0E;
        private const char FullwidthM = (char)0xFF4D;
        private const char FullwidthYen = (char)0xFFE5;
        private const char CjkKan = (char)0x6F22;
        private const char HalfwidthKatakanaA = (char)0xFF71;

        public static IEnumerable<object[]> AcceptedProtectedValues
        {
            get
            {
                yield return new object[] { "0012" };
                yield return new object[] { "1,234.50" };
                yield return new object[] { "1.234,50" };
                yield return new object[] { "12.5 mm" };
                yield return new object[] { "(¥1,250)" };
                yield return new object[] { "2026/10/14" };
                yield return new object[] { "2026年10月14日" };
                yield return new object[] { "AB-123" };
                yield return new object[] { "+12" };
                yield return new object[] { "-12" };
                yield return new object[] { "12%" };
                yield return new object[] { "12.5" };
                yield return new object[] { "12,5" };
                yield return new object[]
                {
                    new string(new[]
                    {
                        FullwidthOne, FullwidthOne, FullwidthPeriod,
                        (char)0xFF15, FullwidthM, FullwidthM
                    })
                };
                yield return new object[]
                {
                    new string(new[]
                    {
                        FullwidthYen, FullwidthOne, FullwidthComma,
                        (char)0xFF12, (char)0xFF15, (char)0xFF10
                    })
                };
                yield return new object[]
                {
                    new string(new[]
                    {
                        FullwidthA, (char)0xFF22, (char)0xFF0D,
                        FullwidthOne, (char)0xFF12, (char)0xFF13
                    })
                };
            }
        }

        public static IEnumerable<object[]> RejectedProtectedValues
        {
            get
            {
                yield return new object[] { "12 mm wide" };
                yield return new object[] { "A B12" };
                yield return new object[] { "1,23,456" };
                yield return new object[] { "AB-123\n" };
                yield return new object[] { "Description" };
                yield return new object[] { "No." };
                yield return new object[] { "ABC" };
                yield return new object[] { "2026/10-14" };
                yield return new object[] { "1\t234" };
                yield return new object[] { null };
                yield return new object[] { string.Empty };
                yield return new object[] { "A" + new string('1', 32) };
                yield return new object[]
                {
                    new string(FullwidthOne, 65)
                };
            }
        }

        public static IEnumerable<object[]> ProtectionThresholdCases
        {
            get
            {
                yield return new object[]
                {
                    new[] { "12", "13", "Description", "14" }, 0, true
                };
                yield return new object[]
                {
                    new[] { "12", "13", "Description" }, 0, false
                };
                yield return new object[]
                {
                    new[] { "12" }, 0, false
                };
                yield return new object[]
                {
                    new[] { "12" }, 1, false
                };
            }
        }

        [Theory]
        [MemberData(nameof(AcceptedProtectedValues))]
        public void ProtectedValue_AcceptsSpecifiedNumericDateAndCodeForms(
            string value)
        {
            Assert.True(FitContentText.IsProtectedValue(value), value);
        }

        [Theory]
        [MemberData(nameof(RejectedProtectedValues))]
        public void ProtectedValue_RejectsProseMultilineAndMalformedForms(
            string value)
        {
            Assert.False(FitContentText.IsProtectedValue(value), value);
        }

        [Theory]
        [MemberData(nameof(ProtectionThresholdCases))]
        public void Protection_UsesExactlySeventyFivePercentAndMinimumSamples(
            string[] values, int headingRowCount, bool expected)
        {
            FitContentCell[] cells = new FitContentCell[values.Length];
            for (int row = 0; row < values.Length; row++)
            {
                cells[row] = new FitContentCell(row, 0, 1, 1, values[row]);
            }

            FitContentTable table = new FitContentTable(
                values.Length, 1, cells);
            FitContentMeasurements measurements = FitContentText.Prepare(
                table, 1.0, headingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            Assert.Equal(expected, measurements.ProtectedColumns[0]);
        }

        [Fact]
        public void Protection_ExcludesOnlyConfiguredHeadingRowsAndMergedCells()
        {
            FitContentCell[] cells =
            {
                new FitContentCell(0, 0, 1, 1, "12"),
                new FitContentCell(0, 1, 1, 1, "Header"),
                new FitContentCell(0, 2, 1, 1, "Header"),
                new FitContentCell(0, 3, 1, 1, "12"),
                new FitContentCell(1, 0, 2, 1, "12"),
                new FitContentCell(1, 1, 1, 2, "12"),
                new FitContentCell(1, 3, 1, 1, "12"),
                new FitContentCell(2, 1, 1, 1, "12"),
                new FitContentCell(2, 2, 1, 1, "12"),
                new FitContentCell(2, 3, 1, 1, "12"),
                new FitContentCell(3, 0, 1, 1, "12"),
                new FitContentCell(3, 1, 1, 1, "12"),
                new FitContentCell(3, 2, 1, 1, "12"),
                new FitContentCell(3, 3, 1, 1, "Description"),
                new FitContentCell(4, 0, 1, 1, "Description"),
                new FitContentCell(4, 1, 1, 1, "Description"),
                new FitContentCell(4, 2, 1, 1, "Description"),
                new FitContentCell(4, 3, 2, 1, "12"),
                new FitContentCell(5, 0, 1, 1, "12"),
                new FitContentCell(5, 1, 1, 2, "12")
            };

            FitContentMeasurements measurements = FitContentText.Prepare(
                new FitContentTable(6, 4, cells), 1.0, 1,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);

            Assert.Equal(4, measurements.ProtectedColumns.Count);
            for (int column = 0; column < measurements.ProtectedColumns.Count;
                column++)
            {
                Assert.False(measurements.ProtectedColumns[column]);
            }
        }

        [Fact]
        public void Prepare_PreservesOriginalTextAndInputCollections()
        {
            string originalText = " １２ ";
            FitContentCell first = new FitContentCell(
                1, 0, 1, 1, originalText);
            FitContentCell second = new FitContentCell(
                0, 0, 1, 1, "AB-123");
            FitContentCell[] input = { first, second };
            FitContentTable table = new FitContentTable(2, 1, input);

            FitContentMeasurements forward = FitContentText.Prepare(
                table, 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            input[0] = new FitContentCell(9, 9, 1, 1, "changed");

            FitContentMeasurements reverse = FitContentText.Prepare(
                new FitContentTable(2, 1, new[] { second, first }),
                1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);

            Assert.Equal(originalText, first.Text);
            Assert.Same(first, table.Cells[0]);
            Assert.Same(second, table.Cells[1]);
            Assert.Equal(2, forward.Cells.Count);
            Assert.Equal(2, reverse.Cells.Count);
            Assert.Equal(forward.Cells[0].Row, reverse.Cells[0].Row);
            Assert.Equal(forward.Cells[0].Column, reverse.Cells[0].Column);
            Assert.Equal(forward.Cells[0].IsProtectedValue, reverse.Cells[0].IsProtectedValue);
            Assert.Equal(forward.Cells[0].LongestLineUnits,
                reverse.Cells[0].LongestLineUnits, 12);
            Assert.Equal(forward.Cells[1].IsProtectedValue, reverse.Cells[1].IsProtectedValue);
            Assert.Equal(forward.Cells[1].LongestLineUnits,
                reverse.Cells[1].LongestLineUnits, 12);
            Assert.Equal(forward.TextWeights[0], reverse.TextWeights[0], 12);
        }

        [Fact]
        public void Lines_PreservesExplicitBreaksAndCountsCjkUnits()
        {
            string breaks = "A\r\nB\rC\n";
            string mixed = new string(new[]
            {
                HalfwidthKatakanaA, (char)0xFF21, ' '
            });
            FitContentMeasurements measurements = FitContentText.Prepare(
                new FitContentTable(3, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, string.Empty),
                    new FitContentCell(1, 0, 1, 1, breaks),
                    new FitContentCell(2, 0, 1, 1, mixed)
                }), 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);

            AssertBins(measurements.Cells[0].Lines,
                new[] { 0 }, new[] { 1 });
            AssertBins(measurements.Cells[1].Lines,
                new[] { 0, 2 }, new[] { 1, 3 });
            AssertBins(measurements.Cells[2].Lines,
                new[] { 7 }, new[] { 1 });
            Assert.Equal(3.5, measurements.Cells[2].LongestLineUnits, 12);
        }

        [Fact]
        public void Lines_CapsEstimatesButNotProtectedValueDemand()
        {
            string longNumber = new string('9', 64);
            FitContentMeasurements measurements = FitContentText.Prepare(
                new FitContentTable(2, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, longNumber),
                    new FitContentCell(1, 0, 1, 1, "12")
                }), 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            FitContentMeasuredCell cell = measurements.Cells[0];

            Assert.True(cell.MeasurementCapped);
            Assert.Equal(120, cell.Lines[0].HalfUnits);
            Assert.Equal(128, cell.FullLines[0].HalfUnits);
            Assert.True(cell.IsProtectedValue);
            double expectedDemand = (64.0 * LayoutMetrics.HalfWidthInchesPerUnit
                + LayoutMetrics.CellPaddingInches)
                / LayoutMetrics.InchesPerFoot;
            Assert.Equal(expectedDemand,
                measurements.ProtectionRequiredFeet[0], 12);
            Assert.True(measurements.ProtectionRequiredFeet[0]
                > cell.PreferredFeet);
        }

        [Fact]
        public void FullLines_PreservesUncappedHistogramAndMaximumGlyph()
        {
            string text = new string('A', 61) + "\n\n" +
                new string(CjkKan, 1);
            FitContentMeasuredCell cell = FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, text)
                }), 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet)
                .Cells[0];

            AssertBins(cell.Lines,
                new[] { 0, 4, 120 }, new[] { 1, 1, 1 });
            AssertBins(cell.FullLines,
                new[] { 0, 4, 122 }, new[] { 1, 1, 1 });
            Assert.Equal(2.0, cell.MaxGlyphUnits, 12);
            Assert.Equal(61.0, cell.LongestLineUnits, 12);

            IList<FitContentLineBin> lines =
                (IList<FitContentLineBin>)cell.Lines;
            IList<FitContentLineBin> fullLines =
                (IList<FitContentLineBin>)cell.FullLines;
            Assert.Throws<NotSupportedException>(() => lines.Clear());
            Assert.Throws<NotSupportedException>(() => fullLines.Clear());
        }

        [Fact]
        public void FullLines_UsesSameKernelWithPhysicalMetrics()
        {
            FitContentMeasuredCell cell = FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, new string('A', 61))
                }), 1.0, 0, LayoutMetrics.MinColumnFeet,
                LayoutMetrics.MaxColumnFeet).Cells[0];

            Assert.Equal(1L, FitContentText.EstimateLineCount(
                cell, 0.46, 1.0));
            Assert.Equal(2L, FitContentText.EstimateRequiredLineCount(
                cell, 0.37, 0.006, 0.01));
        }

        [Fact]
        public void FullLines_DoesNotUseCappedOneLineShortcut()
        {
            FitContentMeasuredCell cell = FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, new string('A', 120))
                }), 1.0, 0, LayoutMetrics.MinColumnFeet,
                LayoutMetrics.MaxColumnFeet).Cells[0];

            Assert.Equal(1L, FitContentText.EstimateLineCount(
                cell, 0.46, 1.0));
            Assert.Equal(2L, FitContentText.EstimateRequiredLineCount(
                cell, 0.43, 0.006, 0.01));
        }

        [Fact]
        public void Lines_UsesPaddingScaleAndExactBoundary()
        {
            FitContentTable table = new FitContentTable(1, 1,
                new[] { new FitContentCell(0, 0, 1, 1, "ABCD") });
            FitContentMeasuredCell scaleOne = FitContentText.Prepare(
                table, 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet)
                .Cells[0];
            FitContentMeasuredCell scaleOneHalf = FitContentText.Prepare(
                table, 1.5, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet)
                .Cells[0];
            FitContentMeasuredCell scaleTwo = FitContentText.Prepare(
                table, 2.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet)
                .Cells[0];

            Assert.Equal(1L, FitContentText.EstimateLineCount(
                scaleOne, scaleOne.PreferredFeet, 1.0));
            Assert.Equal(2L, FitContentText.EstimateLineCount(
                scaleOne, scaleOne.PreferredFeet - 0.001, 1.0));
            Assert.True(scaleOneHalf.PreferredFeet > scaleOne.PreferredFeet);
            Assert.True(scaleTwo.PreferredFeet > scaleOneHalf.PreferredFeet);
            Assert.Equal(1L, FitContentText.EstimateLineCount(
                scaleOneHalf, scaleOneHalf.PreferredFeet, 1.5));
            Assert.Equal(1L, FitContentText.EstimateLineCount(
                scaleTwo, scaleTwo.PreferredFeet, 2.0));
            Assert.Equal(2L, FitContentText.EstimateLineCount(
                scaleTwo, scaleOneHalf.PreferredFeet, 2.0));
        }

        [Fact]
        public void Prepare_RejectsMalformedGeometryScaleAndBounds()
        {
            Assert.Throws<ArgumentNullException>(() => FitContentText.Prepare(
                null, 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet));
            Assert.Throws<ArgumentNullException>(() => new FitContentTable(
                1, 1, null));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(-1, 1, new FitContentCell[0]),
                1.0, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new FitContentCell[0]),
                1.0, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1"),
                    new FitContentCell(0, 0, 1, 1, "2")
                }), 1.0, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(1, 0, 1, 1, "1")
                }), 1.0, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1")
                }), double.NaN, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1")
                }), double.PositiveInfinity, 0, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1")
                }), 1.0, -1, 0.1, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1")
                }), 1.0, 0, double.NaN, 2.0));
            Assert.Throws<ArgumentException>(() => FitContentText.Prepare(
                new FitContentTable(1, 1, new[]
                {
                    new FitContentCell(0, 0, 1, 1, "1")
                }), 1.0, 0, 2.0, 0.1));
        }

        [Fact]
        public void Prepare_SupportsZeroAndFiveHundredByFiftyTables()
        {
            FitContentMeasurements empty = FitContentText.Prepare(
                new FitContentTable(0, 0, new FitContentCell[0]),
                1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            Assert.Equal(0, empty.RowCount);
            Assert.Equal(0, empty.ColumnCount);
            Assert.Empty(empty.Cells);
            Assert.Empty(empty.ProtectedColumns);

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
                new FitContentTable(500, 50, cells),
                1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            Assert.Equal(500, largest.RowCount);
            Assert.Equal(50, largest.ColumnCount);
            Assert.Equal(25000, largest.Cells.Count);
            Assert.Equal(50, largest.PreferredColumnFeet.Count);

            FitContentTable mergedTable = new FitContentTable(3, 3, new[]
            {
                new FitContentCell(2, 0, 1, 2, "AB"),
                new FitContentCell(0, 2, 3, 1, "ABCDEFGHIJ"),
                new FitContentCell(0, 0, 2, 2, "ABCD")
            });
            FitContentMeasurements merged = FitContentText.Prepare(
                mergedTable, 1.0, 0, 0.01, 2.0);
            Assert.Equal(3, merged.Cells.Count);
            Assert.Equal(0, merged.Cells[0].Row);
            Assert.Equal(2, merged.Cells[0].ColumnSpan);
            Assert.Equal(0, merged.Cells[1].Row);
            Assert.Equal(3, merged.Cells[1].RowSpan);
            Assert.Equal(2, merged.Cells[2].Row);
            Assert.Equal(2, merged.Cells[2].ColumnSpan);

            // Each anchor's weight counts once, split over its columns,
            // regardless of how many rows it spans.
            Assert.Equal(3.0, merged.TextWeights[0], 12);
            Assert.Equal(3.0, merged.TextWeights[1], 12);
            Assert.Equal(10.0, merged.TextWeights[2], 12);

            // Horizontally merged text never raises an ordinary column's
            // preference; a vertical-only anchor does.
            double tenUnitFeet = (10.0 * LayoutMetrics.HalfWidthInchesPerUnit
                + LayoutMetrics.CellPaddingInches)
                / LayoutMetrics.InchesPerFoot;
            Assert.Equal(0.01, merged.PreferredColumnFeet[0], 12);
            Assert.Equal(0.01, merged.PreferredColumnFeet[1], 12);
            Assert.Equal(tenUnitFeet, merged.PreferredColumnFeet[2], 12);

            // The column preference is bounded; the cell keeps its demand.
            FitContentMeasurements bounded = FitContentText.Prepare(
                mergedTable, 1.0, 0, 0.01, 0.05);
            Assert.Equal(0.05, bounded.PreferredColumnFeet[2], 12);
            Assert.Equal(tenUnitFeet, bounded.Cells[1].PreferredFeet, 12);
        }

        [Fact]
        public void GetColumnHeadings_UsesEarliestUnmergedNormalizedAndBoundedText()
        {
            string longHeading = new string('A', 30) + "\U0001F600B";
            FitContentTable table = new FitContentTable(3, 4, new[]
            {
                new FitContentCell(1, 2, 1, 1, longHeading),
                new FitContentCell(2, 3, 1, 1, "later C"),
                new FitContentCell(1, 0, 2, 1, "Vertical later"),
                new FitContentCell(0, 1, 1, 2, "Merged title"),
                new FitContentCell(0, 3, 1, 1, "C\tthree"),
                new FitContentCell(2, 1, 1, 1, string.Empty),
                new FitContentCell(1, 3, 1, 1, "data"),
                new FitContentCell(0, 0, 1, 1,
                    "\t First \t Heading \r\n second"),
                new FitContentCell(1, 1, 1, 1, "B \t label"),
                new FitContentCell(2, 2, 1, 1, "later")
            });

            FitContentText.Prepare(table, 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);

            string[] headings = FitContentText.GetColumnHeadings(table);

            Assert.Equal("First Heading", headings[0]);
            Assert.Equal("B label", headings[1]);
            Assert.Equal(new string('A', 30) + "\u2026", headings[2]);
            Assert.Equal(31, headings[2].Length);
            Assert.Equal("C three", headings[3]);
        }

        [Fact]
        public void GetColumnHeadings_DoesNotUseHorizontalMergeAnchors()
        {
            FitContentTable table = new FitContentTable(1, 2,
                new[] { new FitContentCell(0, 0, 1, 2, "Merged title") });

            FitContentText.Prepare(table, 1.0, 0,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);

            Assert.Equal(new[] { string.Empty, string.Empty },
                FitContentText.GetColumnHeadings(table));
        }

        [Fact]
        public void ColumnHeadings_SkipsHorizontalTitleAndUsesTopmostAnchor()
        {
            FitContentCell[] forward =
            {
                new FitContentCell(0, 0, 1, 1, string.Empty),
                new FitContentCell(0, 1, 1, 2, "Merged title"),
                new FitContentCell(1, 0, 1, 1, "Description"),
                new FitContentCell(1, 1, 1, 2, "Merged data"),
                new FitContentCell(2, 0, 1, 1, "later data"),
                new FitContentCell(2, 1, 1, 2, "Merged later")
            };
            FitContentCell[] reverse = (FitContentCell[])forward.Clone();
            Array.Reverse(reverse);

            string[] first = GetPreparedHeadings(
                new FitContentTable(3, 3, forward));
            string[] second = GetPreparedHeadings(
                new FitContentTable(3, 3, reverse));

            Assert.Equal(new[] { "Description", string.Empty, string.Empty }, first);
            Assert.Equal(first, second);
        }

        [Fact]
        public void ColumnHeadings_AcceptsVerticalMerge()
        {
            FitContentTable table = new FitContentTable(4, 1, new[]
            {
                new FitContentCell(0, 0, 1, 1, string.Empty),
                new FitContentCell(1, 0, 3, 1, "Vertical label")
            });

            string[] headings = GetPreparedHeadings(table);

            Assert.Equal(new[] { "Vertical label" }, headings);
        }

        [Fact]
        public void ColumnHeadings_UsesFirstNonblankExplicitLineAndCollapsesWhitespace()
        {
            FitContentTable table = new FitContentTable(2, 1, new[]
            {
                new FitContentCell(0, 0, 1, 1, " \r\n\t\u3000\rCa\uFF21\u3000 Label\t \r\nSecond"),
                new FitContentCell(1, 0, 1, 1, "later")
            });

            string[] headings = GetPreparedHeadings(table);

            Assert.Equal("CaＡ Label", headings[0]);
        }

        [Fact]
        public void ColumnHeadings_BoundsLengthWithoutSplittingSurrogatePair()
        {
            FitContentTable table = new FitContentTable(1, 4, new[]
            {
                new FitContentCell(0, 0, 1, 1, new string('A', 32)),
                new FitContentCell(0, 1, 1, 1, new string('B', 40)),
                new FitContentCell(0, 2, 1, 1, new string(CjkKan, 40)),
                new FitContentCell(0, 3, 1, 1,
                    new string('C', 30) + "\U0001F600tail")
            });

            string[] headings = GetPreparedHeadings(table);

            Assert.Equal(new string('A', 32), headings[0]);
            Assert.Equal(new string('B', 31) + "\u2026", headings[1]);
            Assert.Equal(new string(CjkKan, 31) + "\u2026", headings[2]);
            Assert.Equal(new string('C', 30) + "\u2026", headings[3]);
            Assert.Equal(32, headings[1].Length);
            Assert.Equal(32, headings[2].Length);
            Assert.Equal(31, headings[3].Length);
        }

        [Fact]
        public void ColumnHeadings_EmptyAndNullContracts()
        {
            FitContentTable blank = new FitContentTable(2, 2, new[]
            {
                new FitContentCell(0, 0, 1, 1, string.Empty),
                new FitContentCell(0, 1, 1, 1, "\r\n"),
                new FitContentCell(1, 0, 1, 1, "\t"),
                new FitContentCell(1, 1, 1, 1, string.Empty)
            });

            Assert.Equal(new[] { string.Empty, string.Empty },
                GetPreparedHeadings(blank));
            Assert.Empty(GetPreparedHeadings(
                new FitContentTable(0, 0, new FitContentCell[0])));
            Assert.Throws<ArgumentNullException>(
                delegate { FitContentText.GetColumnHeadings(null); });
        }

        private static string[] GetPreparedHeadings(FitContentTable table)
        {
            FitContentText.Prepare(table, 1.0,
                LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            return FitContentText.GetColumnHeadings(table);
        }

        private static void AssertBins(IReadOnlyList<FitContentLineBin> bins,
            int[] expectedHalfUnits, int[] expectedCounts)
        {
            Assert.Equal(expectedHalfUnits.Length, bins.Count);
            Assert.Equal(expectedCounts.Length, bins.Count);
            for (int i = 0; i < bins.Count; i++)
            {
                Assert.Equal(expectedHalfUnits[i], bins[i].HalfUnits);
                Assert.Equal(expectedCounts[i], bins[i].Count);
            }
        }
    }
}
