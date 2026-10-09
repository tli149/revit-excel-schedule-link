using System.Collections.Generic;
using ExcelScheduleLink.Layout;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    /// <summary>
    /// Unit tests for the pure layout math (ExcelScheduleLink.Layout). CJK
    /// characters are built from code points via (char)0xNNNN so this source
    /// stays pure ASCII (no UTF-8 BOM required). Widths are feet; assertions
    /// check behavior and ordering, not exact calibrated numbers (those are
    /// tuned on-device via LayoutMetrics).
    /// </summary>
    public class LayoutMathTests
    {
        // Representative code points (never embed the glyphs in source).
        private const char HiraganaA = (char)0x3042;      // full-width
        private const char CjkKan = (char)0x6F22;         // full-width ideograph
        private const char CjkJi = (char)0x5B57;          // full-width ideograph
        private const char FullwidthLatinA = (char)0xFF21; // full-width
        private const char HalfwidthKatakanaA = (char)0xFF71; // half-width

        [Theory]
        [InlineData('A', 1.0)] // ASCII letter
        [InlineData('1', 1.0)] // digit
        [InlineData('#', 1.0)] // symbol
        [InlineData(' ', 0.5)] // space
        public void UnitsFor_classifies_ascii_width(char c, double expected)
        {
            Assert.Equal(expected, CharWidth.UnitsFor(c), 3);
        }

        [Fact]
        public void UnitsFor_classifies_cjk_width()
        {
            Assert.Equal(2.0, CharWidth.UnitsFor(HiraganaA), 3);
            Assert.Equal(2.0, CharWidth.UnitsFor(CjkKan), 3);
            Assert.Equal(2.0, CharWidth.UnitsFor(FullwidthLatinA), 3);
            // Half-width katakana must stay half-width.
            Assert.Equal(1.0, CharWidth.UnitsFor(HalfwidthKatakanaA), 3);
        }

        [Fact]
        public void MeasureUnits_sums_mixed_and_handles_empty()
        {
            // ideograph + ideograph + 'A' + 'B' = 2 + 2 + 1 + 1 = 6
            string mixed = new string(new char[] { CjkKan, CjkJi, 'A', 'B' });
            Assert.Equal(6.0, CharWidth.MeasureUnits(mixed), 3);
            Assert.Equal(0.0, CharWidth.MeasureUnits(null), 3);
            Assert.Equal(0.0, CharWidth.MeasureUnits(string.Empty), 3);
        }

        [Fact]
        public void CJK_column_is_wider_than_ascii_of_the_same_character_count()
        {
            // 20 full-width chars (40 units) vs 20 ASCII chars (20 units). Both
            // are above the min-width floor, so the per-character width
            // difference is visible (short columns both clamp to the floor,
            // which is the intended readability minimum).
            double[] cjk = ColumnWidthMath.ComputeColumnFeet(new double[] { 40.0 }, null, 1.0);
            double[] ascii = ColumnWidthMath.ComputeColumnFeet(new double[] { 20.0 }, null, 1.0);
            Assert.True(cjk[0] > ascii[0]);
        }

        [Fact]
        public void Width_is_clamped_between_min_and_max()
        {
            // Empty content floors to the minimum.
            double[] tiny = ColumnWidthMath.ComputeColumnFeet(new double[] { 0.0 }, null, 1.0);
            Assert.Equal(LayoutMetrics.MinColumnFeet, tiny[0], 4);

            // Capped content (>= MaxContentUnits) at a large scale exceeds the
            // maximum and is clamped down.
            double[] huge = ColumnWidthMath.ComputeColumnFeet(new double[] { 200.0 }, null, 10.0);
            Assert.Equal(LayoutMetrics.MaxColumnFeet, huge[0], 4);
        }

        [Fact]
        public void Content_units_are_capped()
        {
            double[] atCap = ColumnWidthMath.ComputeColumnFeet(
                new double[] { LayoutMetrics.MaxContentUnits }, null, 1.0);
            double[] overCap = ColumnWidthMath.ComputeColumnFeet(
                new double[] { LayoutMetrics.MaxContentUnits * 4.0 }, null, 1.0);
            Assert.Equal(atCap[0], overCap[0], 6);
        }

        [Fact]
        public void Excel_width_wins_when_wider_than_content()
        {
            Dictionary<int, double> excel = new Dictionary<int, double> { { 0, 40.0 } };
            double[] withExcel = ColumnWidthMath.ComputeColumnFeet(new double[] { 1.0 }, excel, 1.0);
            double[] contentOnly = ColumnWidthMath.ComputeColumnFeet(new double[] { 1.0 }, null, 1.0);
            Assert.True(withExcel[0] > contentOnly[0]);
        }

        [Fact]
        public void WidthScale_multiplies_before_clamp()
        {
            double[] s1 = ColumnWidthMath.ComputeColumnFeet(new double[] { 10.0 }, null, 1.0);
            double[] s2 = ColumnWidthMath.ComputeColumnFeet(new double[] { 10.0 }, null, 2.0);
            Assert.True(s2[0] > s1[0]);
        }

        [Fact]
        public void NonPositive_scale_is_treated_as_one()
        {
            double[] scaleZero = ColumnWidthMath.ComputeColumnFeet(new double[] { 10.0 }, null, 0.0);
            double[] scaleOne = ColumnWidthMath.ComputeColumnFeet(new double[] { 10.0 }, null, 1.0);
            Assert.Equal(scaleOne[0], scaleZero[0], 6);
        }

        [Fact]
        public void MeasureLongestLineUnits_uses_the_widest_line()
        {
            // "AB\nABCD\nA" -> longest line is 4 half-width units
            Assert.Equal(4.0, CharWidth.MeasureLongestLineUnits("AB\nABCD\nA"), 3);
            // CRLF and CR are handled the same way
            Assert.Equal(4.0, CharWidth.MeasureLongestLineUnits("AB\r\nABCD"), 3);
            Assert.Equal(4.0, CharWidth.MeasureLongestLineUnits("AB\rABCD"), 3);
            // single line == MeasureUnits
            Assert.Equal(CharWidth.MeasureUnits("ABC"), CharWidth.MeasureLongestLineUnits("ABC"), 6);
            // full-width line wins (2 CJK = 4 units vs 3 ASCII = 3)
            string cjkLine = new string(new char[] { CjkKan, CjkJi }) + "\nABC";
            Assert.Equal(4.0, CharWidth.MeasureLongestLineUnits(cjkLine), 3);
            // empty / null
            Assert.Equal(0.0, CharWidth.MeasureLongestLineUnits(""), 6);
            Assert.Equal(0.0, CharWidth.MeasureLongestLineUnits(null), 6);
            Assert.Equal(0.0, CharWidth.MeasureLongestLineUnits("\n\n"), 6);
        }

        [Fact]
        public void ContentFeetForUnits_pads_caps_and_guards()
        {
            double empty = ColumnWidthMath.ContentFeetForUnits(0.0);
            Assert.True(empty > 0.0);                                   // padding only
            Assert.True(ColumnWidthMath.ContentFeetForUnits(10.0) > empty);

            // capped at MaxContentUnits
            double atCap = ColumnWidthMath.ContentFeetForUnits(LayoutMetrics.MaxContentUnits);
            double overCap = ColumnWidthMath.ContentFeetForUnits(LayoutMetrics.MaxContentUnits * 10);
            Assert.Equal(atCap, overCap, 6);

            // invalid input behaves like zero content
            Assert.Equal(empty, ColumnWidthMath.ContentFeetForUnits(double.NaN), 6);
            Assert.Equal(empty, ColumnWidthMath.ContentFeetForUnits(-5.0), 6);
            Assert.Equal(empty, ColumnWidthMath.ContentFeetForUnits(double.PositiveInfinity), 6);
        }

        [Fact]
        public void DefaultRowFeet_is_positive_and_matches_metric()
        {
            double feet = RowHeightMath.DefaultRowFeet();
            Assert.True(feet > 0.0);
            Assert.Equal(LayoutMetrics.DefaultRowInches / LayoutMetrics.InchesPerFoot, feet, 6);
        }
    }
}
