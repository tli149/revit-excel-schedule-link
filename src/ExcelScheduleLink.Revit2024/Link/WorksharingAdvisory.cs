using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Process-local, non-persisted guard for the v0.9.1 worksharing advisory.
    /// The document reference is intentionally kept for the lifetime of the
    /// Revit session so each open document gets one notice per session.
    /// </summary>
    internal static class WorksharingAdvisory
    {
        private static readonly List<Document> ShownDocuments = new List<Document>();

        /// <summary>
        /// Revit hands different managed wrappers for the same document to
        /// different commands (the Link Manager and the ribbon reload saw two
        /// wrappers in the re-verification run), so reference equality showed
        /// the advisory twice. Document.Equals compares the underlying document;
        /// a wrapper for a closed document may throw, which counts as different.
        /// </summary>
        private static bool SameDocument(Document a, Document b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            try
            {
                return a.IsValidObject && b.IsValidObject && a.Equals(b);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsWorkshared(Document doc)
        {
            try
            {
                return doc != null && doc.IsWorkshared;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool ShouldMerge(Document doc)
        {
            bool isDetached = false;
            try
            {
                isDetached = doc != null && doc.IsDetached;
            }
            catch (Exception)
            {
                return false;
            }
            return ScheduleIntegrityFlowDecisions.ShouldShowWorksharingAdvisory(
                IsWorkshared(doc), isDetached, HasShown(doc));
        }

        public static void MarkShown(Document doc)
        {
            bool isDetached = false;
            try
            {
                isDetached = doc != null && doc.IsDetached;
            }
            catch (Exception)
            {
                return;
            }
            if (!ScheduleIntegrityFlowDecisions.ShouldShowWorksharingAdvisory(
                IsWorkshared(doc), isDetached, HasShown(doc)))
            {
                return;
            }
            ShownDocuments.Add(doc);
        }

        /// <summary>
        /// Shows the general advisory immediately before a mutation when no
        /// stronger dialog is going to carry it.
        /// </summary>
        public static void ShowIfNeeded(Document doc)
        {
            if (!ShouldMerge(doc))
            {
                return;
            }

            MarkShown(doc);
            TaskDialog.Show(UserMessages.DialogTitle,
                UserMessages.WorksharingAdvisoryInstruction + "\n\n"
                + UserMessages.WorksharingAdvisoryContent);
        }

        private static bool HasShown(Document doc)
        {
            for (int i = 0; i < ShownDocuments.Count; i++)
            {
                if (SameDocument(ShownDocuments[i], doc))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
