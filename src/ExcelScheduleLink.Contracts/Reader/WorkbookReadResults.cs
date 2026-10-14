using System;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Plain metadata for the exact workbook snapshot that was read. The hash
    /// and size describe the copied snapshot; the timestamp is the source file
    /// timestamp observed while that snapshot was created.
    /// </summary>
    public sealed class SourceFingerprint
    {
        public SourceFingerprint(string sha256, long sizeBytes, long lastWriteUtcTicks)
        {
            Sha256 = sha256 ?? string.Empty;
            SizeBytes = sizeBytes;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }

        public string Sha256 { get; }
        public long SizeBytes { get; }
        public long LastWriteUtcTicks { get; }
    }

    /// <summary>Worksheet data and the fingerprint of the same temporary snapshot.</summary>
    public sealed class SheetReadResult
    {
        public SheetReadResult(SheetModel sheet, SourceFingerprint fingerprint)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));
            Sheet = sheet;
            Fingerprint = fingerprint;
        }

        public SheetModel Sheet { get; }
        public SourceFingerprint Fingerprint { get; }
    }
}
