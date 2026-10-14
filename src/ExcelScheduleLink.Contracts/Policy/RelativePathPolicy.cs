// Revit-free decision logic, kept in Contracts so the net8 tests can exercise it.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>String-only path rules shared by Revit adapters and unit tests.</summary>
    public static class RelativePathPolicy
    {
        private static readonly Regex WindowsDrivePath =
            new Regex("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant);
        private static readonly Regex UriScheme =
            new Regex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant);

        /// <summary>
        /// Derives a path from the folder containing the stable anchor file.
        /// An empty string means that a safe filesystem-relative value cannot be
        /// represented (unsaved/cloud/different volume or scheme).
        /// </summary>
        public static string Derive(string anchorFilePath, string sourceFilePath)
        {
            if (!IsFileSystemPath(anchorFilePath) || !IsFileSystemPath(sourceFilePath))
            {
                return string.Empty;
            }

            try
            {
                string anchorFull = Path.GetFullPath(anchorFilePath);
                string sourceFull = Path.GetFullPath(sourceFilePath);
                string anchorDirectory = Path.GetDirectoryName(anchorFull);
                if (string.IsNullOrEmpty(anchorDirectory))
                {
                    return string.Empty;
                }

                Uri baseUri = new Uri(EnsureDirectorySeparator(anchorDirectory), UriKind.Absolute);
                Uri sourceUri = new Uri(sourceFull, UriKind.Absolute);
                if (!baseUri.IsFile || !sourceUri.IsFile
                    || !string.Equals(baseUri.Scheme, sourceUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                Uri relativeUri = baseUri.MakeRelativeUri(sourceUri);
                if (relativeUri.IsAbsoluteUri)
                {
                    return string.Empty;
                }

                string relative = Uri.UnescapeDataString(relativeUri.ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
                return IsTrulyRelative(relative) ? relative : string.Empty;
            }
            catch (UriFormatException)
            {
                return string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
        }

        /// <summary>Returns true only for a non-empty filesystem-relative value.</summary>
        public static bool IsTrulyRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            {
                return false;
            }

            // Path.IsPathRooted is platform-specific; these checks also reject
            // Windows rooted values when a cross-platform test host examines them.
            if (WindowsDrivePath.IsMatch(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
                || path.StartsWith("/", StringComparison.Ordinal) || UriScheme.IsMatch(path))
            {
                return false;
            }

            try
            {
                Uri uri;
                return !Uri.TryCreate(path, UriKind.Absolute, out uri) || !uri.IsAbsoluteUri;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves a stored relative value against an anchor file. Rooted and
        /// URI values are rejected before Path.Combine so legacy bad values can
        /// never become candidates.
        /// </summary>
        public static string Resolve(string anchorFilePath, string storedRelativePath)
        {
            if (!IsFileSystemPath(anchorFilePath) || !IsTrulyRelative(storedRelativePath))
            {
                return null;
            }

            try
            {
                string anchorDirectory = Path.GetDirectoryName(Path.GetFullPath(anchorFilePath));
                if (string.IsNullOrEmpty(anchorDirectory))
                {
                    return null;
                }
                string candidate = Path.GetFullPath(Path.Combine(anchorDirectory, storedRelativePath));
                return IsFileSystemPath(candidate) && !IsTrulyRelative(candidate) ? candidate : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>Returns false for cloud/URI paths and true for local filesystem paths.</summary>
        public static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (WindowsDrivePath.IsMatch(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return true;
            }
            if (UriScheme.IsMatch(path))
            {
                // ModelPathUtils may return cloud/URI schemes. A file: URI is
                // still a URI here, not a stable Windows filesystem anchor.
                return false;
            }
            return Path.IsPathRooted(path);
        }

        private static string EnsureDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }
    }
}
