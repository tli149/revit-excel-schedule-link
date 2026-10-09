using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal enum PathResolutionStatus
    {
        Resolved,
        Missing,
        Invalid,
        NetworkNotApproved,
        Cancelled
    }

    /// <summary>Result of the centralized stored/resolved path gate.</summary>
    internal sealed class PathResolutionResult
    {
        public PathResolutionStatus Status;
        public string ResolvedPath = string.Empty;
        public string DisplayPath = string.Empty;
        public string Server = string.Empty;
        public StoredWorkbookPathAssessment Assessment;

        public bool IsResolved
        {
            get { return Status == PathResolutionStatus.Resolved; }
        }
    }

    /// <summary>
    /// Resolves a workbook with absolute-first semantics and a stable fallback.
    /// Every File.Exists call is reached only after StoredWorkbookPathPolicy has
    /// accepted the exact candidate. Unapproved UNC candidates are returned as
    /// a status in inspection mode and require a cancel-default TaskDialog in
    /// action mode before the first network probe.
    /// </summary>
    internal static class PathResolver
    {
        public static PathResolutionResult Inspect(Document doc, ExcelLinkInfo info)
        {
            return ResolveCore(doc, info, false);
        }

        public static PathResolutionResult ResolveForAction(
            Document doc, ExcelLinkInfo info)
        {
            return ResolveCore(doc, info, true);
        }

        /// <summary>
        /// Compatibility wrapper for diagnostic commands. Production paths that
        /// need to distinguish cancellation use ResolveForAction directly.
        /// </summary>
        public static string Resolve(Document doc, ExcelLinkInfo info)
        {
            PathResolutionResult result = ResolveForAction(doc, info);
            return result.IsResolved ? result.ResolvedPath : null;
        }

        /// <summary>
        /// Authorizes a path selected in the file dialog before the reader can
        /// probe or copy it. Choosing the file is explicit user approval for
        /// its network server, so no second network prompt is required here.
        /// </summary>
        public static PathResolutionResult AuthorizeUserSelectedPath(string path)
        {
            UserNetworkServerApprovalStore store = new UserNetworkServerApprovalStore();
            StoredWorkbookPathAssessment assessment =
                StoredWorkbookPathPolicy.Evaluate(path, store);
            if (!assessment.IsSyntacticallyValid)
            {
                return FromAssessment(PathResolutionStatus.Invalid, assessment);
            }
            if (!assessment.IsApproved)
            {
                store.Approve(assessment.Server);
                assessment = StoredWorkbookPathPolicy.Evaluate(path, store);
                if (!assessment.IsAllowed)
                {
                    return FromAssessment(PathResolutionStatus.NetworkNotApproved, assessment);
                }
            }
            return FromAssessment(PathResolutionStatus.Resolved, assessment);
        }

        public static string GetModelRelativePath(Document doc, string absolutePath)
        {
            if (!StoredWorkbookPathPolicy.ValidateSyntax(absolutePath).IsSyntacticallyValid)
            {
                return string.Empty;
            }
            return RelativePathPolicy.Derive(GetStableAnchorPath(doc), absolutePath);
        }

        /// <summary>Returns a candidate represented by a stored relative field.</summary>
        public static string ResolveRelativeCandidate(Document doc, string relativePath)
        {
            StoredWorkbookPathAssessment assessment =
                StoredWorkbookPathPolicy.ValidateSyntax(relativePath);
            if (!assessment.IsSyntacticallyValid)
            {
                return null;
            }
            return RelativePathPolicy.Resolve(GetStableAnchorPath(doc), relativePath);
        }

        /// <summary>
        /// Normalizes a syntactically valid path for duplicate comparison. This
        /// method never calls File.Exists and therefore cannot contact a UNC host.
        /// </summary>
        public static string NormalizeForComparison(string path)
        {
            StoredWorkbookPathAssessment assessment =
                StoredWorkbookPathPolicy.ValidateSyntax(path);
            if (!assessment.IsSyntacticallyValid)
            {
                return null;
            }
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        public static string DescribeFailure(
            ExcelLinkInfo info, PathResolutionResult result)
        {
            if (result != null && result.Status == PathResolutionStatus.Cancelled)
            {
                return UserMessages.NetworkAccessCancelled(result.DisplayPath);
            }
            if (result != null && result.Status == PathResolutionStatus.NetworkNotApproved)
            {
                return UserMessages.NetworkAccessNotApproved(
                    result.Server, result.DisplayPath);
            }
            if (result != null && result.Status == PathResolutionStatus.Invalid
                && result.Assessment != null)
            {
                return UserMessages.StoredWorkbookPathRejected(
                    result.DisplayPath, result.Assessment.Issue);
            }
            return UserMessages.ReloadSourceNotFound(info);
        }

        private static PathResolutionResult ResolveCore(
            Document doc, ExcelLinkInfo info, bool allowNetworkPrompt)
        {
            if (info == null)
            {
                return new PathResolutionResult
                {
                    Status = PathResolutionStatus.Invalid,
                    DisplayPath = string.Empty
                };
            }

            UserNetworkServerApprovalStore store = new UserNetworkServerApprovalStore();
            PathResolutionResult last = new PathResolutionResult
            {
                Status = PathResolutionStatus.Missing,
                DisplayPath = !string.IsNullOrEmpty(info.AbsolutePath)
                    ? info.AbsolutePath : info.RelativePath
            };

            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(info.AbsolutePath))
            {
                candidates.Add(info.AbsolutePath);
            }
            StoredWorkbookPathAssessment relativeAssessment = null;
            if (!string.IsNullOrEmpty(info.RelativePath))
            {
                relativeAssessment = StoredWorkbookPathPolicy.ValidateSyntax(info.RelativePath);
            }
            string relativeCandidate = relativeAssessment == null
                || relativeAssessment.IsSyntacticallyValid
                ? ResolveRelativeCandidate(doc, info.RelativePath)
                : null;
            if (!string.IsNullOrEmpty(relativeCandidate))
            {
                candidates.Add(relativeCandidate);
            }
            else if (relativeAssessment != null && !relativeAssessment.IsSyntacticallyValid
                && last.Status == PathResolutionStatus.Missing)
            {
                last = FromAssessment(PathResolutionStatus.Invalid, relativeAssessment);
            }

            foreach (string candidate in candidates)
            {
                PathResolutionResult current = TryCandidate(
                    candidate, store, allowNetworkPrompt);
                if (current.IsResolved)
                {
                    return current;
                }
                if (current.Status == PathResolutionStatus.NetworkNotApproved
                    || current.Status == PathResolutionStatus.Cancelled)
                {
                    // Absolute-preferred resolution must not silently fall back
                    // from a denied network candidate to another path.
                    return current;
                }
                last = current;
            }
            return last;
        }

        private static PathResolutionResult TryCandidate(
            string candidate, UserNetworkServerApprovalStore store,
            bool allowNetworkPrompt)
        {
            StoredWorkbookPathAssessment assessment =
                StoredWorkbookPathPolicy.Evaluate(candidate, store);
            if (!assessment.IsSyntacticallyValid)
            {
                return FromAssessment(PathResolutionStatus.Invalid, assessment);
            }
            if (!assessment.IsApproved)
            {
                if (!allowNetworkPrompt)
                {
                    return FromAssessment(PathResolutionStatus.NetworkNotApproved, assessment);
                }
                if (!AskForNetworkApproval(assessment))
                {
                    return FromAssessment(PathResolutionStatus.Cancelled, assessment);
                }
                store.Approve(assessment.Server);
                assessment = StoredWorkbookPathPolicy.Evaluate(candidate, store);
                if (!assessment.IsAllowed)
                {
                    return FromAssessment(PathResolutionStatus.NetworkNotApproved, assessment);
                }
            }

            try
            {
                if (File.Exists(candidate))
                {
                    return new PathResolutionResult
                    {
                        Status = PathResolutionStatus.Resolved,
                        ResolvedPath = candidate,
                        DisplayPath = candidate,
                        Server = assessment.Server,
                        Assessment = assessment
                    };
                }
            }
            catch (ArgumentException)
            {
                return FromAssessment(PathResolutionStatus.Invalid, assessment);
            }
            catch (NotSupportedException)
            {
                return FromAssessment(PathResolutionStatus.Invalid, assessment);
            }
            catch (PathTooLongException)
            {
                return FromAssessment(PathResolutionStatus.Invalid, assessment);
            }
            return new PathResolutionResult
            {
                Status = PathResolutionStatus.Missing,
                DisplayPath = candidate,
                Server = assessment.Server,
                Assessment = assessment
            };
        }

        private static PathResolutionResult FromAssessment(
            PathResolutionStatus status, StoredWorkbookPathAssessment assessment)
        {
            return new PathResolutionResult
            {
                Status = status,
                DisplayPath = assessment != null ? assessment.Path : string.Empty,
                Server = assessment != null ? assessment.Server : string.Empty,
                Assessment = assessment
            };
        }

        private static bool AskForNetworkApproval(
            StoredWorkbookPathAssessment assessment)
        {
            return StoredWorkbookPathDialogs.Authorize(
                assessment.Server, assessment.Path);
        }

        /// <summary>Returns the stable file-system anchor used by relative links.</summary>
        public static string GetStableAnchorPath(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            try
            {
                if (doc.IsWorkshared)
                {
                    ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();
                    if (centralModelPath == null)
                    {
                        return null;
                    }
                    string centralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                        centralModelPath);
                    return RelativePathPolicy.IsFileSystemPath(centralPath)
                        ? centralPath : null;
                }

                return RelativePathPolicy.IsFileSystemPath(doc.PathName)
                    ? doc.PathName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Stable-anchor class used by the read-only worksharing probe.</summary>
        public static string GetStableAnchorClass(Document doc)
        {
            try
            {
                if (doc != null && doc.IsWorkshared)
                {
                    return GetStableAnchorPath(doc) == null
                        ? "NO FILE-SYSTEM ANCHOR (unsaved/cloud)"
                        : "CENTRAL FILE FOLDER";
                }
                return GetStableAnchorPath(doc) == null
                    ? "NO FILE-SYSTEM ANCHOR (unsaved/cloud)"
                    : "MODEL FILE FOLDER";
            }
            catch (Exception)
            {
                return "NO FILE-SYSTEM ANCHOR (unavailable)";
            }
        }

        /// <summary>
        /// Description used by the probe for relative-path degradation and
        /// legacy rooted values.
        /// </summary>
        public static string DescribeRelativeField(Document doc, ExcelLinkInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.RelativePath))
            {
                return "RELATIVE UNAVAILABLE (different volume or no file-system anchor)";
            }
            if (!RelativePathPolicy.IsTrulyRelative(info.RelativePath))
            {
                return "INVALID RELATIVE FIELD (rooted legacy value ignored)";
            }

            string candidate = ResolveRelativeCandidate(doc, info.RelativePath);
            return candidate == null
                ? "RELATIVE UNAVAILABLE (different volume or no file-system anchor)"
                : "RELATIVE CANDIDATE (stable anchor: " + candidate + ")";
        }
    }
}
