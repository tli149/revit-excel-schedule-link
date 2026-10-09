using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Shared report writer for diagnostic probes. It appends all results to
    /// Step0Report.txt under %APPDATA%\ExcelScheduleLink. Revit's
    /// Path.GetTempPath() is a per-session sandbox, so a %TEMP%-based report
    /// would be split across a different file on every launch. %APPDATA% is
    /// stable between sessions and does not require administrator rights; it is
    /// also where UiSettings.cs stores user settings. Reports include operation
    /// results, Revit and Windows versions, loaded Open XML assemblies, Dynamo
    /// load state for reference, and full exceptions.
    /// </summary>
    internal sealed class Step0Reporter
    {
        private readonly List<string> _lines = new List<string>();

        public static string ReportDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ExcelScheduleLink");
            }
        }

        public static string ReportPath
        {
            get { return Path.Combine(ReportDirectory, "Step0Report.txt"); }
        }

        public void Add(string line)
        {
            _lines.Add(line ?? string.Empty);
        }

        public void AddOk(string operation)
        {
            _lines.Add("[OK]   " + operation);
        }

        public void AddFail(string operation, Exception ex)
        {
            _lines.Add("[FAIL] " + operation);
            if (ex != null)
            {
                _lines.Add("       Full exception:");
                foreach (string l in ex.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    _lines.Add("       " + l);
                }
            }
        }

        public bool HasFailure
        {
            get { return _lines.Any(l => l.StartsWith("[FAIL]", StringComparison.Ordinal)); }
        }

        /// <summary>
        /// Append a section to the report and return its full path. Call this
        /// even when an operation throws, preferably from finally.
        /// </summary>
        public string Flush(UIApplication uiapp, string commandName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC] Command: " + commandName);
            AppendEnvironment(sb, uiapp);
            sb.AppendLine("User-supplied condition: " + (_userCondition ?? "Not specified"));
            AppendDynamoState(sb);
            AppendLoadedOpenXmlAssemblies(sb, "when section was written");
            sb.AppendLine(new string('-', 80));
            foreach (string line in _lines)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            Directory.CreateDirectory(ReportDirectory);
            File.AppendAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            return ReportPath;
        }

        private static void AppendEnvironment(StringBuilder sb, UIApplication uiapp)
        {
            try
            {
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
                sb.AppendLine("Revit   : VersionNumber=" + app.VersionNumber
                    + " / VersionBuild=" + app.VersionBuild
                    + " / VersionName=" + app.VersionName);
            }
            catch (Exception ex)
            {
                sb.AppendLine("Revit   : read failed " + ex.Message);
            }
            sb.AppendLine("Windows : " + Environment.OSVersion.VersionString);
            sb.AppendLine("CLR     : " + Environment.Version + " (64-bit process: " + Environment.Is64BitProcess + ")");
        }

        /// <summary>
        /// Record Dynamo-related assembly state as reference information.
        /// Revit may load Dynamo Player assemblies before Dynamo starts, so the
        /// count is not used to infer condition A or B. The supplied condition
        /// is recorded through SetUserCondition instead.
        /// </summary>
        private static void AppendDynamoState(StringBuilder sb)
        {
            string[] dynamoNames = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => n != null && n.IndexOf("Dynamo", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            sb.AppendLine("Dynamo  : related assemblies " + dynamoNames.Length
                + " (reference only; no automatic condition detection)");
            foreach (string n in dynamoNames.Take(12))
            {
                sb.AppendLine("          - " + n);
            }
            if (dynamoNames.Length > 12)
            {
                sb.AppendLine("          ... " + (dynamoNames.Length - 12) + " more");
            }
        }

        private string _userCondition;

        /// <summary>Record the user-supplied session condition in the section header.</summary>
        public void SetUserCondition(string condition)
        {
            _userCondition = condition;
        }

        public static void AppendLoadedOpenXmlAssemblies(StringBuilder sb, string label)
        {
            sb.AppendLine("Open XML load state (" + label + "):");
            Assembly[] hits = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    string n = a.GetName().Name;
                    return n != null
                        && (n.StartsWith("DocumentFormat.OpenXml", StringComparison.OrdinalIgnoreCase)
                            || n.Equals("System.IO.Packaging", StringComparison.OrdinalIgnoreCase));
                })
                .ToArray();
            if (hits.Length == 0)
            {
                sb.AppendLine("  (no matching assemblies loaded)");
                return;
            }
            foreach (Assembly a in hits)
            {
                string location;
                try
                {
                    location = a.Location;
                }
                catch (Exception ex)
                {
                    location = "(read failed: " + ex.GetType().Name + ")";
                }
                sb.AppendLine("  - " + a.FullName);
                sb.AppendLine("    " + location);
            }
        }

        /// <summary>Add loaded Open XML assemblies as report lines for a probe.</summary>
        public void AddLoadedOpenXmlAssemblies(string label)
        {
            StringBuilder sb = new StringBuilder();
            AppendLoadedOpenXmlAssemblies(sb, label);
            foreach (string l in sb.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                _lines.Add(l);
            }
        }
    }
}
