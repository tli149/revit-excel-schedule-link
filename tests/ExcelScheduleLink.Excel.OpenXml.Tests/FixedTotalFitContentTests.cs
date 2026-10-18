using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExcelScheduleLink.Layout;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    [Collection("CultureSensitive")]
    public class FixedTotalFitContentTests
    {
        private static double Tolerance => LayoutOps.Tolerance;

        [Fact]
        public void Boundaries_RejectInvalidAndHandleZeroColumns()
        {
            FitContentMeasurements measurements = PrepareBlank(1, 1);
            double[] invalidTotals =
            {
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity,
                -0.01
            };

            foreach (double total in invalidTotals)
            {
                FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                    measurements, total);
                Assert.Equal(FitContentStage.InvalidInput, result.Stage);
                Assert.Empty(result.Feet);
                Assert.False(result.CanApply);
                Assert.False(result.TargetPreserved);
                Assert.Equal(0.0, result.ActualTotalFeet, 12);
                Assert.Equal(0, result.OptimizationTrials);
            }

            FitContentMeasurements empty = FitContentText.Prepare(
                new FitContentTable(0, 0,
                    new ReadOnlyCollection<FitContentCell>(
                        new List<FitContentCell>())),
                1.0, 0, 0.1, 2.0);
            FixedTotalFitResult zero = LayoutOps.FitContentToTotal(empty, 0.0);
            Assert.Equal(FitContentStage.Comfortable, zero.Stage);
            Assert.Equal(FitContentLimitReason.None, zero.LimitReason);
            Assert.Empty(zero.Feet);
            Assert.False(zero.CanApply);
            Assert.True(zero.TargetPreserved);
            Assert.Equal(0.0, zero.ActualTotalFeet, 12);

            FixedTotalFitResult positive = LayoutOps.FitContentToTotal(
                empty, 0.1);
            Assert.Equal(FitContentStage.OutOfRange, positive.Stage);
            Assert.Equal(FitContentLimitReason.NoColumns,
                positive.LimitReason);
            Assert.Empty(positive.Feet);
            Assert.False(positive.CanApply);
            Assert.False(positive.TargetPreserved);

            Assert.Throws<ArgumentNullException>(() =>
                LayoutOps.FitContentToTotal(null, 0.0));
        }

        [Fact]
        public void Boundaries_UseStrictMinMaxAndPreserveExactEndpoints()
        {
            double min = 0.1;
            double max = 2.0;
            FitContentMeasurements measurements = PrepareBlank(
                1, 2, 1.0, 0, min, max);
            double minimumTotal = 2.0 * min;
            double maximumTotal = 2.0 * max;

            FixedTotalFitResult below = LayoutOps.FitContentToTotal(
                measurements, minimumTotal - 0.5 * Tolerance);
            Assert.Equal(FitContentStage.OutOfRange, below.Stage);
            Assert.Equal(FitContentLimitReason.BelowMinimum,
                below.LimitReason);
            Assert.All(below.Feet, value => Assert.Equal(min, value, 12));
            Assert.False(below.TargetPreserved);

            FixedTotalFitResult atMinimum = LayoutOps.FitContentToTotal(
                measurements, minimumTotal);
            Assert.Equal(FitContentStage.Comfortable, atMinimum.Stage);
            Assert.True(atMinimum.TargetPreserved);
            Assert.Equal(minimumTotal, atMinimum.ActualTotalFeet, 12);
            Assert.All(atMinimum.Feet, value => Assert.Equal(min, value, 12));

            FixedTotalFitResult zero = LayoutOps.FitContentToTotal(
                measurements, 0.0);
            Assert.Equal(FitContentStage.OutOfRange, zero.Stage);
            Assert.Equal(FitContentLimitReason.BelowMinimum,
                zero.LimitReason);
            Assert.False(zero.TargetPreserved);

            FixedTotalFitResult atMaximum = LayoutOps.FitContentToTotal(
                measurements, maximumTotal);
            Assert.Equal(FitContentStage.Comfortable, atMaximum.Stage);
            Assert.True(atMaximum.TargetPreserved);
            Assert.Equal(maximumTotal, atMaximum.ActualTotalFeet, 12);
            Assert.All(atMaximum.Feet, value => Assert.Equal(max, value, 12));

            FixedTotalFitResult above = LayoutOps.FitContentToTotal(
                measurements, maximumTotal + 0.5 * Tolerance);
            Assert.Equal(FitContentStage.OutOfRange, above.Stage);
            Assert.Equal(FitContentLimitReason.AboveMaximum,
                above.LimitReason);
            Assert.All(above.Feet, value => Assert.Equal(max, value, 12));
            Assert.False(above.TargetPreserved);
        }

        [Fact]
        public void Comfortable_DistributesSurplusAndRedistributesAtMax()
        {
            FitContentMeasurements weighted = Prepare(
                1, 3,
                new[]
                {
                    new FitContentCell(0, 0, 1, 1,
                        new string('A', 32)),
                    new FitContentCell(0, 1, 1, 1, "A"),
                    new FitContentCell(0, 2, 1, 1, string.Empty)
                },
                2.0, 0, 0.1, 0.5);
            FixedTotalFitResult weightedResult = LayoutOps.FitContentToTotal(
                weighted, 1.2);

            Assert.Equal(FitContentStage.Comfortable, weightedResult.Stage);
            Assert.True(weightedResult.TargetPreserved);
            Assert.Equal(1.2, weightedResult.ActualTotalFeet, 12);
            Assert.Equal(0.5, weightedResult.Feet[0], 12);
            Assert.Equal(0.5, weightedResult.Feet[1], 12);
            Assert.Equal(0.2, weightedResult.Feet[2], 12);
            AssertNoSearch(weightedResult);

            // Exactly the comfortable total: no surplus and still no search.
            FixedTotalFitResult exact = LayoutOps.FitContentToTotal(
                weighted, weightedResult.ComfortableTotalFeet);
            Assert.Equal(FitContentStage.Comfortable, exact.Stage);
            Assert.Equal(0.5, exact.Feet[0], 12);
            Assert.Equal(0.1, exact.Feet[1], 12);
            Assert.Equal(0.1, exact.Feet[2], 12);
            AssertNoSearch(exact);

            // A demand above the column maximum leaves the estimate limited:
            // the surplus route skips the search yet reports Constrained.
            FitContentMeasurements limited = Prepare(
                1, 2,
                new[]
                {
                    new FitContentCell(0, 0, 1, 1, new string('A', 50)),
                    new FitContentCell(0, 1, 1, 1, "A")
                },
                2.0, 0, 0.1, 0.5);
            FixedTotalFitResult limitedResult = LayoutOps.FitContentToTotal(
                limited, 1.0);
            Assert.True(limitedResult.ComfortableEstimateLimited);
            Assert.True(limitedResult.ComfortableTotalFeet <= 1.0);
            Assert.Equal(FitContentStage.Constrained, limitedResult.Stage);
            AssertNoSearch(limitedResult);

            FitContentMeasurements blank = PrepareBlank(1, 3);
            FixedTotalFitResult blankResult = LayoutOps.FitContentToTotal(
                blank, 0.9);
            Assert.Equal(FitContentStage.Comfortable, blankResult.Stage);
            Assert.All(blankResult.Feet,
                value => Assert.Equal(0.3, value, 12));
        }

        [Fact]
        public void Merges_ObjectiveStaysFiniteForLargeFiniteInputs()
        {
            // The allocation is scale-invariant: multiplying scale, bounds and
            // total by K must multiply the widths by K. At this K the raw
            // merge-deficit sum would overflow before normalization.
            const double k = 2.5e307;
            FitContentMeasurements normal = BuildAlternatingMergeFixture(
                1.5, 0.1, 2.0);
            FitContentMeasurements huge = BuildAlternatingMergeFixture(
                1.5 * k, 0.1 * k, 2.0 * k);

            FixedTotalFitResult normalResult = LayoutOps.FitContentToTotal(
                normal, 0.9);
            FixedTotalFitResult hugeResult = LayoutOps.FitContentToTotal(
                huge, 0.9 * k);

            Assert.True(normalResult.UnmetMerges.Count > 0);
            Assert.True(normalResult.OptimizationTrials > 0);
            Assert.Equal(normalResult.EstimatedRowLineTotal,
                hugeResult.EstimatedRowLineTotal);
            Assert.Equal(normalResult.OptimizationTrials,
                hugeResult.OptimizationTrials);
            for (int i = 0; i < normalResult.Feet.Count; i++)
            {
                Assert.False(double.IsInfinity(hugeResult.Feet[i]));
                Assert.Equal(normalResult.Feet[i], hugeResult.Feet[i] / k, 9);
            }
        }

        private static FitContentMeasurements BuildAlternatingMergeFixture(
            double scale, double min, double max)
        {
            const int rows = 100;
            string text = new string('A', 60);
            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < rows; row++)
            {
                if (row % 2 == 0)
                {
                    cells.Add(new FitContentCell(row, 0, 1, 2, text));
                    cells.Add(new FitContentCell(row, 2, 1, 1, text));
                }
                else
                {
                    cells.Add(new FitContentCell(row, 0, 1, 1, text));
                    cells.Add(new FitContentCell(row, 1, 1, 2, text));
                }
            }
            return Prepare(rows, 3, cells.ToArray(), scale, 0, min, max);
        }

        private static void AssertNoSearch(FixedTotalFitResult result)
        {
            Assert.Equal(0, result.OptimizationTrials);
            Assert.False(result.OptimizationBudgetExhausted);
            Assert.Equal(result.BaselineRowLineTotal,
                result.EstimatedRowLineTotal);
        }

        [Fact]
        public void Constrained_ProtectsMatchingValuesButNotHeadings()
        {
            string protectedText = "12345678901234567890";
            FitContentMeasurements measurements = Prepare(
                4, 2,
                new[]
                {
                    new FitContentCell(0, 0, 1, 1, "ID"),
                    new FitContentCell(0, 1, 1, 1,
                        new string('H', 50)),
                    new FitContentCell(1, 0, 1, 1, protectedText),
                    new FitContentCell(1, 1, 1, 1, string.Empty),
                    new FitContentCell(2, 0, 1, 1, protectedText),
                    new FitContentCell(2, 1, 1, 1, string.Empty),
                    new FitContentCell(3, 0, 1, 1, protectedText),
                    new FitContentCell(3, 1, 1, 1, string.Empty)
                },
                1.0, 1, 0.1, 2.0);
            double[] preferredBefore = measurements.PreferredColumnFeet
                .ToArray();
            double[] protectionBefore = measurements.ProtectionRequiredFeet
                .ToArray();

            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, 0.3);

            Assert.True(measurements.ProtectedColumns[0]);
            Assert.True(result.Feet[0]
                >= measurements.ProtectionRequiredFeet[0] - Tolerance);
            Assert.True(result.Feet[1]
                < measurements.Cells[1].PreferredFeet - Tolerance);
            Assert.Equal(FitContentStage.Constrained, result.Stage);
            Assert.Equal(0.3, result.ActualTotalFeet, 12);
            Assert.Equal(preferredBefore,
                measurements.PreferredColumnFeet.ToArray());
            Assert.Equal(protectionBefore,
                measurements.ProtectionRequiredFeet.ToArray());
        }

        [Fact]
        public void ProtectionFailure_ReportsOriginalDemandAndKeepsTotal()
        {
            string fortyMillimetreNumber = new string('9', 20);
            string aboveMaximumNumber = new string('9', 64);
            double fortyMillimetresFeet = 40.0 / (12.0 * 25.4);
            double hundredMillimetresFeet = 100.0 / (12.0 * 25.4);
            double scale = fortyMillimetresFeet / 0.16;
            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    cells.Add(new FitContentCell(row, column, 1, 1,
                        column == 3
                            ? aboveMaximumNumber
                            : fortyMillimetreNumber));
                }
            }

            FitContentMeasurements measurements = Prepare(3, 4, cells,
                scale, 0, 0.05, hundredMillimetresFeet);
            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, hundredMillimetresFeet);

            Assert.Equal(FitContentStage.Constrained, result.Stage);
            Assert.Equal(hundredMillimetresFeet, result.ActualTotalFeet, 12);
            Assert.Equal(new[] { 0, 1, 2, 3 },
                result.ProtectionFailedColumns.ToArray());
            Assert.All(result.ProtectionFailedColumns,
                column => Assert.True(result.Feet[column]
                    < measurements.ProtectionRequiredFeet[column]
                        - Tolerance));
            Assert.All(result.Feet,
                value => Assert.InRange(value, 0.05 - Tolerance,
                    hundredMillimetresFeet + Tolerance));
            Assert.Equal(fortyMillimetresFeet,
                measurements.ProtectionRequiredFeet[0], 12);
            Assert.True(measurements.ProtectionRequiredFeet[3]
                > measurements.MaxColumnFeet);
        }

        [Fact]
        public void Merges_UseFinalSharedBudgetAndReportDeficits()
        {
            string longText = new string('M', 100);
            FitContentMeasurements measurements = Prepare(
                6, 3,
                new[]
                {
                    new FitContentCell(0, 0, 1, 3, longText),
                    new FitContentCell(1, 0, 2, 2, longText),
                    new FitContentCell(1, 2, 2, 1, longText),
                    new FitContentCell(3, 0, 1, 2, longText),
                    new FitContentCell(3, 2, 3, 1, longText),
                    new FitContentCell(4, 0, 1, 1, string.Empty),
                    new FitContentCell(4, 1, 1, 1, string.Empty),
                    new FitContentCell(5, 0, 1, 1, string.Empty),
                    new FitContentCell(5, 1, 1, 1, string.Empty)
                },
                1.0, 0, 0.1, 2.0);

            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, 0.4);

            Assert.Equal(0.4, result.ActualTotalFeet, 12);
            Assert.Equal(FitContentStage.Constrained, result.Stage);
            Assert.Equal(new[] { 0, 1, 3 },
                result.UnmetMerges.Select(merge => merge.Row).ToArray());
            Assert.Equal(new[] { 0, 0, 0 },
                result.UnmetMerges.Select(merge => merge.Column).ToArray());
            Assert.DoesNotContain(result.UnmetMerges,
                merge => merge.ColumnSpan == 1);
            Assert.All(result.UnmetMerges, merge =>
            {
                double actual = 0.0;
                for (int column = merge.Column;
                    column < merge.Column + merge.ColumnSpan; column++)
                {
                    actual += result.Feet[column];
                }
                Assert.Equal(actual, merge.ActualFeet, 12);
                Assert.True(merge.DeficitFeet > Tolerance);
            });
        }

        [Fact]
        public void ComfortableEstimate_IsDeterministicBoundedAndMarkedLimited()
        {
            string capped = new string('9', 64);
            FitContentCell first = new FitContentCell(0, 0, 1, 1, capped);
            FitContentCell second = new FitContentCell(1, 0, 1, 1,
                string.Empty);
            FitContentMeasurements forward = Prepare(2, 1,
                new[] { first, second }, 1.0, 0, 0.1, 0.2);
            FitContentMeasurements reverse = Prepare(2, 1,
                new[] { second, first }, 1.0, 0, 0.1, 0.2);

            FixedTotalFitResult left = LayoutOps.FitContentToTotal(
                forward, 0.2);
            FixedTotalFitResult right = LayoutOps.FitContentToTotal(
                reverse, 0.2);

            Assert.True(left.ComfortableEstimateLimited);
            Assert.Equal(left.Feet.ToArray(), right.Feet.ToArray());
            Assert.Equal(left.ComfortableFeet.ToArray(),
                right.ComfortableFeet.ToArray());
            Assert.All(left.ComfortableFeet,
                value => Assert.InRange(value, 0.1 - Tolerance,
                    0.2 + Tolerance));
            Assert.All(left.Feet,
                value => Assert.InRange(value, 0.1 - Tolerance,
                    0.2 + Tolerance));
            Assert.Equal(0.2, left.ActualTotalFeet, 12);
        }

        [Fact]
        public void RowObjective_NeverExceedsDefinedLongestFirstBaseline()
        {
            FitContentMeasurements measurements = BuildRepeatedProseFixture();
            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, 0.8);

            double[] baseline = LongestFirstBaseline(measurements,
                result.ComfortableFeet.ToArray(), 0.8);
            long expectedBaseline = EstimateRowLineTotal(measurements,
                baseline);
            long expectedFinal = EstimateRowLineTotal(measurements,
                result.Feet.ToArray());

            Assert.Equal(expectedBaseline, result.BaselineRowLineTotal);
            Assert.Equal(expectedFinal, result.EstimatedRowLineTotal);
            Assert.True(result.EstimatedRowLineTotal
                <= result.BaselineRowLineTotal);
        }

        [Fact]
        public void RowObjective_ImprovesAConcreteTwoColumnFixture()
        {
            FitContentMeasurements measurements =
                BuildConcreteTwoColumnFixture();
            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, 0.8);

            Assert.Equal(0.69, result.ComfortableFeet[0], 12);
            Assert.Equal(0.5775, result.ComfortableFeet[1], 12);
            double[] baseline = LongestFirstBaseline(measurements,
                result.ComfortableFeet.ToArray(), 0.8);
            Assert.Equal(0.2225, baseline[0], 12);
            Assert.Equal(0.5775, baseline[1], 12);
            Assert.Equal(21L, EstimateRowLineTotal(measurements, baseline));
            Assert.Equal(21L, result.BaselineRowLineTotal);

            long feasibleObjective = EstimateRowLineTotal(measurements,
                new[] { 0.45, 0.35 });
            Assert.Equal(11L, feasibleObjective);
            Assert.Equal(result.EstimatedRowLineTotal,
                EstimateRowLineTotal(measurements, result.Feet.ToArray()));
            Assert.Equal(2L, result.EstimatedTallestRowLines);
            Assert.Equal(29, result.OptimizationTrials);
            Assert.False(result.OptimizationBudgetExhausted);
            Assert.True(result.EstimatedRowLineTotal < 21L);
        }

        [Fact]
        public void Allocation_IsDeterministicIdempotentAndInputOrderIndependent()
        {
            FitContentCell[] cells =
            {
                new FitContentCell(0, 0, 1, 1, "ABCD"),
                new FitContentCell(0, 1, 1, 1, "ABCD"),
                new FitContentCell(1, 0, 1, 1, "ABCD"),
                new FitContentCell(1, 1, 1, 1, "ABCD")
            };
            FitContentMeasurements forward = Prepare(2, 2, cells,
                1.0, 0, 0.1, 2.0);
            FitContentMeasurements reverse = Prepare(2, 2,
                cells.Reverse().ToArray(), 1.0, 0, 0.1, 2.0);

            FixedTotalFitResult first = LayoutOps.FitContentToTotal(
                forward, 1.0);
            FixedTotalFitResult repeated = LayoutOps.FitContentToTotal(
                forward, 1.0);
            FixedTotalFitResult reordered = LayoutOps.FitContentToTotal(
                reverse, 1.0);

            Assert.Equal(first.Feet.ToArray(), repeated.Feet.ToArray());
            Assert.Equal(first.Feet.ToArray(), reordered.Feet.ToArray());
            Assert.Equal(first.EstimatedRowLineTotal,
                repeated.EstimatedRowLineTotal);
            Assert.Equal(first.OptimizationTrials,
                repeated.OptimizationTrials);
            Assert.Equal(1.0, first.ActualTotalFeet, 12);
        }

        [Theory]
        [MemberData(nameof(AllocationFixtures))]
        public void Allocation_AlwaysPreservesBoundsAndBudget(
            AllocationFixture fixture)
        {
            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                fixture.Measurements, fixture.TotalFeet);

            Assert.True(result.CanApply);
            Assert.True(result.TargetPreserved);
            Assert.True(Math.Abs(fixture.TotalFeet
                - result.ActualTotalFeet) <= Tolerance);
            Assert.All(result.Feet, value =>
                Assert.InRange(value, fixture.MinimumFeet - Tolerance,
                    fixture.MaximumFeet + Tolerance));
            Assert.True(result.OptimizationTrials
                <= LayoutMetrics.FitContentMaxTransferTrials);
            Assert.Equal(fixture.Measurements.ColumnCount,
                result.Feet.Count);
        }

        [Fact]
        public void Allocation_StopsWithinTrialBudgetAtMaximumShape()
        {
            FitContentMeasurements measurements =
                BuildMaximumShapeFixture();
            // An in-range total below the comfortable total, so the search runs.
            const double total = 20.0;

            FixedTotalFitResult result = LayoutOps.FitContentToTotal(
                measurements, total);

            Assert.True(measurements.ColumnCount * measurements.MinColumnFeet
                < total);
            Assert.True(result.ComfortableTotalFeet > total);
            Assert.True(result.TargetPreserved);
            Assert.True(Math.Abs(total - result.ActualTotalFeet)
                <= Tolerance);
            Assert.All(result.Feet, value =>
                Assert.InRange(value, measurements.MinColumnFeet - Tolerance,
                    measurements.MaxColumnFeet + Tolerance));
            Assert.InRange(result.OptimizationTrials, 1,
                LayoutMetrics.FitContentMaxTransferTrials);
        }

        public static IEnumerable<object[]> AllocationFixtures
        {
            get
            {
                yield return new object[]
                {
                    new AllocationFixture(1, 1, 1.5, 0.05, 0.8)
                };
                yield return new object[]
                {
                    new AllocationFixture(1, 3, 2.0, 0.08, 0.7)
                };
                yield return new object[]
                {
                    new AllocationFixture(1, 50, 1.5, 0.05, 1.1)
                };
                yield return new object[]
                {
                    new AllocationFixture(500, 1, 2.0, 0.08, 0.9)
                };
                yield return new object[]
                {
                    new AllocationFixture(500, 3, 1.5, 0.05, 1.0)
                };
                yield return new object[]
                {
                    new AllocationFixture(500, 50, 2.0, 0.05, 1.0)
                };
            }
        }

        private static FitContentMeasurements PrepareBlank(int rows,
            int columns, double scale = 1.0, int headingRows = 0,
            double minimumFeet = 0.1, double maximumFeet = 2.0)
        {
            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    cells.Add(new FitContentCell(row, column, 1, 1,
                        string.Empty));
                }
            }
            return Prepare(rows, columns, cells, scale, headingRows,
                minimumFeet, maximumFeet);
        }

        private static FitContentMeasurements Prepare(int rows, int columns,
            IEnumerable<FitContentCell> cells, double scale,
            int headingRows, double minimumFeet, double maximumFeet)
        {
            List<FitContentCell> owned = new List<FitContentCell>(cells);
            return FitContentText.Prepare(
                new FitContentTable(rows, columns,
                    new ReadOnlyCollection<FitContentCell>(owned)),
                scale, headingRows, minimumFeet, maximumFeet);
        }

        private static FitContentMeasurements BuildRepeatedProseFixture()
        {
            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < 6; row++)
            {
                cells.Add(new FitContentCell(row, 0, 1, 1,
                    row < 5 ? new string('P', 60) : string.Empty));
                cells.Add(new FitContentCell(row, 1, 1, 1,
                    row == 5 ? new string('Q', 60) : string.Empty));
            }
            return Prepare(6, 2, cells, 1.0, 0, 0.1, 2.0);
        }

        private static FitContentMeasurements BuildConcreteTwoColumnFixture()
        {
            List<FitContentCell> cells = new List<FitContentCell>
            {
                new FitContentCell(0, 0, 1, 1, "Description"),
                new FitContentCell(0, 1, 1, 1, "Reference")
            };
            for (int row = 1; row < 6; row++)
            {
                cells.Add(new FitContentCell(row, 0, 1, 1,
                    new string('X', 60)));
                cells.Add(new FitContentCell(row, 1, 1, 1,
                    row == 1 ? new string('Y', 50) : string.Empty));
            }
            return Prepare(6, 2, cells, 1.5, 1, 0.1, 2.0);
        }

        private static double[] LongestFirstBaseline(
            FitContentMeasurements measurements, double[] comfortable,
            double total)
        {
            double[] result = (double[])comfortable.Clone();
            double comfortableTotal = result.Sum();
            List<int> order = new List<int>();
            for (int column = 0; column < result.Length; column++)
            {
                order.Add(column);
            }
            order.Sort(delegate(int left, int right)
            {
                int comparison = result[right].CompareTo(result[left]);
                return comparison != 0
                    ? comparison
                    : left.CompareTo(right);
            });

            double reduction = comfortableTotal - total;
            for (int i = 0; i < order.Count && reduction > 0.0; i++)
            {
                int column = order[i];
                double floor = measurements.ProtectedColumns[column]
                    ? Math.Max(measurements.MinColumnFeet,
                        Math.Min(measurements.MaxColumnFeet,
                            measurements.ProtectionRequiredFeet[column]))
                    : measurements.MinColumnFeet;
                double room = result[column] - floor;
                double subtract = Math.Min(reduction, Math.Max(0.0, room));
                result[column] -= subtract;
                reduction -= subtract;
            }
            return result;
        }

        private static long EstimateRowLineTotal(
            FitContentMeasurements measurements, double[] feet)
        {
            long[] rowMaximums = new long[measurements.RowCount];
            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                double span = 0.0;
                for (int column = cell.Column;
                    column < cell.Column + cell.ColumnSpan; column++)
                {
                    span += feet[column];
                }
                long lines = FitContentText.EstimateLineCount(cell, span,
                    measurements.WidthScale);
                long contribution = lines / cell.RowSpan;
                if (lines % cell.RowSpan != 0L)
                {
                    contribution++;
                }
                if (contribution < 1L)
                {
                    contribution = 1L;
                }
                for (int row = cell.Row;
                    row < cell.Row + cell.RowSpan; row++)
                {
                    if (contribution > rowMaximums[row])
                    {
                        rowMaximums[row] = contribution;
                    }
                }
            }
            return rowMaximums.Sum();
        }

        private static FitContentMeasurements BuildMaximumShapeFixture()
        {
            List<FitContentCell> cells =
                new List<FitContentCell>(500 * 50);
            for (int row = 0; row < 500; row++)
            {
                for (int column = 0; column < 50; column++)
                {
                    int pattern = (row * 17 + column * 11) % 19;
                    string text;
                    if (pattern == 0)
                    {
                        text = new string('A', 60);
                    }
                    else if (pattern == 1)
                    {
                        text = "A\n" + new string('B', 17);
                    }
                    else if (pattern == 2)
                    {
                        text = new string((char)0x6F22,
                            1 + ((row + column) % 12));
                    }
                    else
                    {
                        text = (row + column) % 7 == 0
                            ? "ABCD"
                            : string.Empty;
                    }
                    cells.Add(new FitContentCell(row, column, 1, 1, text));
                }
            }
            return Prepare(500, 50, cells, 1.5, 1, 0.1, 2.0);
        }

        public sealed class AllocationFixture
        {
            public AllocationFixture(int rows, int columns, double scale,
                double minimumFeet, double maximumFeet)
            {
                Measurements = BuildMeasurements(rows, columns, scale,
                    minimumFeet, maximumFeet);
                MinimumFeet = minimumFeet;
                MaximumFeet = maximumFeet;
                TotalFeet = columns * minimumFeet
                    + columns * (maximumFeet - minimumFeet) * 0.4;
            }

            public FitContentMeasurements Measurements { get; }
            public double MinimumFeet { get; }
            public double MaximumFeet { get; }
            public double TotalFeet { get; }

            private static FitContentMeasurements BuildMeasurements(int rows,
                int columns, double scale, double minimumFeet,
                double maximumFeet)
            {
                List<FitContentCell> cells =
                    new List<FitContentCell>(rows * columns);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        string text = (row + column) % 23 == 0
                            ? new string('Z', 23)
                            : string.Empty;
                        cells.Add(new FitContentCell(row, column, 1, 1,
                            text));
                    }
                }
                return Prepare(rows, columns, cells, scale, 1,
                    minimumFeet, maximumFeet);
            }
        }
    }
}
