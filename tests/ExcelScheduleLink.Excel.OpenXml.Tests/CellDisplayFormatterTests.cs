using System;
using System.Globalization;
using ExcelScheduleLink.Excel.OpenXml.Internal;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    [Collection("CultureSensitive")]
    public class CellDisplayFormatterTests
    {
        private static RawCell Number(double value, string formatCode)
        {
            return new RawCell
            {
                Kind = RawCellKind.Number,
                Raw = value.ToString("R", CultureInfo.InvariantCulture),
                NumFmtId = formatCode == null ? 0 : 164,
                FormatCode = formatCode
            };
        }

        private static string Fmt(double value, string formatCode, bool date1904 = false)
        {
            return CellDisplayFormatter.Format(Number(value, formatCode), date1904);
        }

        private static string FmtBuiltin(double value, int numFmtId, bool date1904 = false)
        {
            return CellDisplayFormatter.Format(new RawCell
            {
                Kind = RawCellKind.Number,
                Raw = value.ToString("R", CultureInfo.InvariantCulture),
                NumFmtId = numFmtId,
                FormatCode = null
            }, date1904);
        }

        // ---- Core numeric formats ----

        [Theory]
        [InlineData(1234.567, "0", "1235")]                  // integer
        [InlineData(1234.567, "0.00", "1234.57")]            // decimals
        [InlineData(1234567.89, "#,##0", "1,234,568")]       // thousands separator
        [InlineData(1234567.89, "#,##0.00", "1,234,567.89")] // thousands + decimals
        [InlineData(0.1235, "0%", "12%")]                    // percent
        [InlineData(0.1235, "0.00%", "12.35%")]              // percent + decimals
        [InlineData(123, "00000", "00123")]                  // leading zeros
        [InlineData(2469.134, "#,##0.00", "2,469.13")]       // formula cached value equivalent
        [InlineData(-12.3, "0.00", "-12.30")]                // negative
        [InlineData(-42, "0000", "-0042")]                   // negative + leading zeros
        public void Numeric_Formats(double value, string code, string expected)
        {
            Assert.Equal(expected, Fmt(value, code));
        }

        [Theory]
        [InlineData(2469.134, "2469.134")]
        [InlineData(1235, "1235")]
        [InlineData(0.5, "0.5")]
        [InlineData(-3.25, "-3.25")]
        public void General_TrimsTrailingZeros(double value, string expected)
        {
            Assert.Equal(expected, Fmt(value, null));
        }

        // ---- Dates and times ----

        [Fact]
        public void Date_CustomJapaneseStyle()
        {
            double serial = new DateTime(2026, 6, 10).ToOADate();
            Assert.Equal("2026/6/10", Fmt(serial, "yyyy/m/d"));
        }

        [Fact]
        public void Date_BuiltinUsStyle()
        {
            double serial = new DateTime(2026, 6, 10).ToOADate();
            Assert.Equal("6/10/2026", Fmt(serial, "m/d/yyyy"));
        }

        [Fact]
        public void BuiltinDateTimeIds_UseCurrentCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            DateTime value = new DateTime(2026, 6, 10, 13, 45, 7);
            try
            {
                foreach (string cultureName in new[] { "en-US", "ja-JP" })
                {
                    CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                    CultureInfo.CurrentCulture = culture;
                    double serial = value.ToOADate();

                    foreach (int id in new[] { 14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 47 })
                    {
                        string pattern = BuiltinNetPattern(id, culture);
                        Assert.NotNull(pattern);
                        Assert.Equal(value.ToString(pattern, culture), FmtBuiltin(serial, id));
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        private static string BuiltinNetPattern(int id, CultureInfo culture)
        {
            switch (id)
            {
                case 14: return culture.DateTimeFormat.ShortDatePattern;
                case 15: return "d-MMM-yy";
                case 16: return "d-MMM";
                case 17: return "MMM-yy";
                case 18: return "h:mm tt";
                case 19: return "h:mm:ss tt";
                case 20: return culture.DateTimeFormat.ShortTimePattern;
                case 21: return "H:mm:ss";
                case 22: return culture.DateTimeFormat.ShortDatePattern + " "
                    + culture.DateTimeFormat.ShortTimePattern;
                case 45:
                case 47: return "mm:ss";
                default: return null;
            }
        }

        [Fact]
        public void BuiltinDateTimeIds_ChangeShortDateAndTimeWithCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            DateTime value = new DateTime(2026, 6, 10, 13, 45, 0);
            try
            {
                CultureInfo en = CultureInfo.GetCultureInfo("en-US");
                CultureInfo ja = CultureInfo.GetCultureInfo("ja-JP");
                CultureInfo.CurrentCulture = en;
                string enDate = FmtBuiltin(value.ToOADate(), 14);
                string enTime = FmtBuiltin(value.ToOADate(), 20);
                CultureInfo.CurrentCulture = ja;
                string jaDate = FmtBuiltin(value.ToOADate(), 14);
                string jaTime = FmtBuiltin(value.ToOADate(), 20);

                Assert.Equal(value.ToString(en.DateTimeFormat.ShortDatePattern, en), enDate);
                Assert.Equal(value.ToString(en.DateTimeFormat.ShortTimePattern, en), enTime);
                Assert.Equal(value.ToString(ja.DateTimeFormat.ShortDatePattern, ja), jaDate);
                Assert.Equal(value.ToString(ja.DateTimeFormat.ShortTimePattern, ja), jaTime);
                Assert.NotEqual(enDate, jaDate);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void Time_24Hour()
        {
            double serial = new DateTime(2026, 6, 10, 13, 45, 0).ToOADate();
            Assert.Equal("13:45", Fmt(serial, "h:mm"));
        }

        [Fact]
        public void Time_FractionOnlySerial()
        {
            // Time-only cell (serial value < 1)
            double serial = 13.75 / 24.0; // 13:45
            Assert.Equal("13:45", Fmt(serial, "h:mm"));
        }

        [Fact]
        public void Time_12Hour_AmPm()
        {
            double serial = new DateTime(2026, 6, 10, 13, 45, 0).ToOADate();
            Assert.Equal("1:45 PM", Fmt(serial, "h:mm AM/PM"));
        }

        [Fact]
        public void DateTime_Combined()
        {
            double serial = new DateTime(2026, 6, 10, 13, 45, 0).ToOADate();
            Assert.Equal("6/10/2026 13:45", Fmt(serial, "m/d/yyyy h:mm"));
        }

        [Fact]
        public void MinutesSeconds_MDisambiguation()
        {
            double serial = new DateTime(2026, 6, 10, 13, 45, 7).ToOADate();
            Assert.Equal("45:07", Fmt(serial, "mm:ss"));
        }

        [Fact]
        public void Date_1904System()
        {
            // 1904-system serial values are 1462 days smaller than 1900-system ones
            double serial = new DateTime(2026, 6, 10).ToOADate() - 1462.0;
            Assert.Equal("2026/6/10", Fmt(serial, "yyyy/m/d", date1904: true));
        }

        [Theory]
        [InlineData(1.0E10)]
        [InlineData(-1.0E10)]
        public void OutOfRangeDateSerial_FallsBackToGeneral(double serial)
        {
            string formatted = Fmt(serial, "m/d/yyyy");

            Assert.IsType<string>(formatted);
            Assert.Equal(CellDisplayFormatter.GeneralString(serial), formatted);
        }

        // ---- Text / boolean / error ----

        [Fact]
        public void Text_PassThrough()
        {
            // Intentional CJK test data: verify Unicode text passes through unchanged.
            var cell = new RawCell { Kind = RawCellKind.Text, Raw = "日本語テキスト ABC123" };
            Assert.Equal("日本語テキスト ABC123", CellDisplayFormatter.Format(cell, false));
        }

        [Theory]
        [InlineData("1", "TRUE")]
        [InlineData("0", "FALSE")]
        public void Boolean_Formats(string raw, string expected)
        {
            var cell = new RawCell { Kind = RawCellKind.Boolean, Raw = raw };
            Assert.Equal(expected, CellDisplayFormatter.Format(cell, false));
        }

        [Fact]
        public void Error_PassThrough()
        {
            var cell = new RawCell { Kind = RawCellKind.Error, Raw = "#DIV/0!" };
            Assert.Equal("#DIV/0!", CellDisplayFormatter.Format(cell, false));
        }

        [Fact]
        public void Empty_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, CellDisplayFormatter.Format(new RawCell(), false));
        }

        // ---- Custom numeric formats with unit literals ----

        [Theory]
        [InlineData(12.345, "0.0 \"A\"", "12.3 A")]
        [InlineData(12.345, "0.00 \"kVA\"", "12.35 kVA")]
        [InlineData(1234.0, "0 \"CFM\"", "1234 CFM")]
        [InlineData(12.3, "0.0 \"GPM\"", "12.3 GPM")]
        [InlineData(-5.0, "0.0 \"A\"", "-5.0 A")]
        [InlineData(1234.5, "0.00\"kg\"", "1234.50kg")]   // suffix with no space
        [InlineData(1234.56, "\"$\"#,##0.00", "$1,234.56")] // currency prefix
        [InlineData(-1234.56, "\"$\"#,##0.00", "-$1,234.56")]
        public void UnitLiteral_Formats(double value, string code, string expected)
        {
            Assert.Equal(expected, Fmt(value, code));
        }

        // ---- Fraction formats ----

        [Theory]
        [InlineData(1.5, "# ?/?", "1 1/2")]
        [InlineData(0.5, "# ?/?", "1/2")]
        [InlineData(2.0, "# ?/?", "2")]
        [InlineData(2.25, "# ?/?", "2 1/4")]
        [InlineData(-1.5, "# ?/?", "-1 1/2")]
        [InlineData(0.0, "# ?/?", "0")]
        [InlineData(2.7, "# ??/??", "2 7/10")]
        [InlineData(1.25, "# ??/??", "1 1/4")]
        [InlineData(3.125, "# ??/??", "3 1/8")]
        [InlineData(1.5, "?/?", "3/2")]               // improper fraction
        public void Fraction_Formats(double value, string code, string expected)
        {
            Assert.Equal(expected, Fmt(value, code));
        }

        [Fact]
        public void FractionWithTooManyDenominatorPlaceholdersFallsBackToGeneral()
        {
            const string longFormat = "# ????????/????????";

            Assert.Equal(CellDisplayFormatter.GeneralString(1.2345), Fmt(1.2345, longFormat));
        }

        [Theory]
        [InlineData(12, "0")]
        [InlineData(13, "0.00")]
        public void Builtin_FractionIds(int id, string notFraction)
        {
            // Built-in fraction ids 12 (# ?/?) and 13 (# ??/??) resolve to codes,
            // and must not collapse into the plain numeric neighbours.
            Assert.NotNull(CellDisplayFormatter.BuiltinFormatCode(id));
            Assert.Contains("/", CellDisplayFormatter.BuiltinFormatCode(id));
            Assert.NotEqual(notFraction, CellDisplayFormatter.BuiltinFormatCode(id));
        }

        // ---- Fallback (unsupported formats) ----

        [Theory]
        [InlineData("0.00E+00")]          // scientific
        public void Unsupported_FallsBackToGeneral(string code)
        {
            Assert.Equal("1234.5", Fmt(1234.5, code));
        }

        [Fact]
        public void TextFormat_AtSign_OnNumber_FallsBackToGeneral()
        {
            Assert.Equal("123.4", Fmt(123.4, "@"));
        }

        [Fact]
        public void NumberWithUnparsableRaw_ReturnsRaw()
        {
            var cell = new RawCell { Kind = RawCellKind.Number, Raw = "not-a-number" };
            Assert.Equal("not-a-number", CellDisplayFormatter.Format(cell, false));
        }

        // ---- Built-in numFmtId resolution ----

        [Theory]
        [InlineData(1, "0")]
        [InlineData(4, "#,##0.00")]
        [InlineData(9, "0%")]
        [InlineData(15, "d-mmm-yy")]
        [InlineData(49, "@")]
        public void BuiltinFormatCodes_Resolved(int id, string expected)
        {
            Assert.Equal(expected, CellDisplayFormatter.BuiltinFormatCode(id));
        }

        [Fact]
        public void Builtin_General_IsNull()
        {
            Assert.Null(CellDisplayFormatter.BuiltinFormatCode(0));
        }

        [Fact]
        public void BuiltinId_UsedWhenNoCustomCode()
        {
            var cell = new RawCell
            {
                Kind = RawCellKind.Number,
                Raw = "0.1235",
                NumFmtId = 10, // 0.00%
                FormatCode = null
            };
            Assert.Equal("12.35%", CellDisplayFormatter.Format(cell, false));
        }
    }
}
