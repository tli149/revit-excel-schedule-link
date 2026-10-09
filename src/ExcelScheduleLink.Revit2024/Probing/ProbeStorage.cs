using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Diagnostic Extensible Storage operations. Revit 2024 supports Int64 in
    /// AddSimpleField, so this checks Set/Get round trips and persistence after
    /// saving and restarting an RVT. Schema.Lookup is always used first so the
    /// diagnostic can be repeated.
    /// </summary>
    internal static class ProbeStorage
    {
        /// <summary>Fixed diagnostic schema GUID, separate from the product schema.</summary>
        public static readonly Guid SchemaGuid = new Guid("51cc6e30-df68-466d-b75c-7d331153c734");

        public const string FieldInt64 = "Int64Value";
        public const string FieldText = "Text";
        public const string FieldWrittenUtcTicks = "WrittenUtcTicks";

        /// <summary>Sentinel value larger than Int32 to expose truncation or type conversion.</summary>
        public const long Int64Sentinel = 1234567890123456789L;

        public static Schema EnsureSchema(Step0Reporter r)
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                r.AddOk("Schema.Lookup: reusing existing schema (" + SchemaGuid + ")");
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("ESLStep0Probe");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldInt64, typeof(long));
            builder.AddSimpleField(FieldText, typeof(string));
            builder.AddSimpleField(FieldWrittenUtcTicks, typeof(long));
            schema = builder.Finish();
            r.AddOk("SchemaBuilder.Finish: created schema (" + SchemaGuid + ")"
                + " / two Int64 fields + one String field");
            return schema;
        }

        /// <summary>
        /// Read and report an existing entity to check persistence. Values from
        /// a previous run after a Revit restart confirm persistence across save.
        /// </summary>
        public static void ReportExistingEntity(Element element, Schema schema, Step0Reporter r)
        {
            try
            {
                Entity entity = element.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                {
                    r.Add("Existing entity: none (first write for this element)");
                    return;
                }
                long int64Value = entity.Get<long>(FieldInt64);
                string text = entity.Get<string>(FieldText);
                long ticks = entity.Get<long>(FieldWrittenUtcTicks);
                r.AddOk("Existing entity read (persistence check): "
                    + FieldInt64 + "=" + int64Value
                    + " (matches expected: " + (int64Value == Int64Sentinel) + "), "
                    + FieldText + "=\"" + text + "\", "
                    + "previous write UTC=" + new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss"));
                r.Add("       Run again after restarting Revit to confirm persistence across RVT save.");
            }
            catch (Exception ex)
            {
                r.AddFail("Reading the existing entity", ex);
            }
        }

        /// <summary>Write and immediately read the entity back inside a transaction.</summary>
        public static void WriteAndVerifyEntity(Element element, Schema schema, Step0Reporter r)
        {
            try
            {
                Entity entity = new Entity(schema);
                entity.Set(FieldInt64, Int64Sentinel);
                entity.Set(FieldText, "Excel Schedule Link diagnostic / CJK test text");
                entity.Set(FieldWrittenUtcTicks, DateTime.UtcNow.Ticks);
                element.SetEntity(entity);
                r.AddOk("Entity.Set + Element.SetEntity(Int64 sentinel " + Int64Sentinel + ")");
            }
            catch (Exception ex)
            {
                r.AddFail("Writing the entity", ex);
                return;
            }

            try
            {
                Entity back = element.GetEntity(schema);
                long got = back.Get<long>(FieldInt64);
                string text = back.Get<string>(FieldText);
                if (got == Int64Sentinel)
                {
                    r.AddOk("Immediate read-back: matching Int64 value (" + got + "), Text=\"" + text + "\"");
                }
                else
                {
                    r.AddFail("Immediate read-back: Int64 mismatch, expected=" + Int64Sentinel + " actual=" + got, null);
                }
            }
            catch (Exception ex)
            {
                r.AddFail("Reading the entity back immediately", ex);
            }
        }
    }
}
