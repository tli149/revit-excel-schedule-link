using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Read-only evidence writer for worksharing verification. It records, for
    /// one run on one machine, the information needed to judge behavior from
    /// files rather than from screenshots: document identity, central/local
    /// paths, workset open state, and per-link ownership plus path resolution.
    ///
    /// It deliberately does NOT reuse <see cref="Step0Reporter"/>. That reporter
    /// appends every run into one shared Step0Report.txt, which is exactly the
    /// evidence shape that made the Revit-Word pair run hard to judge: two
    /// machines produced files that could not be ordered or told apart. Here
    /// each run is its own timestamped, username-stamped file.
    ///
    /// Nothing here opens a transaction, creates a schema, or touches the model.
    /// </summary>
    internal static class WorksharingStatusReport
    {
        /// <summary>
        /// Set this environment variable to collect evidence directly on the QA
        /// kit drive. The probe never guesses a path next to the model: writing
        /// beside a central file that turned out to be a business model is not a
        /// risk worth taking for tidiness.
        /// </summary>
        public const string EvidenceDirectoryVariable = "ESL_QA_EVIDENCE_DIR";

        private const string Rule =
            "--------------------------------------------------------------------------------";
        private const string HeavyRule =
            "================================================================================";

        /// <summary>Where this run will be written, and why that directory.</summary>
        public static string ResolveEvidenceDirectory(out string source)
        {
            string configured = null;
            try
            {
                configured = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);
            }
            catch (System.Security.SecurityException)
            {
            }

            if (!string.IsNullOrEmpty(configured))
            {
                try
                {
                    Directory.CreateDirectory(configured);
                    source = EvidenceDirectoryVariable;
                    return configured;
                }
                catch (Exception ex)
                {
                    source = EvidenceDirectoryVariable + " unusable (" + ex.GetType().Name
                        + "), fell back to %APPDATA%";
                    return DefaultDirectory();
                }
            }

            source = "default %APPDATA% (" + EvidenceDirectoryVariable + " not set)";
            return DefaultDirectory();
        }

        /// <summary>
        /// Deliberately NOT under <see cref="Path.GetTempPath"/>. Measured on
        /// Revit 2024: inside the Revit process that call returns a per-session
        /// sandbox such as %LOCALAPPDATA%\Temp\{guid}\, not %TEMP% itself. One
        /// machine had 124 such folders, six of them holding ExcelScheduleLink
        /// output from earlier sessions. Evidence that lands in a fresh
        /// directory every launch cannot be compared across the legs of a relay,
        /// and an operator told to look in "%TEMP%" would not find it at all.
        ///
        /// %APPDATA%\ExcelScheduleLink is stable across sessions, needs no admin
        /// rights, and is already where <c>UiSettings</c> keeps its file.
        /// </summary>
        private static string DefaultDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ExcelScheduleLink", "worksharing");
        }

        /// <summary>
        /// Builds the report text and writes it to a run-specific file, plus a
        /// convenience copy named ESL_Worksharing.last.txt. Returns the
        /// run-specific path, which is the one to keep as evidence.
        /// </summary>
        public static string Run(UIApplication uiapp, Document doc)
        {
            DateTime nowUtc = DateTime.UtcNow;
            string evidenceSource;
            string directory = ResolveEvidenceDirectory(out evidenceSource);
            string user = SafeUsername(uiapp);

            string text = Build(uiapp, doc, nowUtc, directory, evidenceSource, user);

            Directory.CreateDirectory(directory);
            string fileName = "ESL_Worksharing_"
                + nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                + "T" + nowUtc.ToString("HHmmss", CultureInfo.InvariantCulture) + "Z"
                + "_" + Sanitize(user) + ".txt";
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, text, Encoding.UTF8);

            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "ESL_Worksharing.last.txt"), text, Encoding.UTF8);
            }
            catch (IOException)
            {
                // The run-specific file is the evidence; the "last" copy is a convenience.
            }
            return path;
        }

        private static string Build(
            UIApplication uiapp, Document doc, DateTime nowUtc,
            string directory, string evidenceSource, string user)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(HeavyRule);
            sb.AppendLine("ESL Worksharing Status Probe (read-only)");
            sb.AppendLine("  written        : " + Utc(nowUtc));
            sb.AppendLine("  evidence dir   : " + directory);
            sb.AppendLine("  dir source     : " + evidenceSource);
            sb.AppendLine("  machine        : " + SafeMachineName());
            sb.AppendLine("  Revit username : " + user);
            AppendRevitVersion(sb, uiapp);
            sb.AppendLine("  Windows        : " + Environment.OSVersion.VersionString);
            sb.AppendLine("  add-in build   : " + AddInVersion());

            AppendDocumentSection(sb, doc);
            AppendWorksetSection(sb, doc);
            List<LinkReportRow> rows = AppendScheduleSection(sb, doc, user);
            AppendSummarySection(sb, rows);
            AppendCaveats(sb);

            sb.AppendLine(HeavyRule);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- document

        private static void AppendDocumentSection(StringBuilder sb, Document doc)
        {
            sb.AppendLine(Rule);
            sb.AppendLine("DOCUMENT");
            sb.AppendLine("  title             : " + Safe(() => doc.Title));
            sb.AppendLine("  Document.PathName : " + Safe(() =>
                string.IsNullOrEmpty(doc.PathName) ? "(unsaved)" : doc.PathName));
            sb.AppendLine("  workshared        : " + Safe(() => doc.IsWorkshared.ToString()));
            sb.AppendLine("  central path      : " + CentralPath(doc));
            sb.AppendLine("  opened as         : " + OpenedAs(doc));
            sb.AppendLine("  detached          : " + Safe(() => doc.IsDetached.ToString()));
            sb.AppendLine("  read-only file    : " + Safe(() => doc.IsReadOnlyFile.ToString()));
            sb.AppendLine();
            sb.AppendLine("  The two lines below describe THIS COMMAND, not the model. The probe is");
            sb.AppendLine("  registered as a read-only external command, which puts the document into");
            sb.AppendLine("  a read-only state for the duration of the run, so they are the same on");
            sb.AppendLine("  every model. Use \"read-only file\" above for the document's own state.");
            sb.AppendLine("  doc.IsReadOnly    : " + Safe(() => doc.IsReadOnly.ToString())
                + "   (constant under a read-only command)");
            sb.AppendLine("  doc.IsModifiable  : " + Safe(() => doc.IsModifiable.ToString())
                + "  (constant under a read-only command)");
            sb.AppendLine();
            sb.AppendLine("  NOTE: in a workshared model opened as a local file, Document.PathName is");
            sb.AppendLine("        the LOCAL file, not the central one. Every model-relative Excel path");
            sb.AppendLine("        the add-in stores or resolves is computed against the folder above,");
            sb.AppendLine("        so it differs per user. The shared-versus-machine-local path comparison examines exactly that.");
        }

        /// <summary>
        /// How this document was opened, derived by comparing the document's own
        /// path against the central path. Worth its own line because opening the
        /// central model directly instead of a local file silently invalidates a
        /// relay leg — element borrowing behaves differently — and nothing else
        /// in the report says so outright.
        /// </summary>
        private static string OpenedAs(Document doc)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    return "(not workshared)";
                }
                if (doc.IsDetached)
                {
                    return "DETACHED from central";
                }
                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                string central = modelPath == null
                    ? null
                    : ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                if (string.IsNullOrEmpty(central) || string.IsNullOrEmpty(doc.PathName))
                {
                    return "(cannot tell - a path is empty)";
                }
                return SamePath(central, doc.PathName)
                    ? "*** THE CENTRAL MODEL ITSELF *** - a relay leg run this way is not valid"
                    : "a local file";
            }
            catch (Exception ex)
            {
                return "(failed: " + ex.GetType().Name + ")";
            }
        }

        private static string CentralPath(Document doc)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    return "(not workshared)";
                }
                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                if (modelPath == null)
                {
                    return "(none reported)";
                }
                string visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                return string.IsNullOrEmpty(visible) ? "(empty)" : visible;
            }
            catch (Exception ex)
            {
                return "(failed: " + ex.GetType().Name + " " + ex.Message + ")";
            }
        }

        // ---------------------------------------------------------------- worksets

        private static void AppendWorksetSection(StringBuilder sb, Document doc)
        {
            sb.AppendLine(Rule);
            sb.AppendLine("WORKSETS");

            if (!IsWorkshared(doc))
            {
                sb.AppendLine("  (document is not workshared - no worksets)");
                return;
            }

            try
            {
                WorksetTable table = doc.GetWorksetTable();
                Workset active = table.GetWorkset(table.GetActiveWorksetId());
                sb.AppendLine("  active workset    : " + Describe(active));

                List<Workset> user = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets()
                    .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int closedUser = user.Count(w => !w.IsOpen);
                sb.AppendLine("  user worksets     : " + user.Count
                    + " (closed: " + closedUser + ")");
                foreach (Workset workset in user)
                {
                    sb.AppendLine("    " + Describe(workset));
                }

                List<Workset> views = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.ViewWorkset)
                    .ToWorksets()
                    .ToList();
                int closedViews = views.Count(w => !w.IsOpen);
                sb.AppendLine("  view worksets     : " + views.Count
                    + " (closed: " + closedViews + ")");

                if (closedUser > 0 || closedViews > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  NOTE: a closed workset is the closed-workset case. Compare the linked-");
                    sb.AppendLine("        schedule count below against a run with every workset open.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (failed: " + ex.GetType().Name + " " + ex.Message + ")");
            }
        }

        private static string Describe(Workset workset)
        {
            if (workset == null)
            {
                return "(null)";
            }
            string owner = string.IsNullOrEmpty(workset.Owner) ? "-" : workset.Owner;
            return "[" + (workset.IsOpen ? "open  " : "CLOSED") + "] "
                + Pad(workset.Name, 34)
                + " id=" + Pad(workset.Id.IntegerValue.ToString(CultureInfo.InvariantCulture), 8)
                + " owner=" + Pad(owner, 18)
                + " editable=" + workset.IsEditable;
        }

        // --------------------------------------------------------------- schedules

        private sealed class LinkReportRow
        {
            public string CheckoutStatus = "unknown";
            public bool SourceResolved;
            public string Creator;
        }

        private static List<LinkReportRow> AppendScheduleSection(
            StringBuilder sb, Document doc, string currentUser)
        {
            List<LinkReportRow> rows = new List<LinkReportRow>();
            sb.AppendLine(Rule);
            sb.AppendLine("SCHEDULES");

            int totalSchedules;
            try
            {
                totalSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Count(v => !v.IsTemplate);
            }
            catch (Exception)
            {
                totalSchedules = -1;
            }

            bool schemaV1Registered;
            bool schemaV2Registered;
            try
            {
                schemaV1Registered = Schema.Lookup(ExcelLinkSchema.SchemaGuid) != null;
                schemaV2Registered = Schema.Lookup(ExcelLinkSchema.SchemaV2Guid) != null;
            }
            catch (Exception)
            {
                schemaV1Registered = false;
                schemaV2Registered = false;
            }

            sb.AppendLine("  ViewSchedules visible to the collector : " + totalSchedules);
            sb.AppendLine("  ESL v1 schema registered in this session: "
                + (schemaV1Registered ? "yes" : "no") + " (" + ExcelLinkSchema.SchemaGuid + ")");
            sb.AppendLine("  ESL v2 schema registered in this session: "
                + (schemaV2Registered ? "yes" : "no") + " (" + ExcelLinkSchema.SchemaV2Guid + ")");

            IList<ViewSchedule> linked;
            try
            {
                linked = LinkedScheduleFinder.FindAllAny(doc);
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (enumeration failed: " + ex.GetType().Name + " " + ex.Message + ")");
                return rows;
            }
            sb.AppendLine("  Excel-linked schedules                 : " + linked.Count);

            int index = 0;
            foreach (ViewSchedule schedule in linked)
            {
                index++;
                sb.AppendLine();
                rows.Add(AppendOneSchedule(sb, doc, schedule, index, currentUser));
            }
            return rows;
        }

        private static LinkReportRow AppendOneSchedule(
            StringBuilder sb, Document doc, ViewSchedule schedule, int index, string currentUser)
        {
            LinkReportRow row = new LinkReportRow();

            sb.AppendLine("  [" + index + "] \"" + Safe(() => schedule.Name) + "\"");
            sb.AppendLine("      element id      : " + Safe(() =>
                schedule.Id.Value.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine("      unique id       : " + Safe(() => schedule.UniqueId));
            sb.AppendLine("      workset         : " + WorksetOf(doc, schedule));

            AppendOwnership(sb, doc, schedule, row);
            AppendLinkData(sb, doc, schedule, row, currentUser);
            return row;
        }

        private static string WorksetOf(Document doc, Element element)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    return "(not workshared)";
                }
                Workset workset = doc.GetWorksetTable().GetWorkset(element.WorksetId);
                if (workset == null)
                {
                    return "(unknown, id "
                        + element.WorksetId.IntegerValue.ToString(CultureInfo.InvariantCulture) + ")";
                }
                return "\"" + workset.Name + "\" (id "
                    + workset.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)
                    + ", " + (workset.IsOpen ? "open" : "CLOSED")
                    + ", kind " + workset.Kind + ")";
            }
            catch (Exception ex)
            {
                return "(failed: " + ex.GetType().Name + ")";
            }
        }

        private static void AppendOwnership(
            StringBuilder sb, Document doc, ViewSchedule schedule, LinkReportRow row)
        {
            if (!IsWorkshared(doc))
            {
                row.CheckoutStatus = "n/a";
                sb.AppendLine("      checkout status : (not workshared)");
                return;
            }

            try
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, schedule.Id);
                row.CheckoutStatus = status.ToString();
                sb.AppendLine("      checkout status : " + status);
            }
            catch (Exception ex)
            {
                row.CheckoutStatus = "failed";
                sb.AppendLine("      checkout status : (failed: " + ex.GetType().Name + ")");
            }

            try
            {
                WorksharingTooltipInfo info =
                    WorksharingUtils.GetWorksharingTooltipInfo(doc, schedule.Id);
                row.Creator = info.Creator;
                sb.AppendLine("      owner           : " + Dash(info.Owner));
                sb.AppendLine("      creator         : " + Dash(info.Creator));
                sb.AppendLine("      last changed by : " + Dash(info.LastChangedBy));
            }
            catch (Exception ex)
            {
                sb.AppendLine("      owner           : (failed: " + ex.GetType().Name + ")");
            }
        }

        private static void AppendLinkData(
            StringBuilder sb, Document doc, ViewSchedule schedule, LinkReportRow row,
            string currentUser)
        {
            ExcelLinkStorage storage;
            try
            {
                storage = ExcelLinkSchema.LoadAny(schedule);
            }
            catch (Exception ex)
            {
                sb.AppendLine("      link data       : (failed: " + ex.GetType().Name + ")");
                return;
            }
            if (storage == null || storage.Info == null)
            {
                sb.AppendLine("      link data       : (none - schedule is not Excel-linked)");
                return;
            }
            ExcelLinkInfo info = storage.Info;

            sb.AppendLine("      -- link data (extensible storage) --");
            sb.AppendLine("      schema guid     : " + storage.SchemaGuid);
            sb.AppendLine("      schema version  : " + info.SchemaVersion
                + " / renderer " + info.RendererVersion);
            sb.AppendLine("      schema state    : " + SchemaState(storage));
            sb.AppendLine("      worksheet       : " + Dash(info.SheetName));
            sb.AppendLine("      range           : " + (info.RangeMode == RangeMode.Explicit
                ? info.RangeAddress : "(used range)"));
            sb.AppendLine("      path mode       : " + info.PathMode);
            if (!storage.IsV2 && info.PathMode == PathMode.AbsolutePreferred)
            {
                sb.AppendLine("      path mode note  : v1 field; v2 migration normalizes to AbsolutePreferred");
            }
            else if (info.PathMode != PathMode.AbsolutePreferred)
            {
                sb.AppendLine("      path mode note  : reserved/invalid value; effective behavior is AbsolutePreferred");
                if (!storage.IsV2)
                {
                    sb.AppendLine("      path mode norm  : v2 migration will write AbsolutePreferred");
                }
            }
            sb.AppendLine("      created sha-256 : " + (string.IsNullOrEmpty(info.CreatedSourceSha256)
                ? (storage.IsV2 ? "unknown (migrated from v1)" : "unknown (unmigrated v1)")
                : info.CreatedSourceSha256));
            sb.AppendLine("      accepted sha-256: " + (string.IsNullOrEmpty(info.LastAcceptedSourceSha256)
                ? "unknown" : info.LastAcceptedSourceSha256));
            sb.AppendLine("      accepted size   : " + (info.LastAcceptedSourceSha256 == null
                || info.LastAcceptedSourceSha256.Length == 0
                ? "unknown" : info.LastAcceptedSourceSizeBytes.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine("      accepted stamp  : " + (string.IsNullOrEmpty(info.LastAcceptedSourceSha256)
                ? "unknown" : Ticks(info.SourceLastWriteUtcTicks)));
            sb.AppendLine("      stored absolute : " + Dash(info.AbsolutePath));
            sb.AppendLine("      stored relative : " + Dash(info.RelativePath));
            sb.AppendLine("      relative anchor : " + PathResolver.GetStableAnchorClass(doc));
            sb.AppendLine("      relative field  : " + PathResolver.DescribeRelativeField(doc, info));

            string resolved = null;
            try
            {
                resolved = PathResolver.Resolve(doc, info);
            }
            catch (Exception)
            {
            }
            row.SourceResolved = resolved != null;

            if (resolved == null)
            {
                sb.AppendLine("      resolved        : *** NOT FOUND *** "
                    + "(neither the absolute nor the model-relative candidate exists here)");
                sb.AppendLine("      winning candidate: none");
                sb.AppendLine("      current sha-256 : READ FAILED (resolved source unavailable)");
                sb.AppendLine("      current size    : unknown");
                sb.AppendLine("      current stamp   : unknown");
                sb.AppendLine("      identity result : READ FAILED");
            }
            else
            {
                sb.AppendLine("      resolved        : " + resolved);
                sb.AppendLine("      winning candidate: " + WhichCandidate(doc, info, resolved));

                SheetReadResult current = null;
                string readFailure = null;
                try
                {
                    string range = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null;
                    current = new OpenXmlExcelReadService().ReadSheetSnapshot(
                        resolved, info.SheetName, range);
                }
                catch (Exception ex)
                {
                    readFailure = ex.GetType().Name + ": " + ex.Message;
                }

                if (current == null)
                {
                    sb.AppendLine("      current sha-256 : READ FAILED (" + readFailure + ")");
                    sb.AppendLine("      current size    : unknown");
                    sb.AppendLine("      current stamp   : unknown");
                    sb.AppendLine("      identity result : READ FAILED");
                }
                else
                {
                    SourceFingerprint fingerprint = current.Fingerprint;
                    sb.AppendLine("      current sha-256 : " + fingerprint.Sha256);
                    sb.AppendLine("      current size    : "
                        + fingerprint.SizeBytes.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("      current stamp   : " + Ticks(fingerprint.LastWriteUtcTicks));
                    sb.AppendLine("      identity result : " + IdentityLabel(SourceIdentity.Compare(
                        info.LastAcceptedSourceSha256, fingerprint.Sha256)));
                }
            }

            sb.AppendLine("      stored stamp    : " + Ticks(info.SourceLastWriteUtcTicks));
            sb.AppendLine("      last loaded     : " + Ticks(info.LastLoadedUtcTicks));
            sb.AppendLine("      schedule creator: " + (string.IsNullOrEmpty(row.Creator)
                ? "UNAVAILABLE" : row.Creator));
            sb.AppendLine("      current user    : " + Dash(currentUser));
            sb.AppendLine("      creator relation: " + CreatorRelation(row.Creator, currentUser, doc));
            sb.AppendLine("      width scale     : "
                + info.WidthScale.ToString("0.###", CultureInfo.InvariantCulture)
                + "   columns=" + info.ColumnWidthMode + "   rows=" + info.RowHeightMode);
        }

        private static string SchemaState(ExcelLinkStorage storage)
        {
            if (!storage.IsV2)
            {
                return "UNMIGRATED V1";
            }
            return string.IsNullOrEmpty(storage.Info.CreatedSourceSha256)
                ? (string.IsNullOrEmpty(storage.Info.LastAcceptedSourceSha256)
                    ? "V2 MIGRATED / BASELINE UNKNOWN"
                    : "V2 MIGRATED / ORIGIN UNKNOWN")
                : "V2 CREATED";
        }

        private static string CreatorRelation(string creator, string currentUser, Document doc)
        {
            return WorksharingContext.Label(
                WorksharingContext.FromKnownCreator(doc,
                    creator == "-" ? string.Empty : creator,
                    currentUser));
        }

        private static string IdentityLabel(SourceIdentityState state)
        {
            switch (state)
            {
                case SourceIdentityState.Match: return "MATCH";
                case SourceIdentityState.Differs: return "DIFFERS";
                case SourceIdentityState.Unknown: return "UNKNOWN";
                case SourceIdentityState.ReadFailed: return "READ FAILED";
                default: return "READ FAILED";
            }
        }

        /// <summary>
        /// Which stored candidate the resolved path actually came from. The
        /// collision and model-relative path checks turn on this line: the resolved
        /// path alone cannot distinguish a correct shared workbook from a same-named one found beside somebody's
        /// local model file.
        /// </summary>
        private static string WhichCandidate(Document doc, ExcelLinkInfo info, string resolved)
        {
            bool matchesAbsolute = !string.IsNullOrEmpty(info.AbsolutePath)
                && SamePath(info.AbsolutePath, resolved);
            string relativeCandidate = PathResolver.ResolveRelativeCandidate(doc, info.RelativePath);
            bool matchesRelative = relativeCandidate != null && SamePath(relativeCandidate, resolved);

            if (matchesAbsolute && matchesRelative)
            {
                return "ABSOLUTE; RELATIVE CANDIDATE AGREES (stable anchor)";
            }
            if (matchesAbsolute)
            {
                return "ABSOLUTE; " + PathResolver.DescribeRelativeField(doc, info);
            }
            if (matchesRelative)
            {
                return "RELATIVE (stable anchor)";
            }
            return "unclear";
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(FullOrSelf(left), FullOrSelf(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string FullOrSelf(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        // ----------------------------------------------------------------- summary

        private static void AppendSummarySection(StringBuilder sb, List<LinkReportRow> rows)
        {
            sb.AppendLine(Rule);
            sb.AppendLine("SUMMARY");
            sb.AppendLine("  linked schedules      : " + rows.Count);
            // "n/a" for every row just means the document is not workshared; an
            // ownership breakdown of a model with no ownership is noise.
            if (rows.Any(r => r.CheckoutStatus != "n/a"))
            {
                foreach (IGrouping<string, LinkReportRow> group in rows
                    .GroupBy(r => r.CheckoutStatus)
                    .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine("    " + Pad(group.Key, 22) + ": " + group.Count());
                }
            }
            sb.AppendLine("  source resolved       : " + rows.Count(r => r.SourceResolved));
            sb.AppendLine("  source MISSING        : " + rows.Count(r => !r.SourceResolved));
        }

        private static void AppendCaveats(StringBuilder sb)
        {
            sb.AppendLine(Rule);
            sb.AppendLine("CAVEATS");
            sb.AppendLine("  - CheckoutStatus is a locally cached value. Autodesk documents it as");
            sb.AppendLine("    suitable for reporting to a user, but NOT as a reliable answer to");
            sb.AppendLine("    \"can this element be edited right now\". Run Reload Latest before");
            sb.AppendLine("    treating an OwnedByOtherUser / NotOwned line as current.");
            sb.AppendLine("  - This probe opens no transaction, creates no schema, and writes nothing");
            sb.AppendLine("    to the model. Re-running it is always safe.");
            sb.AppendLine("  - Links in a CLOSED workset cannot be listed here. Absence from the");
            sb.AppendLine("    SCHEDULES section is not proof that a link does not exist.");
            sb.AppendLine("  - \"ESL schema registered in this session\" is a property of the Revit");
            sb.AppendLine("    SESSION, not of this document. Extensible-storage schemas register");
            sb.AppendLine("    process-wide, so opening any other model with ESL links earlier in");
            sb.AppendLine("    the same Revit run turns it to yes. Read it together with the");
            sb.AppendLine("    Excel-linked count; a \"yes\" beside a count of 0 means the schema is");
            sb.AppendLine("    known to Revit and this document simply has no links.");
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsWorkshared(Document doc)
        {
            try
            {
                return doc.IsWorkshared;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void AppendRevitVersion(StringBuilder sb, UIApplication uiapp)
        {
            try
            {
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
                sb.AppendLine("  Revit          : " + app.VersionNumber
                    + " / build " + app.VersionBuild
                    + " / " + app.VersionName);
            }
            catch (Exception ex)
            {
                sb.AppendLine("  Revit          : (failed: " + ex.GetType().Name + ")");
            }
        }

        private static string AddInVersion()
        {
            try
            {
                return typeof(WorksharingStatusReport).Assembly.GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        private static string SafeUsername(UIApplication uiapp)
        {
            try
            {
                string name = uiapp.Application.Username;
                return string.IsNullOrEmpty(name) ? "(empty)" : name;
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        private static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                string value = read();
                return value ?? "(null)";
            }
            catch (Exception ex)
            {
                return "(failed: " + ex.GetType().Name + ")";
            }
        }

        private static string Dash(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static string Ticks(long ticks)
        {
            if (ticks <= 0)
            {
                return "-";
            }
            try
            {
                return Utc(new DateTime(ticks, DateTimeKind.Utc));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "(invalid: " + ticks.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static string Utc(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
        }

        private static string Pad(string value, int width)
        {
            string text = value ?? string.Empty;
            return text.Length >= width ? text : text.PadRight(width);
        }

        private static string Sanitize(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in value ?? string.Empty)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            string result = sb.ToString().Trim();
            return result.Length == 0 ? "unknown" : result;
        }
    }
}
