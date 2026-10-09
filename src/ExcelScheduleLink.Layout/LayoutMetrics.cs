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
        // inches. CellPaddingInches is added once per column.
        // 0.09 in = one half-width glyph (so a full-width CJK glyph = 0.18 in).
        // This matches the pre-0.7.0 per-character width for ASCII while giving
        // CJK text twice the room it used to get. Calibrated on-device.
        public const double HalfWidthInchesPerUnit = 0.09;
        public const double FullWidthUnits = 2.0;
        public const double HalfWidthUnits = 1.0;
        public const double SpaceUnits = 0.5;
        public const double CellPaddingInches = 0.12;

        // Cap on the content units considered, so one very long string does not
        // produce an unreadable extreme column width.
        public const double MaxContentUnits = 60.0;

        // ---- column width: Excel-width component (pixel approximation) ----
        // The conventional pixel approximation of an Excel width unit at 96 DPI
        // is pixels = width * 7 + 5.
        public const double ExcelPixelsPerWidthUnit = 7.0;
        public const double ExcelPixelPadding = 5.0;
        public const double PixelsPerInch = 96.0;

        // ---- column width: clamp (feet) ----
        // Lower bound keeps a column from collapsing; upper bound stays safely
        // within Revit's per-column limit so "Column Width Too Large" never
        // appears. Both clamps are always enforced.
        public const double MinColumnFeet = 0.10; // ~1.2 inch
        public const double MaxColumnFeet = 2.0;  // ~24 inch

        // ---- body (schedule-view) width ----
        // Method B keeps one dummy body column. The schedule VIEW lays the
        // header table out inside that column's width, so the body column is
        // widened to the rendered table total. A small margin is added because
        // the two error directions are not symmetric: slightly too wide only
        // leaves a sliver of empty space, while slightly too narrow makes one
        // header column absorb the whole deficit and collapse to a single
        // character. Set to 0 if on-device review shows the exact total is
        // enough.
        public const double BodyWidthMarginInches = 0.06;

        // ---- row height ----
        // Comfortable uniform row height applied on an initial render so rows
        // are not crammed at Revit's minimum height.
        public const double DefaultRowInches = 0.25;
    }
}
