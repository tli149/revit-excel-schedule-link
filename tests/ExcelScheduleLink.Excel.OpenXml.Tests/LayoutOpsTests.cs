using System;
using System.Collections.Generic;
using System.Linq;
using ExcelScheduleLink.Layout;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    /// <summary>
    /// Unit tests for the Layout-tool pure functions (LayoutOps), including the
    /// boundary / non-finite / mutation / property conditions required by the
    /// B1 algorithm review.
    /// </summary>
    public class LayoutOpsTests
    {
        private static double Min => LayoutMetrics.MinColumnFeet;
        private static double Max => LayoutMetrics.MaxColumnFeet;
        private static double Tol => LayoutOps.Tolerance;
        private static double Sum(double[] a) => a.Sum();
        private static bool SumMatches(double[] a, double target) => Math.Abs(Sum(a) - target) <= Tol;
        private static void AssertAllFiniteInBounds(double[] a)
        {
            foreach (double w in a)
            {
                Assert.False(double.IsNaN(w) || double.IsInfinity(w));
                Assert.InRange(w, Min - 1e-9, Max + 1e-9);
            }
        }

        // ---------------- Restore Excel ----------------

        [Fact]
        public void ResetToExcel_uses_excel_width_and_min_for_missing()
        {
            var excel = new Dictionary<int, double> { { 0, 40.0 } };
            double[] feet = LayoutOps.ResetToExcelFeet(excel, 3, 1.0);
            Assert.True(feet[0] > Min);
            Assert.Equal(Min, feet[1], 6);
            Assert.Equal(Min, feet[2], 6);
        }

        [Fact]
        public void ResetToExcel_nan_excel_width_becomes_min()
        {
            var excel = new Dictionary<int, double> { { 0, double.NaN } };
            double[] feet = LayoutOps.ResetToExcelFeet(excel, 1, 1.0);
            Assert.Equal(Min, feet[0], 6);
        }

        [Fact]
        public void ResetToExcel_zero_and_negative_columns()
        {
            Assert.Empty(LayoutOps.ResetToExcelFeet(null, 0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LayoutOps.ResetToExcelFeet(null, -1, 1.0));
        }

        // ---------------- Distribute evenly ----------------

        [Fact]
        public void Distribute_evenly_splits_and_clamps()
        {
            double[] feet = LayoutOps.DistributeEvenlyFeet(4, 4.0);
            Assert.All(feet, w => Assert.Equal(1.0, w, 6));
            Assert.All(LayoutOps.DistributeEvenlyFeet(4, 0.0), w => Assert.Equal(Min, w, 6));
        }

        [Fact]
        public void Distribute_edge_cases()
        {
            Assert.Empty(LayoutOps.DistributeEvenlyFeet(0, 5.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LayoutOps.DistributeEvenlyFeet(-1, 5.0));
            Assert.All(LayoutOps.DistributeEvenlyFeet(3, double.NaN), w => Assert.Equal(Min, w, 6)); // NaN -> Min
            Assert.All(LayoutOps.DistributeEvenlyFeet(3, 100.0), w => Assert.Equal(Max, w, 6));       // over -> Max
        }

        // ---------------- Fit to total: happy path ----------------

        [Fact]
        public void FitToTotal_reaches_target_and_preserves_ratio()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 2, 3 }, 3.0);
            Assert.True(r.Achievable);
            Assert.True(SumMatches(r.Feet, 3.0));
            Assert.Equal(0.5, r.Feet[0], 3);
            Assert.Equal(1.0, r.Feet[1], 3);
            Assert.Equal(1.5, r.Feet[2], 3);
        }

        [Fact]
        public void FitToTotal_current_sum_zero_distributes_evenly()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { 0, 0, 0 }, 3.0);
            Assert.True(r.Achievable);
            Assert.True(SumMatches(r.Feet, 3.0));
            Assert.All(r.Feet, w => Assert.Equal(1.0, w, 4));
        }

        [Fact]
        public void FitToTotal_fixes_min_and_max_in_same_computation()
        {
            // col0 wants ~0 (-> Min), col1 wants ~2.1 (-> Max), both fixed at once.
            var r = LayoutOps.FitToTotalFeet(new double[] { 0.01, 100.0 }, 2.1);
            Assert.True(r.Achievable);
            Assert.Equal(Min, r.Feet[0], 6);
            Assert.Equal(Max, r.Feet[1], 6);
            Assert.True(SumMatches(r.Feet, 2.1));
        }

        [Fact]
        public void FitToTotal_single_column()
        {
            // (Empty-array behavior is covered by FitToTotal_zero_columns_only_zero_target_is_achievable.)
            var one = LayoutOps.FitToTotalFeet(new double[] { 0.5 }, 1.5);
            Assert.True(one.Achievable);
            Assert.Equal(1.5, one.Feet[0], 6);
        }

        [Fact]
        public void FitToTotal_does_not_mutate_input()
        {
            double[] current = { 1, 2, 3 };
            double[] snapshot = (double[])current.Clone();
            LayoutOps.FitToTotalFeet(current, 3.0);
            Assert.Equal(snapshot, current);
        }

        // ---------------- Fit to total: exact feasibility boundaries ----------------

        [Fact]
        public void FitToTotal_target_exactly_feasible_min_is_achievable()
        {
            int n = 3;
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 1, 1 }, n * Min);
            Assert.True(r.Achievable);
            Assert.True(SumMatches(r.Feet, n * Min));
            Assert.All(r.Feet, w => Assert.Equal(Min, w, 6));
        }

        [Fact]
        public void FitToTotal_target_exactly_feasible_max_is_achievable()
        {
            int n = 3;
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 1, 1 }, n * Max);
            Assert.True(r.Achievable);
            Assert.True(SumMatches(r.Feet, n * Max));
            Assert.All(r.Feet, w => Assert.Equal(Max, w, 6));
        }

        [Fact]
        public void FitToTotal_just_below_feasible_min_is_not_achievable()
        {
            int n = 3;
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 1, 1 }, n * Min - 0.5 * Tol);
            Assert.False(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.All(r.Feet, w => Assert.Equal(Min, w, 6));
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void FitToTotal_just_above_feasible_max_is_not_achievable()
        {
            int n = 3;
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 1, 1 }, n * Max + 0.5 * Tol);
            Assert.False(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.All(r.Feet, w => Assert.Equal(Max, w, 6));
        }

        // ---------------- Fit to total: non-finite handling ----------------

        [Fact]
        public void FitToTotal_current_nan_is_not_achievable_but_finite()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { double.NaN, 1, 1 }, 2.0);
            Assert.False(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void FitToTotal_current_infinity_is_not_achievable_but_finite()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { double.PositiveInfinity, 1, 1 }, 2.0);
            Assert.False(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
        }

        [Fact]
        public void FitToTotal_target_nan_is_not_achievable_but_finite()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { 1, 1, 1 }, double.NaN);
            Assert.False(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.False(string.IsNullOrEmpty(r.Reason));
            // non-finite target must not leak into the deficit
            Assert.False(double.IsNaN(r.UnresolvedDeficit) || double.IsInfinity(r.UnresolvedDeficit));
            Assert.Equal(0.0, r.UnresolvedDeficit, 6);
        }

        [Fact]
        public void FitToTotal_negative_current_is_invalid_even_if_sum_matches()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { -1.0, 1.0, 1.0 }, 2.0);
            Assert.False(r.Achievable);          // negative input is invalid
            AssertAllFiniteInBounds(r.Feet);
            Assert.False(string.IsNullOrEmpty(r.Reason));
        }

        [Fact]
        public void FitToTotal_preserves_tiny_positive_ratios()
        {
            // 1:9 ratio must be kept (not evened to 0.5/0.5).
            var r = LayoutOps.FitToTotalFeet(new double[] { 1e-7, 9e-7 }, 1.0);
            Assert.True(r.Achievable);
            Assert.True(SumMatches(r.Feet, 1.0));
            Assert.Equal(0.1, r.Feet[0], 3);
            Assert.Equal(0.9, r.Feet[1], 3);
        }

        [Fact]
        public void FitToTotal_huge_finite_weights_stay_finite()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { 1e308, 1e308 }, 2.0);
            Assert.True(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.True(SumMatches(r.Feet, 2.0));
        }

        [Fact]
        public void FitToTotal_huge_uneven_weights_in_bounds_and_on_target()
        {
            var r = LayoutOps.FitToTotalFeet(new double[] { 1e308, 1e300, 1.0 }, 3.0);
            Assert.True(r.Achievable);
            AssertAllFiniteInBounds(r.Feet);
            Assert.True(SumMatches(r.Feet, 3.0));
        }

        [Fact]
        public void FitToTotal_zero_columns_only_zero_target_is_achievable()
        {
            var zero = LayoutOps.FitToTotalFeet(new double[0], 0.0);
            Assert.True(zero.Achievable);
            Assert.Empty(zero.Feet);

            var nonZero = LayoutOps.FitToTotalFeet(new double[0], 1.0);
            Assert.False(nonZero.Achievable);
            Assert.Equal(1.0, nonZero.UnresolvedDeficit, 6);
            Assert.False(string.IsNullOrEmpty(nonZero.Reason));

            var nan = LayoutOps.FitToTotalFeet(new double[0], double.NaN);
            Assert.False(nan.Achievable);
            Assert.Equal(0.0, nan.UnresolvedDeficit, 6);
        }

        // ---------------- Merge constraints (Fit Content only) ----------------

        [Fact]
        public void Merge_deficit_distributed_so_span_meets_required()
        {
            double[] feet = { Min, Min, Min };
            var merges = new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 2, RequiredFeet = 1.0 } };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.True(r.Feet[0] + r.Feet[1] + r.Feet[2] >= 1.0 - Tol);
            Assert.Equal(0.0, r.UnresolvedDeficit, 6);
        }

        [Fact]
        public void Merge_distributes_proportional_to_room()
        {
            // col0 room 0.1, col1 room 1.5 -> col1 should absorb much more.
            double[] feet = { 1.9, 0.5 };
            var merges = new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = 3.0 } };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            double g0 = r.Feet[0] - 1.9;
            double g1 = r.Feet[1] - 0.5;
            Assert.True(g1 > g0);                               // more room -> more added
            Assert.True(r.Feet[0] + r.Feet[1] >= 3.0 - Tol);
            Assert.Equal(0.0, r.UnresolvedDeficit, 6);
        }

        [Fact]
        public void Merge_partial_room_then_reports_remaining_deficit()
        {
            double[] feet = { 1.95, 1.99 }; // room 0.05 + 0.01 = 0.06
            var merges = new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = 5.0 } };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.Equal(Max, r.Feet[0], 6);
            Assert.Equal(Max, r.Feet[1], 6);
            // 5.0 required - (3.94 span + 0.06 room) = 1.0 unresolved
            Assert.Equal(1.0, r.UnresolvedDeficit, 4);
        }

        [Fact]
        public void Merge_required_over_span_max_reports_unresolved()
        {
            double[] feet = { Max, Max };
            var merges = new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = 5.0 } };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.Equal(1.0, r.UnresolvedDeficit, 4);
        }

        [Fact]
        public void Merge_tiny_per_column_room_but_meaningful_total_is_filled()
        {
            double half = 0.5 * Tol;
            double[] feet = { Max - half, Max - half, Max - half }; // each room ~0.5*Tolerance
            var merges = new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 2, RequiredFeet = 3.0 * Max } };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.True(r.Feet[0] + r.Feet[1] + r.Feet[2] >= 3.0 * Max - Tol);
            Assert.True(r.UnresolvedDeficit <= Tol);
        }

        [Fact]
        public void Merge_multiple_ranges_each_enforced()
        {
            double[] feet = { Min, Min, Min, Min };
            var merges = new[]
            {
                new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = 0.8 },
                new MergeRequirement { FirstColumn = 2, LastColumn = 3, RequiredFeet = 0.6 }
            };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.True(r.Feet[0] + r.Feet[1] >= 0.8 - Tol);
            Assert.True(r.Feet[2] + r.Feet[3] >= 0.6 - Tol);
        }

        [Fact]
        public void Merge_invalid_required_and_range_are_skipped()
        {
            double[] feet = { 0.5, 0.5 };
            var merges = new[]
            {
                new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = double.NaN },
                new MergeRequirement { FirstColumn = 0, LastColumn = 5, RequiredFeet = 3.0 } // out of range
            };
            var r = LayoutOps.ApplyMergeConstraints(feet, merges);
            Assert.Equal(0.5, r.Feet[0], 6);
            Assert.Equal(0.5, r.Feet[1], 6);
            Assert.Equal(0.0, r.UnresolvedDeficit, 6);
        }

        [Fact]
        public void Merge_none_and_no_mutation()
        {
            double[] feet = { 0.5, 0.7 };
            double[] snapshot = (double[])feet.Clone();
            var r = LayoutOps.ApplyMergeConstraints(feet,
                new[] { new MergeRequirement { FirstColumn = 0, LastColumn = 1, RequiredFeet = 3.0 } });
            Assert.Equal(snapshot, feet);                 // input not mutated
            Assert.NotSame(feet, r.Feet);
        }

        // ---------------- ClampAll / ValidateAll ----------------

        [Fact]
        public void ClampAll_handles_nan_and_bounds()
        {
            double[] r = LayoutOps.ClampAll(new double[] { double.NaN, -1.0, 3.0, 0.5 });
            Assert.Equal(Min, r[0], 6);
            Assert.Equal(Min, r[1], 6);
            Assert.Equal(Max, r[2], 6);
            Assert.Equal(0.5, r[3], 6);
        }

        [Fact]
        public void ValidateAll_flags_invalid_columns()
        {
            var v = LayoutOps.ValidateAll(new double[] { double.NaN, 3.0, 0.5 }, null);
            Assert.False(v.IsValid);
            Assert.Equal(new[] { 0, 1 }, v.InvalidColumns);   // NaN + out-of-range(>Max)
            Assert.Equal(Min, v.NormalizedFeet[0], 6);
            Assert.Equal(Max, v.NormalizedFeet[1], 6);
            Assert.False(string.IsNullOrEmpty(v.Reason));
        }

        [Fact]
        public void ValidateAll_flags_total_limit()
        {
            var over = LayoutOps.ValidateAll(new double[] { 1.0, 1.0, 1.0 }, 2.5);
            Assert.False(over.IsValid);
            Assert.True(over.ExceedsTotalLimit);
            Assert.Equal(3.0, over.TotalFeet, 6);

            var ok = LayoutOps.ValidateAll(new double[] { 0.5, 0.5 }, 5.0);
            Assert.True(ok.IsValid);
            Assert.False(ok.ExceedsTotalLimit);
        }

        // ---------------- Randomized property test (fixed seed) ----------------

        [Fact]
        public void FitToTotal_property_holds_for_feasible_finite_inputs()
        {
            Random rng = new Random(20260724);
            for (int iter = 0; iter < 2000; iter++)
            {
                int n = rng.Next(1, 8);
                double[] current = new double[n];
                for (int i = 0; i < n; i++) current[i] = rng.NextDouble() * 3.0;
                double target = (n * Min) + rng.NextDouble() * (n * Max - n * Min);
                double[] snapshot = (double[])current.Clone();

                var r = LayoutOps.FitToTotalFeet(current, target);

                Assert.True(r.Achievable, "iter " + iter + " not achievable");
                AssertAllFiniteInBounds(r.Feet);
                Assert.True(Math.Abs(Sum(r.Feet) - target) <= Tol,
                    "iter " + iter + " sum " + Sum(r.Feet) + " vs target " + target);

                var r2 = LayoutOps.FitToTotalFeet(current, target);
                Assert.Equal(r.Feet, r2.Feet);         // deterministic
                Assert.Equal(snapshot, current);        // input not mutated
            }
        }
    }
}
