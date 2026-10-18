using System;
using System.Collections.Generic;

namespace ExcelScheduleLink.Layout
{
    /// <summary>Result of a Fit-to-Total-Width computation. Feet is always finite and clamped.</summary>
    public sealed class FitToTotalResult
    {
        /// <summary>Per-column widths (feet); always finite and in [Min, Max].</summary>
        public double[] Feet;
        /// <summary>True only if the target was reached within Tolerance AND the input was valid.</summary>
        public bool Achievable;
        /// <summary>Signed feet that could not be placed (target unreachable); always finite. 0 when achievable.</summary>
        public double UnresolvedDeficit;
        /// <summary>Reason when not achievable; null otherwise.</summary>
        public string Reason;
    }

    /// <summary>A horizontal merged range and the width its content needs (feet).</summary>
    public sealed class MergeRequirement
    {
        public int FirstColumn;
        public int LastColumn;
        public double RequiredFeet;
    }

    /// <summary>Result of applying merged-cell width guarantees (Fit Content only).</summary>
    public sealed class MergeFitResult
    {
        public double[] Feet;
        /// <summary>Total feet that could not be added because span columns hit Max.</summary>
        public double UnresolvedDeficit;
    }

    /// <summary>Result of validating candidate widths before applying them to Revit.</summary>
    public sealed class ValidationResult
    {
        /// <summary>
        /// True if every changed width is finite and within Tolerance of [Min, Max]
        /// and the final total is within the safe limit. A live-equivalent width
        /// may be preserved outside the calculated range.
        /// </summary>
        public bool IsValid;
        /// <summary>
        /// Finite copy of the input normalized for application. A live-equivalent
        /// native width is retained exactly, even when it is outside the calculated range.
        /// </summary>
        public double[] NormalizedFeet;
        /// <summary>Indices whose changed input value was NaN/Infinity or outside [Min, Max] by more than Tolerance.</summary>
        public int[] InvalidColumns;
        /// <summary>Indices whose changed input value was NaN or Infinity.</summary>
        public int[] NonFiniteColumns;
        /// <summary>Indices whose changed input value was outside the width range.</summary>
        public int[] OutOfRangeColumns;
        /// <summary>The exact safe total limit supplied by the caller.</summary>
        public double? MaxTotalFeet;
        /// <summary>Sum of NormalizedFeet.</summary>
        public double TotalFeet;
        /// <summary>True if TotalFeet exceeds the caller-supplied safe total limit.</summary>
        public bool ExceedsTotalLimit;
    }

    /// <summary>
    /// Pure Layout-tool operations (no Revit / no Excel types). All widths are
    /// feet (paper space). Legacy calculated widths are finite and clamped into
    /// the global LayoutMetrics range; bounds-aware overloads use the limits
    /// supplied by their caller. Live-equivalent widths may be preserved outside
    /// the calculated range by validation.
    ///
    /// The legacy two-argument fit-to-total operation and the legacy merge
    /// operation use the global column bounds. Their bounds overloads accept
    /// per-position limits for fixed-total allocation and comfortable merge
    /// estimates. Merge growth remains a grow-only operation; fixed-total
    /// allocation is owned by FitContentToTotal.
    /// </summary>
    public static class LayoutOps
    {
        /// <summary>
        /// Floating-point tolerance used for final total/residual comparisons,
        /// live-width equality and body-sync comparisons (feet).
        /// </summary>
        public const double Tolerance = 1e-6;

        // ---------------------------------------------------------- Restore Excel

        /// <summary>
        /// Each column = its Excel-derived width (scaled/clamped), or Min if none.
        /// Non-finite Excel widths become Min (via Clamp). Throws for negative columnCount.
        /// </summary>
        public static double[] ResetToExcelFeet(
            IDictionary<int, double> excelWidthsByColumn, int columnCount, double widthScale)
        {
            if (columnCount < 0) throw new ArgumentOutOfRangeException("columnCount");
            double[] feet = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                double w;
                if (excelWidthsByColumn != null && excelWidthsByColumn.TryGetValue(c, out w))
                {
                    feet[c] = ColumnWidthMath.FromExcelWidth(w, widthScale); // Clamp inside (NaN -> Min)
                }
                else
                {
                    feet[c] = LayoutMetrics.MinColumnFeet;
                }
            }
            return feet;
        }

        // ------------------------------------------------------ Distribute evenly

        /// <summary>
        /// All columns equal: Clamp(totalFeet / n). Throws for negative columnCount.
        /// Precondition: callers that need Sum(result) == totalFeet must pass a
        /// feasible totalFeet in [n*Min, n*Max] (Quick Layout passes the current
        /// total). If totalFeet is outside that range (or non-finite), the result
        /// is clamped and its sum will differ from totalFeet.
        /// </summary>
        public static double[] DistributeEvenlyFeet(int columnCount, double totalFeet)
        {
            if (columnCount < 0) throw new ArgumentOutOfRangeException("columnCount");
            double[] feet = new double[columnCount];
            double each = columnCount > 0 ? totalFeet / columnCount : 0.0;
            for (int c = 0; c < columnCount; c++)
            {
                feet[c] = ColumnWidthMath.Clamp(each); // NaN/Infinity -> Min/Max
            }
            return feet;
        }

        // -------------------------------------------------------- Fit to total

        /// <summary>
        /// Scales the columns to a target total width using constrained
        /// redistribution (water-filling): columns that would breach Min/Max are
        /// fixed at the bound and the remaining width is redistributed among the
        /// rest, preserving their current ratios. The input array is never mutated.
        ///
        /// Ratios are computed against the largest free weight each round, so tiny
        /// but non-zero ratios are preserved and the weight sum cannot overflow to
        /// Infinity even for huge finite widths. Even distribution is used only
        /// when every free weight is exactly zero.
        ///
        /// Input handling: current values that are NaN/Infinity OR negative are all
        /// treated as INVALID (the result stays finite but Achievable is forced
        /// false with a Reason; the invalid column contributes zero ratio weight).
        /// A non-finite target, or a target strictly outside [n*Min, n*Max] (no
        /// tolerance), yields Achievable=false with a finite fallback, a Reason and
        /// a finite UnresolvedDeficit. With zero columns the result is achievable
        /// only when the target is finite and zero. Tolerance is used ONLY to judge
        /// the final total. The returned Feet is always finite and in [Min, Max].
        /// </summary>
        public static FitToTotalResult FitToTotalFeet(IList<double> current, double targetTotalFeet)
        {
            int n = current != null ? current.Count : 0;
            double[] minimumFeet = new double[n];
            double[] maximumFeet = new double[n];
            for (int i = 0; i < n; i++)
            {
                minimumFeet[i] = LayoutMetrics.MinColumnFeet;
                maximumFeet[i] = LayoutMetrics.MaxColumnFeet;
            }

            return FitToTotalFeet(current, targetTotalFeet,
                minimumFeet, maximumFeet);
        }

