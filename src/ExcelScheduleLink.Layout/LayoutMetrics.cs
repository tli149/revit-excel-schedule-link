namespace ExcelScheduleLink.Layout
{
    /// <summary>
    /// Single source of truth for every calibratable layout constant used when
    /// an Excel sheet is rendered into a schedule header (column widths and row
    /// heights). Tuning numbers live ONLY here -- never scatter them across the
    /// renderer or the converters -- so the whole layout can be re-tuned in one
    /// place after on-device review.
    ///
    /// Lengths are feet (paper space) when the name ends in "Feet" and inches
    /// when it ends in "Inches"; character sizes are unitless "Units" (one
    /// half-width glyph = 1 unit).
    ///
    /// The initial numbers below are starting points to be calibrated on the
    /// real 4K machine (the Revit GUI cannot be driven in automated tests);
    /// adjust here only.
    /// </summary>
    public static class LayoutMetrics
    {
        // ---- shared ----
        public const double InchesPerFoot = 12.0;

        // ---- column width: content model (half-width units) ----
        // Text is measured in half-width units. A half-width glyph (ASCII
        // letter / digit / symbol) is HalfWidthUnits, a full-width glyph (CJK
        // ideograph, kana, Hangul, full-width form) is FullWidthUnits, and a
        // space is SpaceUnits. HalfWidthInchesPerUnit converts units to paper
        // inches. CellPaddingInches is total horizontal padding: the width
        // model adds it once per column, while row fitting adds it once per
        // anchor.
        // 0.09 in = one half-width glyph (so a full-width CJK glyph = 0.18 in).
        // This matches the pre-0.7.0 per-character width for ASCII while giving
        // CJK text twice the room it used to get. Calibrated on-device.
        // The three glyph widths must stay multiples of 0.5: fit-content line
        // histograms store exact half units and reject any other value.
        public const double HalfWidthInchesPerUnit = 0.09;
        public const double FullWidthUnits = 2.0;
        public const double HalfWidthUnits = 1.0;
        public const double SpaceUnits = 0.5;
        public const double CellPaddingInches = 0.12;

        // Cap on the content units considered, so one very long string does not
        // produce an unreadable extreme column width.
        public const double MaxContentUnits = 60.0;

        // ---- fit-content protection and line estimation ----
        public const int FitContentHeadingRowCount = 1;
        public const int FitContentProtectionPercent = 75;
        public const int FitContentProtectionMinSamples = 2;
        public const int FitContentProtectionMaxCharacters = 64;
        public const int FitContentCodeMaxCharacters = 32;
        public const int ColumnHeadingMaxLength = 32;
        public const double FitContentMinCapacityUnits = 0.5;
        public const double FitContentLineToleranceUnits = 1e-9;

        // ---- fit-content allocation ----
        public const int FitContentMaxTransferTrials = 2048;
        public const int FitContentCoarseDivisor = 64;
        public const int FitContentMediumDivisor = 256;
        public const int FitContentFineDivisor = 1024;
        public const double FitContentObjectiveTolerance = 1e-9;

        // ---- column width: Excel-width component (pixel approximation) ----
        // The conventional pixel approximation of an Excel width unit at 96 DPI
        // is pixels = width * 7 + 5.
        public const double ExcelPixelsPerWidthUnit = 7.0;
        public const double ExcelPixelPadding = 5.0;
        public const double PixelsPerInch = 96.0;

        // ---- column width: clamp (feet) ----
        // Lower bound keeps a calculated column from collapsing; upper bound
        // stays safely within Revit's per-column limit. A preserved live width
        // may remain outside these bounds when the candidate is unchanged.
        public const double MinColumnFeet = 0.10; // ~1.2 inch
        public const double MaxColumnFeet = 2.0;  // ~24 inch

        // ---- body (schedule-view) width ----
        // Revit 2024 stretches header columns when GridColumnWidth is set.
        // Sync to the exact header total: positive padding causes cumulative growth.
        // Keep zero shared with the raw diagnostic probe; do not add a visual margin.
        public const double BodyWidthMarginInches = 0.0;

        // ---- row height ----
        // Estimated one-line minimum for fitted dimensions; this constant does
        // not apply a height during rendering.
        public const double DefaultRowInches = 0.25;
        // Provisional physical row-fitting calibration values.
        public const double RowFitHalfUnitEm = 0.60;
        public const double RowFitLinePitchFactor = 1.35;
        // Total top and bottom padding.
        public const double RowFitVerticalPaddingInches = 0.04;
        public const int RowFitWrappedLineAllowance = 1;
    }
}
