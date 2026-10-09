using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Isolated verification of the "N dummy body columns" idea (candidate
    /// design):
    /// can dragging a BODY column grip on a SHEET drive the corresponding HEADER
    /// (title) column width, so a header-rendered table gains native per-column
    /// sheet resize? Also measures how many schedulable fields can be added with
    /// ZERO new project parameters (the 50-column feasibility question).
    ///
    /// English-only, pure ASCII: internal debug tooling. The report is appended to
    /// %APPDATA%\ExcelScheduleLink\NDummyProbeReport.txt so it can be handed back.
    /// (%APPDATA%, not %TEMP%: inside the Revit process Path.GetTempPath() returns
    /// a per-session %LOCALAPPDATA%\Temp\{guid}\ sandbox, so a %TEMP%-based path
    /// scatters one report per Revit launch. See Step0Reporter.ReportDirectory.)
    ///
    /// Protocol:
    ///   1. Run Setup: builds a 3-body-field + 3-header-column schedule with
    ///      deliberately DIFFERENT widths, runs two API coupling tests, measures
    ///      the 50-field feasibility, and places the schedule on a new sheet.
    ///   2. Manually open the sheet and drag ONLY the middle column grip.
    ///   3. Run Measure: records the widths again (drag result).
    ///   4. Save, close, reopen, run Measure again (persistence).
    /// </summary>
    internal static class NDummyBodyProbe
    {
        private const string ScheduleName = "ESL_PROBE_NDummy";
        private const string TempScheduleName = "ESL_PROBE_50_TMP";
        private const int ColumnCount = 3;
        private const int FeasibilityTarget = 50;

        // Deliberately distinct starting widths (feet) so any coupling is obvious.
        private static readonly double[] BodyWidths = { 0.50, 1.00, 1.50 };
        private static readonly double[] HeaderWidths = { 0.30, 0.90, 1.40 };

        public static string ReportPath
        {
            get { return Path.Combine(Step0Reporter.ReportDirectory, "NDummyProbeReport.txt"); }
        }

        // ---------------------------------------------------------------- Setup

        public static Result RunSetup(ExternalCommandData commandData, ref string message)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            List<string> log = new List<string>();
            log.Add(SectionHeader("SETUP", uiapp));

            if (uidoc == null)
            {
                log.Add("[FAIL] No active document. Open a project first.");
                Write(log);
                TaskDialog.Show("ESL Probe", "No active document.\nReport: " + ReportPath);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            ViewSchedule schedule = null;
            try
            {
                using (Transaction t = new Transaction(doc, "ESL Probe N-Dummy Setup"))
                {
                    t.Start();

                    // Feasibility first (own throwaway schedule, then deleted).
                    MeasureFieldFeasibility(doc, log);

                    // Clean previous probe schedule so re-runs start fresh.
                    ViewSchedule prior = FindByName(doc, ScheduleName);
                    if (prior != null)
                    {
                        doc.Delete(prior.Id);
                        log.Add("[OK] Deleted prior probe schedule.");
                    }

                    schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                    schedule.Name = ScheduleName;
                    ScheduleDefinition def = schedule.Definition;

                    // Add ColumnCount fields => body columns. field[0] MUST be a
                    // TEXT parameter, because the body-emptying filter uses a
                    // string Equal value; non-string fields (Type / Family) reject
                    // it (that was the earlier rollback). Prefer known text params,
                    // then top up columns with any instance fields.
                    IList<SchedulableField> avail = def.GetSchedulableFields();
                    List<ScheduleFieldId> fids = new List<ScheduleFieldId>();
                    HashSet<long> usedParams = new HashSet<long>();

                    BuiltInParameter[] textPrefs =
                    {
                        BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                        BuiltInParameter.ALL_MODEL_MARK,
                        BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
                        BuiltInParameter.ALL_MODEL_TYPE_MARK,
                        BuiltInParameter.ALL_MODEL_MODEL,
                        BuiltInParameter.ALL_MODEL_MANUFACTURER,
                        BuiltInParameter.ALL_MODEL_DESCRIPTION
                    };
                    foreach (BuiltInParameter bip in textPrefs)
                    {
                        if (fids.Count >= ColumnCount) break;
                        ElementId pid = new ElementId(bip);
                        SchedulableField sf = avail.FirstOrDefault(f => f.ParameterId == pid);
                        if (sf == null || usedParams.Contains(pid.Value)) continue;
                        try
                        {
                            fids.Add(def.AddField(sf).FieldId);
                            usedParams.Add(pid.Value);
                            log.Add("[OK] Added text body field: " + SafeName(sf, doc));
                        }
                        catch (Exception ex) { log.Add("[WARN] AddField skipped: " + ex.Message); }
                    }
                    foreach (SchedulableField sf in avail)
                    {
                        if (fids.Count >= ColumnCount) break;
                        if (sf.FieldType != ScheduleFieldType.Instance) continue;
                        if (usedParams.Contains(sf.ParameterId.Value)) continue;
                        try
                        {
                            fids.Add(def.AddField(sf).FieldId);
                            usedParams.Add(sf.ParameterId.Value);
                            log.Add("[OK] Added body field: " + SafeName(sf, doc));
                        }
                        catch (Exception ex) { log.Add("[WARN] AddField skipped: " + ex.Message); }
                    }
                    if (fids.Count < ColumnCount)
                    {
                        log.Add("[FAIL] Only added " + fids.Count + "/" + ColumnCount + " body fields.");
                    }

                    // Keep the body empty via contradictory filters on field[0].
                    if (fids.Count > 0)
                    {
                        def.AddFilter(new ScheduleFilter(fids[0], ScheduleFilterType.Equal, "ESL_X"));
                        def.AddFilter(new ScheduleFilter(fids[0], ScheduleFilterType.Equal, "ESL_Y"));
                        log.Add("[OK] Body kept empty (two contradictory Equal filters).");
                    }

                    // Distinct body widths.
                    for (int i = 0; i < fids.Count && i < BodyWidths.Length; i++)
                    {
                        TrySetGrid(def, fids[i], BodyWidths[i], log, i);
                    }

                    // Header table with DIFFERENT distinct widths.
                    def.ShowTitle = true;
                    def.ShowHeaders = false;
                    BuildHeader(schedule, log);

                    log.Add("-- baseline widths (feet) --");
                    Snapshot(schedule, def, fids, log);

                    // API test A: change a BODY field width, see if the HEADER follows.
                    log.Add("-- API test A: set BODY field[1].GridColumnWidth = 2.0000 --");
                    TrySetGrid(def, fids.Count > 1 ? fids[1] : null, 2.0, log, 1);
                    Snapshot(schedule, def, fids, log);
                    log.Add("   (coupling? header[1] should read 2.0000 if body drives header)");

                    // API test B: change a HEADER column width, see if the BODY follows.
                    log.Add("-- API test B: set HEADER col[1] width = 1.7000 --");
                    TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
                    try { tsd.SetColumnWidth(tsd.FirstColumnNumber + 1, 1.70); }
                    catch (Exception ex) { log.Add("[WARN] header SetColumnWidth: " + ex.Message); }
                    Snapshot(schedule, def, fids, log);
                    log.Add("   (coupling? body[1] should read 1.7000 if header drives body)");

                    // Place on a fresh sheet.
                    ElementId sheetId = CreateSheet(doc, log);
                    if (sheetId != ElementId.InvalidElementId)
                    {
                        try
                        {
                            ScheduleSheetInstance ssi = ScheduleSheetInstance.Create(
                                doc, sheetId, schedule.Id, new XYZ(0, 0, 0));
                            log.Add("[OK] Placed on sheet (instance Id=" + ssi.Id.Value + ").");
                            log.Add("   MANUAL: open that sheet, count how many column grips appear, "
                                + "then drag ONLY the middle grip.");
                        }
                        catch (Exception ex)
                        {
                            log.Add("[FAIL] ScheduleSheetInstance.Create: " + ex.Message);
                        }
                    }

                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                log.Add("[FAIL] setup exception: " + ex);
                message = ex.Message;
            }
            Write(log);

            if (schedule != null && schedule.IsValidObject)
            {
                try { uidoc.ActiveView = schedule; } catch (Exception) { }
            }

            TaskDialog.Show("ESL Probe - N-Dummy Setup",
                "Setup complete. Report appended:\n" + ReportPath
                + "\n\nNEXT (manual): open the sheet, note how many column grips appear, "
                + "DRAG ONLY the MIDDLE grip, then run 'ESL Probe: N-Dummy Measure'.");
            return Result.Succeeded;
        }

        // -------------------------------------------------------------- Measure

        public static Result RunMeasure(ExternalCommandData commandData, ref string message)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            List<string> log = new List<string>();
            log.Add(SectionHeader("MEASURE (after manual sheet drag / after reopen)", uiapp));

            if (uidoc == null)
            {
                log.Add("[FAIL] No active document.");
                Write(log);
                TaskDialog.Show("ESL Probe", "No active document.\nReport: " + ReportPath);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            ViewSchedule schedule = FindByName(doc, ScheduleName);
            if (schedule == null)
            {
                log.Add("[FAIL] Probe schedule not found. Run Setup first.");
                Write(log);
                TaskDialog.Show("ESL Probe", "Probe schedule not found. Run Setup first.\nReport: " + ReportPath);
                return Result.Cancelled;
            }

            ScheduleDefinition def = schedule.Definition;
            List<ScheduleFieldId> fids = def.GetFieldOrder().Take(ColumnCount).ToList();
            log.Add("-- current widths (feet) --");
            // Reading post-drag widths can trigger an internal regeneration, which
            // a ReadOnly command rejects ("changes are disabled"). A committed
            // transaction lets that regeneration proceed.
            try
            {
                using (Transaction t = new Transaction(doc, "ESL Probe Measure"))
                {
                    t.Start();
                    Snapshot(schedule, def, fids, log);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                log.Add("[WARN] measure exception: " + ex.Message);
            }
            Write(log);

            TaskDialog.Show("ESL Probe - Measure",
                "Current widths appended:\n" + ReportPath
                + "\n\nFor persistence: SAVE, close, reopen, then run Measure once more.");
            return Result.Succeeded;
        }

        // --------------------------------------------------------------- helpers

        private static void MeasureFieldFeasibility(Document doc, List<string> log)
        {
            try
            {
                ViewSchedule tmp = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                tmp.Name = TempScheduleName;
                ScheduleDefinition d = tmp.Definition;
                IList<SchedulableField> avail = d.GetSchedulableFields();
                int instanceAvail = avail.Count(f => f.FieldType == ScheduleFieldType.Instance);
                log.Add("[INFO] GetSchedulableFields total=" + avail.Count
                    + ", instance-type=" + instanceAvail
                    + "  (multi-category schedule)");

                int added = 0;
                foreach (SchedulableField sf in avail)
                {
                    if (added >= FeasibilityTarget) break;
                    if (sf.FieldType != ScheduleFieldType.Instance) continue;
                    try { d.AddField(sf); added++; }
                    catch (Exception) { }
                }
                log.Add("[INFO] 50-col feasibility: added " + added + "/" + FeasibilityTarget
                    + " body fields using ONLY existing schedulable params (0 new project params).");
                if (added >= FeasibilityTarget)
                {
                    log.Add("   => N body columns up to " + added + " are possible with NO parameter pollution.");
                }
                else
                {
                    log.Add("   => multi-category exposes only " + added + " addable instance fields; "
                        + "a broader single category or calculated fields may be needed for 50.");
                }
                doc.Delete(tmp.Id);
                log.Add("[OK] Deleted the feasibility schedule (no residue).");
            }
            catch (Exception ex)
            {
                log.Add("[WARN] feasibility measurement failed: " + ex.Message);
            }
        }

        private static void BuildHeader(ViewSchedule schedule, List<string> log)
        {
            try
            {
                TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
                int guard = 0;
                while (tsd.NumberOfColumns < ColumnCount && guard++ < 1000)
                {
                    tsd.InsertColumn(tsd.FirstColumnNumber + 1);
                }
                guard = 0;
                while (tsd.NumberOfRows < 2 && guard++ < 1000)
                {
                    tsd.InsertRow(tsd.FirstRowNumber + 1);
                }
                int r0 = tsd.FirstRowNumber;
                int c0 = tsd.FirstColumnNumber;
                for (int c = 0; c < ColumnCount; c++)
                {
                    tsd.SetCellText(r0, c0 + c, "H" + (c + 1));
                    tsd.SetCellText(r0 + 1, c0 + c, "cell" + (c + 1));
                    try { tsd.SetColumnWidth(c0 + c, HeaderWidths[c]); }
                    catch (Exception ex) { log.Add("[WARN] header SetColumnWidth[" + c + "]: " + ex.Message); }
                }
                log.Add("[OK] Header table built (" + ColumnCount + " cols, distinct widths).");
            }
            catch (Exception ex)
            {
                log.Add("[FAIL] BuildHeader: " + ex.Message);
            }
        }

        private static void Snapshot(ViewSchedule schedule, ScheduleDefinition def,
            IList<ScheduleFieldId> fids, List<string> log)
        {
            TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
            int c0 = tsd.FirstColumnNumber;
            for (int i = 0; i < ColumnCount; i++)
            {
                double grid = double.NaN, sheet = double.NaN, hdr = double.NaN;
                if (i < fids.Count)
                {
                    try { ScheduleField f = def.GetField(fids[i]); grid = f.GridColumnWidth; } catch (Exception) { }
                    try { ScheduleField f = def.GetField(fids[i]); sheet = f.SheetColumnWidth; } catch (Exception) { }
                }
                try { hdr = tsd.GetColumnWidth(c0 + i); } catch (Exception) { }
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "   col{0}: bodyGrid={1:F4}  bodySheet={2:F4}  header={3:F4}", i, grid, sheet, hdr));
            }
        }

        private static void TrySetGrid(ScheduleDefinition def, ScheduleFieldId fid, double feet,
            List<string> log, int index)
        {
            if (fid == null) { log.Add("[WARN] no field to set at index " + index); return; }
            try { def.GetField(fid).GridColumnWidth = feet; }
            catch (Exception ex) { log.Add("[WARN] set GridColumnWidth[" + index + "]: " + ex.Message); }
        }

        private static ElementId CreateSheet(Document doc, List<string> log)
        {
            try
            {
                ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                log.Add("[OK] Created sheet '" + sheet.Name + "' (Id=" + sheet.Id.Value + ").");
                return sheet.Id;
            }
            catch (Exception ex)
            {
                log.Add("[FAIL] ViewSheet.Create: " + ex.Message);
                return ElementId.InvalidElementId;
            }
        }

        private static ViewSchedule FindByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));
        }

        private static string SafeName(SchedulableField sf, Document doc)
        {
            try { return sf.GetName(doc) + " (ParamId=" + sf.ParameterId.Value + ")"; }
            catch (Exception) { return "(name unavailable)"; }
        }

        private static string SectionHeader(string name, UIApplication uiapp)
        {
            string rev = "?";
            try { rev = uiapp.Application.VersionNumber + "/" + uiapp.Application.VersionBuild; }
            catch (Exception) { }
            return new string('=', 70) + Environment.NewLine
                + "[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " UTC] " + name + "   Revit=" + rev;
        }

        private static void Write(List<string> log)
        {
            try
            {
                string dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(ReportPath,
                    string.Join(Environment.NewLine, log) + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // best effort
            }
        }
    }
}
