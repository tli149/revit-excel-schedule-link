namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// Centralized user-facing strings for the Excel reading layer.
    /// All messages that can surface in dialogs or logs must live here so they
    /// are not scattered across the codebase (English-only by requirement;
    /// data values such as sheet names and paths are interpolated as-is and
    /// keep full Unicode).
    /// </summary>
    internal static class Messages
    {
        // ---- Cell / range references ----

        public const string CellReferenceEmpty =
            "Cell reference is empty.";

        public static string CellReferenceInvalid(string reference)
        {
            return "Invalid cell reference format: " + reference;
        }

        public static string CellReferenceInvalidRow(string reference)
        {
            return "Invalid row number in cell reference: " + reference;
        }

        public static string CellReferenceOutOfBounds(
            string reference, string coordinateKind, int maximum)
        {
            return "The cell reference exceeds Excel's maximum " + coordinateKind
                + " in \"" + reference + "\" (maximum " + maximum + ").";
        }

        public const string RangeEmpty =
            "Range address is empty.";

        public static string RangeInvalid(string rangeAddress)
        {
            return "Invalid range address format (expected e.g. A1:F30): " + rangeAddress;
        }

        public static string RangeReversed(string rangeAddress)
        {
            return "Invalid range address direction (expected top-left:bottom-right): " + rangeAddress;
        }

        // ---- Workbook / worksheet ----

        public const string WorkbookPartMissing =
            "The workbook part is missing (the .xlsx file may be corrupted).";

        public static string SheetNotFound(string sheetName)
        {
            return "Worksheet not found: \"" + sheetName + "\"";
        }

        public const string SheetNameNotSpecified =
            "Worksheet name is not specified.";

        public static string SheetNamesFailed(string xlsxPath)
        {
            return "Failed to list worksheets in: " + xlsxPath;
        }

        public static string ReadSheetFailed(string xlsxPath, string sheetName)
        {
            return "Failed to read workbook: " + xlsxPath + " (worksheet \"" + sheetName + "\")";
        }

        public const string ReadSheetFingerprintFailed =
            "Failed to fingerprint the exact temporary workbook snapshot.";

        // ---- Workbook safety limits ----

        public static string WorkbookLimitExceeded(string limit, long actual, long maximum)
        {
            return "The workbook exceeds the Excel Schedule Link safety limit for "
                + limit + " (" + actual + "; maximum " + maximum + "). "
                + "Use a smaller workbook or select a smaller worksheet range and try again.";
        }

        public static string ExcelDimensionExceeded(
            string subject, int rows, int columns, int maximumRows, int maximumColumns)
        {
            return "The dimensions in " + subject + " are " + rows + " rows x " + columns
                + " columns, which exceeds Excel's legal limit of " + maximumRows
                + " rows x " + maximumColumns + " columns. Repair the workbook or select a "
                + "valid worksheet or range and try again.";
        }

        public static string ExcelCoordinateExceeded(
            string coordinateKind, long value, int maximum, string sheetName)
        {
            return "The workbook contains an invalid " + coordinateKind + " coordinate ("
                + value + ") in worksheet \"" + sheetName + "\". The maximum is "
                + maximum + ". Select a valid worksheet or range and try again.";
        }

        public static string InvalidColumnSpan(uint min, uint max, int maximum)
        {
            return "The workbook contains an invalid column span (min=" + min + ", max="
                + max + "). Column spans must stay between 1 and " + maximum
                + ". Repair the workbook or select a different worksheet and try again.";
        }

        public static string ZipCompressionRatioExceeded(string entryName, double maximum)
        {
            return "The workbook ZIP entry \"" + entryName
                + "\" has an unsafe compression ratio (maximum " + maximum
                + ":1). Save the workbook again or use a trusted, smaller workbook.";
        }

        public const string ZipPreScanFailed =
            "The workbook ZIP package could not be safely inspected before reading. "
            + "Save the workbook again or use a trusted .xlsx file.";

        public static string OpenXmlCharacterLimitExceeded(string subject, long maximum)
        {
            return "The " + subject + " exceeds the Open XML character safety limit ("
                + maximum + " characters per part). Save a smaller workbook or split the "
                + "workbook into smaller files and try again.";
        }

        public static string ZipEntryMetadataInvalid(string entryName)
        {
            return "The workbook ZIP entry \"" + entryName
                + "\" has invalid size metadata. Save the workbook again or use a trusted .xlsx file.";
        }

        public static string WorksheetRelationshipMissing(string sheetName)
        {
            return "Worksheet relationship is missing for worksheet \"" + sheetName + "\".";
        }

        public static string WorksheetPartMissing(string sheetName)
        {
            return "Worksheet content is missing for worksheet \"" + sheetName + "\".";
        }

        // ---- Temp copy ----

        public const string SourcePathNotSpecified =
            "The .xlsx path is not specified.";

        public static string SourceFileNotFound(string path)
        {
            return "The .xlsx file was not found: " + path;
        }

        public static string TempCopyFailed(string path, int attempts)
        {
            return "Failed to create a temporary copy of the .xlsx file after "
                + attempts + " attempt(s): " + path;
        }

        public static string SourceFileTooLarge(string path, long actual, long maximum)
        {
            return "The .xlsx file is too large to read safely (" + actual
                + " bytes; maximum " + maximum + "). Use a smaller workbook or split it "
                + "into smaller files: " + path;
        }

        public static string InvalidSourcePath(
            string path, ExcelScheduleLink.Contracts.StoredWorkbookPathIssue issue)
        {
            return "The workbook path was rejected before filesystem access ("
                + issue + "): " + path;
        }
    }
}
