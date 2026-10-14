// Revit-free decision logic, kept in Contracts so the net8 tests can exercise it.
using System;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>
    /// Reason a stored workbook path cannot be used.
    /// This enum is deliberately free of UI text so the Contracts assembly
    /// remains dependency-free and the Revit layer remains the owner of
    /// user-facing messages.
    /// </summary>
    public enum StoredWorkbookPathIssue
    {
        None,
        Empty,
        TooLong,
        DeviceNamespace,
        WrongExtension,
        InvalidSyntax,
        NetworkNotApproved
    }

    /// <summary>Per-user approval store for UNC server names.</summary>
    public interface INetworkServerApprovalStore
    {
        bool IsApproved(string server);
        void Approve(string server);
    }

    /// <summary>
    /// Result of the string-only part of the stored workbook path policy.
    /// No property or method in this type performs filesystem I/O.
    /// </summary>
    public sealed class StoredWorkbookPathAssessment
    {
        internal StoredWorkbookPathAssessment(
            string path,
            bool syntacticallyValid,
            bool networkPath,
            string server,
            StoredWorkbookPathIssue issue,
            bool approved)
        {
            Path = path ?? string.Empty;
            IsSyntacticallyValid = syntacticallyValid;
            IsNetworkPath = networkPath;
            Server = server ?? string.Empty;
            Issue = issue;
            IsApproved = approved;
        }

        public string Path { get; }
        public bool IsSyntacticallyValid { get; }
        public bool IsNetworkPath { get; }
        public string Server { get; }
        public StoredWorkbookPathIssue Issue { get; }
        public bool IsApproved { get; }

        public bool IsAllowed
        {
            get { return IsSyntacticallyValid && IsApproved; }
        }
    }

    /// <summary>
    /// Single stored/resolved workbook path policy. It validates the complete
    /// path string before a caller is allowed to call File.Exists, read a
    /// timestamp, copy, hash, or open the workbook. UNC approval is keyed only
    /// by server name and is never stored in a Revit model.
    /// </summary>
    public static class StoredWorkbookPathPolicy
    {
        /// <summary>
        /// 1,024 characters leaves room for ordinary deep project folders while
        /// rejecting paths that are difficult to display, audit, or handle
        /// consistently across Revit 2024 and Windows APIs.
        /// </summary>
        public const int MaxPathLength = 1024;

        /// <summary>Maximum length for a stored path-like string.</summary>
        public const int MaxStoredStringLength = 4096;

        private const string Win32LongPathPrefix = @"\\?\";
        private const string Win32LongUncPrefix = @"\\?\UNC\";

        private static readonly string[] DevicePrefixes =
        {
            @"\?\",
            @"\.\",
            @"\\.\",
            @"\??\",
            @"\\??\",
            @"\Device\",
            @"\\Device\"
        };

        /// <summary>
        /// Validates syntax and extension without checking approval or touching
        /// the filesystem. UNC paths are syntactically valid but not approved
        /// until Evaluate is called with a store containing that server.
        /// </summary>
        public static StoredWorkbookPathAssessment ValidateSyntax(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Invalid(path, StoredWorkbookPathIssue.Empty);
            }

            // The extended Win32 prefix is an I/O form, not a device namespace,
            // when it wraps an ordinary drive or UNC path. Policy decisions use
            // the equivalent ordinary path; the original path remains in the
            // assessment so long-path-capable filesystem APIs keep receiving it.
            string normalizedPath = NormalizeForPolicy(path);
            if (normalizedPath == null || path.IndexOf('\0') >= 0)
            {
                return Invalid(path, StoredWorkbookPathIssue.DeviceNamespace);
            }
            if (normalizedPath.Length > MaxPathLength
                || normalizedPath.Length > MaxStoredStringLength)
            {
                return Invalid(path, StoredWorkbookPathIssue.TooLong);
            }
            if (HasDeviceNamespace(normalizedPath))
            {
                return Invalid(path, StoredWorkbookPathIssue.DeviceNamespace);
            }
            if (LooksLikeUri(normalizedPath) || !HasXlsxExtension(normalizedPath))
            {
                return Invalid(path, StoredWorkbookPathIssue.WrongExtension);
            }

            bool network = IsUncPath(normalizedPath);
            string server = network ? GetServer(normalizedPath) : string.Empty;
            if (network && string.IsNullOrEmpty(server))
            {
                return Invalid(path, StoredWorkbookPathIssue.InvalidSyntax);
            }

            return new StoredWorkbookPathAssessment(
                path, true, network, server,
                network ? StoredWorkbookPathIssue.NetworkNotApproved : StoredWorkbookPathIssue.None,
                !network);
        }

        /// <summary>
        /// Applies syntax validation and the per-user UNC-server approval
        /// decision. The approval store is not consulted for local paths,
        /// including mapped drive letters.
        /// </summary>
        public static StoredWorkbookPathAssessment Evaluate(
            string path, INetworkServerApprovalStore approvalStore)
        {
            StoredWorkbookPathAssessment syntax = ValidateSyntax(path);
            if (!syntax.IsSyntacticallyValid || !syntax.IsNetworkPath)
            {
                return syntax;
            }

            bool approved = approvalStore != null
                && approvalStore.IsApproved(syntax.Server);
            return new StoredWorkbookPathAssessment(
                syntax.Path, true, true, syntax.Server,
                approved
                    ? StoredWorkbookPathIssue.None
                    : StoredWorkbookPathIssue.NetworkNotApproved,
                approved);
        }

        public static bool IsStoredStringLengthAllowed(string value)
        {
            return value != null && value.Length <= MaxStoredStringLength;
        }

        public static bool IsUncPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && (path.StartsWith(@"\\", StringComparison.Ordinal)
                    || path.StartsWith("//", StringComparison.Ordinal));
        }

        /// <summary>Returns the server component of a UNC path, without slashes.</summary>
        public static string GetServer(string path)
        {
            if (!IsUncPath(path) || path.Length <= 2)
            {
                return string.Empty;
            }

            int end = path.IndexOfAny(new[] { '\\', '/' }, 2);
            string server = end < 0 ? path.Substring(2) : path.Substring(2, end - 2);
            return NormalizeServer(server);
        }

        /// <summary>
        /// Server names are case-insensitive. Removing trailing dots prevents
        /// one harmless spelling variation from creating a second approval.
        /// </summary>
        public static string NormalizeServer(string server)
        {
            return (server ?? string.Empty).Trim().TrimEnd('.');
        }

        private static StoredWorkbookPathAssessment Invalid(
            string path, StoredWorkbookPathIssue issue)
        {
            return new StoredWorkbookPathAssessment(
                path, false, false, string.Empty, issue, false);
        }

        private static bool HasDeviceNamespace(string path)
        {
            foreach (string prefix in DevicePrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeForPolicy(string path)
        {
            if (!path.StartsWith(Win32LongPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (path.StartsWith(Win32LongUncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path.Substring(Win32LongUncPrefix.Length);
            }

            int driveOffset = Win32LongPathPrefix.Length;
            if (path.Length > driveOffset + 2
                && IsAsciiDriveLetter(path[driveOffset])
                && path[driveOffset + 1] == ':'
                && path[driveOffset + 2] == '\\')
            {
                return path.Substring(driveOffset);
            }

            // Every other \?\ form remains a rejected device/namespace form.
            return null;
        }

        private static bool IsAsciiDriveLetter(char value)
        {
            return (value >= 'A' && value <= 'Z')
                || (value >= 'a' && value <= 'z');
        }

        private static bool LooksLikeUri(string path)
        {
            return path.IndexOf("://", StringComparison.Ordinal) >= 0;
        }

        private static bool HasXlsxExtension(string path)
        {
            int separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            int dot = path.LastIndexOf('.');
            return dot > separator
                && string.Equals(path.Substring(dot), ".xlsx", StringComparison.OrdinalIgnoreCase);
        }
    }
}
