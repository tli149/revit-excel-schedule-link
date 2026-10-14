using System.Collections.Generic;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Abstraction of the .xlsx reading service.
    /// This boundary allows swapping the in-process implementation
    /// (Excel.OpenXml) for the out-of-process one (via ReaderHost.exe) in case
    /// of an OpenXml DLL conflict inside the Revit process.
    /// </summary>
    public interface IExcelReadService
    {
        /// <summary>Returns the list of worksheet names.</summary>
        IReadOnlyList<string> GetSheetNames(string xlsxPath);

        /// <summary>
        /// Reads a worksheet and returns the parsed model together with the
        /// fingerprint of the exact temporary snapshot that was parsed.
        /// </summary>
        SheetReadResult ReadSheetSnapshot(string xlsxPath, string sheetName, string rangeAddress);
    }
}
