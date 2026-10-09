using System;

using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>How the source path is resolved on reload.</summary>
    internal enum PathMode
    {
        /// <summary>Try the absolute path first, then the model-relative path.</summary>
        AbsolutePreferred = 0,
        /// <summary>Try the model-relative path first, then the absolute path.</summary>
        RelativePreferred = 1
    }

    /// <summary>Which part of the worksheet is read.</summary>
    internal enum RangeMode
    {
        /// <summary>The used range (A1 through the last used cell).</summary>
        UsedRange = 0,
        /// <summary>An explicit range such as "A1:F30".</summary>
        Explicit = 1
    }

    /// <summary>How column widths are treated on reload.</summary>
    internal enum ColumnWidthMode
    {
        /// <summary>Apply the Excel column widths on every reload.</summary>
        ApplyExcelWidths = 0,
        /// <summary>Keep column widths manually adjusted on the Revit side.</summary>
        KeepManualWidths = 1
    }

    /// <summary>How row heights are treated on reload.</summary>
    internal enum RowHeightMode
    {
        /// <summary>Apply Excel-derived or auto-computed row heights on every reload.</summary>
        ApplyExcelHeights = 0,
        /// <summary>Keep row heights manually adjusted on the Revit side.</summary>
        KeepManualHeights = 1
    }

    /// <summary>
    /// Link information stored on a linked ViewSchedule via extensible storage.
    /// Carries the common fields of schema v1 and the v2 fingerprint fields.
    /// The default version remains v1 for legacy parsing; new-link and migration
    /// writers set version 2 before calling SaveV2.
    /// </summary>
    internal sealed class ExcelLinkInfo
    {
        public const int LegacySchemaVersion = LinkFieldSnapshot.LegacySchemaVersion;
        public const int CurrentSchemaVersion = LinkFieldSnapshot.CurrentSchemaVersion;
        public const int CurrentRendererVersion = 2;

        public int SchemaVersion = LegacySchemaVersion;
        public int RendererVersion = CurrentRendererVersion;

        public string AbsolutePath = string.Empty;
        /// <summary>Path relative to the model file's folder; empty when not derivable.</summary>
        public string RelativePath = string.Empty;
        public PathMode PathMode = PathMode.AbsolutePreferred;

        public string SheetName = string.Empty;
        public RangeMode RangeMode = RangeMode.UsedRange;
        /// <summary>"A1:F30"-style address when RangeMode is Explicit; empty otherwise.</summary>
        public string RangeAddress = string.Empty;

        /// <summary>File.GetLastWriteTimeUtc().Ticks of the source at load time.</summary>
        public long SourceLastWriteUtcTicks;
        /// <summary>UTC ticks of the last successful load/reload.</summary>
        public long LastLoadedUtcTicks;

        /// <summary>Fingerprint fields used only when this instance is stored as v2.</summary>
        public string CreatedSourceSha256 = string.Empty;
        public string LastAcceptedSourceSha256 = string.Empty;
        public long LastAcceptedSourceSizeBytes;

        public ColumnWidthMode ColumnWidthMode = ColumnWidthMode.ApplyExcelWidths;
        public RowHeightMode RowHeightMode = RowHeightMode.ApplyExcelHeights;
        /// <summary>
        /// Global multiplier applied when converting Excel column widths to
        /// Revit widths. Default revisited to 1.5 after the v0.3.2 on-machine
        /// test (1.0 was too narrow; 1.2 still felt cramped; ~1.5 reads best in
        /// practice). Content-based minimum widths apply on top of this scale,
        /// and the user can change it per link in the dialog.
        /// </summary>
        public const double DefaultWidthScale = 1.5;
        public double WidthScale = DefaultWidthScale;
    }
}
