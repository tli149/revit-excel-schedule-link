using System;
using System.IO;
using System.Threading;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// Copies an .xlsx file to a temporary file with retries and deletes the
    /// copy on Dispose. The source is opened with FileShare.ReadWrite and
    /// stream-copied so the read works even while Excel holds the file open
    /// in another process (we never parse the original file directly; Open XML
    /// always reads the copy). A failed copy throws ExcelReadException — by
    /// contract the caller must not modify the Revit side in that case.
    /// </summary>
    internal sealed class TempCopyGuard : IDisposable
    {
        private const int DefaultMaxAttempts = 3;
        private const int DefaultRetryDelayMs = 500;

        public string CopyPath { get; }
        public long SourceLastWriteUtcTicks { get; }

        private TempCopyGuard(string copyPath, long sourceLastWriteUtcTicks)
        {
            CopyPath = copyPath;
            SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
        }

        public static TempCopyGuard Create(string sourcePath)
        {
            return Create(sourcePath, DefaultMaxAttempts, DefaultRetryDelayMs);
        }

        public static TempCopyGuard Create(string sourcePath, int maxAttempts, int retryDelayMs)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                throw new ExcelReadException(Messages.SourcePathNotSpecified);
            }
            StoredWorkbookPathAssessment assessment =
                StoredWorkbookPathPolicy.ValidateSyntax(sourcePath);
            if (!assessment.IsSyntacticallyValid)
            {
                throw new ExcelReadException(Messages.InvalidSourcePath(
                    sourcePath, assessment.Issue));
            }
            if (!File.Exists(sourcePath))
            {
                throw new ExcelReadException(Messages.SourceFileNotFound(sourcePath));
            }

            long initialSourceLength;
            try
            {
                initialSourceLength = new FileInfo(sourcePath).Length;
            }
            catch (Exception ex)
            {
                throw new ExcelReadException(Messages.SourceFileNotFound(sourcePath), ex);
            }
            if (initialSourceLength > WorkbookReadLimits.MaxSourceBytes)
            {
                throw new ExcelReadException(Messages.SourceFileTooLarge(
                    sourcePath, initialSourceLength, WorkbookReadLimits.MaxSourceBytes));
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ExcelScheduleLink");
            string destPath = Path.Combine(
                tempDir, "copy_" + Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath));

            Exception lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Recreated per attempt: Dispose removes the folder once it is
                    // empty, so creating it once outside the loop could leave a
                    // retry with no directory to write into.
                    Directory.CreateDirectory(tempDir);
                    long sourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
                    using (FileStream src = new FileStream(
                        sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (FileStream dst = new FileStream(
                        destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        CopyBounded(src, dst, sourcePath);
                    }
                    return new TempCopyGuard(destPath, sourceLastWriteUtcTicks);
                }
                catch (ExcelReadException)
                {
                    TryDeleteFile(destPath);
                    throw;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }
                if (attempt < maxAttempts)
                {
                    Thread.Sleep(retryDelayMs);
                }
            }

            throw new ExcelReadException(
                Messages.TempCopyFailed(sourcePath, maxAttempts), lastError);
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
                    throw new ExcelReadException(Messages.SourceFileTooLarge(
                        sourcePath, total, WorkbookReadLimits.MaxSourceBytes));
                }
                destination.Write(buffer, 0, read);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(CopyPath))
                {
                    File.Delete(CopyPath);
                }
            }
            catch (IOException)
            {
                // Ignore deletion failures; the OS temp cleanup will collect strays.
            }
            catch (UnauthorizedAccessException)
            {
            }

            TryRemoveEmptyDirectory(Path.GetDirectoryName(CopyPath));
        }

        /// <summary>
        /// Removes the shared temp folder once it holds no more copies
        /// (best effort). Inside the Revit process Path.GetTempPath() returns a
        /// per-session sandbox, so keeping the folder leaves one empty directory
        /// behind per Revit launch. Never recursive: it succeeds only while the
        /// folder is empty, so a copy another read still needs is never removed.
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
                // Not empty (another read is in flight) or already gone.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
