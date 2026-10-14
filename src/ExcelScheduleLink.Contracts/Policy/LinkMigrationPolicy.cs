// Revit-free decision logic, kept in Contracts so the net8 tests can exercise it.
using System;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Dependency-free representation of the v1 fields used by the migration
    /// normalizer. Revit maps its Entity values to and from this object.
    /// </summary>
    public sealed class LinkFieldSnapshot
    {
        public const int LegacySchemaVersion = 1;
        public const int CurrentSchemaVersion = LegacySchemaVersion + 1;

        public int SchemaVersion;
        public int RendererVersion;
        public string AbsolutePath = string.Empty;
        public string RelativePath = string.Empty;
        public int PathMode;
        public string SheetName = string.Empty;
        public int RangeMode;
        public string RangeAddress = string.Empty;
        public long SourceLastWriteUtcTicks;
        public long LastLoadedUtcTicks;
        public int ColumnWidthMode;
        public int RowHeightMode;
        public double WidthScale;
        public string CreatedSourceSha256 = string.Empty;
        public string LastAcceptedSourceSha256 = string.Empty;
        public long LastAcceptedSourceSizeBytes;
    }

    /// <summary>Pure v1-to-v2 field normalization; no Revit or transaction dependency.</summary>
    public static class LinkMigrationNormalizer
    {
        public static LinkFieldSnapshot ToV2(LinkFieldSnapshot v1, string stableRelativePath)
        {
            if (v1 == null) throw new ArgumentNullException(nameof(v1));

            return new LinkFieldSnapshot
            {
                SchemaVersion = LinkFieldSnapshot.CurrentSchemaVersion,
                RendererVersion = v1.RendererVersion,
                AbsolutePath = v1.AbsolutePath ?? string.Empty,
                RelativePath = RelativePathPolicy.IsTrulyRelative(stableRelativePath)
                    ? stableRelativePath
                    : string.Empty,
                PathMode = 0,
                SheetName = v1.SheetName ?? string.Empty,
                RangeMode = v1.RangeMode,
                RangeAddress = v1.RangeAddress ?? string.Empty,
                SourceLastWriteUtcTicks = v1.SourceLastWriteUtcTicks,
                LastLoadedUtcTicks = v1.LastLoadedUtcTicks,
                ColumnWidthMode = v1.ColumnWidthMode,
                RowHeightMode = v1.RowHeightMode,
                WidthScale = v1.WidthScale,
                CreatedSourceSha256 = string.Empty,
                LastAcceptedSourceSha256 = string.Empty,
                LastAcceptedSourceSizeBytes = 0
            };
        }
    }
}
