using System;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Callable v1-to-v2 migration engine. Phase 2 callers invoke this only
    /// after the consent dialog and inside the action's existing single
    /// transaction.
    /// </summary>
    internal static class ExcelLinkMigration
    {
        /// <summary>
        /// Upgrades one v1 entity while the caller's transaction is already
        /// Started. No transaction is opened here. If SaveV2 or DeleteEntity
        /// throws, the caller must roll back its transaction; that leaves v1 as
        /// the only entity and preserves the atomic migration contract.
        /// </summary>
        public static bool MigrateInOpenTransaction(ViewSchedule schedule)
        {
            if (schedule == null)
            {
                return false;
            }

            Document doc = schedule.Document;
            if (doc == null || !doc.IsModifiable)
            {
                throw new InvalidOperationException(
                    "v1-to-v2 migration requires the caller's transaction to be open.");
            }

            if (ExcelLinkSchema.LoadV2(schedule) != null)
            {
                throw new InvalidOperationException(
                    "The schedule already carries schema v2; dual-write migration is not allowed.");
            }

            ExcelLinkInfo v1 = ExcelLinkSchema.Load(schedule);
            if (v1 == null)
            {
                return false;
            }

            LinkFieldSnapshot normalized = LinkMigrationNormalizer.ToV2(
                ToSnapshot(v1),
                PathResolver.GetModelRelativePath(doc, v1.AbsolutePath));
            ExcelLinkInfo v2 = FromSnapshot(normalized);

            ExcelLinkSchema.SaveV2(schedule, v2);
            // Element.DeleteEntity takes the Schema object, not its GUID. Load()
            // succeeded above, so the v1 schema is registered in this session.
            Autodesk.Revit.DB.ExtensibleStorage.Schema v1Schema =
                Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(ExcelLinkSchema.SchemaGuid);
            if (v1Schema == null)
            {
                throw new InvalidOperationException("The v1 schema is not registered; cannot remove the legacy entity.");
            }
            schedule.DeleteEntity(v1Schema);
            return true;
        }

        private static LinkFieldSnapshot ToSnapshot(ExcelLinkInfo info)
        {
            return new LinkFieldSnapshot
            {
                SchemaVersion = info.SchemaVersion,
                RendererVersion = info.RendererVersion,
                AbsolutePath = info.AbsolutePath,
                RelativePath = info.RelativePath,
                PathMode = (int)info.PathMode,
                SheetName = info.SheetName,
                RangeMode = (int)info.RangeMode,
                RangeAddress = info.RangeAddress,
                SourceLastWriteUtcTicks = info.SourceLastWriteUtcTicks,
                LastLoadedUtcTicks = info.LastLoadedUtcTicks,
                ColumnWidthMode = (int)info.ColumnWidthMode,
                RowHeightMode = (int)info.RowHeightMode,
                WidthScale = info.WidthScale
            };
        }

        private static ExcelLinkInfo FromSnapshot(LinkFieldSnapshot snapshot)
        {
            ExcelLinkInfo info = new ExcelLinkInfo
            {
                SchemaVersion = snapshot.SchemaVersion,
                RendererVersion = snapshot.RendererVersion,
                AbsolutePath = snapshot.AbsolutePath,
                RelativePath = snapshot.RelativePath,
                PathMode = (PathMode)snapshot.PathMode,
                SheetName = snapshot.SheetName,
                RangeMode = (RangeMode)snapshot.RangeMode,
                RangeAddress = snapshot.RangeAddress,
                SourceLastWriteUtcTicks = snapshot.SourceLastWriteUtcTicks,
                LastLoadedUtcTicks = snapshot.LastLoadedUtcTicks,
                ColumnWidthMode = (ColumnWidthMode)snapshot.ColumnWidthMode,
                RowHeightMode = (RowHeightMode)snapshot.RowHeightMode,
                WidthScale = snapshot.WidthScale,
                CreatedSourceSha256 = snapshot.CreatedSourceSha256,
                LastAcceptedSourceSha256 = snapshot.LastAcceptedSourceSha256,
                LastAcceptedSourceSizeBytes = snapshot.LastAcceptedSourceSizeBytes
            };
            return info;
        }
    }
}
