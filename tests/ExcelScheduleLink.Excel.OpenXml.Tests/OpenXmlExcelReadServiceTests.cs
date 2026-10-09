using System;
using System.IO;
using System.Linq;
using ExcelScheduleLink.Contracts;
using System.Globalization;
using System.Security.Cryptography;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    /// <summary>
    /// Integration tests against Step0_TestBook.xlsx (the fixture that was
    /// already read successfully on the Revit test machine during the initial
    /// feasibility probe).
    /// On the "Step0" sheet, column B (formatted value) is paired with
    /// column C (expected display string), so "display(B) == text(C)" makes
    /// the format interpretation self-verifying. The fixture intentionally
    /// contains CJK sheet names and cell values — these are test data and must
    /// keep full Unicode support.
    /// </summary>
    [Collection("WorkbookFileSystem")]
    public class OpenXmlExcelReadServiceTests
    {
        private static string FixturePath
        {
            get { return TestDataPath("Step0_TestBook.xlsx"); }
        }

        private static string BuiltinFormatsFixturePath
        {
            get { return TestDataPath("v0.9.1_BuiltinDateFormats.xlsx"); }
        }

        private static string OutOfRangeDateSerialFixturePath
        {
            get { return TestDataPath("v0.9.3_OutOfRangeDateSerial.xlsx"); }
        }

        private static string OversizedFixturePath
        {
            get { return TestDataPath("v0.9.3_Oversized.xlsx"); }
        }

        private readonly OpenXmlExcelReadService _service = new OpenXmlExcelReadService();

        [Fact]
        public void GetSheetNames_ReturnsBothSheets()
        {
            // Intentional CJK fixture data: preserve the sheet name verbatim.
            var names = _service.GetSheetNames(FixturePath);
            Assert.Equal(new[] { "Step0", "データ" }, names);
        }

        [Fact]
        public void ReadSheet_Dimensions_AnchoredAtA1()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;
            Assert.Equal("Step0", model.SheetName);
            Assert.Equal(17, model.RowCount);   // A1..A17 (row 17 is the merged-cell row)
            Assert.Equal(3, model.ColumnCount);
        }

        [Fact]
        public void ReadSheet_DisplayTexts_MatchExpectedColumn()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;

            // 1-based rows 4..15 (0-based 3..14) are the test-case rows:
            // A = case name, B = formatted value, C = expected display string
            for (int r = 3; r <= 14; r++)
            {
                string caseName = model.GetText(r, 0);
                string actual = model.GetText(r, 1);
                string expected = model.GetText(r, 2);
                Assert.False(string.IsNullOrEmpty(expected), "Expected-value cell is empty: row=" + r);
                Assert.True(expected == actual,
                    "Case \"" + caseName + "\" (0-based row " + r + "): expected \"" + expected
                    + "\" actual \"" + actual + "\"");
            }
        }

        [Fact]
        public void ReadSheet_KnownCases_Explicit()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;
            Assert.Equal("ESL Step0 テストブック(書式ケース)", model.GetText(0, 0));
            Assert.Equal("1235", model.GetText(3, 1));        // integer "0"
            Assert.Equal("2026/6/10", model.GetText(9, 1));   // date yyyy/m/d
            Assert.Equal("13:45", model.GetText(10, 1));      // time h:mm
            Assert.Equal("00123", model.GetText(11, 1));      // leading zeros
            Assert.Equal("2,469.13", model.GetText(12, 1));   // formula cached value
            Assert.Equal("TRUE", model.GetText(14, 1));       // boolean
            Assert.Equal(string.Empty, model.GetText(15, 1)); // empty row
        }

        [Fact]
        public void ReadSheet_MergedRanges()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;
            Assert.Equal(2, model.MergedRanges.Count);
            Assert.Contains(model.MergedRanges,
                m => m.FirstRow == 0 && m.FirstColumn == 0 && m.LastRow == 0 && m.LastColumn == 2);
            Assert.Contains(model.MergedRanges,
                m => m.FirstRow == 16 && m.FirstColumn == 0 && m.LastRow == 16 && m.LastColumn == 2);
        }

        [Fact]
        public void ReadSheet_ColumnWidths()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;
            Assert.Equal(3, model.Columns.Count);
            Assert.Equal(0, model.Columns[0].ColumnIndex);
            Assert.True(model.Columns[0].Width > model.Columns[1].Width); // A(24) > B(18)
        }

        [Fact]
        public void ReadSheet_JapaneseSheetName()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "データ", null).Sheet;
            Assert.Equal("日本語シートの A1", model.GetText(0, 0));
            Assert.Equal(4, model.RowCount);
            Assert.Equal(3, model.ColumnCount);
            Assert.Equal("11", model.GetText(1, 0)); // 11 with #,##0
        }

        [Fact]
        public void ReadSheet_ExplicitRange_AnchorsAtRangeOrigin()
        {
            SheetModel model = _service.ReadSheetSnapshot(FixturePath, "データ", "B2:C3").Sheet;
            Assert.Equal(2, model.RowCount);
            Assert.Equal(2, model.ColumnCount);
            Assert.Equal("12", model.GetText(0, 0)); // B2 = 12
            Assert.Equal("23", model.GetText(1, 1)); // C3 = 23
        }

        [Fact]
        public void ReadSheet_MissingSheet_Throws()
        {
            var ex = Assert.Throws<ExcelReadException>(
                () => _service.ReadSheetSnapshot(FixturePath, "存在しないシート", null).Sheet);
            Assert.Contains("存在しないシート", ex.Message); // sheet name (data) is kept verbatim in the message
        }

        [Fact]
        public void ReadSheet_MissingFile_Throws()
        {
            Assert.Throws<ExcelReadException>(
                () => _service.ReadSheetSnapshot(
                    Path.GetFullPath(Path.Combine(
                        AppContext.BaseDirectory, "no_such_file.xlsx")), "Step0", null).Sheet);
        }

        [Fact]
        public void ReadSheet_WhileFileIsLockedForWriting_Succeeds()
        {
            // Simulates Excel holding the file open (write access held, read sharing allowed)
            using (new FileStream(FixturePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                SheetModel model = _service.ReadSheetSnapshot(FixturePath, "Step0", null).Sheet;
                Assert.Equal("1235", model.GetText(3, 1));
            }
        }

        [Fact]
        public void ReadSheet_WhileFileIsFullyLocked_ThrowsExcelReadException()
        {
            string lockedCopy = Path.Combine(
                Path.GetTempPath(), "esl_locked_" + Guid.NewGuid().ToString("N") + ".xlsx");
            File.Copy(FixturePath, lockedCopy);
            try
            {
                using (new FileStream(lockedCopy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.Throws<ExcelReadException>(
                        () => _service.ReadSheetSnapshot(lockedCopy, "Step0", null).Sheet);
                }
            }
            finally
            {
                File.Delete(lockedCopy);
            }
        }

        [Fact]
        public void ReadSheetSnapshot_FingerprintsTheExactParsedSnapshot()
        {
            SheetReadResult result = _service.ReadSheetSnapshot(
                BuiltinFormatsFixturePath, "BuiltinFormats", null);
            byte[] sourceBytes = File.ReadAllBytes(BuiltinFormatsFixturePath);
            string expectedHash;
            using (SHA256 sha = SHA256.Create())
            {
                expectedHash = BitConverter.ToString(sha.ComputeHash(sourceBytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }

            Assert.Equal(expectedHash, result.Fingerprint.Sha256);
            Assert.Equal(sourceBytes.LongLength, result.Fingerprint.SizeBytes);
            Assert.True(result.Fingerprint.LastWriteUtcTicks > 0);
            Assert.Equal("Built-in 14 short date", result.Sheet.GetText(1, 0));
        }

        [Fact]
        public void BuiltinDateFormatsFixture_UsesCultureForBuiltInsAndInvariantCustomControl()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            DateTime value = new DateTime(2026, 6, 10, 13, 45, 0);
            try
            {
                CultureInfo en = CultureInfo.GetCultureInfo("en-US");
                CultureInfo ja = CultureInfo.GetCultureInfo("ja-JP");
                CultureInfo.CurrentCulture = en;
                SheetModel enModel = _service.ReadSheetSnapshot(
                    BuiltinFormatsFixturePath, "BuiltinFormats", null).Sheet;
                CultureInfo.CurrentCulture = ja;
                SheetModel jaModel = _service.ReadSheetSnapshot(
                    BuiltinFormatsFixturePath, "BuiltinFormats", null).Sheet;

                Assert.Equal(value.ToString(en.DateTimeFormat.ShortDatePattern, en), enModel.GetText(1, 1));
                Assert.Equal(value.ToString(en.DateTimeFormat.ShortTimePattern, en), enModel.GetText(2, 1));
                Assert.Equal(value.ToString(
                    en.DateTimeFormat.ShortDatePattern + " " + en.DateTimeFormat.ShortTimePattern, en),
                    enModel.GetText(3, 1));
                Assert.Equal(value.ToString(ja.DateTimeFormat.ShortDatePattern, ja), jaModel.GetText(1, 1));
                Assert.Equal(value.ToString(ja.DateTimeFormat.ShortTimePattern, ja), jaModel.GetText(2, 1));
                Assert.Equal(value.ToString(
                    ja.DateTimeFormat.ShortDatePattern + " " + ja.DateTimeFormat.ShortTimePattern, ja),
                    jaModel.GetText(3, 1));
                Assert.Equal("2026/6/10", enModel.GetText(4, 1));
                Assert.Equal("2026/6/10", jaModel.GetText(4, 1));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void ReadSheet_DoesNotLeaveTempCopies()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ExcelScheduleLink");
            int before = CountTempCopies(tempDir);
            _service.ReadSheetSnapshot(FixturePath, "Step0", null);
            int after = CountTempCopies(tempDir);
            Assert.Equal(before, after);
        }

        [Fact]
        public void OutOfRangeDateSerialFixture_FallsBackAndSucceeds()
        {
            SheetModel model = _service.ReadSheetSnapshot(
                OutOfRangeDateSerialFixturePath, "OutOfRangeDates", null).Sheet;

            Assert.Equal("10000000000", model.GetText(1, 1));
            Assert.Equal("-10000000000", model.GetText(2, 1));
        }

        [Theory]
        [InlineData("Rows600", 500, 10, 600, 10)]
        [InlineData("Cols60", 20, 50, 20, 60)]
        [InlineData("Both", 500, 50, 520, 55)]
        public void V093OversizedFixture_IsBoundedAndKeepsSourceDimensions(
            string sheetName, int rows, int columns, int sourceRows, int sourceColumns)
        {
            SheetModel model = _service.ReadSheetSnapshot(
                OversizedFixturePath, sheetName, null).Sheet;

            Assert.Equal(rows, model.RowCount);
            Assert.Equal(columns, model.ColumnCount);
            Assert.Equal(sourceRows, model.SourceRowCount);
            Assert.Equal(sourceColumns, model.SourceColumnCount);
            Assert.Equal("R1C1", model.GetText(0, 0));
        }

        private static int CountTempCopies(string tempDir)
        {
            return Directory.Exists(tempDir)
                ? Directory.GetFiles(tempDir, "copy_*.xlsx").Length
                : 0;
        }

        private static string TestDataPath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "TestData", fileName));
        }
    }
}
