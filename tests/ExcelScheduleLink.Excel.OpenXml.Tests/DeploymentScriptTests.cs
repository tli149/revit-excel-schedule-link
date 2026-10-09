using System;
using System.IO;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public class DeploymentScriptTests
    {
        private static string RepositoryRoot
        {
            get
            {
                DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
                for (int level = 0; level < 5; level++)
                {
                    if (directory.Parent == null)
                    {
                        throw new InvalidOperationException(
                            "The test output directory is shallower than expected.");
                    }
                    directory = directory.Parent;
                }
                return directory.FullName;
            }
        }

        [Fact]
        public void InstallScriptUsesAnExactCopiedFileListForUnblocking()
        {
            string script = ReadScript("scripts", "install-revit2024.ps1");

            Assert.Contains("$copiedRelativePaths", script);
            Assert.Contains("Unblock-File -LiteralPath $path", script);
            Assert.Contains("$receiptName", script);
            Assert.DoesNotContain("Get-ChildItem -Path $deployDir -Recurse", script);
        }

        [Fact]
        public void UninstallScriptUsesReceiptOrAllowlistAndNeverRecursivelyDeletesDeployment()
        {
            string script = ReadScript("scripts", "uninstall-revit2024.ps1");

            Assert.Contains("$receiptName", script);
            Assert.Contains("$knownPackageFiles", script);
            Assert.Contains("Get-RemainingTreeItems", script);
            Assert.Contains("[IO.Directory]::Delete($deployDir, $false)", script);
            Assert.DoesNotContain("Remove-Item", script);
            Assert.DoesNotContain("-Recurse -Force", script);
        }

        [Fact]
        public void BuildCleanupRestrictsTheDeletionHelperAndRejectsReparsePoints()
        {
            string script = ReadScript("scripts", "build.ps1");
            int start = script.IndexOf("function Remove-GeneratedTree", StringComparison.Ordinal);
            int end = script.IndexOf("function Test-ByteSequence", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);

            string helper = script.Substring(start, end - start);
            Assert.Contains("$script:generatedTreeAllowedRoots", script);
            Assert.Contains("not an exact generated distribution or staging root", helper);
            Assert.Contains("ReparsePoint", helper);
            Assert.DoesNotContain("-Recurse", helper);
        }

        private static string ReadScript(params string[] parts)
        {
            string path = RepositoryRoot;
            foreach (string part in parts)
            {
                path = Path.Combine(path, part);
            }
            return File.ReadAllText(path);
        }
    }
}