        public static FitToTotalResult FitToTotalFeet(IList<double> current,
            double targetTotalFeet, IList<double> minimumFeet,
            IList<double> maximumFeet)
        {
            int n = current != null ? current.Count : 0;
            double[] lower = CopyAndValidateBounds(minimumFeet, maximumFeet,
                n, "minimumFeet", "maximumFeet");
            double[] upper = CopyAndValidateUpperBounds(maximumFeet, n);
            return FitToTotalWithBounds(current, targetTotalFeet, lower, upper);
        }

        private static FitToTotalResult FitToTotalWithBounds(
            IList<double> current, double targetTotalFeet,
            double[] minimumFeet, double[] maximumFeet)
        {
            int n = minimumFeet.Length;
            bool targetFinite = !double.IsNaN(targetTotalFeet)
                && !double.IsInfinity(targetTotalFeet);
            double feasibleMin = SumFinite(minimumFeet,
                "The minimum total is too large.");
            double feasibleMax = SumFinite(maximumFeet,
                "The maximum total is too large.");

            if (n == 0)
            {
                FitToTotalResult empty = new FitToTotalResult
                {
                    Feet = new double[0]
                };
                if (!targetFinite)
                {
                    empty.Achievable = false;
                    empty.UnresolvedDeficit = 0.0;
                    empty.Reason = "Target total is not a finite number.";
                }
                else if (Math.Abs(targetTotalFeet) <= Tolerance)
                {
                    empty.Achievable = true;
                }
                else
                {
                    empty.Achievable = false;
                    empty.UnresolvedDeficit = targetTotalFeet;
                    empty.Reason = "No columns, but the target total is non-zero.";
                }
                return empty;
            }

            double[] weights = new double[n];
            bool hadInvalidCurrent = false;
            for (int i = 0; i < n; i++)
            {
                double value = current[i];
                if (double.IsNaN(value) || double.IsInfinity(value)
                    || value < 0.0)
                {
                    weights[i] = 0.0;
                    hadInvalidCurrent = true;
                }
                else
                {
                    weights[i] = value;
                }
            }

            if (!targetFinite)
            {
                return BoundedUniform(minimumFeet, false, 0.0,
                    "Target total is not a finite number.");
            }

            if (targetTotalFeet < feasibleMin)
            {
                return BoundedUniform(minimumFeet, false,
                    targetTotalFeet - feasibleMin,
                    "Target total is below the minimum possible (columns x min width).");
            }
            if (targetTotalFeet > feasibleMax)
            {
                return BoundedUniform(maximumFeet, false,
                    targetTotalFeet - feasibleMax,
                    "Target total is above the maximum possible (columns x max width).");
            }

            bool[] fixedColumn = new bool[n];
            double[] result = new double[n];
            double remaining = targetTotalFeet;

            while (true)
            {
                int freeCount = 0;
                double maximumWeight = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (fixedColumn[i])
                    {
                        continue;
                    }

                    freeCount++;
                    if (weights[i] > maximumWeight)
                    {
                        maximumWeight = weights[i];
                    }
                }
                if (freeCount == 0)
                {
                    break;
                }

                bool evenMode = maximumWeight <= 0.0;
                double normalizedWeightSum = 0.0;
                if (!evenMode)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (!fixedColumn[i])
                        {
                            normalizedWeightSum += weights[i]
                                / maximumWeight;
                        }
                    }
                }

                bool fixedAny = false;
                for (int i = 0; i < n; i++)
                {
                    if (fixedColumn[i])
                    {
                        continue;
                    }

                    double candidate = evenMode
                        ? remaining / freeCount
                        : remaining * (weights[i] / maximumWeight)
                            / normalizedWeightSum;
                    if (candidate < minimumFeet[i])
                    {
                        result[i] = minimumFeet[i];
                        fixedColumn[i] = true;
                        remaining -= minimumFeet[i];
                        fixedAny = true;
                    }
                    else if (candidate > maximumFeet[i])
                    {
                        result[i] = maximumFeet[i];
                        fixedColumn[i] = true;
                        remaining -= maximumFeet[i];
                        fixedAny = true;
                    }
                }

                if (!fixedAny)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (fixedColumn[i])
                        {
                            continue;
                        }

                        result[i] = evenMode
                            ? remaining / freeCount
                            : remaining * (weights[i] / maximumWeight)
                                / normalizedWeightSum;
                        fixedColumn[i] = true;
                    }
                    break;
                }
            }

            for (int i = 0; i < n; i++)
            {
                result[i] = ClampToBounds(result[i], minimumFeet[i],
                    maximumFeet[i]);
            }
            double residual = RepairResidual(result, targetTotalFeet,
                minimumFeet, maximumFeet);

            FitToTotalResult output = new FitToTotalResult
            {
                Feet = result
            };
            bool sumOk = Math.Abs(targetTotalFeet - Total(result)) <= Tolerance;
            output.Achievable = sumOk && !hadInvalidCurrent;
            if (!output.Achievable)
            {
                output.Reason = hadInvalidCurrent
                    ? "Current widths contained invalid (non-finite or negative) values."
                    : "Could not distribute the target within the allowed column widths.";
                output.UnresolvedDeficit = sumOk ? 0.0 : residual;
            }
            return output;
        }

        private static FitToTotalResult BoundedUniform(
            IList<double> values, bool achievable, double deficit,
            string reason)
        {
            double[] feet = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                feet[i] = values[i];
            }

            return new FitToTotalResult
            {
                Feet = feet,
                Achievable = achievable,
                UnresolvedDeficit = deficit,
                Reason = reason
            };
        }

        // --------------------------------------------------- Merge constraints

        /// <summary>
        /// Ensures each merged span's total width is at least its content's
        /// required width, adding the deficit across the span (proportional to
        /// each column's remaining room up to the supplied maximum). Used for
        /// comfortable-width estimates only.
        ///
        /// The same "room &gt; 0" column set is used both to size the total room
        /// and to distribute, and each adjusted column is finally clamped to Max,
        /// so a span whose columns each have only a tiny (&lt;= Tolerance) room but
        /// a meaningful combined room is still filled (or its remaining deficit is
        /// reported). The working widths are clamped to the supplied bounds first
        /// (input array is never mutated); merge ranges with a
        /// non-finite/non-positive RequiredFeet or an out-of-range span are
        /// skipped.
        /// </summary>
        public static MergeFitResult ApplyMergeConstraints(double[] feet, IEnumerable<MergeRequirement> merges)
        {
            return ApplyMergeConstraints(feet, merges,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
        }

        public static MergeFitResult ApplyMergeConstraints(double[] feet,
            IEnumerable<MergeRequirement> merges, double minColumnFeet,
            double maxColumnFeet)
        {
            ValidateScalarBounds(minColumnFeet, maxColumnFeet,
                "minColumnFeet", "maxColumnFeet");
            int n = feet != null ? feet.Length : 0;
            double[] result = new double[n];
            for (int i = 0; i < n; i++)
            {
                result[i] = ClampToBounds(feet[i], minColumnFeet,
                    maxColumnFeet);
            }
            double unresolved = 0.0;

            if (merges != null)
            {
                foreach (MergeRequirement m in merges)
                {
                    if (m == null || m.FirstColumn < 0 || m.LastColumn >= n || m.LastColumn < m.FirstColumn)
                        continue;
                    if (double.IsNaN(m.RequiredFeet) || double.IsInfinity(m.RequiredFeet) || m.RequiredFeet <= 0.0)
                        continue; // invalid / no requirement

                    double spanSum = 0.0;
                    for (int c = m.FirstColumn; c <= m.LastColumn; c++) spanSum += result[c];
                    double deficit = m.RequiredFeet - spanSum;
                    if (deficit <= Tolerance) continue;

                    double totalRoom = 0.0;
                    for (int c = m.FirstColumn; c <= m.LastColumn; c++)
                    {
                        double room = maxColumnFeet - result[c];
                        if (room > 0.0) totalRoom += room;
                    }
                    if (totalRoom <= 0.0) { unresolved += deficit; continue; }

                    double add = Math.Min(deficit, totalRoom);
                    for (int c = m.FirstColumn; c <= m.LastColumn; c++)
                    {
                        double room = maxColumnFeet - result[c];
                        if (room <= 0.0) continue;
                        result[c] += add * (room / totalRoom);
                        if (result[c] > maxColumnFeet)
                        {
                            result[c] = maxColumnFeet;
                        }
                    }
                    if (deficit > totalRoom) unresolved += (deficit - totalRoom);
                }
            }

            return new MergeFitResult { Feet = result, UnresolvedDeficit = unresolved };
        }

        // ----------------------------------------------------- Fixed-total fit

        public static FixedTotalFitResult FitContentToTotal(
            FitContentMeasurements measurements, double currentTotalFeet)
        {
            if (measurements == null)
            {
                throw new ArgumentNullException("measurements");
            }

            ComfortableFit comfortable = BuildComfortableFit(measurements);
            int columnCount = measurements.ColumnCount;
            bool totalFinite = !double.IsNaN(currentTotalFeet)
                && !double.IsInfinity(currentTotalFeet);
            IReadOnlyList<bool> protectedColumns = measurements.ProtectedColumns;

            if (!totalFinite || currentTotalFeet < 0.0)
            {
                return new FixedTotalFitResult(
                    new double[0], FitContentStage.InvalidInput,
                    FitContentLimitReason.None, false, currentTotalFeet, 0.0,
                    false, protectedColumns, new int[0],
                    new FitContentMergeDeficit[0], comfortable.Feet,
                    comfortable.TotalFeet, comfortable.Limited, 0L, 0L, 0L,
                    0, false);
            }

            if (columnCount == 0)
            {
                if (currentTotalFeet == 0.0)
                {
                    return new FixedTotalFitResult(
                        new double[0], FitContentStage.Comfortable,
                        FitContentLimitReason.None, false, currentTotalFeet,
                        0.0, true, protectedColumns, new int[0],
                        new FitContentMergeDeficit[0], comfortable.Feet,
                        comfortable.TotalFeet, comfortable.Limited, 0L, 0L,
                        0L, 0, false);
                }

                return new FixedTotalFitResult(
                    new double[0], FitContentStage.OutOfRange,
                    FitContentLimitReason.NoColumns, false, currentTotalFeet,
                    0.0, false, protectedColumns, new int[0],
                    new FitContentMergeDeficit[0], comfortable.Feet,
                    comfortable.TotalFeet, comfortable.Limited, 0L, 0L, 0L,
                    0, false);
            }

            double minimumTotal = columnCount * measurements.MinColumnFeet;
            double maximumTotal = columnCount * measurements.MaxColumnFeet;
            if (currentTotalFeet < minimumTotal)
            {
                return CreateOutOfRangeResult(measurements, comfortable,
                    currentTotalFeet, FitContentLimitReason.BelowMinimum,
                    CreateUniformWidths(columnCount,
                        measurements.MinColumnFeet));
            }
            if (currentTotalFeet > maximumTotal)
            {
                return CreateOutOfRangeResult(measurements, comfortable,
                    currentTotalFeet, FitContentLimitReason.AboveMaximum,
                    CreateUniformWidths(columnCount,
                        measurements.MaxColumnFeet));
            }

            double[] protectionFloors = BuildProtectionFloors(measurements);
            double[] lowerBounds = BuildOptimizationFloors(
                measurements, protectionFloors, currentTotalFeet);
            double[] upperBounds = CreateUniformWidths(columnCount,
                measurements.MaxColumnFeet);
            double[] baseline = BuildBaseline(measurements, comfortable.Feet,
                lowerBounds, currentTotalFeet);
            RepairResidual(baseline, currentTotalFeet, lowerBounds,
                upperBounds);
            double[] preservedBaseline = (double[])baseline.Clone();

            FitContentSearch search = new FitContentSearch(measurements,
                baseline, comfortable.Feet);
            long baselineRowLineTotal = search.RowLineTotal;
            // At or above the comfortable total every capped demand already has
            // its width; searching would only even out the weighted surplus.
            if (comfortable.TotalFeet > currentTotalFeet)
            {
                RunLocalSearch(search, currentTotalFeet, lowerBounds,
                    upperBounds);
            }

            double[] finalFeet = search.CopyWidths();
            RepairResidual(finalFeet, currentTotalFeet, lowerBounds,
                upperBounds);
            search.Rebuild(finalFeet);
            if (search.RowLineTotal > baselineRowLineTotal)
            {
                finalFeet = (double[])preservedBaseline.Clone();
                search.Rebuild(finalFeet);
            }
            else
            {
                finalFeet = search.CopyWidths();
            }

            FinalFitDiagnostics diagnostics = GetFinalFitDiagnostics(
                measurements, finalFeet);
            FitContentStage stage = diagnostics.Comfortable
                ? FitContentStage.Comfortable
                : FitContentStage.Constrained;
            return new FixedTotalFitResult(
                finalFeet, stage, FitContentLimitReason.None, true,
                currentTotalFeet, Total(finalFeet),
                Math.Abs(Total(finalFeet) - currentTotalFeet) <= Tolerance,
                protectedColumns, diagnostics.ProtectionFailedColumns,
                diagnostics.UnmetMerges, comfortable.Feet,
                comfortable.TotalFeet, comfortable.Limited,
                search.RowLineTotal, baselineRowLineTotal,
                search.TallestRowLines, search.Trials,
                search.BudgetExhausted);
        }

        private static ComfortableFit BuildComfortableFit(
            FitContentMeasurements measurements)
        {
            int columnCount = measurements.ColumnCount;
            double min = measurements.MinColumnFeet;
            double max = measurements.MaxColumnFeet;
            double[] feet = new double[columnCount];
            bool limited = measurements.MeasurementCapped;
            List<MergeRequirement> merges = new List<MergeRequirement>();

            for (int column = 0; column < columnCount; column++)
            {
                double protection = measurements.ProtectionRequiredFeet[column];
                double floor = measurements.ProtectedColumns[column]
                    ? ClampToBounds(protection, min, max)
                    : min;
                double preferred = ClampToBounds(
                    measurements.PreferredColumnFeet[column], min, max);
                feet[column] = Math.Max(preferred, floor);
                if (measurements.ProtectedColumns[column]
                    && protection > max)
                {
                    limited = true;
                }
            }

            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                if (cell.ColumnSpan == 1 && cell.PreferredFeet > max)
                {
                    limited = true;
                }
                if (cell.ColumnSpan > 1)
                {
                    merges.Add(new MergeRequirement
                    {
                        FirstColumn = cell.Column,
                        LastColumn = checked(cell.Column + cell.ColumnSpan - 1),
                        RequiredFeet = cell.PreferredFeet
                    });
                }
            }

            MergeFitResult mergeResult = ApplyMergeConstraints(feet, merges,
                min, max);
            for (int i = 0; i < merges.Count; i++)
            {
                MergeRequirement merge = merges[i];
                double actual = SpanTotal(mergeResult.Feet,
                    merge.FirstColumn, merge.LastColumn);
                if (merge.RequiredFeet - actual > Tolerance)
                {
                    limited = true;
                    break;
                }
            }

            return new ComfortableFit
            {
                Feet = mergeResult.Feet,
                TotalFeet = Total(mergeResult.Feet),
                Limited = limited
            };
        }

        private static FixedTotalFitResult CreateOutOfRangeResult(
            FitContentMeasurements measurements, ComfortableFit comfortable,
            double requestedTotalFeet, FitContentLimitReason limitReason,
            double[] fallback)
        {
            FitContentSearch search = new FitContentSearch(measurements,
                fallback, comfortable.Feet);
            FinalFitDiagnostics diagnostics = GetFinalFitDiagnostics(
                measurements, fallback);
            return new FixedTotalFitResult(
                fallback, FitContentStage.OutOfRange, limitReason, true,
                requestedTotalFeet, Total(fallback), false,
                measurements.ProtectedColumns,
                diagnostics.ProtectionFailedColumns,
                diagnostics.UnmetMerges, comfortable.Feet,
                comfortable.TotalFeet, comfortable.Limited,
                search.RowLineTotal, search.RowLineTotal,
                search.TallestRowLines, 0, false);
        }

        private static double[] BuildProtectionFloors(
            FitContentMeasurements measurements)
        {
            int n = measurements.ColumnCount;
            double[] floors = new double[n];
            for (int i = 0; i < n; i++)
            {
                double demand = measurements.ProtectionRequiredFeet[i];
                floors[i] = measurements.ProtectedColumns[i]
                    ? ClampToBounds(demand, measurements.MinColumnFeet,
                        measurements.MaxColumnFeet)
                    : measurements.MinColumnFeet;
            }
            return floors;
        }

        private static double[] BuildOptimizationFloors(
            FitContentMeasurements measurements, double[] protectionFloors,
            double targetTotalFeet)
        {
            int n = measurements.ColumnCount;
            double[] floors = new double[n];
            double protectionTotal = Total(protectionFloors);
            if (protectionTotal <= targetTotalFeet)
            {
                Array.Copy(protectionFloors, floors, n);
                return floors;
            }

            double extraTarget = targetTotalFeet
                - n * measurements.MinColumnFeet;
            double[] capacities = new double[n];
            for (int i = 0; i < n; i++)
            {
                capacities[i] = measurements.ProtectedColumns[i]
                    ? protectionFloors[i] - measurements.MinColumnFeet
                    : 0.0;
            }

            double[] extra = AllocateExtra(capacities, extraTarget,
                capacities);
            for (int i = 0; i < n; i++)
            {
                floors[i] = measurements.ProtectedColumns[i]
                    ? measurements.MinColumnFeet + extra[i]
                    : measurements.MinColumnFeet;
            }
            return floors;
        }

        private static double[] BuildBaseline(
            FitContentMeasurements measurements, double[] comfortableFeet,
            double[] lowerBounds, double targetTotalFeet)
        {
            int n = measurements.ColumnCount;
            double comfortableTotal = Total(comfortableFeet);
            double[] result = (double[])comfortableFeet.Clone();
            if (comfortableTotal <= targetTotalFeet)
            {
                double extraTarget = targetTotalFeet - comfortableTotal;
                double[] capacities = new double[n];
                for (int i = 0; i < n; i++)
                {
                    capacities[i] = measurements.MaxColumnFeet
                        - comfortableFeet[i];
                }
                double[] extra = AllocateExtra(
                    new List<double>(measurements.TextWeights),
                    extraTarget, capacities);
                for (int i = 0; i < n; i++)
                {
                    result[i] = comfortableFeet[i] + extra[i];
                }
                return result;
            }

            List<int> order = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                order.Add(i);
            }
            order.Sort(delegate(int left, int right)
            {
                int comparison = comfortableFeet[right]
                    .CompareTo(comfortableFeet[left]);
                return comparison != 0
                    ? comparison
                    : left.CompareTo(right);
            });

            double reduction = comfortableTotal - targetTotalFeet;
            for (int i = 0; i < order.Count && reduction > 0.0; i++)
            {
                int column = order[i];
                double room = result[column] - lowerBounds[column];
                if (room <= 0.0)
                {
                    continue;
                }

                double subtract = Math.Min(reduction, room);
                result[column] -= subtract;
                reduction -= subtract;
            }
            return result;
        }

        private static double[] AllocateExtra(IList<double> weights,
            double targetTotalFeet, IList<double> capacities)
        {
            double[] zeroFloors = new double[capacities.Count];
            FitToTotalResult result = FitToTotalFeet(weights, targetTotalFeet,
                zeroFloors, capacities);
            return result.Feet;
        }

        private static FinalFitDiagnostics GetFinalFitDiagnostics(
            FitContentMeasurements measurements, double[] feet)
        {
            List<int> protectionFailures = new List<int>();
            List<FitContentMergeDeficit> unmetMerges =
                new List<FitContentMergeDeficit>();
            bool comfortable = true;

            for (int column = 0; column < measurements.ColumnCount;
                column++)
            {
                if (measurements.ProtectedColumns[column]
                    && feet[column]
                        < measurements.ProtectionRequiredFeet[column]
                            - Tolerance)
                {
                    protectionFailures.Add(column);
                    comfortable = false;
                }
            }

            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                double actual = SpanTotal(feet, cell.Column,
                    checked(cell.Column + cell.ColumnSpan - 1));
                if (cell.ColumnSpan == 1)
                {
                    if (feet[cell.Column] < cell.PreferredFeet - Tolerance)
                    {
                        comfortable = false;
                    }
                }
                else if (cell.PreferredFeet - actual > Tolerance)
                {
                    unmetMerges.Add(new FitContentMergeDeficit(
                        cell.Row, cell.Column, cell.RowSpan,
                        cell.ColumnSpan, cell.PreferredFeet, actual));
                    comfortable = false;
                }
            }

            return new FinalFitDiagnostics
            {
                Comfortable = comfortable,
                ProtectionFailedColumns = protectionFailures,
                UnmetMerges = unmetMerges
            };
        }

        private static void RunLocalSearch(FitContentSearch search,
            double targetTotalFeet, double[] lowerBounds,
            double[] upperBounds)
        {
            int[] divisors =
            {
                LayoutMetrics.FitContentCoarseDivisor,
                LayoutMetrics.FitContentMediumDivisor,
                LayoutMetrics.FitContentFineDivisor
            };
            int[] budgets = { 683, 683, 682 };

            for (int resolution = 0; resolution < divisors.Length;
                resolution++)
            {
                double quantum = Math.Max(4.0 * Tolerance,
                    targetTotalFeet / divisors[resolution]);
                int resolutionTrials = 0;
                while (resolutionTrials < budgets[resolution])
                {
                    int[] receivers = search.RankReceivers();
                    int[] donors = search.RankDonors(lowerBounds);
                    bool accepted = false;
                    for (int receiver = 0; receiver < receivers.Length;
                        receiver++)
                    {
                        int receiverColumn = receivers[receiver];
                        for (int donor = 0; donor < donors.Length; donor++)
                        {
                            int donorColumn = donors[donor];
                            if (receiverColumn == donorColumn)
                            {
                                continue;
                            }

                            double donorRoom = search.Widths[donorColumn]
                                - lowerBounds[donorColumn];
                            double receiverRoom = upperBounds[receiverColumn]
                                - search.Widths[receiverColumn];
                            double amount = Math.Min(quantum,
                                Math.Min(donorRoom, receiverRoom));
                            if (amount <= 0.0)
                            {
                                continue;
                            }

                            resolutionTrials++;
                            search.Trials++;
                            if (search.TryTransfer(receiverColumn,
                                donorColumn, amount))
                            {
                                accepted = true;
                                break;
                            }
                            if (resolutionTrials >= budgets[resolution])
                            {
                                break;
                            }
                        }
                        if (accepted
                            || resolutionTrials >= budgets[resolution])
                        {
                            break;
                        }
                    }

                    if (!accepted)
                    {
                        break;
                    }
                }
            }
        }

        private sealed class ComfortableFit
        {
            internal double[] Feet;
            internal double TotalFeet;
            internal bool Limited;
        }

        private sealed class FinalFitDiagnostics
        {
            internal bool Comfortable;
            internal List<int> ProtectionFailedColumns;
            internal List<FitContentMergeDeficit> UnmetMerges;
        }

        private sealed class FitContentSearch
        {
            private readonly FitContentMeasurements _measurements;
            private readonly int _columnCount;
            private readonly int _rowCount;
            private readonly IReadOnlyList<FitContentMeasuredCell> _cells;
            private readonly double[] _comfortableFeet;
            private readonly List<int>[] _anchorsByColumn;
            private readonly List<int>[] _anchorsByRow;
            private readonly int[] _anchorAt;
            private readonly int[] _anchorMarks;
            private readonly int[] _candidateRowMarks;
            private readonly long[] _lines;
            private readonly long[] _contributions;
            private readonly double[] _spans;
            private readonly double[] _mergeDeficits;
            private readonly long[] _rowMaximums;
            private readonly long[] _candidateLines;
            private readonly long[] _candidateContributions;
            private readonly double[] _candidateSpans;
            private readonly double[] _candidateMergeDeficits;
            private readonly long[] _candidateRowMaximums;
            private readonly double _mergeDenominator;
            private readonly double _distanceScale;
            private int _mark;
            private double _mergeDeficitTotal;
            private double _distanceFromComfortable;

            internal FitContentSearch(FitContentMeasurements measurements,
                double[] initialFeet, double[] comfortableFeet)
            {
                _measurements = measurements;
                _columnCount = measurements.ColumnCount;
                _rowCount = measurements.RowCount;
                _cells = measurements.Cells;
                _comfortableFeet = (double[])comfortableFeet.Clone();
                Widths = (double[])initialFeet.Clone();

                _anchorsByColumn = new List<int>[_columnCount];
                for (int column = 0; column < _columnCount; column++)
                {
                    _anchorsByColumn[column] = new List<int>();
                }
                _anchorsByRow = new List<int>[_rowCount];
                for (int row = 0; row < _rowCount; row++)
                {
                    _anchorsByRow[row] = new List<int>();
                }

                _anchorAt = new int[checked(_rowCount * _columnCount)];
                for (int i = 0; i < _anchorAt.Length; i++)
                {
                    _anchorAt[i] = -1;
                }

                int anchorCount = _cells.Count;
                _anchorMarks = new int[anchorCount];
                _candidateRowMarks = new int[_rowCount];
                _lines = new long[anchorCount];
                _contributions = new long[anchorCount];
                _spans = new double[anchorCount];
                _mergeDeficits = new double[anchorCount];
                _rowMaximums = new long[_rowCount];
                _candidateLines = new long[anchorCount];
                _candidateContributions = new long[anchorCount];
                _candidateSpans = new double[anchorCount];
                _candidateMergeDeficits = new double[anchorCount];
                _candidateRowMaximums = new long[_rowCount];

                double largestMergedPreferred = 0.0;
                for (int i = 0; i < anchorCount; i++)
                {
                    FitContentMeasuredCell cell = _cells[i];
                    if (cell.ColumnSpan > 1
                        && cell.PreferredFeet > largestMergedPreferred)
                    {
                        largestMergedPreferred = cell.PreferredFeet;
                    }
                }
                // Deficits are stored already divided by this denominator, so
                // each term is at most 1 and their sum stays finite.
                _mergeDenominator = Math.Max(1.0,
                    Math.Max(_columnCount * measurements.MaxColumnFeet,
                        largestMergedPreferred));

                for (int i = 0; i < anchorCount; i++)
                {
                    FitContentMeasuredCell cell = _cells[i];
                    int rowEnd = checked(cell.Row + cell.RowSpan);
                    int columnEnd = checked(cell.Column + cell.ColumnSpan);
                    _spans[i] = SpanTotal(Widths, cell.Column,
                        columnEnd - 1);
                    _lines[i] = FitContentText.EstimateLineCount(cell,
                        _spans[i], measurements.WidthScale);
                    _contributions[i] = Contribution(_lines[i],
                        cell.RowSpan);
                    _mergeDeficits[i] = NormalizedMergeDeficit(cell,
                        _spans[i]);
                    _mergeDeficitTotal += _mergeDeficits[i];

                    for (int row = cell.Row; row < rowEnd; row++)
                    {
                        _anchorsByRow[row].Add(i);
                        for (int column = cell.Column;
                            column < columnEnd; column++)
                        {
                            _anchorsByColumn[column].Add(i);
                            _anchorAt[checked(row * _columnCount + column)] = i;
                        }
                    }
                }

                _distanceScale = Math.Max(measurements.MaxColumnFeet,
                    Tolerance);
                RecomputeRows();
                RecomputeDistance();
            }

            internal double[] Widths { get; private set; }

            private double NormalizedMergeDeficit(FitContentMeasuredCell cell,
                double spanFeet)
            {
                return cell.ColumnSpan > 1
                    ? Math.Max(0.0, cell.PreferredFeet - spanFeet)
                        / _mergeDenominator
                    : 0.0;
            }
            internal long RowLineTotal { get; private set; }
            internal long TallestRowLines { get; private set; }
            internal int Trials { get; set; }
            internal bool BudgetExhausted
            {
                get
                {
                    return Trials >= LayoutMetrics.FitContentMaxTransferTrials;
                }
            }

            internal double[] CopyWidths()
            {
                return (double[])Widths.Clone();
            }

            internal int[] RankReceivers()
            {
                long[] pressure = new long[_columnCount];
                for (int row = 0; row < _rowCount; row++)
                {
                    int rowOffset = checked(row * _columnCount);
                    for (int column = 0; column < _columnCount;
                        column++)
                    {
                        int anchor = _anchorAt[rowOffset + column];
                        if (anchor < 0)
                        {
                            continue;
                        }

                        long contribution = _contributions[anchor];
                        if (contribution > 1L)
                        {
                            pressure[column] += contribution - 1L;
                        }
                    }
                }

                List<int> result = new List<int>(_columnCount);
                for (int column = 0; column < _columnCount; column++)
                {
                    result.Add(column);
                }
                result.Sort(delegate(int left, int right)
                {
                    int comparison = pressure[right].CompareTo(
                        pressure[left]);
                    return comparison != 0
                        ? comparison
                        : left.CompareTo(right);
                });
                return result.ToArray();
            }

            internal int[] RankDonors(double[] lowerBounds)
            {
                List<int> result = new List<int>(_columnCount);
                for (int column = 0; column < _columnCount; column++)
                {
                    result.Add(column);
                }
                result.Sort(delegate(int left, int right)
                {
                    double leftRoom = Widths[left] - lowerBounds[left];
                    double rightRoom = Widths[right]
                        - lowerBounds[right];
                    int comparison = rightRoom.CompareTo(leftRoom);
                    return comparison != 0
                        ? comparison
                        : left.CompareTo(right);
                });
                return result.ToArray();
            }

            internal bool TryTransfer(int receiver, int donor,
                double amount)
            {
                double donorBefore = Widths[donor];
                double receiverBefore = Widths[receiver];
                Widths[donor] = donorBefore - amount;
                Widths[receiver] = receiverBefore + amount;

                int mark = NextMark();
                List<int> affectedAnchors = new List<int>();
                AddAffectedAnchors(_anchorsByColumn[donor], mark,
                    affectedAnchors);
                AddAffectedAnchors(_anchorsByColumn[receiver], mark,
                    affectedAnchors);
                List<int> affectedRows = new List<int>();

                for (int i = 0; i < affectedAnchors.Count; i++)
                {
                    int anchor = affectedAnchors[i];
                    FitContentMeasuredCell cell = _cells[anchor];
                    int columnEnd = checked(cell.Column + cell.ColumnSpan);
                    bool coversDonor = donor >= cell.Column
                        && donor < columnEnd;
                    bool coversReceiver = receiver >= cell.Column
                        && receiver < columnEnd;
                    double delta = (coversReceiver ? amount : 0.0)
                        - (coversDonor ? amount : 0.0);
                    double candidateSpan = _spans[anchor] + delta;
                    _candidateSpans[anchor] = candidateSpan;
                    if (delta == 0.0)
                    {
                        _candidateLines[anchor] = _lines[anchor];
                        _candidateContributions[anchor] =
                            _contributions[anchor];
                    }
                    else
                    {
                        _candidateLines[anchor] =
                            FitContentText.EstimateLineCount(cell,
                                candidateSpan,
                                _measurements.WidthScale);
                        _candidateContributions[anchor] = Contribution(
                            _candidateLines[anchor], cell.RowSpan);
                    }

                    _candidateMergeDeficits[anchor] =
                        NormalizedMergeDeficit(cell, candidateSpan);
                    int rowEnd = checked(cell.Row + cell.RowSpan);
                    for (int row = cell.Row; row < rowEnd; row++)
                    {
                        if (_candidateRowMarks[row] != mark)
                        {
                            _candidateRowMarks[row] = mark;
                            affectedRows.Add(row);
                        }
                    }
                }

                long candidateRowTotal = RowLineTotal;
                for (int i = 0; i < affectedRows.Count; i++)
                {
                    int row = affectedRows[i];
                    long maximum = 0L;
                    List<int> rowAnchors = _anchorsByRow[row];
                    for (int j = 0; j < rowAnchors.Count; j++)
                    {
                        int anchor = rowAnchors[j];
                        long contribution = _anchorMarks[anchor] == mark
                            ? _candidateContributions[anchor]
                            : _contributions[anchor];
                        if (contribution > maximum)
                        {
                            maximum = contribution;
                        }
                    }
                    _candidateRowMaximums[row] = maximum;
                    candidateRowTotal += maximum - _rowMaximums[row];
                }

                long candidateTallest = 0L;
                for (int row = 0; row < _rowCount; row++)
                {
                    long maximum = _candidateRowMarks[row] == mark
                        ? _candidateRowMaximums[row]
                        : _rowMaximums[row];
                    if (maximum > candidateTallest)
                    {
                        candidateTallest = maximum;
                    }
                }

                double candidateMergeTotal = _mergeDeficitTotal;
                for (int i = 0; i < affectedAnchors.Count; i++)
                {
                    int anchor = affectedAnchors[i];
                    candidateMergeTotal += _candidateMergeDeficits[anchor]
                        - _mergeDeficits[anchor];
                }

                double candidateDistance = _distanceFromComfortable;
                candidateDistance -= NormalizedSquare(
                    donorBefore - _comfortableFeet[donor]);
                candidateDistance -= NormalizedSquare(
                    receiverBefore - _comfortableFeet[receiver]);
                candidateDistance += NormalizedSquare(
                    Widths[donor] - _comfortableFeet[donor]);
                candidateDistance += NormalizedSquare(
                    Widths[receiver] - _comfortableFeet[receiver]);

                bool better = IsBetter(candidateRowTotal,
                    candidateTallest, candidateMergeTotal,
                    candidateDistance);
                if (!better)
                {
                    Widths[donor] = donorBefore;
                    Widths[receiver] = receiverBefore;
                    return false;
                }

                for (int i = 0; i < affectedAnchors.Count; i++)
                {
                    int anchor = affectedAnchors[i];
                    _spans[anchor] = _candidateSpans[anchor];
                    _lines[anchor] = _candidateLines[anchor];
                    _contributions[anchor] =
                        _candidateContributions[anchor];
                    _mergeDeficits[anchor] =
                        _candidateMergeDeficits[anchor];
                }
                for (int i = 0; i < affectedRows.Count; i++)
                {
                    int row = affectedRows[i];
                    _rowMaximums[row] = _candidateRowMaximums[row];
                }
                _mergeDeficitTotal = candidateMergeTotal;
                _distanceFromComfortable = candidateDistance;
                RowLineTotal = candidateRowTotal;
                TallestRowLines = candidateTallest;
                return true;
            }

            internal void Rebuild(double[] feet)
            {
                Widths = (double[])feet.Clone();
                _mergeDeficitTotal = 0.0;
                for (int i = 0; i < _cells.Count; i++)
                {
                    FitContentMeasuredCell cell = _cells[i];
                    int columnEnd = checked(cell.Column + cell.ColumnSpan);
                    _spans[i] = SpanTotal(Widths, cell.Column,
                        columnEnd - 1);
                    _lines[i] = FitContentText.EstimateLineCount(cell,
                        _spans[i], _measurements.WidthScale);
                    _contributions[i] = Contribution(_lines[i],
                        cell.RowSpan);
                    _mergeDeficits[i] = NormalizedMergeDeficit(cell,
                        _spans[i]);
                    _mergeDeficitTotal += _mergeDeficits[i];
                }
                RecomputeRows();
                RecomputeDistance();
            }

            private void AddAffectedAnchors(IList<int> source, int mark,
                List<int> destination)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    int anchor = source[i];
                    if (_anchorMarks[anchor] == mark)
                    {
                        continue;
                    }
                    _anchorMarks[anchor] = mark;
                    destination.Add(anchor);
                }
            }

            private int NextMark()
            {
                if (_mark == int.MaxValue)
                {
                    Array.Clear(_anchorMarks, 0, _anchorMarks.Length);
                    Array.Clear(_candidateRowMarks, 0,
                        _candidateRowMarks.Length);
                    _mark = 1;
                    return _mark;
                }

                _mark++;
                return _mark;
            }

            private void RecomputeRows()
            {
                Array.Clear(_rowMaximums, 0, _rowMaximums.Length);
                RowLineTotal = 0L;
                TallestRowLines = 0L;
                for (int row = 0; row < _rowCount; row++)
                {
                    long maximum = 0L;
                    List<int> rowAnchors = _anchorsByRow[row];
                    for (int i = 0; i < rowAnchors.Count; i++)
                    {
                        long contribution =
                            _contributions[rowAnchors[i]];
                        if (contribution > maximum)
                        {
                            maximum = contribution;
                        }
                    }
                    _rowMaximums[row] = maximum;
                    RowLineTotal += maximum;
                    if (maximum > TallestRowLines)
                    {
                        TallestRowLines = maximum;
                    }
                }
            }

            private void RecomputeDistance()
            {
                _distanceFromComfortable = 0.0;
                for (int column = 0; column < _columnCount;
                    column++)
                {
                    _distanceFromComfortable += NormalizedSquare(
                        Widths[column] - _comfortableFeet[column]);
                }
            }

            private double NormalizedSquare(double difference)
            {
                double normalized = difference / _distanceScale;
                return normalized * normalized;
            }

            private static long Contribution(long lines, int rowSpan)
            {
                long contribution = lines / rowSpan;
                if (lines % rowSpan != 0L)
                {
                    contribution++;
                }
                return contribution < 1L ? 1L : contribution;
            }

            private bool IsBetter(long candidateRowTotal,
                long candidateTallest, double candidateMerge,
                double candidateDistance)
            {
                if (candidateRowTotal != RowLineTotal)
                {
                    return candidateRowTotal < RowLineTotal;
                }
                if (candidateTallest != TallestRowLines)
                {
                    return candidateTallest < TallestRowLines;
                }

                double currentMerge = _mergeDeficitTotal;
                if (candidateMerge < currentMerge
                    - LayoutMetrics.FitContentObjectiveTolerance)
                {
                    return true;
                }
                if (candidateMerge > currentMerge
                    + LayoutMetrics.FitContentObjectiveTolerance)
                {
                    return false;
                }

                if (candidateDistance < _distanceFromComfortable
                    - LayoutMetrics.FitContentObjectiveTolerance)
                {
                    return true;
                }
                return false;
            }
        }

        // ------------------------------------------------------------- Validate

        /// <summary>
        /// Silently clamps every width into [Min, Max] (NaN/Infinity -> Min/Max).
        /// A convenience transform; use <see cref="ValidateAll"/> when you need to
        /// know WHETHER the input was valid before applying to Revit.
        /// </summary>
        public static double[] ClampAll(IList<double> feet)
        {
            int n = feet != null ? feet.Count : 0;
            double[] r = new double[n];
            for (int i = 0; i < n; i++) r[i] = ColumnWidthMath.Clamp(feet[i]);
            return r;
        }

        /// <summary>
        /// Validates candidate widths before applying to Revit. The two-argument
        /// overload uses only calculated-width validation; the overload with a
        /// current-width baseline preserves a positive live width when the
        /// candidate is within Tolerance of it.
        /// </summary>
        public static ValidationResult ValidateAll(IList<double> feet, double? maxTotalFeet)
        {
            return ValidateAll(feet, maxTotalFeet, null);
        }

        /// <summary>
        /// Validates candidates and optionally preserves widths that already match
        /// the live header. The baseline is never modified and must be a complete
        /// positive finite vector when supplied.
        /// </summary>
        public static ValidationResult ValidateAll(
            IList<double> feet, double? maxTotalFeet, IList<double> currentFeet)
        {
            int n = feet != null ? feet.Count : 0;
            double min = LayoutMetrics.MinColumnFeet;
            double max = LayoutMetrics.MaxColumnFeet;

            if (currentFeet != null)
            {
                if (currentFeet.Count != n)
                {
                    throw new ArgumentException(
                        "The current-width baseline must match the candidate count.",
                        "currentFeet");
                }
                double currentTotal = 0.0;
                for (int i = 0; i < currentFeet.Count; i++)
                {
                    double current = currentFeet[i];
                    if (current <= 0.0
                        || double.IsNaN(current)
                        || double.IsInfinity(current))
                    {
                        throw new ArgumentException(
                            "The current-width baseline must contain positive finite values.",
                            "currentFeet");
                    }
                    currentTotal += current;
                }
                if (double.IsNaN(currentTotal) || double.IsInfinity(currentTotal))
                {
                    throw new ArgumentException(
                        "The current-width baseline total must be finite.",
                        "currentFeet");
                }
            }

            double[] norm = new double[n];
            List<int> invalid = new List<int>();
            List<int> nonFinite = new List<int>();
            List<int> outOfRange = new List<int>();
            for (int i = 0; i < n; i++)
            {
                double v = feet[i];
                if (currentFeet != null
                    && v > 0.0
                    && !double.IsNaN(v)
                    && !double.IsInfinity(v)
                    && Math.Abs(v - currentFeet[i]) <= Tolerance)
                {
                    norm[i] = currentFeet[i];
                    continue;
                }

                bool bad = false;
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    nonFinite.Add(i);
                    bad = true;
                }
                else if (v < min - Tolerance || v > max + Tolerance)
                {
                    outOfRange.Add(i);
                    bad = true;
                }
                if (bad) invalid.Add(i);
                norm[i] = ColumnWidthMath.Clamp(v);
            }

            double total = Total(norm);
            bool exceeds = maxTotalFeet.HasValue && total > maxTotalFeet.Value + Tolerance;

            ValidationResult vr = new ValidationResult
            {
                NormalizedFeet = norm,
                InvalidColumns = invalid.ToArray(),
                NonFiniteColumns = nonFinite.ToArray(),
                OutOfRangeColumns = outOfRange.ToArray(),
                MaxTotalFeet = maxTotalFeet,
                TotalFeet = total,
                ExceedsTotalLimit = exceeds
            };
            vr.IsValid = invalid.Count == 0 && !exceeds;
            return vr;
        }

        private static double[] CopyAndValidateBounds(
            IList<double> minimumFeet, IList<double> maximumFeet,
            int count, string minimumName, string maximumName)
        {
            if (minimumFeet == null)
            {
                throw new ArgumentNullException(minimumName);
            }
            if (maximumFeet == null)
            {
                throw new ArgumentNullException(maximumName);
            }
            if (minimumFeet.Count != count)
            {
                throw new ArgumentException(
                    "The minimum bounds must match the column count.",
                    minimumName);
            }
            if (maximumFeet.Count != count)
            {
                throw new ArgumentException(
                    "The maximum bounds must match the column count.",
                    maximumName);
            }

            double[] lower = new double[count];
            for (int i = 0; i < count; i++)
            {
                double minimum = minimumFeet[i];
                double maximum = maximumFeet[i];
                if (minimum < 0.0 || double.IsNaN(minimum)
                    || double.IsInfinity(minimum))
                {
                    throw new ArgumentException(
                        "Bounds must be finite and nonnegative.",
                        minimumName);
                }
                if (maximum < 0.0 || double.IsNaN(maximum)
                    || double.IsInfinity(maximum))
                {
                    throw new ArgumentException(
                        "Bounds must be finite and nonnegative.",
                        maximumName);
                }
                if (minimum > maximum)
                {
                    throw new ArgumentException(
                        "Each minimum bound must not exceed its maximum bound.",
                        minimumName);
                }
                lower[i] = minimum;
            }
            return lower;
        }

        private static double[] CopyAndValidateUpperBounds(
            IList<double> maximumFeet, int count)
        {
            double[] upper = new double[count];
            for (int i = 0; i < count; i++)
            {
                upper[i] = maximumFeet[i];
            }
            return upper;
        }

        private static void ValidateScalarBounds(double minimumFeet,
            double maximumFeet, string minimumName, string maximumName)
        {
            if (minimumFeet < 0.0 || double.IsNaN(minimumFeet)
                || double.IsInfinity(minimumFeet))
            {
                throw new ArgumentException(
                    "Bounds must be finite and nonnegative.", minimumName);
            }
            if (maximumFeet < 0.0 || double.IsNaN(maximumFeet)
                || double.IsInfinity(maximumFeet))
            {
                throw new ArgumentException(
                    "Bounds must be finite and nonnegative.", maximumName);
            }
            if (minimumFeet > maximumFeet)
            {
                throw new ArgumentException(
                    "The minimum bound must not exceed the maximum bound.",
                    minimumName);
            }
        }

        private static double SumFinite(double[] values, string message)
        {
            if (values.Length == 0)
            {
                return 0.0;
            }

            bool allEqual = true;
            double first = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] != first)
                {
                    allEqual = false;
                    break;
                }
            }
            if (allEqual)
            {
                double repeatedTotal = values.Length * first;
                if (double.IsNaN(repeatedTotal)
                    || double.IsInfinity(repeatedTotal))
                {
                    throw new ArgumentException(message);
                }
                return repeatedTotal;
            }

            double total = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                total += values[i];
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    throw new ArgumentException(message);
                }
            }
            return total;
        }

        private static double ClampToBounds(double value, double minimum,
            double maximum)
        {
            if (double.IsNaN(value) || value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }

        private static double RepairResidual(double[] result,
            double targetTotalFeet, double[] minimumFeet,
            double[] maximumFeet)
        {
            double residual = targetTotalFeet - Total(result);
            for (int i = 0; i < result.Length
                && Math.Abs(residual) > Tolerance; i++)
            {
                double room = residual > 0.0
                    ? maximumFeet[i] - result[i]
                    : result[i] - minimumFeet[i];
                if (room <= 0.0)
                {
                    continue;
                }

                double adjustment = residual > 0.0
                    ? Math.Min(residual, room)
                    : Math.Max(residual, -room);
                result[i] += adjustment;
                residual -= adjustment;
            }
            return residual;
        }

        private static double SpanTotal(double[] feet, int firstColumn,
            int lastColumn)
        {
            double total = 0.0;
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                total += feet[column];
            }
            return total;
        }

        private static double[] CreateUniformWidths(int count, double value)
        {
            double[] result = new double[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = value;
            }
            return result;
        }

        private static double Total(double[] a)
        {
            double s = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                s += a[i];
            }
            return s;
        }
    }
}
