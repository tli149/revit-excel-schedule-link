using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    [Collection("WorkbookFileSystem")]
    public class WorkbookSecurityTests
    {
        private readonly OpenXmlExcelReadService _service = new OpenXmlExcelReadService();

        [Fact]
        public void GetSheetNames_RejectsAHighlyCompressedOversizedPartBeforeDomLoad()
        {
            string path = CreateWorkbook(CreateSimpleSheet(), AddCompressedEntry);
            try
            {
                ExcelReadException ex = Assert.Throws<ExcelReadException>(
                    () => _service.GetSheetNames(path));

                Assert.Contains("compression ratio", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadSheet_RejectsAnUncompressedOversizedPartBeforeDomLoad()
        {
            string path = CreateWorkbook(CreateSimpleSheet(), AddOversizedEntry);
            try
            {
                ExcelReadException ex = Assert.Throws<ExcelReadException>(
                    () => _service.ReadSheetSnapshot(path, "Sheet1", null));

                Assert.Contains("ZIP entry uncompressed size", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadSheet_UsesActualCellsWhenDimensionReachesExcelMaximum()
        {
            string path = CreateWorkbook(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:XFD1048576\" />"
                + "<sheetData><row r=\"1048576\"><c r=\"XFD1048576\" t=\"str\">"
                + "<v>far away</v></c></row></sheetData></worksheet>");
            try
            {
                SheetModel model = _service.ReadSheetSnapshot(path, "Sheet1", null).Sheet;

                Assert.Equal(500, model.RowCount);
                Assert.Equal(50, model.ColumnCount);
                Assert.Equal(1048576, model.SourceRowCount);
                Assert.Equal(16384, model.SourceColumnCount);
                Assert.Equal(string.Empty, model.GetText(0, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExplicitRangeIsBoundedToTheRenderEnvelope()
        {
            string path = CreateWorkbook(CreateSimpleSheet());
            try
            {
                SheetModel model = _service.ReadSheetSnapshot(
                    path, "Sheet1", "A1:XFD1048576").Sheet;

                Assert.Equal(500, model.RowCount);
                Assert.Equal(50, model.ColumnCount);
                Assert.Equal(1048576, model.SourceRowCount);
                Assert.Equal(16384, model.SourceColumnCount);
                Assert.Equal("ok", model.GetText(0, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void StaleMaximumDimensionOnSmallSheetIsIgnoredForUsedRange()
        {
            string path = CreateWorkbook(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:XFD1048576\" /><sheetData><row r=\"1\"><c r=\"A1\" t=\"str\">"
                + "<v>small</v></c></row></sheetData></worksheet>");
            try
            {
                SheetModel model = _service.ReadSheetSnapshot(path, "Sheet1", null).Sheet;

                Assert.Equal(1, model.RowCount);
                Assert.Equal(1, model.ColumnCount);
                Assert.Equal("small", model.GetText(0, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadSheet_SelectedLargeWorksheetIsBoundedAndRetainsTopLeftCells()
        {
            string path = CreateWorkbook(CreateDenseSheet(800, 60));
            try
            {
                SheetModel model = _service.ReadSheetSnapshot(path, "Sheet1", null).Sheet;

                Assert.Equal(500, model.RowCount);
                Assert.Equal(50, model.ColumnCount);
                Assert.Equal(800, model.SourceRowCount);
                Assert.Equal(60, model.SourceColumnCount);
                Assert.Equal("R1C1", model.GetText(0, 0));
                Assert.Equal("R500C50", model.GetText(499, 49));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void GetSheetNamesAndSelectedReadIgnoreAnUnselectedTwentyThousandRowSheet()
        {
            string path = CreateWorkbookWithUnselectedLargeSheet(
                CreateRowsOnlySheet(20000, true), CreateSimpleSheet());
            try
            {
                Assert.Equal(new[] { "Large", "Small" }, _service.GetSheetNames(path));

                SheetModel model = _service.ReadSheetSnapshot(path, "Small", null).Sheet;
                Assert.Equal(1, model.RowCount);
                Assert.Equal(1, model.ColumnCount);
                Assert.Equal("ok", model.GetText(0, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadSheet_AllowsThirtyThousandCellFormats()
        {
            string path = CreateWorkbook(
                CreateSimpleSheet(), null, CreateStylesXml(30000));
            try
            {
                SheetModel model = _service.ReadSheetSnapshot(path, "Sheet1", null).Sheet;

                Assert.Equal("ok", model.GetText(0, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadSheet_RejectsAReferenceBeyondExcelsLegalColumnLimit()
        {
            string path = CreateWorkbook(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:XFE1\" /><sheetData><row r=\"1\"><c r=\"XFE1\" t=\"str\">"
                + "<v>illegal</v></c></row></sheetData></worksheet>");
            try
            {
                ExcelReadException ex = Assert.Throws<ExcelReadException>(
                    () => _service.ReadSheetSnapshot(path, "Sheet1", null));

                Assert.Contains("maximum column", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void InvalidColumnSpanIsRejectedWithoutIteratingTheAttackerSpan()
        {
            string path = CreateWorkbook(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:A1\" /><cols><col min=\"1\" max=\"2147483648\" "
                + "width=\"8\" customWidth=\"1\" /></cols>"
                + "<sheetData><row r=\"1\"><c r=\"A1\" t=\"str\"><v>ok</v></c></row></sheetData>"
                + "</worksheet>");
            try
            {
                ExcelReadException ex = Assert.Throws<ExcelReadException>(
                    () => _service.ReadSheetSnapshot(path, "Sheet1", null));

                Assert.Contains("column span", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void OverlongCellValueIsRejectedByTheStringLimit()
        {
            string value = new string('x', 32768);
            string path = CreateWorkbook(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:A1\" /><sheetData><row r=\"1\"><c r=\"A1\" t=\"str\"><v>"
                + value + "</v></c></row></sheetData></worksheet>");
            try
            {
                ExcelReadException ex = Assert.Throws<ExcelReadException>(
                    () => _service.ReadSheetSnapshot(path, "Sheet1", null));

                Assert.Contains("cell value length", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateSimpleSheet()
        {
            return "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<dimension ref=\"A1:A1\" /><sheetData><row r=\"1\"><c r=\"A1\" t=\"str\">"
                + "<v>ok</v></c></row></sheetData></worksheet>";
        }

        private static string CreateWorkbook(
            string worksheetXml,
            Action<ZipArchive> addExtraEntry = null,
            string stylesXml = null)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "esl_security_" + Guid.NewGuid().ToString("N") + ".xlsx");
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
            {
                AddText(archive, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />"
                    + "<Default Extension=\"xml\" ContentType=\"application/xml\" />"
                    + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" />"
                    + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />"
                    + (stylesXml == null ? string.Empty
                        : "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\" />")
                    + "</Types>");
                AddText(archive, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\" />"
                    + "</Relationships>");
                AddText(archive, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                    + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                    + "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\" /></sheets></workbook>");
                AddText(archive, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" />"
                    + (stylesXml == null ? string.Empty
                        : "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\" />")
                    + "</Relationships>");
                AddText(archive, "xl/worksheets/sheet1.xml", worksheetXml);
                if (stylesXml != null)
                {
                    AddText(archive, "xl/styles.xml", stylesXml);
                }
                if (addExtraEntry != null)
                {
                    addExtraEntry(archive);
                }
            }
            return path;
        }

        private static void AddText(ZipArchive archive, string name, string value)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(
                stream, new UTF8Encoding(false), 4096, true))
            {
                writer.Write(value);
            }
        }

        private static string CreateWorkbookWithUnselectedLargeSheet(
            string largeWorksheetXml, string selectedWorksheetXml)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "esl_multisheet_security_"
                    + Guid.NewGuid().ToString("N") + ".xlsx");
            using (FileStream stream = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
            {
                AddText(archive, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />"
                    + "<Default Extension=\"xml\" ContentType=\"application/xml\" />"
                    + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" />"
                    + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />"
                    + "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />"
                    + "</Types>");
                AddText(archive, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\" />"
                    + "</Relationships>");
                AddText(archive, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                    + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                    + "<sheets><sheet name=\"Large\" sheetId=\"1\" r:id=\"rId1\" />"
                    + "<sheet name=\"Small\" sheetId=\"2\" r:id=\"rId2\" /></sheets></workbook>");
                AddText(archive, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" />"
                    + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\" />"
                    + "</Relationships>");
                AddText(archive, "xl/worksheets/sheet1.xml", largeWorksheetXml);
                AddText(archive, "xl/worksheets/sheet2.xml", selectedWorksheetXml);
            }
            return path;
        }

        private static string CreateRowsOnlySheet(
            int rowCount, bool includeInvalidColumnSpan = false)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            xml.Append("<dimension ref=\"A1:A").Append(rowCount).Append("\" />");
            if (includeInvalidColumnSpan)
            {
                // This proves that an unselected worksheet is not scanned by
                // either sheet-name discovery or the selected-sheet read.
                xml.Append("<cols><col min=\"1\" max=\"2147483648\" width=\"8\" customWidth=\"1\" /></cols>");
            }
            xml.Append("<sheetData>");
            for (int row = 1; row <= rowCount; row++)
            {
                xml.Append("<row r=\"").Append(row).Append("\"><c r=\"A")
                    .Append(row).Append("\" t=\"str\"><v>row-").Append(row)
                    .Append("</v></c></row>");
            }
            xml.Append("</sheetData></worksheet>");
            return xml.ToString();
        }

        private static string CreateDenseSheet(int rowCount, int columnCount)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            xml.Append("<dimension ref=\"A1:BH").Append(rowCount).Append("\" /><sheetData>");
            for (int row = 1; row <= rowCount; row++)
            {
                xml.Append("<row r=\"").Append(row).Append("\">");
                for (int column = 1; column <= columnCount; column++)
                {
                    xml.Append("<c r=\"").Append(ExcelColumn(column)).Append(row)
                        .Append("\" t=\"str\"><v>R").Append(row).Append("C")
                        .Append(column).Append("</v></c>");
                }
                xml.Append("</row>");
            }
            xml.Append("</sheetData></worksheet>");
            return xml.ToString();
        }

        private static string ExcelColumn(int oneBasedColumn)
        {
            StringBuilder letters = new StringBuilder();
            int value = oneBasedColumn;
            while (value > 0)
            {
                value--;
                letters.Insert(0, (char)('A' + (value % 26)));
                value /= 26;
            }
            return letters.ToString();
        }

        private static string CreateStylesXml(int cellFormatCount)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
                .Append("<fonts count=\"1\"><font><sz val=\"11\" /></font></fonts>")
                .Append("<fills count=\"2\"><fill><patternFill patternType=\"none\" /></fill>")
                .Append("<fill><patternFill patternType=\"gray125\" /></fill></fills>")
                .Append("<borders count=\"1\"><border /></borders>")
                .Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" /></cellStyleXfs>")
                .Append("<cellXfs count=\"").Append(cellFormatCount).Append("\">");
            for (int i = 0; i < cellFormatCount; i++)
            {
                xml.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" />");
            }
            xml.Append("</cellXfs></styleSheet>");
            return xml.ToString();
        }

        private static void AddCompressedEntry(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "xl/compressed-oversized.xml", CompressionLevel.Optimal);
            byte[] buffer = new byte[1024 * 1024];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)'A';
            }
            using (Stream stream = entry.Open())
            {
                for (int i = 0; i < 4; i++)
                {
                    stream.Write(buffer, 0, buffer.Length);
                }
            }
        }

        private static void AddOversizedEntry(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "xl/uncompressed-oversized.xml", CompressionLevel.NoCompression);
            byte[] buffer = new byte[1024 * 1024];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)'B';
            }
            long remaining = 64L * 1024L * 1024L + 1L;
            using (Stream stream = entry.Open())
            {
                while (remaining > 0L)
                {
                    int count = (int)Math.Min((long)buffer.Length, remaining);
                    stream.Write(buffer, 0, count);
                    remaining -= count;
                }
            }
        }
    }
}
