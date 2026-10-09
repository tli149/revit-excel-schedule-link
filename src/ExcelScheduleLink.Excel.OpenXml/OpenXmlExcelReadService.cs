using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml.Internal;

namespace ExcelScheduleLink.Excel.OpenXml
{
    /// <summary>
    /// In-process implementation of IExcelReadService (Open XML SDK).
    /// Every read follows "temp copy with retries → parse the copy read-only →
    /// delete the copy", so it works while Excel holds the source file open.
    /// All failures surface as ExcelReadException — by contract the caller
    /// must not start a Transaction nor modify the Revit side in that case.
    /// </summary>
    public sealed class OpenXmlExcelReadService : IExcelReadService
    {
        public IReadOnlyList<string> GetSheetNames(string xlsxPath)
        {
            try
            {
                using (TempCopyGuard copy = TempCopyGuard.Create(xlsxPath))
                {
                    WorkbookReadLimits.ValidateZip(copy.CopyPath);
                    using (SpreadsheetDocument doc = SpreadsheetDocument.Open(
                        copy.CopyPath, false, WorkbookReadLimits.CreateOpenSettings()))
                    {
                        WorkbookReadLimits.ValidateWorkbookPart(doc);
                        return RawCellReader.GetSheetNames(doc);
                    }
                }
            }
            catch (ExcelReadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (WorkbookReadLimits.IsMaxCharactersInPartException(ex))
                {
                    throw new ExcelReadException(
                        Messages.OpenXmlCharacterLimitExceeded(
                            "workbook XML part", WorkbookReadLimits.MaxCharactersInPart), ex);
                }
                throw new ExcelReadException(Messages.SheetNamesFailed(xlsxPath), ex);
            }
        }

        public SheetReadResult ReadSheetSnapshot(string xlsxPath, string sheetName, string rangeAddress)
        {
            if (string.IsNullOrEmpty(sheetName))
            {
                throw new ExcelReadException(Messages.SheetNameNotSpecified);
            }

            try
            {
                using (TempCopyGuard copy = TempCopyGuard.Create(xlsxPath))
                {
                    WorkbookReadLimits.ValidateZip(copy.CopyPath);
                    RawSheetData raw;
                    using (SpreadsheetDocument doc = SpreadsheetDocument.Open(
                        copy.CopyPath, false, WorkbookReadLimits.CreateOpenSettings()))
                    {
                        WorkbookReadLimits.ValidateDocument(doc, sheetName, rangeAddress);
                        raw = RawCellReader.Read(doc, sheetName, rangeAddress);
                    }
                    SheetModel sheet = ToSheetModel(raw);
                    SourceFingerprint fingerprint = Fingerprint(copy);
                    return new SheetReadResult(sheet, fingerprint);
                }
            }
            catch (ExcelReadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (WorkbookReadLimits.IsMaxCharactersInPartException(ex))
                {
                    throw new ExcelReadException(
                        Messages.OpenXmlCharacterLimitExceeded(
                            "XML part in the selected workbook",
                            WorkbookReadLimits.MaxCharactersInPart), ex);
                }
                throw new ExcelReadException(Messages.ReadSheetFailed(xlsxPath, sheetName), ex);
            }
        }

        /// <summary>
        /// Hashes the same temporary copy that RawCellReader consumed. The
        /// source and package limits were enforced before this method runs, so
        /// the fingerprint still describes the exact bounded snapshot.
        /// </summary>
        private static SourceFingerprint Fingerprint(TempCopyGuard copy)
        {
            try
            {
                FileInfo file = new FileInfo(copy.CopyPath);
                using (SHA256 algorithm = SHA256.Create())
                using (FileStream stream = new FileStream(
                    copy.CopyPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] hash = algorithm.ComputeHash(stream);
                    string sha256 = BitConverter.ToString(hash)
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                    return new SourceFingerprint(
                        sha256, file.Length, copy.SourceLastWriteUtcTicks);
                }
            }
            catch (Exception ex)
            {
                throw new ExcelReadException(
                    Messages.ReadSheetFingerprintFailed, ex);
            }
        }

        private static SheetModel ToSheetModel(RawSheetData raw)
        {
            string[,] texts = new string[raw.RowCount, raw.ColumnCount];
            for (int r = 0; r < raw.RowCount; r++)
            {
                for (int c = 0; c < raw.ColumnCount; c++)
                {
                    texts[r, c] = CellDisplayFormatter.Format(raw.Cells[r, c], raw.Date1904);
                }
            }

            List<MergedRange> merges = new List<MergedRange>(raw.Merges.Count);
            foreach (MergeInfo m in raw.Merges)
            {
                merges.Add(new MergedRange(m.FirstRow, m.FirstColumn, m.LastRow, m.LastColumn));
            }

            List<ColumnInfo> columns = new List<ColumnInfo>(raw.ColumnWidths.Count);
            foreach (KeyValuePair<int, double> kv in raw.ColumnWidths)
            {
                columns.Add(new ColumnInfo(kv.Key, kv.Value));
            }
            columns.Sort(CompareColumns);

            return new SheetModel(
                raw.SheetName, texts, merges, columns,
                raw.SourceRowCount, raw.SourceColumnCount);
        }

        private static int CompareColumns(ColumnInfo a, ColumnInfo b)
        {
            return a.ColumnIndex.CompareTo(b.ColumnIndex);
        }
    }
}
