namespace ExcelScheduleLink.Layout
{
    /// <summary>
    /// Pure paper-space row-height math (feet). Kept separate from the width
    /// math so both can be tuned independently through <see cref="LayoutMetrics"/>.
    /// </summary>
    public static class RowHeightMath
    {
        /// <summary>
        /// Comfortable uniform row height (feet) applied on an initial render so
        /// rows are not crammed at Revit's minimum height. Calibrated by
        /// <see cref="LayoutMetrics.DefaultRowInches"/>.
        /// </summary>
        public static double DefaultRowFeet()
        {
            return LayoutMetrics.DefaultRowInches / LayoutMetrics.InchesPerFoot;
        }
    }
}
