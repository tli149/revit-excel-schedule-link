using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Extensible storage schema (v1) for Excel link information, stored
    /// directly on the linked ViewSchedule element.
    ///
    /// The schema GUID and the field set are FROZEN once shipped. Any change
    /// requires a new GUID plus a migration keyed on SchemaVersion.
    /// The WidthScale field is a unitless double and therefore carries
    /// SpecTypeId.Number; values are written and read with UnitTypeId.General.
    /// </summary>
    internal static class ExcelLinkSchema
    {
        public static readonly Guid SchemaGuid = new Guid("6fd491d4-0092-45ae-82ad-2d51ce4a0615");
        public static readonly Guid SchemaV2Guid = new Guid("4d40b55b-c928-4e46-8c15-2c85dbf510ad");

        public const string FieldSchemaVersion = "SchemaVersion";
        public const string FieldRendererVersion = "RendererVersion";
        public const string FieldAbsolutePath = "AbsolutePath";
        public const string FieldRelativePath = "RelativePath";
        public const string FieldPathMode = "PathMode";
        public const string FieldSheetName = "SheetName";
        public const string FieldRangeMode = "RangeMode";
        public const string FieldRangeAddress = "RangeAddress";
        public const string FieldSourceLastWriteUtcTicks = "SourceLastWriteUtcTicks";
        public const string FieldLastLoadedUtcTicks = "LastLoadedUtcTicks";
        public const string FieldColumnWidthMode = "ColumnWidthMode";
        public const string FieldRowHeightMode = "RowHeightMode";
        public const string FieldWidthScale = "WidthScale";
        public const string FieldCreatedSourceSha256 = "CreatedSourceSha256";
        public const string FieldLastAcceptedSourceSha256 = "LastAcceptedSourceSha256";
        public const string FieldLastAcceptedSourceSizeBytes = "LastAcceptedSourceSizeBytes";

        public static Schema GetOrCreate()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("ExcelScheduleLink");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetDocumentation(
                "Excel Schedule Link v1: source workbook and layout settings for a linked ViewSchedule.");

            builder.AddSimpleField(FieldSchemaVersion, typeof(int));
            builder.AddSimpleField(FieldRendererVersion, typeof(int));
            builder.AddSimpleField(FieldAbsolutePath, typeof(string));
            builder.AddSimpleField(FieldRelativePath, typeof(string));
            builder.AddSimpleField(FieldPathMode, typeof(int));
            builder.AddSimpleField(FieldSheetName, typeof(string));
            builder.AddSimpleField(FieldRangeMode, typeof(int));
            builder.AddSimpleField(FieldRangeAddress, typeof(string));
            builder.AddSimpleField(FieldSourceLastWriteUtcTicks, typeof(long));
            builder.AddSimpleField(FieldLastLoadedUtcTicks, typeof(long));
            builder.AddSimpleField(FieldColumnWidthMode, typeof(int));
            builder.AddSimpleField(FieldRowHeightMode, typeof(int));
            FieldBuilder widthScale = builder.AddSimpleField(FieldWidthScale, typeof(double));
            widthScale.SetSpec(SpecTypeId.Number); // unitless double

            return builder.Finish();
        }

        /// <summary>Writes the link info onto the schedule. Must run inside a Transaction.</summary>
        public static void Save(ViewSchedule schedule, ExcelLinkInfo info)
        {
            Schema schema = GetOrCreate();
            Entity entity = new Entity(schema);
            entity.Set(FieldSchemaVersion, info.SchemaVersion);
            entity.Set(FieldRendererVersion, info.RendererVersion);
            entity.Set(FieldAbsolutePath, info.AbsolutePath ?? string.Empty);
            entity.Set(FieldRelativePath, info.RelativePath ?? string.Empty);
            entity.Set(FieldPathMode, (int)info.PathMode);
            entity.Set(FieldSheetName, info.SheetName ?? string.Empty);
            entity.Set(FieldRangeMode, (int)info.RangeMode);
            entity.Set(FieldRangeAddress, info.RangeAddress ?? string.Empty);
            entity.Set(FieldSourceLastWriteUtcTicks, info.SourceLastWriteUtcTicks);
            entity.Set(FieldLastLoadedUtcTicks, info.LastLoadedUtcTicks);
            entity.Set(FieldColumnWidthMode, (int)info.ColumnWidthMode);
            entity.Set(FieldRowHeightMode, (int)info.RowHeightMode);
            entity.Set(FieldWidthScale, info.WidthScale, UnitTypeId.General);
            schedule.SetEntity(entity);
        }

        /// <summary>Reads the link info from a schedule. Returns null when the
        /// schedule carries no (valid) link entity.</summary>
        public static ExcelLinkInfo Load(ViewSchedule schedule)
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return null;
            }
            Entity entity = schedule.GetEntity(schema);
            if (entity == null || !entity.IsValid())
            {
                return null;
            }

            ExcelLinkInfo info = new ExcelLinkInfo();
            info.SchemaVersion = entity.Get<int>(FieldSchemaVersion);
            info.RendererVersion = entity.Get<int>(FieldRendererVersion);
            info.AbsolutePath = entity.Get<string>(FieldAbsolutePath) ?? string.Empty;
            info.RelativePath = entity.Get<string>(FieldRelativePath) ?? string.Empty;
            info.PathMode = (PathMode)entity.Get<int>(FieldPathMode);
            info.SheetName = entity.Get<string>(FieldSheetName) ?? string.Empty;
            info.RangeMode = (RangeMode)entity.Get<int>(FieldRangeMode);
            info.RangeAddress = entity.Get<string>(FieldRangeAddress) ?? string.Empty;
            info.SourceLastWriteUtcTicks = entity.Get<long>(FieldSourceLastWriteUtcTicks);
            info.LastLoadedUtcTicks = entity.Get<long>(FieldLastLoadedUtcTicks);
            info.ColumnWidthMode = (ColumnWidthMode)entity.Get<int>(FieldColumnWidthMode);
            info.RowHeightMode = (RowHeightMode)entity.Get<int>(FieldRowHeightMode);
            info.WidthScale = entity.Get<double>(FieldWidthScale, UnitTypeId.General);
            return info;
        }

        /// <summary>Creates schema v2. This method is never called by Phase 1 user actions.</summary>
        public static Schema GetOrCreateV2()
        {
            Schema schema = Schema.Lookup(SchemaV2Guid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaV2Guid);
            builder.SetSchemaName("ExcelScheduleLink");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetDocumentation(
                "Excel Schedule Link v2: source workbook, fingerprint identity, and layout settings.");

            builder.AddSimpleField(FieldSchemaVersion, typeof(int));
            builder.AddSimpleField(FieldRendererVersion, typeof(int));
            builder.AddSimpleField(FieldAbsolutePath, typeof(string));
            builder.AddSimpleField(FieldRelativePath, typeof(string));
            builder.AddSimpleField(FieldPathMode, typeof(int));
            builder.AddSimpleField(FieldSheetName, typeof(string));
            builder.AddSimpleField(FieldRangeMode, typeof(int));
            builder.AddSimpleField(FieldRangeAddress, typeof(string));
            builder.AddSimpleField(FieldSourceLastWriteUtcTicks, typeof(long));
            builder.AddSimpleField(FieldLastLoadedUtcTicks, typeof(long));
            builder.AddSimpleField(FieldColumnWidthMode, typeof(int));
            builder.AddSimpleField(FieldRowHeightMode, typeof(int));
            FieldBuilder widthScale = builder.AddSimpleField(FieldWidthScale, typeof(double));
            widthScale.SetSpec(SpecTypeId.Number);
            builder.AddSimpleField(FieldCreatedSourceSha256, typeof(string));
            builder.AddSimpleField(FieldLastAcceptedSourceSha256, typeof(string));
            builder.AddSimpleField(FieldLastAcceptedSourceSizeBytes, typeof(long));

            return builder.Finish();
        }

        /// <summary>Writes v2 data. The caller owns the surrounding transaction.</summary>
        public static void SaveV2(ViewSchedule schedule, ExcelLinkInfo info)
        {
            Schema schema = GetOrCreateV2();
            Entity entity = new Entity(schema);
            entity.Set(FieldSchemaVersion, ExcelLinkInfo.CurrentSchemaVersion);
            entity.Set(FieldRendererVersion, info.RendererVersion);
            entity.Set(FieldAbsolutePath, info.AbsolutePath ?? string.Empty);
            entity.Set(FieldRelativePath, info.RelativePath ?? string.Empty);
            entity.Set(FieldPathMode, (int)info.PathMode);
            entity.Set(FieldSheetName, info.SheetName ?? string.Empty);
            entity.Set(FieldRangeMode, (int)info.RangeMode);
            entity.Set(FieldRangeAddress, info.RangeAddress ?? string.Empty);
            entity.Set(FieldSourceLastWriteUtcTicks, info.SourceLastWriteUtcTicks);
            entity.Set(FieldLastLoadedUtcTicks, info.LastLoadedUtcTicks);
            entity.Set(FieldColumnWidthMode, (int)info.ColumnWidthMode);
            entity.Set(FieldRowHeightMode, (int)info.RowHeightMode);
            entity.Set(FieldWidthScale, info.WidthScale, UnitTypeId.General);
            entity.Set(FieldCreatedSourceSha256, info.CreatedSourceSha256 ?? string.Empty);
            entity.Set(FieldLastAcceptedSourceSha256, info.LastAcceptedSourceSha256 ?? string.Empty);
            entity.Set(FieldLastAcceptedSourceSizeBytes, info.LastAcceptedSourceSizeBytes);
            schedule.SetEntity(entity);
        }

        /// <summary>Reads only schema v2 without creating it.</summary>
        public static ExcelLinkInfo LoadV2(ViewSchedule schedule)
        {
            Schema schema = Schema.Lookup(SchemaV2Guid);
            if (schema == null)
            {
                return null;
            }
            Entity entity = schedule.GetEntity(schema);
            if (entity == null || !entity.IsValid())
            {
                return null;
            }

            ExcelLinkInfo info = new ExcelLinkInfo();
            info.SchemaVersion = entity.Get<int>(FieldSchemaVersion);
            info.RendererVersion = entity.Get<int>(FieldRendererVersion);
            info.AbsolutePath = entity.Get<string>(FieldAbsolutePath) ?? string.Empty;
            info.RelativePath = entity.Get<string>(FieldRelativePath) ?? string.Empty;
            info.PathMode = (PathMode)entity.Get<int>(FieldPathMode);
            info.SheetName = entity.Get<string>(FieldSheetName) ?? string.Empty;
            info.RangeMode = (RangeMode)entity.Get<int>(FieldRangeMode);
            info.RangeAddress = entity.Get<string>(FieldRangeAddress) ?? string.Empty;
            info.SourceLastWriteUtcTicks = entity.Get<long>(FieldSourceLastWriteUtcTicks);
            info.LastLoadedUtcTicks = entity.Get<long>(FieldLastLoadedUtcTicks);
            info.ColumnWidthMode = (ColumnWidthMode)entity.Get<int>(FieldColumnWidthMode);
            info.RowHeightMode = (RowHeightMode)entity.Get<int>(FieldRowHeightMode);
            info.WidthScale = entity.Get<double>(FieldWidthScale, UnitTypeId.General);
            info.CreatedSourceSha256 = entity.Get<string>(FieldCreatedSourceSha256) ?? string.Empty;
            info.LastAcceptedSourceSha256 = entity.Get<string>(FieldLastAcceptedSourceSha256) ?? string.Empty;
            info.LastAcceptedSourceSizeBytes = entity.Get<long>(FieldLastAcceptedSourceSizeBytes);
            return info;
        }

        /// <summary>
        /// Read-only router used by diagnostics. It prefers v2 when a malformed
        /// dual entity exists so the newer protected representation is visible.
        /// </summary>
        public static ExcelLinkStorage LoadAny(ViewSchedule schedule)
        {
            ExcelLinkInfo v2 = LoadV2(schedule);
            if (v2 != null)
            {
                return new ExcelLinkStorage { SchemaGuid = SchemaV2Guid, IsV2 = true, Info = v2 };
            }

            ExcelLinkInfo v1 = Load(schedule);
            if (v1 != null)
            {
                return new ExcelLinkStorage { SchemaGuid = SchemaGuid, IsV2 = false, Info = v1 };
            }
            return null;
        }
    }

    internal sealed class ExcelLinkStorage
    {
        public Guid SchemaGuid;
        public bool IsV2;
        public ExcelLinkInfo Info;
    }
}
