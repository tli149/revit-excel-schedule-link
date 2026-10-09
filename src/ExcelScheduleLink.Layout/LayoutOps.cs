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
        /// <summary>True if every width is finite and within Tolerance of [Min, Max] and the total is within the safe limit.</summary>
        public bool IsValid;
        /// <summary>Clamped, finite copy of the input (always safe to apply).</summary>
        public double[] NormalizedFeet;
        /// <summary>Indices whose input value was NaN/Infinity or outside [Min, Max] by more than Tolerance.</summary>
        public int[] InvalidColumns;
        /// <summary>Human-readable reason when not valid; null otherwise.</summary>
        public string Reason;
        /// <summary>Sum of NormalizedFeet.</summary>
        public double TotalFeet;
        /// <summary>True if TotalFeet exceeds the caller-supplied safe total limit.</summary>
        public bool ExceedsTotalLimit;
    }

    /// <summary>
    /// Pure Layout-tool operations (no Revit / no Excel types). All widths are
    /// feet (paper space). Every returned width is finite and clamped into
    /// [<see cref="LayoutMetrics.MinColumnFeet"/>, <see cref="LayoutMetrics.MaxColumnFeet"/>].
    ///
    /// Scope note (design v0.8.0 5.7): the merged-cell width guarantee
    /// (<see cref="ApplyMergeConstraints"/>) is applied ONLY by Fit Content.
    /// Distribute Evenly / Fit to Total / manual numeric edits are deliberate
    /// narrowing operations and are allowed to let merged content wrap.
    /// </summary>
    public static class LayoutOps
    {
        /// <summary>Floating-point tolerance used ONLY for final total/residual comparisons (feet).</summary>
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
            double min = LayoutMetrics.MinColumnFeet;
            double max = LayoutMetrics.MaxColumnFeet;
            bool targetFinite = !double.IsNaN(targetTotalFeet) && !double.IsInfinity(targetTotalFeet);

            if (n == 0)
            {
                FitToTotalResult e = new FitToTotalResult { Feet = new double[0] };
                if (!targetFinite)
                {
                    e.Achievable = false; e.UnresolvedDeficit = 0.0;
                    e.Reason = "Target total is not a finite number.";
                }
                else if (Math.Abs(targetTotalFeet) <= Tolerance)
                {
                    e.Achievable = true;
                }
                else
                {
                    e.Achievable = false; e.UnresolvedDeficit = targetTotalFeet;
                    e.Reason = "No columns, but the target total is non-zero.";
                }
                return e;
            }

            // Sanitize current into finite, non-negative ratio weights (never mutate input).
            double[] w = new double[n];
            bool hadInvalidCurrent = false;
            for (int i = 0; i < n; i++)
            {
                double v = current[i];
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0) { w[i] = 0.0; hadInvalidCurrent = true; }
                else { w[i] = v; }
            }

            if (!targetFinite)
            {
                return Uniform(n, min, false, 0.0, "Target total is not a finite number.");
            }

            double feasibleMin = n * min;
            double feasibleMax = n * max;
            if (targetTotalFeet < feasibleMin)   // strict: no tolerance loosening
            {
                return Uniform(n, min, false, targetTotalFeet - feasibleMin,
                    "Target total is below the minimum possible (columns x min width).");
            }
            if (targetTotalFeet > feasibleMax)   // strict
            {
                return Uniform(n, max, false, targetTotalFeet - feasibleMax,
                    "Target total is above the maximum possible (columns x max width).");
            }

            bool[] fixedCol = new bool[n];
            double[] result = new double[n];
            double remaining = targetTotalFeet;

            while (true)
            {
                int freeCount = 0;
                double maxW = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (fixedCol[i]) continue;
                    freeCount++;
                    if (w[i] > maxW) maxW = w[i];
                }
                if (freeCount == 0) break;

                // Normalize by the largest free weight: preserves tiny ratios and
                // prevents the weight sum from overflowing to Infinity.
                bool evenMode = maxW <= 0.0; // only when every free weight is exactly zero
                double sumNorm = 0.0;
                if (!evenMode)
                {
                    for (int i = 0; i < n; i++) if (!fixedCol[i]) sumNorm += w[i] / maxW;
                }

                bool newFix = false;
                for (int i = 0; i < n; i++)
                {
                    if (fixedCol[i]) continue;
                    double cand = evenMode ? remaining / freeCount : remaining * (w[i] / maxW) / sumNorm;
                    if (cand < min) { result[i] = min; fixedCol[i] = true; remaining -= min; newFix = true; }
                    else if (cand > max) { result[i] = max; fixedCol[i] = true; remaining -= max; newFix = true; }
                }

                if (!newFix)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (fixedCol[i]) continue;
                        result[i] = evenMode ? remaining / freeCount : remaining * (w[i] / maxW) / sumNorm;
                        fixedCol[i] = true;
                    }
                    break;
                }
            }

            // Safety: guarantee finite + bounds.
            for (int i = 0; i < n; i++) result[i] = ColumnWidthMath.Clamp(result[i]);

            // Absorb any small residual into a column that has room, keeping bounds.
            double residual = targetTotalFeet - Total(result);
            for (int i = 0; i < n && Math.Abs(residual) > Tolerance; i++)
            {
                double room = residual > 0 ? (max - result[i]) : (result[i] - min);
                if (room <= 0.0) continue;
                double add = residual > 0 ? Math.Min(residual, room) : Math.Max(residual, -room);
                result[i] += add;
                residual -= add;
            }

            FitToTotalResult r = new FitToTotalResult { Feet = result };
            bool sumOk = Math.Abs(targetTotalFeet - Total(result)) <= Tolerance;
            r.Achievable = sumOk && !hadInvalidCurrent;
            if (!r.Achievable)
            {
                r.Reason = hadInvalidCurrent
                    ? "Current widths contained invalid (non-finite or negative) values."
                    : "Could not distribute the target within the allowed column widths.";
                r.UnresolvedDeficit = sumOk ? 0.0 : residual;
            }
            return r;
        }

        private static FitToTotalResult Uniform(int n, double value, bool achievable, double deficit, string reason)
        {
            double[] feet = new double[n];
            for (int i = 0; i < n; i++) feet[i] = value;
            return new FitToTotalResult { Feet = feet, Achievable = achievable, UnresolvedDeficit = deficit, Reason = reason };
        }

        // --------------------------------------------------- Merge constraints

        /// <summary>
        /// Ensures each merged span's total width is at least its content's
        /// required width, adding the deficit across the span (proportional to
        /// each column's remaining room up to Max). Used by Fit Content only.
        ///
        /// The same "room &gt; 0" column set is used both to size the total room
        /// and to distribute, and each adjusted column is finally clamped to Max,
        /// so a span whose columns each have only a tiny (&lt;= Tolerance) room but
        /// a meaningful combined room is still filled (or its remaining deficit is
        /// reported). The working widths are clamped to [Min, Max] first (input
        /// array is never mutated); merge ranges with a non-finite/non-positive
        /// RequiredFeet or an out-of-range span are skipped. Assumes Excel's valid
        /// merged ranges do not overlap, so per-range adjustments are independent.
        /// </summary>
        public static MergeFitResult ApplyMergeConstraints(double[] feet, IEnumerable<MergeRequirement> merges)
        {
            int n = feet != null ? feet.Length : 0;
            double[] result = new double[n];
            for (int i = 0; i < n; i++) result[i] = ColumnWidthMath.Clamp(feet[i]); // finite + bounds, no mutation
            double max = LayoutMetrics.MaxColumnFeet;
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
                        double room = max - result[c];
                        if (room > 0.0) totalRoom += room;
                    }
                    if (totalRoom <= 0.0) { unresolved += deficit; continue; }

                    double add = Math.Min(deficit, totalRoom);
                    for (int c = m.FirstColumn; c <= m.LastColumn; c++)
                    {
                        double room = max - result[c];
                        if (room <= 0.0) continue;
                        result[c] += add * (room / totalRoom);
                        if (result[c] > max) result[c] = max; // final clamp
                    }
                    if (deficit > totalRoom) unresolved += (deficit - totalRoom);
                }
            }

            return new MergeFitResult { Feet = result, UnresolvedDeficit = unresolved };
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
        /// Validates candidate widths before applying to Revit. A width is accepted
        /// (not flagged) when it is finite and within Tolerance of [Min, Max] - the
        /// Tolerance band absorbs unit-conversion rounding - and NormalizedFeet is
        /// the clamped copy actually safe to apply (so a value 1e-9 over Max is
        /// treated as valid and stored as Max, not rejected). Values that are
        /// NaN/Infinity or outside [Min, Max] by more than Tolerance are reported in
        /// InvalidColumns. If <paramref name="maxTotalFeet"/> is given (e.g. a Revit
        /// section limit), an over-limit total is also flagged. The input array is
        /// never mutated.
        /// </summary>
        public static ValidationResult ValidateAll(IList<double> feet, double? maxTotalFeet)
        {
            int n = feet != null ? feet.Count : 0;
            double min = LayoutMetrics.MinColumnFeet;
            double max = LayoutMetrics.MaxColumnFeet;

            double[] norm = new double[n];
            List<int> invalid = new List<int>();
            int nonFinite = 0, outOfRange = 0;
            for (int i = 0; i < n; i++)
            {
                double v = feet[i];
                bool bad = false;
                if (double.IsNaN(v) || double.IsInfinity(v)) { nonFinite++; bad = true; }
                else if (v < min - Tolerance || v > max + Tolerance) { outOfRange++; bad = true; }
                if (bad) invalid.Add(i);
                norm[i] = ColumnWidthMath.Clamp(v);
            }

            double total = Total(norm);
            bool exceeds = maxTotalFeet.HasValue && total > maxTotalFeet.Value + Tolerance;

            ValidationResult vr = new ValidationResult
            {
                NormalizedFeet = norm,
                InvalidColumns = invalid.ToArray(),
                TotalFeet = total,
                ExceedsTotalLimit = exceeds
            };
            vr.IsValid = invalid.Count == 0 && !exceeds;
            if (!vr.IsValid)
            {
                string r = "";
                if (nonFinite > 0) r += nonFinite + " column(s) had a non-finite width. ";
                if (outOfRange > 0) r += outOfRange + " column(s) were outside the allowed width range. ";
                if (exceeds) r += "Total width exceeds the safe limit (" + total.ToString("F3")
                    + " > " + maxTotalFeet.Value.ToString("F3") + " ft). ";
                vr.Reason = r.TrimEnd();
            }
            return vr;
        }

        private static double Total(double[] a)
        {
            double s = 0.0;
            for (int i = 0; i < a.Length; i++) s += a[i];
            return s;
        }
    }
}
