using System;
using System.Collections.Generic;
using ExcelScheduleLink.Contracts;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class StoredWorkbookPathPolicyTests
    {
        [Theory]
        [InlineData(@"\?\C:\Projects\book.xlsx")]
        [InlineData(@"\.\C:\Projects\book.xlsx")]
        [InlineData(@"\\.\C:\Projects\book.xlsx")]
        [InlineData(@"\??\C:\Projects\book.xlsx")]
        [InlineData(@"\\??\C:\Projects\book.xlsx")]
        [InlineData(@"\Device\HarddiskVolume1\book.xlsx")]
        [InlineData(@"\\Device\HarddiskVolume1\book.xlsx")]
        [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\book.xlsx")]
        [InlineData(@"\\?\Volume{01234567-89AB-CDEF-0123-456789ABCDEF}\book.xlsx")]
        [InlineData(@"\\?\not-an-ordinary-path\book.xlsx")]
        [InlineData(@"\\?\C:relative\book.xlsx")]
        [InlineData(@"\\?\C:/Projects/book.xlsx")]
        public void DeviceNamespacesAreRejectedBeforeFilesystemAccess(string path)
        {
            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.ValidateSyntax(path);

            Assert.False(result.IsSyntacticallyValid);
            Assert.Equal(StoredWorkbookPathIssue.DeviceNamespace, result.Issue);
            Assert.False(result.IsAllowed);
        }

        [Fact]
        public void Win32LongDrivePathIsAcceptedAsLocalAndPreservesOriginalForFileAccess()
        {
            const string path = @"\\?\C:\Projects\book.xlsx";

            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.ValidateSyntax(path);

            Assert.True(result.IsSyntacticallyValid);
            Assert.False(result.IsNetworkPath);
            Assert.Equal(StoredWorkbookPathIssue.None, result.Issue);
            Assert.True(result.IsAllowed);
            Assert.Equal(path, result.Path);
        }

        [Fact]
        public void Win32LongUncPathIsNormalizedForApprovalAndPreservesOriginalForFileAccess()
        {
            const string path = @"\\?\UNC\FileServer.example.com\shared\book.xlsx";
            MemoryApprovalStore store = new MemoryApprovalStore();

            StoredWorkbookPathAssessment before =
                StoredWorkbookPathPolicy.Evaluate(path, store);

            Assert.True(before.IsSyntacticallyValid);
            Assert.True(before.IsNetworkPath);
            Assert.Equal("FileServer.example.com", before.Server);
            Assert.Equal(StoredWorkbookPathIssue.NetworkNotApproved, before.Issue);
            Assert.False(before.IsAllowed);
            Assert.Equal(path, before.Path);

            store.Approve("fileserver.example.com");
            StoredWorkbookPathAssessment after =
                StoredWorkbookPathPolicy.Evaluate(path, store);

            Assert.True(after.IsAllowed);
            Assert.Equal(StoredWorkbookPathIssue.None, after.Issue);
            Assert.Equal(path, after.Path);
        }

        [Fact]
        public void Win32LongDrivePathUsesTheNormalizedLengthLimit()
        {
            string normalizedPath = @"C:\" + new string(
                'a', StoredWorkbookPathPolicy.MaxPathLength - 8) + ".xlsx";
            string path = @"\\?\" + normalizedPath;

            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.ValidateSyntax(path);

            Assert.Equal(StoredWorkbookPathPolicy.MaxPathLength, normalizedPath.Length);
            Assert.True(path.Length > StoredWorkbookPathPolicy.MaxPathLength);
            Assert.True(result.IsAllowed);
        }

        [Fact]
        public void UncPathIsDetectedAndRequiresTheExactApprovedServer()
        {
            const string path = @"\\FileServer.example.com\shared\book.xlsx";
            MemoryApprovalStore store = new MemoryApprovalStore();

            StoredWorkbookPathAssessment before =
                StoredWorkbookPathPolicy.Evaluate(path, store);

            Assert.True(before.IsSyntacticallyValid);
            Assert.True(before.IsNetworkPath);
            Assert.Equal("FileServer.example.com", before.Server);
            Assert.Equal(StoredWorkbookPathIssue.NetworkNotApproved, before.Issue);
            Assert.False(before.IsAllowed);

            store.Approve("fileserver.example.com.");
            StoredWorkbookPathAssessment after =
                StoredWorkbookPathPolicy.Evaluate(path, store);

            Assert.True(after.IsAllowed);
            Assert.Equal(StoredWorkbookPathIssue.None, after.Issue);
            Assert.Equal(2, store.ApprovalQueries);
        }

        [Fact]
        public void SlashNormalizedUncPathIsAlsoNetworkGated()
        {
            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.Evaluate("//FileServer.example.com/shared/book.xlsx",
                    new MemoryApprovalStore());

            Assert.True(result.IsNetworkPath);
            Assert.Equal("FileServer.example.com", result.Server);
            Assert.False(result.IsAllowed);
        }

        [Fact]
        public void MappedDriveIsTreatedAsLocalAndDoesNotNeedServerApproval()
        {
            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.Evaluate(@"Z:\Projects\book.xlsx", new MemoryApprovalStore());

            Assert.True(result.IsSyntacticallyValid);
            Assert.False(result.IsNetworkPath);
            Assert.True(result.IsAllowed);
            Assert.Equal(StoredWorkbookPathIssue.None, result.Issue);
        }

        [Theory]
        [InlineData(@"C:\Projects\book.xls")]
        [InlineData(@"C:\Projects\book.xlsx.bak")]
        [InlineData(@"https://server.example.com/book.xlsx")]
        public void NonXlsxPathsAreRejectedWithoutIochecks(string path)
        {
            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.ValidateSyntax(path);

            Assert.False(result.IsSyntacticallyValid);
            Assert.Equal(StoredWorkbookPathIssue.WrongExtension, result.Issue);
        }

        [Fact]
        public void OverlongStoredPathIsRejectedBeforeFilesystemAccess()
        {
            string path = @"C:\" + new string('a', StoredWorkbookPathPolicy.MaxPathLength) + ".xlsx";

            StoredWorkbookPathAssessment result =
                StoredWorkbookPathPolicy.ValidateSyntax(path);

            Assert.False(result.IsSyntacticallyValid);
            Assert.Equal(StoredWorkbookPathIssue.TooLong, result.Issue);
            Assert.True(StoredWorkbookPathPolicy.IsStoredStringLengthAllowed(path));
            Assert.False(StoredWorkbookPathPolicy.IsStoredStringLengthAllowed(
                new string('a', StoredWorkbookPathPolicy.MaxStoredStringLength + 1)));
        }

        private sealed class MemoryApprovalStore : INetworkServerApprovalStore
        {
            private readonly HashSet<string> _servers =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int ApprovalQueries { get; private set; }

            public bool IsApproved(string server)
            {
                ApprovalQueries++;
                return _servers.Contains(StoredWorkbookPathPolicy.NormalizeServer(server));
            }

            public void Approve(string server)
            {
                _servers.Add(StoredWorkbookPathPolicy.NormalizeServer(server));
            }
        }
    }
}
