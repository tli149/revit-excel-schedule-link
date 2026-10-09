using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// One resource policy for every Open XML workbook read. The package and
    /// DOM limits are intentionally generous for ordinary multi-sheet project
    /// workbooks (128 MiB source, 64 MiB per XML part, and 512 MiB cumulative
    /// expansion), while the output array remains bounded by the existing
    /// 500-by-50 Revit render envelope. Worksheet DOMs are loaded only for the
    /// selected sheet, and cells outside the bounded top-left output window are
    /// never retained. The ZIP/source/part limits remain the primary DoS budget;
    /// they do not turn ordinary large worksheets into an unusable workbook.
    /// </summary>
    public static class WorkbookReadLimits
    {
        // The package budgets are deliberately above normal project exports:
        // 128 MiB accepts multi-sheet, tens-of-megabytes workbooks; 64 MiB per
        // XML part and 512 MiB cumulatively bound expansion before DOM loading;
        // 20,000 entries and 256 sheets leave room for real office templates.
        // The selected-sheet backstops (10,000 merges and 500,000 shared
        // strings) are far above ordinary schedules and remain resource guards,
        // not render-size rules. Style records are handled separately below.
        public const long MaxSourceBytes = 128L * 1024L * 1024L;
        public const int MaxZipEntries = 20000;
        public const long MaxZipEntryUncompressedBytes = 64L * 1024L * 1024L;
        public const long MaxZipTotalUncompressedBytes = 512L * 1024L * 1024L;
        // Ordinary workbook XML compresses well below this ratio; 1,000:1 is
        // generous for real files but still rejects highly repetitive ZIP bombs.
        public const double MaxZipCompressionRatio = 1000.0;
        // A 32-million-character part leaves room for large sheets while
        // stopping the SDK's otherwise-unbounded XML character allocation.
        public const long MaxCharactersInPart = 32000000L;

        public const int MaxWorksheets = 256;
        public const int MaxMergesPerWorksheet = 10000;
        // Excel's cellXfs limit is approximately 64,000. Keep this above the
        // format-file limit so bloated but valid office templates remain usable;
        // ZIP and MaxCharactersInPart still bound the parsed stylesheet.
        public const int MaxStyleRecords = 100000;
        public const int MaxSharedStrings = 500000;
        public const int MaxStringLength = 32767;

        // Excel limits are part of the file format, not a render preference.
        public const int ExcelMaximumRows = 1048576;
        public const int ExcelMaximumColumns = 16384;

        // These values are the single owner of the array/render envelope.
        public const int MaxRowsForArray = StyleIntegrityLimits.MaxRowsPerSchedule;
        public const int MaxColumnsForArray = StyleIntegrityLimits.MaxColumnsPerSchedule;

        // Excel fraction formats use at most three denominator placeholders.
        public const int MaxFractionDenominatorDigits = 3;
        public const int MaxFractionApproximationIterations = 64;

        public static OpenSettings CreateOpenSettings()
        {
            return new OpenSettings
            {
                MaxCharactersInPart = MaxCharactersInPart
            };
        }

        /// <summary>
        /// Recognizes the SDK failure raised when MaxCharactersInPart rejects
        /// an XML part. SDK versions use different exception types, so inspect
        /// the exception chain rather than coupling this policy to one type.
        /// </summary>
        public static bool IsMaxCharactersInPartException(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("MaxCharactersInPart", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("maximum number of characters", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Scans ZIP metadata before SpreadsheetDocument.Open can load DOM parts.</summary>
        public static void ValidateZip(string packagePath)
        {
            try
            {
                long totalUncompressed = 0L;
                using (FileStream stream = new FileStream(
                    packagePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
                {
                    if (archive.Entries.Count > MaxZipEntries)
                    {
                        throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                            "ZIP entry count", archive.Entries.Count, MaxZipEntries));
                    }

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        long uncompressed = entry.Length;
                        long compressed = entry.CompressedLength;
                        if (uncompressed < 0L || compressed < 0L)
                        {
                            throw new ExcelReadException(Messages.ZipEntryMetadataInvalid(entry.FullName));
                        }
                        if (uncompressed > MaxZipEntryUncompressedBytes)
                        {
                            throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                                "ZIP entry uncompressed size for " + entry.FullName,
                                uncompressed, MaxZipEntryUncompressedBytes));
                        }
                        if (uncompressed > 0L && compressed == 0L)
                        {
                            throw new ExcelReadException(Messages.ZipCompressionRatioExceeded(
                                entry.FullName, MaxZipCompressionRatio));
                        }

                        totalUncompressed = checked(totalUncompressed + uncompressed);
                        if (totalUncompressed > MaxZipTotalUncompressedBytes)
                        {
                            throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                                "cumulative ZIP uncompressed size",
                                totalUncompressed, MaxZipTotalUncompressedBytes));
                        }

                        if (compressed > 0L
                            && (double)uncompressed / (double)compressed > MaxZipCompressionRatio)
                        {
                            throw new ExcelReadException(Messages.ZipCompressionRatioExceeded(
                                entry.FullName, MaxZipCompressionRatio));
                        }
                    }
                }
            }
            catch (ExcelReadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ExcelReadException(Messages.ZipPreScanFailed, ex);
            }
        }

        /// <summary>
        /// Validates only the workbook part. In particular, this method never
        /// resolves a worksheet relationship, so sheet-name discovery cannot
        /// make an unrelated worksheet part load or fail.
        /// </summary>
        public static void ValidateWorkbookPart(SpreadsheetDocument document)
        {
            ValidateWorkbookPart(RequireWorkbookPart(document));
        }

        /// <summary>
        /// Validates package tables and the selected worksheet only. The
        /// worksheet dimension element is syntax/coordinate metadata, not the
        /// source of the used range: other tools often leave it stale.
        /// </summary>
        public static void ValidateDocument(
            SpreadsheetDocument document, string selectedSheetName, string rangeAddress)
        {
            WorkbookPart workbookPart = RequireWorkbookPart(document);
            ValidateWorkbookPart(workbookPart);
            ValidateStyles(workbookPart);
            ValidateSharedStrings(workbookPart);

            if (string.IsNullOrEmpty(selectedSheetName))
            {
                return;
            }

            Sheet sheet = workbookPart.Workbook.Descendants<Sheet>()
                .FirstOrDefault(s => s.Name != null
                    && string.Equals(s.Name.Value, selectedSheetName, StringComparison.Ordinal));
            if (sheet == null)
            {
                throw new ExcelReadException(Messages.SheetNotFound(selectedSheetName));
            }
            if (sheet.Id == null)
            {
                throw new ExcelReadException(Messages.WorksheetRelationshipMissing(
                    selectedSheetName));
            }

            WorksheetPart worksheetPart = workbookPart.GetPartById(sheet.Id) as WorksheetPart;
            if (worksheetPart == null || worksheetPart.Worksheet == null)
            {
                throw new ExcelReadException(Messages.WorksheetPartMissing(selectedSheetName));
            }
            ValidateSelectedWorksheet(worksheetPart, selectedSheetName, rangeAddress);
        }

        /// <summary>Clamps a legal source window to the existing render envelope.</summary>
        public static void GetBoundedOutputDimensions(
            int sourceRows, int sourceColumns, string subject,
            out int outputRows, out int outputColumns)
        {
            ValidateExcelDimensions(sourceRows, sourceColumns, subject);
            outputRows = Math.Min(sourceRows, MaxRowsForArray);
            outputColumns = Math.Min(sourceColumns, MaxColumnsForArray);
        }

        private static void ValidateExcelDimensions(int rows, int columns, string subject)
        {
            if (rows < 1 || columns < 1)
            {
                throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                    subject + " must be positive", 0L, 1L));
            }
            if (rows > ExcelMaximumRows || columns > ExcelMaximumColumns)
            {
                throw new ExcelReadException(Messages.ExcelDimensionExceeded(
                    subject, rows, columns, ExcelMaximumRows, ExcelMaximumColumns));
            }
        }

        public static void ValidateString(string value, string subject)
        {
            if (value != null && value.Length > MaxStringLength)
            {
                throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                    subject + " length", value.Length, MaxStringLength));
            }
        }

        /// <summary>
        /// Validates a column span before converting unsigned OOXML values to
        /// signed indexes. The returned intersection is small and safe to loop.
        /// </summary>
        public static bool TryGetAcceptedColumnIntersection(
            uint minValue, uint maxValue, int originColumn, int columnCount,
            out int firstColumn, out int lastColumn)
        {
            firstColumn = 0;
            lastColumn = -1;
            if (minValue < 1U || maxValue < minValue || maxValue > ExcelMaximumColumns)
            {
                throw new ExcelReadException(Messages.InvalidColumnSpan(
                    minValue, maxValue, ExcelMaximumColumns));
            }

            long requestedFirst = (long)minValue - 1L;
            long requestedLast = (long)maxValue - 1L;
            long acceptedFirst = Math.Max(requestedFirst, (long)originColumn);
            long acceptedLast = Math.Min(
                requestedLast, (long)originColumn + (long)columnCount - 1L);
            if (acceptedFirst > acceptedLast)
            {
                return false;
            }

            firstColumn = checked((int)acceptedFirst);
            lastColumn = checked((int)acceptedLast);
            return true;
        }

        private static WorkbookPart RequireWorkbookPart(SpreadsheetDocument document)
        {
            WorkbookPart workbookPart = document != null ? document.WorkbookPart : null;
            if (workbookPart == null || workbookPart.Workbook == null)
            {
                throw new ExcelReadException(Messages.WorkbookPartMissing);
            }
            return workbookPart;
        }

        private static void ValidateWorkbookPart(WorkbookPart workbookPart)
        {
            List<Sheet> sheets = new List<Sheet>(workbookPart.Workbook.Descendants<Sheet>());
            if (sheets.Count > MaxWorksheets)
            {
                throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                    "worksheet count", sheets.Count, MaxWorksheets));
            }

            foreach (Sheet sheet in sheets)
            {
                string sheetName = sheet.Name != null ? sheet.Name.Value : string.Empty;
                ValidateString(sheetName, "worksheet name");
                if (sheet.Id == null)
                {
                    throw new ExcelReadException(Messages.WorksheetRelationshipMissing(sheetName));
                }
            }
        }

        private static void ValidateSelectedWorksheet(
            WorksheetPart worksheetPart, string sheetName, string rangeAddress)
        {
            if (!string.IsNullOrEmpty(rangeAddress))
            {
                int firstRow;
                int firstColumn;
                int lastRow;
                int lastColumn;
                CellReference.ParseRange(
                    rangeAddress, out firstRow, out firstColumn, out lastRow, out lastColumn);
                ValidateExcelDimensions(
                    lastRow - firstRow + 1, lastColumn - firstColumn + 1,
                    "the requested cell range");
            }

            // A dimension declaration is only syntax/coordinate metadata. Its
            // size is intentionally ignored because Excel writers often leave
            // a stale A1:XFD1048576 declaration on a small worksheet.
            SheetDimension dimension = worksheetPart.Worksheet.GetFirstChild<SheetDimension>();
            if (dimension != null && dimension.Reference != null)
            {
                int firstRow;
                int firstColumn;
                int lastRow;
                int lastColumn;
                CellReference.ParseRange(
                    dimension.Reference.Value,
                    out firstRow, out firstColumn, out lastRow, out lastColumn);
            }

            foreach (Column column in worksheetPart.Worksheet.Descendants<Column>())
            {
                if (column.Min != null && column.Max != null)
                {
                    int ignoredFirst;
                    int ignoredLast;
                    TryGetAcceptedColumnIntersection(
                        column.Min.Value, column.Max.Value, 0, ExcelMaximumColumns,
                        out ignoredFirst, out ignoredLast);
                }
            }

            int mergeCount = 0;
            foreach (MergeCell merge in worksheetPart.Worksheet.Descendants<MergeCell>())
            {
                mergeCount++;
                if (mergeCount > MaxMergesPerWorksheet)
                {
                    throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                        "merged-cell count in " + sheetName,
                        mergeCount, MaxMergesPerWorksheet));
                }
                if (merge.Reference != null)
                {
                    int firstRow;
                    int firstColumn;
                    int lastRow;
                    int lastColumn;
                    CellReference.ParseRange(
                        merge.Reference.Value,
                        out firstRow, out firstColumn, out lastRow, out lastColumn);
                }
            }
        }

        private static void ValidateStyles(WorkbookPart workbookPart)
        {
            IEnumerable<WorkbookStylesPart> stylesParts = workbookPart
                .GetPartsOfType<WorkbookStylesPart>();
            foreach (WorkbookStylesPart part in stylesParts)
            {
                Stylesheet stylesheet = part.Stylesheet;
                if (stylesheet == null)
                {
                    continue;
                }
                if (stylesheet.NumberingFormats != null)
                {
                    foreach (NumberingFormat format in
                        stylesheet.NumberingFormats.Elements<NumberingFormat>())
                    {
                        if (format.FormatCode != null)
                        {
                            ValidateString(format.FormatCode.Value, "number format code");
                        }
                    }
                }
                long records = 0L;
                records += CountElements<NumberingFormat>(stylesheet.NumberingFormats);
                records += CountElements<Font>(stylesheet.Fonts);
                records += CountElements<Fill>(stylesheet.Fills);
                records += CountElements<Border>(stylesheet.Borders);
                records += CountElements<CellStyle>(stylesheet.CellStyles);
                records += CountElements<CellFormat>(stylesheet.CellStyleFormats);
                records += CountElements<CellFormat>(stylesheet.CellFormats);
                records += CountElements<DifferentialFormat>(stylesheet.DifferentialFormats);
                if (records > MaxStyleRecords)
                {
                    throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                        "style record count", records, MaxStyleRecords));
                }
            }
        }

        private static long CountElements<T>(OpenXmlElement parent)
            where T : OpenXmlElement
        {
            return parent == null ? 0L : parent.Elements<T>().LongCount();
        }

        private static void ValidateSharedStrings(WorkbookPart workbookPart)
        {
            IEnumerable<SharedStringTablePart> parts =
                workbookPart.GetPartsOfType<SharedStringTablePart>();
            foreach (SharedStringTablePart part in parts)
            {
                SharedStringTable table = part.SharedStringTable;
                if (table == null)
                {
                    continue;
                }
                if (table.ChildElements.Count > MaxSharedStrings)
                {
                    throw new ExcelReadException(Messages.WorkbookLimitExceeded(
                        "shared-string count", table.ChildElements.Count, MaxSharedStrings));
                }
                foreach (OpenXmlElement item in table.ChildElements)
                {
                    ValidateString(item.InnerText, "shared-string value");
                }
            }
        }

    }
}
