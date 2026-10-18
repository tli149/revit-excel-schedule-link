using System;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Obtains the Revit-side creator relation used by the v0.9.1 decision
    /// table. Non-workshared documents deliberately use Same: there is no
    /// cross-user worksharing boundary to raise the confirmation bar.
    /// </summary>
    internal static class WorksharingContext
    {
        public static CreatorRelation GetCreatorRelation(
            Document doc, ElementId elementId, out string creator)
        {
            creator = string.Empty;
            if (doc == null)
            {
                return CreatorRelation.Unavailable;
            }

            bool workshared;
            try
            {
                workshared = doc.IsWorkshared;
            }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return CreatorRelation.Unavailable;
            }

            if (!workshared)
            {
                return CreatorRelation.Same;
            }

            try
            {
                WorksharingTooltipInfo tooltip =
                    WorksharingUtils.GetWorksharingTooltipInfo(doc, elementId);
                creator = tooltip.Creator ?? string.Empty;
                string currentUser = doc.Application.Username;
                if (string.IsNullOrWhiteSpace(creator)
                    || string.IsNullOrWhiteSpace(currentUser))
                {
                    return CreatorRelation.Unavailable;
                }
                return string.Equals(creator, currentUser, StringComparison.OrdinalIgnoreCase)
                    ? CreatorRelation.Same
                    : CreatorRelation.Different;
            }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                creator = string.Empty;
                return CreatorRelation.Unavailable;
            }
        }

        public static CreatorRelation FromKnownCreator(
            Document doc, string creator, string currentUser)
        {
            if (doc == null)
            {
                return CreatorRelation.Unavailable;
            }

            try
            {
                if (!doc.IsWorkshared)
                {
                    return CreatorRelation.Same;
                }
            }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return CreatorRelation.Unavailable;
            }

            if (string.IsNullOrWhiteSpace(creator)
                || string.IsNullOrWhiteSpace(currentUser)
                || string.Equals(currentUser, "(unknown)", StringComparison.Ordinal)
                || string.Equals(currentUser, "(empty)", StringComparison.Ordinal))
            {
                return CreatorRelation.Unavailable;
            }
            return string.Equals(creator, currentUser, StringComparison.OrdinalIgnoreCase)
                ? CreatorRelation.Same
                : CreatorRelation.Different;
        }

        public static string Label(CreatorRelation relation)
        {
            switch (relation)
            {
                case CreatorRelation.Same: return "SAME";
                case CreatorRelation.Different: return "DIFFERENT";
                case CreatorRelation.NotApplicable: return "SAME";
                default: return "UNAVAILABLE";
            }
        }
    }
}
