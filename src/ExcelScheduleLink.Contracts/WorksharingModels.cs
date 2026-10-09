using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Plain metadata for the exact workbook snapshot that was read. The hash
    /// and size describe the copied snapshot; the timestamp is the source file
    /// timestamp observed while that snapshot was created.
    /// </summary>
    public sealed class SourceFingerprint
    {
        public SourceFingerprint(string sha256, long sizeBytes, long lastWriteUtcTicks)
        {
            Sha256 = sha256 ?? string.Empty;
            SizeBytes = sizeBytes;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }

        public string Sha256 { get; }
        public long SizeBytes { get; }
        public long LastWriteUtcTicks { get; }
    }

    /// <summary>Worksheet data and the fingerprint of the same temporary snapshot.</summary>
    public sealed class SheetReadResult
    {
        public SheetReadResult(SheetModel sheet, SourceFingerprint fingerprint)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));
            Sheet = sheet;
            Fingerprint = fingerprint;
        }

        public SheetModel Sheet { get; }
        public SourceFingerprint Fingerprint { get; }
    }

    /// <summary>Identity state used by the Phase 1 reload decision table.</summary>
    public enum SourceIdentityState
    {
        Match,
        Differs,
        Unknown,
        ReadFailed
    }

    /// <summary>Relationship between the schedule creator and the current user.</summary>
    public enum CreatorRelation
    {
        Same,
        Different,
        Unavailable,
        NotApplicable
    }

    public enum ReloadScope
    {
        SingleLink,
        AllLinks
    }

    public enum ReloadDecisionKind
    {
        Proceed,
        WarnContentChanged,
        RequireConfirmation,
        RequireStrongConfirmation,
        SkipConfirmationRequired,
        FailRead
    }

    /// <summary>Pure result of applying design §7.1 to one link.</summary>
    public sealed class ReloadDecision
    {
        internal ReloadDecision(ReloadDecisionKind kind)
        {
            Kind = kind;
        }

        public ReloadDecisionKind Kind { get; }
    }

    /// <summary>
    /// The Phase 1 decision table. It deliberately returns a value only; dialogs,
    /// transactions, and bulk summary presentation belong to later layers.
    /// </summary>
    public static class ReloadDecisionTable
    {
        public static ReloadDecision Decide(
            SourceIdentityState identity,
            CreatorRelation creator,
            ReloadScope scope)
        {
            return Decide(identity, creator, scope, false);
        }

        /// <summary>
        /// Applies the decision table and, when requested, the separate
        /// cross-user ApplyExcelWidths protection rule from design §10.1.
        /// </summary>
        public static ReloadDecision Decide(
            SourceIdentityState identity,
            CreatorRelation creator,
            ReloadScope scope,
            bool crossUserManualWidthRisk)
        {
            if (identity == SourceIdentityState.ReadFailed)
            {
                return new ReloadDecision(ReloadDecisionKind.FailRead);
            }

            if (identity == SourceIdentityState.Match)
            {
                if (crossUserManualWidthRisk)
                {
                    return new ReloadDecision(scope == ReloadScope.AllLinks
                        ? ReloadDecisionKind.SkipConfirmationRequired
                        : ReloadDecisionKind.RequireConfirmation);
                }
                return new ReloadDecision(ReloadDecisionKind.Proceed);
            }

            if (scope == ReloadScope.AllLinks)
            {
                return new ReloadDecision(ReloadDecisionKind.SkipConfirmationRequired);
            }

            bool creatorIsDifferent = creator == CreatorRelation.Different
                || creator == CreatorRelation.Unavailable;

            if (identity == SourceIdentityState.Differs)
            {
                return new ReloadDecision(
                    creatorIsDifferent
                        ? ReloadDecisionKind.RequireStrongConfirmation
                        : ReloadDecisionKind.WarnContentChanged);
            }

            if (identity == SourceIdentityState.Unknown)
            {
                return new ReloadDecision(
                    creatorIsDifferent
                        ? ReloadDecisionKind.RequireStrongConfirmation
                        : ReloadDecisionKind.RequireConfirmation);
            }

            // An out-of-range enum value is safer as a confirmation boundary
            // than as an implicit proceed. This keeps the function fail-closed.
            return new ReloadDecision(ReloadDecisionKind.RequireStrongConfirmation);
        }
    }

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

    /// <summary>SHA-256 identity comparison. Empty current data is a read failure.</summary>
    public static class SourceIdentity
    {
        public static SourceIdentityState Compare(string acceptedSha256, string currentSha256)
        {
            if (string.IsNullOrWhiteSpace(currentSha256))
            {
                return SourceIdentityState.ReadFailed;
            }
            if (string.IsNullOrWhiteSpace(acceptedSha256))
            {
                return SourceIdentityState.Unknown;
            }
            return string.Equals(
                acceptedSha256.Trim(), currentSha256.Trim(), StringComparison.OrdinalIgnoreCase)
                ? SourceIdentityState.Match
                : SourceIdentityState.Differs;
        }
    }
}
