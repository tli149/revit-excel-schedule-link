using System;
using System.Collections.Generic;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>Plain facts about one resolved schedule text style.</summary>
    public sealed class TextStyleFacts
    {
        public bool IsResolvable;
        public bool IsMissing;
        public bool ReadAttempted;
        public bool ReadFailed;
        public bool AccessDenied;
        public string FontName = string.Empty;
        public double RawSize = double.NaN;
        public double SizeInches = double.NaN;
        public double SizePoints = double.NaN;
        public double SizeTableUnits = double.NaN;
        public string ReadError = string.Empty;
    }

    /// <summary>
    /// Plain facts read from one header cell. Row and column are relative to the
    /// header section's FirstRowNumber and FirstColumnNumber.
    /// </summary>
    public sealed class CellStyleFacts
    {
        public int Row;
        public int Column;
        public bool ReadComplete;
        public bool CellTypeReadComplete;
        public bool IsTextCell;
        public long BorderTopLineStyle;
        public long BorderBottomLineStyle;
        public long BorderLeftLineStyle;
        public long BorderRightLineStyle;
        public string FontName = string.Empty;
        public double TextSize;
        public bool IsCellOverridden;
        public bool IsStyleOverridden;
        public bool HasAnyOverrideOption;
        public bool ElementReadDenied;
        public string ReadError = string.Empty;

        public bool HasAllInvalidBorders(long invalidElementId)
        {
            return BorderTopLineStyle == invalidElementId
                && BorderBottomLineStyle == invalidElementId
                && BorderLeftLineStyle == invalidElementId
                && BorderRightLineStyle == invalidElementId;
        }

        public bool HasExplicitOverride
        {
            get { return IsCellOverridden || IsStyleOverridden || HasAnyOverrideOption; }
        }
    }

    /// <summary>Plain observations gathered during one header scan.</summary>
    public sealed class ScheduleStyleFacts
    {
        public bool DimensionsRead;
        public int NumberOfRows;
        public int NumberOfColumns;
        public int FirstRowNumber;
        public int FirstColumnNumber;
        public int ExpectedHeaderCellCount;
        public bool LimitExceeded;
        public bool CellBudgetExceededBeforeScan;
        public int SkippedScheduleCount;
        public TextStyleFacts TitleTextStyle;
        public TextStyleFacts HeaderTextStyle;
        public CellStyleFacts ReferenceCell;
        public IList<CellStyleFacts> Cells = new List<CellStyleFacts>();
        public string FirstReadError = string.Empty;
    }

    public enum StyleIntegrityState
    {
        Healthy,
        Affected,
        Indeterminate
    }

    /// <summary>
    /// Stable reason codes used by the probe and user-flow seams. The enum is
    /// deliberately independent of Revit exception types.
    /// </summary>
    public enum StyleIntegrityReasonCode
    {
        Healthy,
        StylelessCellDetected,
        InvalidFacts,
        DimensionReadFailed,
        ScanLimit,
        TextStyleUnavailable,
        ReferenceCellUnavailable,
        IncompleteScan,
        CellReadFailed,
        AmbiguousInvalidBorders,
        UnexpectedCellType,
        InvalidSchedule,
        InvalidDocument,
        DocumentReadOnly,
        ForeignTransactionActive,
        DocumentNotModifiable,
        TransactionStartFailed,
        TransactionRollbackFailed,
        ElementReadDenied,
        TextStyleReadFailed,
        TableReadFailed,
        AmbiguousTextSizeQuantization
    }

    public sealed class StyleIntegrityCoordinate
    {
        public int Row;
        public int Column;

        public override string ToString()
        {
            return "row=" + Row + " col=" + Column;
        }
    }

    /// <summary>Pure result of applying the schedule style decision table.</summary>
    public sealed class StyleIntegrityResult
    {
        public StyleIntegrityState State;
        public StyleIntegrityReasonCode ReasonCode;
        public int HeaderCellsExpected;
        public int HeaderCellsRead;
        public int StylelessCellCount;
        public int SkippedScheduleCount;
        public IList<StyleIntegrityCoordinate> StylelessCells =
            new List<StyleIntegrityCoordinate>();
        public string FirstReadError = string.Empty;
        public ScheduleStyleFacts Facts;

        // Revit-adapter evidence. These remain strings so Contracts stays free
        // of Revit references and reports can preserve exact API statuses.
        public string ReadTransactionStartStatus = string.Empty;
        public string ReadTransactionRollbackStatus = string.Empty;
        public string ReadTransactionRollbackError = string.Empty;
        public bool? DocumentModifiedBeforeScan;
        public bool? DocumentModifiedAfterRollback;

        public bool IsHealthy { get { return State == StyleIntegrityState.Healthy; } }
    }

    public enum StyleIntegrityScanScope
    {
        Manual,
        Automatic
    }

    /// <summary>Boundaries from design §3.5 and the existing renderer.</summary>
    public static class StyleIntegrityLimits
    {
        public const int MaxRowsPerSchedule = 500;
        public const int MaxColumnsPerSchedule = 50;
        public const int MaxHeaderCellsPerSchedule = 25000;
        public const int MaxManualSchedules = 256;
        public const int MaxAutomaticSchedules = 64;
        public const int MaxAutomaticHeaderCells = 100000;

        public struct RenderedSize
        {
            public RenderedSize(int rows, int columns)
            {
                Rows = rows;
                Columns = columns;
            }

            public readonly int Rows;
            public readonly int Columns;
        }

        public static RenderedSize GetEffectiveRenderedSize(int rows, int columns)
        {
            return new RenderedSize(
                Math.Min(rows, MaxRowsPerSchedule),
                Math.Min(columns, MaxColumnsPerSchedule));
        }

        public static bool ExceedsScheduleLimit(int rows, int columns)
        {
            RenderedSize effective = GetEffectiveRenderedSize(rows, columns);
            long cells = (long)rows * columns;
            return rows < 0 || columns < 0
                || rows != effective.Rows
                || columns != effective.Columns
                || cells > MaxHeaderCellsPerSchedule;
        }

        public static int MaxSchedules(StyleIntegrityScanScope scope)
        {
            return scope == StyleIntegrityScanScope.Automatic
                ? MaxAutomaticSchedules : MaxManualSchedules;
        }

        public static long MaxHeaderCells(StyleIntegrityScanScope scope)
        {
            return scope == StyleIntegrityScanScope.Automatic
                ? MaxAutomaticHeaderCells : long.MaxValue;
        }

        public static bool ExceedsHeaderCellBudget(
            StyleIntegrityScanScope scope, long cellsScanned, int expectedCells)
        {
            if (scope != StyleIntegrityScanScope.Automatic)
            {
                return false;
            }
            if (cellsScanned < 0 || expectedCells < 0)
            {
                return true;
            }
            return cellsScanned > MaxAutomaticHeaderCells
                || expectedCells > MaxAutomaticHeaderCells - cellsScanned;
        }
    }

    /// <summary>
    /// Dependency-free implementation of design §§3.1-3.3. It never knows
    /// about Revit objects, fonts, or exception types.
    /// </summary>
    public static class StyleIntegrityDecisionTable
    {
        public const long DefaultInvalidElementId = -1L;
        public const double ExactSizeToleranceTableUnits = 0.01;
        public const double QuantizedAmbiguousSizeToleranceTableUnits = 0.5;

        public enum TextPairMatchKind
        {
            Exact,
            QuantizedAmbiguous,
            Mismatch
        }

        public static TextPairMatchKind ClassifyTextPair(
            string fontName, double sizeTableUnits, TextStyleFacts expected)
        {
            if (expected == null || !expected.IsResolvable)
            {
                return TextPairMatchKind.Mismatch;
            }

            string actualFont = (fontName ?? string.Empty).Trim();
            string expectedFont = (expected.FontName ?? string.Empty).Trim();
            if (!string.Equals(actualFont, expectedFont, StringComparison.OrdinalIgnoreCase))
            {
                return TextPairMatchKind.Mismatch;
            }

            double difference = Math.Abs(sizeTableUnits - expected.SizeTableUnits);
            if (double.IsNaN(difference) || double.IsInfinity(difference))
            {
                return TextPairMatchKind.Mismatch;
            }
            if (difference <= ExactSizeToleranceTableUnits)
            {
                return TextPairMatchKind.Exact;
            }
            if (difference <= QuantizedAmbiguousSizeToleranceTableUnits)
            {
                return TextPairMatchKind.QuantizedAmbiguous;
            }
            return TextPairMatchKind.Mismatch;
        }

        public static bool TextPairMatches(
            string fontName, double sizeTableUnits, TextStyleFacts expected)
        {
            return ClassifyTextPair(fontName, sizeTableUnits, expected)
                != TextPairMatchKind.Mismatch;
        }

        public static bool CellMatches(
            CellStyleFacts cell, TextStyleFacts expected)
        {
            return cell != null && cell.ReadComplete
                && TextPairMatches(cell.FontName, cell.TextSize, expected);
        }

        /// <summary>
        /// Returns true only for the strict styleless rule applied to an
        /// ordinary header cell. The reference baseline is included only when
        /// the reference itself is completely read and is not styleless.
        /// An all-invalid cell with any explicit override is intentionally false.
        /// </summary>
        public static bool IsStylelessCell(
            CellStyleFacts cell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            CellStyleFacts referenceCell,
            long invalidElementId = DefaultInvalidElementId)
        {
            if (cell == null || !cell.ReadComplete
                || !cell.HasAllInvalidBorders(invalidElementId)
                || cell.HasExplicitOverride)
            {
                return false;
            }

            TextStyleFacts referenceBaseline = ReferenceBaseline(
                referenceCell, titleTextStyle, headerTextStyle, invalidElementId);
            if (!IsResolvable(titleTextStyle) && !IsResolvable(headerTextStyle)
                && referenceBaseline == null)
            {
                return false;
            }
            return ClassifyCell(cell, titleTextStyle, headerTextStyle,
                referenceBaseline) == TextPairMatchKind.Mismatch;
        }

        public static bool IsAmbiguousCell(
            CellStyleFacts cell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            CellStyleFacts referenceCell,
            long invalidElementId = DefaultInvalidElementId)
        {
            if (cell == null || !cell.ReadComplete
                || !cell.HasAllInvalidBorders(invalidElementId)
                || cell.HasExplicitOverride)
            {
                return false;
            }

            TextStyleFacts referenceBaseline = ReferenceBaseline(
                referenceCell, titleTextStyle, headerTextStyle, invalidElementId);
            if (!IsResolvable(titleTextStyle) && !IsResolvable(headerTextStyle)
                && referenceBaseline == null)
            {
                return false;
            }
            return ClassifyCell(cell, titleTextStyle, headerTextStyle,
                referenceBaseline) == TextPairMatchKind.QuantizedAmbiguous;
        }

        /// <summary>
        /// Applies the same structural test to the (0,0) reference cell, but
        /// deliberately compares it only with the resolvable text-type
        /// baselines. Otherwise the reference would always match itself.
        ///
        /// The full font/size pair is compared with the resolvable text-type
        /// baselines. A matching pair makes the all-invalid reference usable;
        /// a non-matching pair is itself a known styleless observation.
        /// </summary>
        public static bool IsStylelessReferenceCell(
            CellStyleFacts referenceCell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            long invalidElementId = DefaultInvalidElementId)
        {
            if (referenceCell == null || !referenceCell.ReadComplete
                || !referenceCell.HasAllInvalidBorders(invalidElementId)
                || referenceCell.HasExplicitOverride)
            {
                return false;
            }

            if (!IsResolvable(titleTextStyle) && !IsResolvable(headerTextStyle))
            {
                return false;
            }

            return ClassifyReferenceCell(referenceCell, titleTextStyle,
                headerTextStyle) == TextPairMatchKind.Mismatch;
        }

        public static bool IsAmbiguousReferenceCell(
            CellStyleFacts referenceCell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            long invalidElementId = DefaultInvalidElementId)
        {
            if (referenceCell == null || !referenceCell.ReadComplete
                || !referenceCell.HasAllInvalidBorders(invalidElementId)
                || referenceCell.HasExplicitOverride)
            {
                return false;
            }

            if (!IsResolvable(titleTextStyle) && !IsResolvable(headerTextStyle))
            {
                return false;
            }

            return ClassifyReferenceCell(referenceCell, titleTextStyle,
                headerTextStyle) == TextPairMatchKind.QuantizedAmbiguous;
        }

        public static StyleIntegrityResult Evaluate(
            ScheduleStyleFacts facts,
            long invalidElementId = DefaultInvalidElementId,
            bool requireTextCells = false)
        {
            StyleIntegrityResult result = new StyleIntegrityResult();
            if (facts == null)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.InvalidFacts, null);
            }

            result.HeaderCellsExpected = facts.ExpectedHeaderCellCount;
            result.HeaderCellsRead = facts.Cells == null ? 0 : facts.Cells.Count;
            result.SkippedScheduleCount = facts.SkippedScheduleCount;
            result.FirstReadError = facts.FirstReadError ?? string.Empty;
            result.Facts = facts;

            if (facts.LimitExceeded)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.ScanLimit,
                    result.FirstReadError);
            }
            if (!facts.DimensionsRead)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.DimensionReadFailed,
                    result.FirstReadError);
            }
            if (facts.ExpectedHeaderCellCount <= 0)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.IncompleteScan,
                    result.FirstReadError);
            }
            if (facts.Cells == null
                || facts.Cells.Count != facts.ExpectedHeaderCellCount)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.IncompleteScan,
                    result.FirstReadError);
            }

            bool hasReadFailure = false;
            bool hasElementReadDenied = false;
            bool hasUnexpectedCellType = false;
            bool hasTextStyleReadFailure = false;
            bool hasTextStyleReadDenied = false;
            CellStyleFacts reference = FindReferenceCell(facts);
            bool referenceStyleless = IsStylelessReferenceCell(reference,
                facts.TitleTextStyle, facts.HeaderTextStyle, invalidElementId);
            bool hasAmbiguousTextSizeQuantization = IsAmbiguousReferenceCell(
                reference, facts.TitleTextStyle, facts.HeaderTextStyle, invalidElementId);
            if (reference != null && !reference.ReadComplete
                && !ContainsReferenceCell(facts.Cells, reference))
            {
                hasReadFailure = true;
                hasElementReadDenied = reference.ElementReadDenied;
                if (string.IsNullOrEmpty(result.FirstReadError))
                {
                    result.FirstReadError = reference.ReadError ?? string.Empty;
                }
            }
            foreach (CellStyleFacts cell in facts.Cells)
            {
                if (cell == null || !cell.ReadComplete)
                {
                    hasReadFailure = true;
                    if (cell != null && cell.ElementReadDenied)
                    {
                        hasElementReadDenied = true;
                    }
                    if (string.IsNullOrEmpty(result.FirstReadError) && cell != null)
                    {
                        result.FirstReadError = cell.ReadError ?? string.Empty;
                    }
                    continue;
                }

                bool isStyleless = cell.Row == 0 && cell.Column == 0
                    ? referenceStyleless
                    : IsStylelessCell(cell, facts.TitleTextStyle,
                        facts.HeaderTextStyle, reference, invalidElementId);
                if (!isStyleless && !(cell.Row == 0 && cell.Column == 0)
                    && IsAmbiguousCell(cell, facts.TitleTextStyle,
                        facts.HeaderTextStyle, reference, invalidElementId))
                {
                    hasAmbiguousTextSizeQuantization = true;
                }
                if (isStyleless)
                {
                    result.StylelessCellCount++;
                    result.StylelessCells.Add(new StyleIntegrityCoordinate
                    {
                        Row = cell.Row,
                        Column = cell.Column
                    });
                }

                if (requireTextCells
                    && (!cell.CellTypeReadComplete || !cell.IsTextCell))
                {
                    hasUnexpectedCellType = true;
                }
            }

            TextStyleFacts[] textStyles =
            {
                facts.TitleTextStyle,
                facts.HeaderTextStyle
            };
            foreach (TextStyleFacts textStyle in textStyles)
            {
                if (textStyle == null)
                {
                    continue;
                }
                if (textStyle.AccessDenied)
                {
                    hasTextStyleReadDenied = true;
                }
                else if (textStyle.ReadAttempted && textStyle.ReadFailed)
                {
                    hasTextStyleReadFailure = true;
                }
                if (string.IsNullOrEmpty(result.FirstReadError)
                    && (textStyle.ReadAttempted || textStyle.IsMissing)
                    && !string.IsNullOrEmpty(textStyle.ReadError))
                {
                    result.FirstReadError = textStyle.ReadError;
                }
            }

            // One known unsafe cell is stronger evidence than a separate read
            // failure; this is the explicit precedence rule in §3.2.
            if (result.StylelessCellCount > 0)
            {
                result.State = StyleIntegrityState.Affected;
                result.ReasonCode = StyleIntegrityReasonCode.StylelessCellDetected;
                return result;
            }
            if (hasElementReadDenied)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.ElementReadDenied,
                    result.FirstReadError);
            }
            if (hasTextStyleReadDenied)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.ElementReadDenied,
                    result.FirstReadError);
            }
            if (hasTextStyleReadFailure)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.TextStyleReadFailed,
                    result.FirstReadError);
            }
            if (hasUnexpectedCellType)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.UnexpectedCellType,
                    result.FirstReadError);
            }
            if (hasReadFailure)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.CellReadFailed,
                    result.FirstReadError);
            }

            if (hasAmbiguousTextSizeQuantization)
            {
                return Indeterminate(result,
                    StyleIntegrityReasonCode.AmbiguousTextSizeQuantization,
                    result.FirstReadError);
            }

            TextStyleFacts referenceBaseline = ReferenceBaseline(reference,
                facts.TitleTextStyle, facts.HeaderTextStyle, invalidElementId);
            bool hasBaseline = IsResolvable(facts.TitleTextStyle)
                || IsResolvable(facts.HeaderTextStyle)
                || referenceBaseline != null;
            if (!hasBaseline)
            {
                return Indeterminate(result, StyleIntegrityReasonCode.TextStyleUnavailable,
                    result.FirstReadError);
            }

            result.State = StyleIntegrityState.Healthy;
            result.ReasonCode = StyleIntegrityReasonCode.Healthy;
            return result;
        }

        public static StyleIntegrityResult LimitResult(int skippedScheduleCount)
        {
            StyleIntegrityResult result = new StyleIntegrityResult();
            result.State = StyleIntegrityState.Indeterminate;
            result.ReasonCode = StyleIntegrityReasonCode.ScanLimit;
            result.SkippedScheduleCount = Math.Max(0, skippedScheduleCount);
            return result;
        }

        private static bool IsResolvable(TextStyleFacts style)
        {
            return style != null && style.IsResolvable;
        }

        private static TextPairMatchKind ClassifyCell(
            CellStyleFacts cell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            TextStyleFacts referenceBaseline)
        {
            if (cell == null || !cell.ReadComplete)
            {
                return TextPairMatchKind.Mismatch;
            }

            TextPairMatchKind best = TextPairMatchKind.Mismatch;
            TextPairMatchKind title = ClassifyTextPair(
                cell.FontName, cell.TextSize, titleTextStyle);
            TextPairMatchKind header = ClassifyTextPair(
                cell.FontName, cell.TextSize, headerTextStyle);
            TextPairMatchKind reference = ClassifyTextPair(
                cell.FontName, cell.TextSize, referenceBaseline);
            if (title == TextPairMatchKind.Exact
                || header == TextPairMatchKind.Exact
                || reference == TextPairMatchKind.Exact)
            {
                return TextPairMatchKind.Exact;
            }
            if (title == TextPairMatchKind.QuantizedAmbiguous
                || header == TextPairMatchKind.QuantizedAmbiguous
                || reference == TextPairMatchKind.QuantizedAmbiguous)
            {
                best = TextPairMatchKind.QuantizedAmbiguous;
            }
            return best;
        }

        private static TextPairMatchKind ClassifyReferenceCell(
            CellStyleFacts referenceCell,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle)
        {
            TextPairMatchKind title = ClassifyTextPair(
                referenceCell.FontName, referenceCell.TextSize, titleTextStyle);
            TextPairMatchKind header = ClassifyTextPair(
                referenceCell.FontName, referenceCell.TextSize, headerTextStyle);
            if (title == TextPairMatchKind.Exact || header == TextPairMatchKind.Exact)
            {
                return TextPairMatchKind.Exact;
            }
            if (title == TextPairMatchKind.QuantizedAmbiguous
                || header == TextPairMatchKind.QuantizedAmbiguous)
            {
                return TextPairMatchKind.QuantizedAmbiguous;
            }
            return TextPairMatchKind.Mismatch;
        }

        private static CellStyleFacts FindReferenceCell(ScheduleStyleFacts facts)
        {
            if (facts.ReferenceCell != null
                && facts.ReferenceCell.Row == 0
                && facts.ReferenceCell.Column == 0)
            {
                return facts.ReferenceCell;
            }
            if (facts.Cells == null)
            {
                return null;
            }
            foreach (CellStyleFacts cell in facts.Cells)
            {
                if (cell != null && cell.Row == 0 && cell.Column == 0)
                {
                    return cell;
                }
            }
            return null;
        }

        public static TextStyleFacts ReferenceBaseline(
            CellStyleFacts reference,
            TextStyleFacts titleTextStyle,
            TextStyleFacts headerTextStyle,
            long invalidElementId = DefaultInvalidElementId)
        {
            if (reference == null || !reference.ReadComplete
                || reference.Row != 0 || reference.Column != 0
                || IsStylelessReferenceCell(reference, titleTextStyle,
                    headerTextStyle, invalidElementId)
                || IsAmbiguousReferenceCell(reference, titleTextStyle,
                    headerTextStyle, invalidElementId))
            {
                return null;
            }

            return new TextStyleFacts
            {
                IsResolvable = true,
                FontName = reference.FontName,
                SizeTableUnits = reference.TextSize
            };
        }

        private static bool ContainsReferenceCell(
            IList<CellStyleFacts> cells, CellStyleFacts reference)
        {
            if (cells == null || reference == null)
            {
                return false;
            }
            foreach (CellStyleFacts cell in cells)
            {
                if (object.ReferenceEquals(cell, reference))
                {
                    return true;
                }
            }
            return false;
        }

        private static StyleIntegrityResult Indeterminate(
            StyleIntegrityResult result,
            StyleIntegrityReasonCode reason,
            string error)
        {
            result.State = StyleIntegrityState.Indeterminate;
            result.ReasonCode = reason;
            if (!string.IsNullOrEmpty(error))
            {
                result.FirstReadError = error;
            }
            return result;
        }
    }
}
