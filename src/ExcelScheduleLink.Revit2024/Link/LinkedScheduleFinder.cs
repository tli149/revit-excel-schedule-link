using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>Locates Excel-linked schedules (those carrying the link schema).</summary>
    internal static class LinkedScheduleFinder
    {
        /// <summary>True when the schedule has a valid v1 or v2 Excel link entity.</summary>
        public static bool IsLinked(ViewSchedule schedule)
        {
            return schedule != null && ExcelLinkSchema.LoadAny(schedule) != null;
        }

        /// <summary>True when either the legacy v1 or protected v2 entity exists.</summary>
        public static bool IsLinkedAny(ViewSchedule schedule)
        {
            return schedule != null && ExcelLinkSchema.LoadAny(schedule) != null;
        }

        /// <summary>All linked schedules in the document.</summary>
        public static IList<ViewSchedule> FindAll(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(v => !v.IsTemplate && IsLinked(v))
                .ToList();
        }

        /// <summary>
        /// Read-only enumeration used by diagnostics. Production enumeration also
        /// routes through the same LoadAny schema router via IsLinked.
        /// </summary>
        public static IList<ViewSchedule> FindAllAny(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(v => !v.IsTemplate && IsLinkedAny(v))
                .ToList();
        }

        /// <summary>
        /// Picks the linked schedule the Reload command should act on:
        ///   1. the active view, if it is a linked schedule;
        ///   2. otherwise a single selected linked schedule;
        ///   3. otherwise null (caller reports that none is targeted).
        /// </summary>
        public static ViewSchedule PickTarget(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            ViewSchedule active = uidoc.ActiveView as ViewSchedule;
            if (active != null && IsLinked(active))
            {
                return active;
            }

            List<ViewSchedule> selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as ViewSchedule)
                .Where(v => v != null && IsLinked(v))
                .ToList();
            if (selected.Count == 1)
            {
                return selected[0];
            }
            return null;
        }

        /// <summary>
        /// The first schedule already linked to the same workbook, worksheet and
        /// range, or null when there is none.
        ///
        /// A different range of the same worksheet is deliberately NOT a
        /// duplicate: splitting one worksheet into several schedules is ordinary
        /// practice, while re-linking the identical block is almost always a
        /// mis-click.
        ///
        /// Each candidate is compared on its currently resolved path, so a
        /// workbook that moved and is now reached through its model-relative
        /// path still matches. Read-only; the caller decides what to do.
        /// </summary>
        public static ViewSchedule FindDuplicate(
            Document doc, string xlsxPath, string sheetName, string rangeAddress)
        {
            string wantedPath = PathResolver.NormalizeForComparison(xlsxPath);
            if (wantedPath == null)
            {
                return null;
            }
            string wantedRange = NormalizeRange(rangeAddress);
            string wantedSheet = sheetName ?? string.Empty;

            foreach (ViewSchedule schedule in FindAll(doc))
            {
                ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
                if (storage == null || storage.Info == null)
                {
                    continue;
                }
                ExcelLinkInfo info = storage.Info;
                if (!string.Equals(info.SheetName ?? string.Empty, wantedSheet,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string existingRange = NormalizeRange(
                    info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null);
                if (!string.Equals(existingRange, wantedRange, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A missing source still has a stored path to compare. Compare
                // the raw absolute and model-relative candidates without probing
                // either one; an unapproved UNC can therefore never be contacted
                // during duplicate checking.
                string existingAbsolute = PathResolver.NormalizeForComparison(info.AbsolutePath);
                string existingRelative = PathResolver.NormalizeForComparison(
                    PathResolver.ResolveRelativeCandidate(doc, info.RelativePath));
                if ((existingAbsolute != null
                        && string.Equals(existingAbsolute, wantedPath,
                            StringComparison.OrdinalIgnoreCase))
                    || (existingRelative != null
                        && string.Equals(existingRelative, wantedPath,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return schedule;
                }
            }
            return null;
        }

        /// <summary>Empty and "whole used range" are the same range.</summary>
        private static string NormalizeRange(string rangeAddress)
        {
            return (rangeAddress ?? string.Empty).Trim();
        }
    }
}
