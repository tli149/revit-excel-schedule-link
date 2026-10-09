using System;
using System.IO;
using System.Threading;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml.Internal;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Copies an XLSX workbook to a temporary file with retries. The source is
    /// opened with FileShare.ReadWrite so it can remain open in Excel; Open XML
    /// reads the copy rather than parsing the source in place. A copy failure
    /// throws, and callers must not change the Revit model in that case.
    /// </summary>
    internal static class LockedFileCopier
    {
        public static string CopyToTemp(string sourcePath, int maxAttempts, int retryDelayMs, Action<string> log)
        {
            StoredWorkbookPathAssessment pathAssessment =
                StoredWorkbookPathPolicy.ValidateSyntax(sourcePath);
            if (!pathAssessment.IsSyntacticallyValid)
            {
                throw new InvalidOperationException(
                    "The probe workbook path was rejected before filesystem access: "
                    + pathAssessment.Issue);
            }
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The source XLSX workbook was not found: " + sourcePath, sourcePath);
            }

            long sourceLength = new FileInfo(sourcePath).Length;
            if (sourceLength > WorkbookReadLimits.MaxSourceBytes)
            {
                throw new InvalidOperationException(
                    "The probe workbook exceeds the source byte limit before copying: "
                    + sourceLength + " bytes; maximum " + WorkbookReadLimits.MaxSourceBytes + ".");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ExcelScheduleLink");
            string destPath = Path.Combine(
                tempDir,
                "copy_" + Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath));

            Exception lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Recreate the directory check on each attempt. Cleanup can
                    // remove the empty directory after an earlier attempt.
                    Directory.CreateDirectory(tempDir);
                    using (FileStream src = new FileStream(
                        sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (FileStream dst = new FileStream(
                        destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        CopyBounded(src, dst, sourcePath);
                    }
                    if (log != null)
                    {
                        log("Temporary copy succeeded (attempt " + attempt + "/" + maxAttempts + "): " + destPath);
                    }
                    return destPath;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                if (log != null)
                {
                    log("Temporary copy failed (attempt " + attempt + "/" + maxAttempts + "): " + lastError.Message);
                }
                if (attempt < maxAttempts)
                {
                    Thread.Sleep(retryDelayMs);
                }
            }

            throw new IOException(
                "The temporary copy failed " + maxAttempts + " times: " + sourcePath, lastError);
        }

        private static void CopyBounded(Stream source, Stream destination, string sourcePath)
        {
            byte[] buffer = new byte[64 * 1024];
            long total = 0L;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total = checked(total + read);
                if (total > WorkbookReadLimits.MaxSourceBytes)
                {
                    throw new InvalidOperationException(
                        "The probe workbook grew beyond the source byte limit while copying: "
                        + sourcePath);
                }
                destination.Write(buffer, 0, read);
            }
        }

        public static void TryDelete(string path, Action<string> log)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    if (log != null)
                    {
                        log("Deleted temporary copy: " + path);
                    }
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log("Could not delete temporary copy (ignored): " + ex.Message);
                }
            }

            TryRemoveEmptyDirectory(Path.GetDirectoryName(path));
        }

        /// <summary>
        /// Best-effort removal of the temporary-copy directory when it is empty.
        /// Revit's Path.GetTempPath() is session-scoped, so leaving the folder
        /// would add one empty directory per launch. This is not recursive: it
        /// succeeds only for an empty directory and cannot remove another copy.
        /// </summary>
        private static void TryRemoveEmptyDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            try
            {
                Directory.Delete(dir, false);
            }
            catch (IOException)
            {
                // It is non-empty (another copy is active) or already absent;
                // both cases are safe to leave alone.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
