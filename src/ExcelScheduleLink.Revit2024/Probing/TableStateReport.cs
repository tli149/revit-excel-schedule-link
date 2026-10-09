using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>Result returned after the table-state evidence file is closed.</summary>
    internal sealed class TableStateReportResult
    {
        public string Path;
        public int SchedulesDumped;
    }

    /// <summary>
    /// Read-only diagnostic writer for persisted ViewSchedule table state.
    ///
    /// This writer intentionally streams directly to a UTF-8-BOM file. The
    /// Revit schedule grid can fail natively while a cell style is being read;
    /// AutoFlush and the progress markers make the last successful boundary
    /// useful even when managed exception handling cannot run.
    /// </summary>
    internal static class TableStateReport
    {
        private const string Rule =
            "--------------------------------------------------------------------------------";
        private const string HeavyRule =
            "================================================================================";

        public static TableStateReportResult Run(UIApplication uiapp, Document doc)
        {
            DateTime nowUtc = DateTime.UtcNow;
            string user = SafeUsername(uiapp);
            string evidenceSource;
            string directory = WorksharingStatusReport.ResolveEvidenceDirectory(out evidenceSource);
            string fileName = "ESL_TableState_"
                + nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                + "_" + Sanitize(user) + ".txt";
            string path = Path.Combine(directory, fileName);

            Directory.CreateDirectory(directory);

            TableStateContext context = new TableStateContext();
            int dumped = 0;
            using (StreamWriter writer = new StreamWriter(
                path, false, new UTF8Encoding(true)))
            {
                writer.AutoFlush = true;
                writer.WriteLine(HeavyRule);
                writer.WriteLine("ESL Table State Probe (read-only)");
                writer.WriteLine("  written UTC    : " + Utc(nowUtc));
                writer.WriteLine("  evidence dir   : " + directory);
                writer.WriteLine("  dir source     : " + evidenceSource);

                AppendHeader(writer, uiapp, doc, user, context);

                writer.WriteLine(Rule);
                writer.WriteLine("LINKED SCHEDULE TABLE STATE");

                IList<ViewSchedule> linked = context.LinkedAny ?? new List<ViewSchedule>();
                List<ViewSchedule> ordered = linked
                    .Where(schedule => schedule != null)
                    .OrderBy(SafeElementIdValue)
                    .ToList();
                IList<StyleIntegrityResult> integrityResults =
                    ScheduleStyleIntegrityScanner.ScanSchedules(
                        doc, ordered, StyleIntegrityScanScope.Manual);

                int index = 0;
                foreach (ViewSchedule schedule in ordered)
                {
                    index++;
                    dumped++;
                    string name = SafeScheduleName(schedule);
                    string id = SafeElementId(schedule);
                    writer.WriteLine();
                    writer.WriteLine("BEGIN [" + index.ToString(CultureInfo.InvariantCulture)
                        + "] \"" + QuotePart(name) + "\" id=" + id);

                    ScheduleDiagnostics diagnostics = new ScheduleDiagnostics(name);
                    StyleIntegrityResult integrityResult = IntegrityResultAt(
                        integrityResults, index - 1, ordered.Count);
                    context.RecordIntegrity(integrityResult);
                    try
                    {
                        AppendSchedule(writer, doc, schedule, diagnostics, integrityResult);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.UnexpectedFailures++;
                        writer.WriteLine("  schedule dump : " + Failure(ex));
                    }

                    try
                    {
                        AppendConsistencyCheck(writer, diagnostics);
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine("  consistency check : " + Failure(ex));
                        writer.WriteLine("CHECK " + EscapeLine(name)
                            + ": SUSPECT consistency check failed");
                    }

                    // This is in a finally-equivalent position after both the
                    // table read and diagnostic summary, so a managed failure
                    // still leaves an unambiguous schedule boundary.
                    writer.WriteLine("END [" + index.ToString(CultureInfo.InvariantCulture) + "]");
                }

                AppendOtherSchedules(writer, context, ordered);
                AppendIntegrityFooter(writer, context);
                writer.WriteLine(HeavyRule);
            }

            return new TableStateReportResult
            {
                Path = path,
                SchedulesDumped = dumped
            };
        }

        private static void AppendHeader(
            StreamWriter writer, UIApplication uiapp, Document doc, string user,
            TableStateContext context)
        {
            writer.WriteLine(Rule);
            writer.WriteLine("HEADER");
            writer.WriteLine("  machine        : " + SafeMachineName());
            writer.WriteLine("  Revit username : " + user);

            Application app;
            bool appOk;
            app = Read(writer, "  Revit application", () => uiapp.Application,
                FormatPresent, out appOk);
            if (appOk && app != null)
            {
                Read(writer, "  Revit version", () => app.VersionNumber,
                    FormatString, out appOk);
                Read(writer, "  Revit build", () => app.VersionBuild,
                    FormatString, out appOk);
                Read(writer, "  Revit name", () => app.VersionName,
                    FormatString, out appOk);
                Read(writer, "  add-in assembly version",
                    () => typeof(TableStateReport).Assembly.GetName().Version,
                    v => FormatObject(v), out appOk);
            }
            else
            {
                writer.WriteLine("  Revit version  : (unavailable)");
                writer.WriteLine("  Revit build    : (unavailable)");
                writer.WriteLine("  Revit name     : (unavailable)");
                writer.WriteLine("  add-in assembly version : "
                    + FormatObject(typeof(TableStateReport).Assembly.GetName().Version));
            }

            writer.WriteLine("  document");
            Read(writer, "    title", () => doc.Title, FormatString, out appOk);
            Read(writer, "    PathName", () => doc.PathName, FormatString, out appOk);
            Read(writer, "    IsWorkshared", () => doc.IsWorkshared,
                FormatBoolean, out appOk);

            UIDocument activeUidoc;
            bool activeUidocOk;
            activeUidoc = Read(writer, "  active UIDocument",
                () => uiapp.ActiveUIDocument, FormatPresent, out activeUidocOk);
            AppendActiveView(writer, activeUidocOk ? activeUidoc : null);

            writer.WriteLine("  add-in link discovery");
            try
            {
                context.AllSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .ToList();
                writer.WriteLine("    ViewSchedules (including templates): "
                    + context.AllSchedules.Count.ToString(CultureInfo.InvariantCulture));
                int nonTemplateFailures;
                int nonTemplate = CountNonTemplate(context.AllSchedules, out nonTemplateFailures);
                writer.WriteLine("    ViewSchedules (non-template)         : "
                    + nonTemplate.ToString(CultureInfo.InvariantCulture));
                if (nonTemplateFailures > 0)
                {
                    writer.WriteLine("    non-template flag failures           : "
                        + nonTemplateFailures.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                context.AllSchedules = new List<ViewSchedule>();
                writer.WriteLine("    ViewSchedules                     : " + Failure(ex));
            }

            try
            {
                context.LinkedAny = LinkedScheduleFinder.FindAllAny(doc)
                    ?? new List<ViewSchedule>();
                writer.WriteLine("    linked schedules (FindAllAny)       : "
                    + context.LinkedAny.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                context.LinkedAny = new List<ViewSchedule>();
                context.LinkedDiscoveryFailed = true;
                writer.WriteLine("    linked schedules (FindAllAny)       : " + Failure(ex));
            }

            try
            {
                context.LinkedCrossCheck = LinkedScheduleFinder.FindAll(doc)
                    ?? new List<ViewSchedule>();
                writer.WriteLine("    linked schedules (FindAll cross-check): "
                    + context.LinkedCrossCheck.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                context.LinkedCrossCheck = new List<ViewSchedule>();
                writer.WriteLine("    linked schedules (FindAll cross-check): " + Failure(ex));
            }

            writer.WriteLine("    linked enumeration used              : FindAllAny");
            writer.WriteLine("    linked enumeration note              : FindAllAny routes through LoadAny and includes v1 and v2 links; FindAll was read above as a count cross-check.");
            writer.WriteLine("  report note: the active view below is read-only metadata; this command never assigns, requests, opens, or activates a view.");
        }

        private static void AppendActiveView(StreamWriter writer, UIDocument uidoc)
        {
            writer.WriteLine("  current active view");
            if (uidoc == null)
            {
                writer.WriteLine("    view : (no active UIDocument)");
                return;
            }

            bool viewOk;
            View view = Read(writer, "    view", () => uidoc.ActiveView,
                FormatPresent, out viewOk);
            if (!viewOk || view == null)
            {
                return;
            }

            bool ignored;
            Read(writer, "    name", () => view.Name, FormatString, out ignored);
            Read(writer, "    element id", () => view.Id, FormatElementId, out ignored);
            Read(writer, "    unique id", () => view.UniqueId, FormatString, out ignored);
            writer.WriteLine("    is schedule : " + (view is ViewSchedule ? "true" : "false"));
            if (view is ViewSchedule)
            {
                writer.WriteLine("    note         : the current active view is a schedule");
            }
        }

        private static void AppendSchedule(
            StreamWriter writer, Document doc, ViewSchedule schedule,
            ScheduleDiagnostics diagnostics, StyleIntegrityResult integrityResult)
        {
            AppendLinkSummary(writer, schedule);

            ScheduleDefinition definition;
            bool definitionOk;
            definition = Read(writer, "  schedule definition", () => schedule.Definition,
                FormatPresent, out definitionOk);
            if (definitionOk && definition != null)
            {
                AppendDefinition(writer, definition, diagnostics);
            }
            else
            {
                writer.WriteLine("  fields : (schedule definition unavailable)");
            }

            AppendTextTypes(writer, doc, schedule, diagnostics);
            AppendStyleIntegrity(writer, doc, schedule, integrityResult);

            // GetTableData refuses to run while the document is not modifiable
            // (a ReadOnly command fails with "Changes are disabled for the active
            // document"), so the table is read inside a transaction that is never
            // committed. Rollback is attempted in finally and the observed final
            // status is written to the report.
            writer.WriteLine("  TABLE DATA BEGIN");
            Transaction readScope = null;
            try
            {
                try
                {
                    readScope = new Transaction(doc, "ESL probe table read (rolled back)");
                    TransactionStatus started = readScope.Start();
                    writer.WriteLine("    read transaction : " + started);
                }
                catch (Exception ex)
                {
                    writer.WriteLine("    read transaction : " + Failure(ex));
                }

                TableData tableData;
                bool tableOk;
                tableData = Read(writer, "    GetTableData", () => schedule.GetTableData(),
                    FormatPresent, out tableOk);
                if (tableOk && tableData != null)
                {
                    AppendTableData(writer, doc, tableData, diagnostics);
                }
                else
                {
                    diagnostics.TableDataUnavailable = true;
                    writer.WriteLine("    table data : (unavailable)");
                }
            }
            finally
            {
                if (readScope != null)
                {
                    try
                    {
                        TransactionStatus? finalStatus =
                            TransactionRollback.RollBackIfStarted(readScope);
                        writer.WriteLine("    read transaction final status : "
                            + (finalStatus.HasValue ? finalStatus.Value.ToString() : "Unreadable"));
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine("    read transaction rollback : " + Failure(ex));
                    }
                    readScope.Dispose();
                }
            }
            writer.WriteLine("  TABLE DATA END");
        }

        /// <summary>
        /// The schedule's title/header/body text types are the style sources the
        /// grid falls back to. Title/header values are recorded as optional
        /// baselines; the decision table can use a complete healthy (0,0)
        /// reference when a text type is unavailable.
        /// </summary>
        private static void AppendTextTypes(
            StreamWriter writer, Document doc, ViewSchedule schedule,
            ScheduleDiagnostics diagnostics)
        {
            writer.WriteLine("  TEXT TYPES");
            AppendTextType(writer, doc, schedule, "TitleTextTypeId", () => schedule.TitleTextTypeId, diagnostics);
            AppendTextType(writer, doc, schedule, "HeaderTextTypeId", () => schedule.HeaderTextTypeId, diagnostics);
            AppendTextType(writer, doc, schedule, "BodyTextTypeId", () => schedule.BodyTextTypeId, diagnostics);
        }

        private static void AppendStyleIntegrity(
            StreamWriter writer, Document doc, ViewSchedule schedule,
            StyleIntegrityResult result)
        {
            writer.WriteLine("  STYLE INTEGRITY");
            if (result == null)
            {
                writer.WriteLine("    scan state : INDETERMINATE");
                writer.WriteLine("    reason code : InvalidFacts");
                return;
            }

            string state = result.ReasonCode == StyleIntegrityReasonCode.ScanLimit
                ? "NOT_CHECKED_LIMIT"
                : result.State.ToString().ToUpperInvariant();
            writer.WriteLine("    scan state : " + state);
            writer.WriteLine("    reason code : " + result.ReasonCode);
            writer.WriteLine("    header cells expected : "
                + result.HeaderCellsExpected.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("    header cells read : "
                + result.HeaderCellsRead.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("    styleless cells : "
                + result.StylelessCellCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("    skipped schedules : "
                + result.SkippedScheduleCount.ToString(CultureInfo.InvariantCulture));

            ScheduleStyleFacts facts = result.Facts;
            AppendTextStyleEvidence(writer, "title", facts == null ? null : facts.TitleTextStyle);
            AppendTextStyleEvidence(writer, "header", facts == null ? null : facts.HeaderTextStyle);
            CellStyleFacts reference = facts == null ? null : facts.ReferenceCell;
            writer.WriteLine("    reference cell : " + (reference == null
                ? "unavailable"
                : "row=" + reference.Row.ToString(CultureInfo.InvariantCulture)
                    + " col=" + reference.Column.ToString(CultureInfo.InvariantCulture)));
            writer.WriteLine("    reference font : "
                + (reference == null ? "unavailable" : FormatString(reference.FontName)));
            writer.WriteLine("    reference TableCellStyle.TextSize raw : "
                + (reference == null || !reference.ReadComplete
                    ? "unavailable" : FormatDouble(reference.TextSize)));
            writer.WriteLine("    reference TableCellStyle.TextSize table units (1/96 inch) : "
                + (reference == null || !reference.ReadComplete
                    ? "unavailable" : FormatDouble(reference.TextSize)));
            writer.WriteLine("    schedule unique id : " + FormatString(schedule.UniqueId));
            AppendSchedulePlacements(writer, doc, schedule);

            StyleIntegrityCoordinate first = result.StylelessCells != null
                && result.StylelessCells.Count > 0 ? result.StylelessCells[0] : null;
            writer.WriteLine("    first styleless cell : " + (first == null
                ? "none" : first.ToString()));
            writer.WriteLine("    first read error : "
                + (string.IsNullOrEmpty(result.FirstReadError)
                    ? "none" : result.FirstReadError));
            writer.WriteLine("    read transaction start : "
                + (string.IsNullOrEmpty(result.ReadTransactionStartStatus)
                    ? "not started" : result.ReadTransactionStartStatus));
            writer.WriteLine("    read transaction rollback : "
                + (string.IsNullOrEmpty(result.ReadTransactionRollbackStatus)
                    ? "not attempted" : result.ReadTransactionRollbackStatus));
            if (!string.IsNullOrEmpty(result.ReadTransactionRollbackError))
            {
                writer.WriteLine("    read transaction rollback detail : "
                    + result.ReadTransactionRollbackError);
            }
            writer.WriteLine("    document IsModified before scan : "
                + FormatNullableBoolean(result.DocumentModifiedBeforeScan));
            writer.WriteLine("    document IsModified after rollback : "
                + FormatNullableBoolean(result.DocumentModifiedAfterRollback));
            writer.WriteLine("    checkout status/owner before scan : not applicable (no checkout requested)");
            writer.WriteLine("    checkout status/owner after rollback : not applicable (no checkout requested)");

            if (facts != null && facts.Cells != null)
            {
                TextStyleFacts referenceBaseline =
                    StyleIntegrityDecisionTable.ReferenceBaseline(
                        reference, facts.TitleTextStyle, facts.HeaderTextStyle,
                        StyleIntegrityDecisionTable.DefaultInvalidElementId);
                foreach (CellStyleFacts cell in facts.Cells)
                {
                    if (cell == null || !cell.HasAllInvalidBorders(
                        StyleIntegrityDecisionTable.DefaultInvalidElementId))
                    {
                        continue;
                    }
                    StyleIntegrityDecisionTable.TextPairMatchKind titleMatch =
                        StyleIntegrityDecisionTable.ClassifyTextPair(
                            cell.FontName, cell.TextSize, facts.TitleTextStyle);
                    StyleIntegrityDecisionTable.TextPairMatchKind headerMatch =
                        StyleIntegrityDecisionTable.ClassifyTextPair(
                            cell.FontName, cell.TextSize, facts.HeaderTextStyle);
                    StyleIntegrityDecisionTable.TextPairMatchKind referenceMatch =
                        StyleIntegrityDecisionTable.ClassifyTextPair(
                            cell.FontName, cell.TextSize, referenceBaseline);
                    writer.WriteLine("    candidate row="
                        + cell.Row.ToString(CultureInfo.InvariantCulture)
                        + " col=" + cell.Column.ToString(CultureInfo.InvariantCulture)
                        + " all_borders_invalid=true"
                        + " text_size_table_units=" + FormatDouble(cell.TextSize)
                        + " cell_override=" + FormatBoolean(cell.IsCellOverridden)
                        + " style_override=" + FormatBoolean(cell.IsStyleOverridden)
                        + " any_override_option=" + FormatBoolean(cell.HasAnyOverrideOption)
                        + " match_title=" + titleMatch
                        + " match_header=" + headerMatch
                        + " match_reference=" + referenceMatch);
                }
            }
        }

        private static StyleIntegrityResult IntegrityResultAt(
            IList<StyleIntegrityResult> results, int index, int scheduleCount)
        {
            if (results != null && index >= 0 && index < results.Count
                && results[index] != null)
            {
                if (results[index].ReasonCode == StyleIntegrityReasonCode.ScanLimit
                    && results[index].SkippedScheduleCount > 0)
                {
                    // The batch result represents the whole remaining suffix;
                    // emit one stable NOT_CHECKED_LIMIT record per schedule so
                    // the footer does not count that suffix repeatedly.
                    return StyleIntegrityDecisionTable.LimitResult(1);
                }
                return results[index];
            }
            return StyleIntegrityDecisionTable.LimitResult(
                Math.Max(1, scheduleCount - Math.Max(0, index)));
        }

        private static void AppendIntegrityFooter(
            StreamWriter writer, TableStateContext context)
        {
            writer.WriteLine();
            writer.WriteLine("STYLE INTEGRITY FOOTER");
            writer.WriteLine("  healthy schedules : "
                + context.IntegrityHealthy.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  affected schedules : "
                + context.IntegrityAffected.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  indeterminate schedules : "
                + context.IntegrityIndeterminate.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  not checked by limit : "
                + context.IntegrityNotCheckedLimit.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  header cells expected : "
                + context.IntegrityHeaderCellsExpected.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  header cells read : "
                + context.IntegrityHeaderCellsRead.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  styleless cells : "
                + context.IntegrityStylelessCells.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  max cells per schedule : "
                + StyleIntegrityLimits.MaxHeaderCellsPerSchedule.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  manual max schedules : "
                + StyleIntegrityLimits.MaxManualSchedules.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  automatic max schedules : "
                + StyleIntegrityLimits.MaxAutomaticSchedules.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("  automatic max header cells : "
                + StyleIntegrityLimits.MaxAutomaticHeaderCells.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendSchedulePlacements(
            StreamWriter writer, Document doc, ViewSchedule schedule)
        {
            writer.WriteLine("    placements :");
            try
            {
                List<ScheduleSheetInstance> placements = new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .Where(instance => instance.ScheduleId == schedule.Id)
                    .OrderBy(instance => instance.Id.Value)
                    .ToList();
                if (placements.Count == 0)
                {
                    writer.WriteLine("      none");
                    return;
                }

                foreach (ScheduleSheetInstance instance in placements)
                {
                    ElementId ownerViewId = instance.OwnerViewId;
                    ViewSheet ownerSheet = doc.GetElement(ownerViewId) as ViewSheet;
                    string width = "unavailable";
                    try
                    {
                        BoundingBoxXYZ box = instance.get_BoundingBox(ownerSheet);
                        if (box != null)
                        {
                            width = FormatDouble(box.Max.X - box.Min.X);
                        }
                    }
                    catch (Exception ex)
                    {
                        width = "error: " + Failure(ex);
                    }

                    writer.WriteLine("      placement id : " + FormatElementId(instance.Id));
                    writer.WriteLine("        owner sheet id : " + FormatElementId(ownerViewId));
                    writer.WriteLine("        owner sheet name : "
                        + (ownerSheet == null ? "unavailable" : FormatString(ownerSheet.Name)));
                    writer.WriteLine("        bounding-box width (feet) : " + width);
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine("      error : " + Failure(ex));
            }
        }

        private static void AppendTextStyleEvidence(
            StreamWriter writer, string label, TextStyleFacts style)
        {
            writer.WriteLine("    " + label + " font : "
                + (style == null ? "unavailable" : FormatString(style.FontName)));
            writer.WriteLine("    " + label + " size raw : "
                + FormatMeasuredDouble(style == null ? double.NaN : style.RawSize));
            writer.WriteLine("    " + label + " size inches : "
                + FormatMeasuredDouble(style == null ? double.NaN : style.SizeInches));
            writer.WriteLine("    " + label + " size points : "
                + FormatMeasuredDouble(style == null ? double.NaN : style.SizePoints));
            writer.WriteLine("    " + label + " size table units (1/96 inch) : "
                + FormatMeasuredDouble(style == null ? double.NaN : style.SizeTableUnits));
            writer.WriteLine("    " + label + " read state : "
                + FormatTextStyleReadState(style));
        }

        private static string FormatTextStyleReadState(TextStyleFacts style)
        {
            if (style == null)
            {
                return "unavailable";
            }
            if (style.IsMissing)
            {
                return "missing-or-not-TextNoteType";
            }
            if (style.AccessDenied)
            {
                return "access-denied";
            }
            if (style.ReadAttempted && style.ReadFailed)
            {
                return "read-failed";
            }
            return style.IsResolvable ? "resolved" : "unresolved";
        }

        private static string FormatMeasuredDouble(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "unavailable" : FormatDouble(value);
        }

        private static void AppendTextType(
            StreamWriter writer, Document doc, ViewSchedule schedule, string label,
            Func<ElementId> getter, ScheduleDiagnostics diagnostics)
        {
            bool ok;
            ElementId id = Read(writer, "    " + label, getter, FormatElementId, out ok);
            if (!ok || id == null)
            {
                diagnostics.UnresolvedTextTypeCount++;
                return;
            }
            bool validOk;
            bool validTextTypeId = Read(writer, "      IsValidTextTypeId",
                () => schedule.IsValidTextTypeId(id), FormatBoolean, out validOk);
            if (validOk && !validTextTypeId)
            {
                diagnostics.InvalidTextTypeIdCount++;
            }
            if (id == ElementId.InvalidElementId)
            {
                return;
            }
            try
            {
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    diagnostics.UnresolvedTextTypeCount++;
                    writer.WriteLine("      element : (not found)");
                }
                else
                {
                    writer.WriteLine("      element : " + element.GetType().Name + " \""
                        + QuotePart(element.Name) + "\"");
                }
            }
            catch (Exception ex)
            {
                diagnostics.UnresolvedTextTypeCount++;
                writer.WriteLine("      element : " + Failure(ex));
            }
        }

        private static void AppendLinkSummary(StreamWriter writer, ViewSchedule schedule)
        {
            writer.WriteLine("  LINK SUMMARY");
            ExcelLinkStorage storage;
            bool storageOk;
            storage = Read(writer, "    storage", () => ExcelLinkSchema.LoadAny(schedule),
                FormatPresent, out storageOk);
            if (!storageOk || storage == null || storage.Info == null)
            {
                writer.WriteLine("    link summary : (unavailable)");
                return;
            }

            ExcelLinkInfo info = storage.Info;
            writer.WriteLine("    schema guid     : " + storage.SchemaGuid);
            writer.WriteLine("    schema version  : "
                + info.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                + " / renderer " + info.RendererVersion.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("    schema storage  : " + (storage.IsV2 ? "v2" : "v1"));
            writer.WriteLine("    worksheet       : " + FormatString(info.SheetName));
            writer.WriteLine("    range           : "
                + (info.RangeMode == RangeMode.Explicit
                    ? FormatString(info.RangeAddress) : "(used range)"));
            writer.WriteLine("    width scale     : "
                + info.WidthScale.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteLine("    column width mode: " + info.ColumnWidthMode);
            writer.WriteLine("    row height mode : " + info.RowHeightMode);
            writer.WriteLine("    last loaded     : " + FormatTicks(info.LastLoadedUtcTicks));
        }

        private static void AppendDefinition(
            StreamWriter writer, ScheduleDefinition definition,
            ScheduleDiagnostics diagnostics)
        {
            writer.WriteLine("  SCHEDULE DEFINITION");
            bool ignored;
            Read(writer, "    IsValidObject", () => definition.IsValidObject,
                FormatBoolean, out ignored);
            AppendDefinitionFlag(writer, definition, "ShowTitle", () => definition.ShowTitle);
            AppendDefinitionFlag(writer, definition, "ShowHeaders", () => definition.ShowHeaders);
            AppendDefinitionFlag(writer, definition, "ShowGridLines", () => definition.ShowGridLines);
            AppendDefinitionFlag(writer, definition, "IsItemized", () => definition.IsItemized);
            AppendDefinitionFlag(writer, definition, "IsFilteredBySheet", () => definition.IsFilteredBySheet);
            AppendDefinitionFlag(writer, definition, "ShowGrandTotal", () => definition.ShowGrandTotal);
            AppendDefinitionFlag(writer, definition, "ShowGrandTotalCount", () => definition.ShowGrandTotalCount);
            AppendDefinitionFlag(writer, definition, "ShowGrandTotalTitle", () => definition.ShowGrandTotalTitle);
            AppendDefinitionFlag(writer, definition, "IsEmbedded", () => definition.IsEmbedded);
            AppendDefinitionFlag(writer, definition, "HasEmbeddedSchedule", () => definition.HasEmbeddedSchedule);
            AppendDefinitionFlag(writer, definition, "IncludeLinkedFiles", () => definition.IncludeLinkedFiles);
            AppendDefinitionFlag(writer, definition, "IsMaterialTakeoff", () => definition.IsMaterialTakeoff);
            AppendDefinitionFlag(writer, definition, "IsKeySchedule", () => definition.IsKeySchedule);
            Read(writer, "    GrandTotalTitle", () => definition.GrandTotalTitle,
                FormatString, out ignored);

            int fieldCount;
            bool countOk;
            fieldCount = Read(writer, "    GetFieldCount", () => definition.GetFieldCount(),
                FormatInteger, out countOk);
            if (!countOk || fieldCount < 0)
            {
                writer.WriteLine("    fields : (field count unavailable)");
                return;
            }

            for (int index = 0; index < fieldCount; index++)
            {
                writer.WriteLine("    FIELD [" + index.ToString(CultureInfo.InvariantCulture) + "]");
                int fieldIndex = index;
                bool fieldOk;
                ScheduleField field = Read(writer, "      field", () => definition.GetField(fieldIndex),
                    FormatPresent, out fieldOk);
                if (!fieldOk || field == null)
                {
                    continue;
                }

                bool ignoredField;
                Read(writer, "      FieldId", () => field.FieldId,
                    FormatScheduleFieldId, out ignoredField);
                Read(writer, "      FieldIndex", () => field.FieldIndex,
                    FormatInteger, out ignoredField);
                Read(writer, "      ColumnHeading", () => field.ColumnHeading,
                    FormatString, out ignoredField);
                bool hiddenOk;
                bool hidden = Read(writer, "      IsHidden", () => field.IsHidden,
                    FormatBoolean, out hiddenOk);
                bool gridOk;
                double gridWidth = Read(writer, "      GridColumnWidth",
                    () => field.GridColumnWidth, FormatDouble, out gridOk);
                CheckDimension(diagnostics, "field GridColumnWidth", gridWidth, gridOk);
                bool sheetOk;
                double sheetWidth = Read(writer, "      SheetColumnWidth",
                    () => field.SheetColumnWidth, FormatDouble, out sheetOk);
                CheckDimension(diagnostics, "field SheetColumnWidth", sheetWidth, sheetOk);
                Read(writer, "      FieldType", () => field.FieldType,
                    v => FormatObject(v), out ignoredField);
                Read(writer, "      HorizontalAlignment", () => field.HorizontalAlignment,
                    v => FormatObject(v), out ignoredField);
                Read(writer, "      VerticalAlignment", () => field.VerticalAlignment,
                    v => FormatObject(v), out ignoredField);
                Read(writer, "      DisplayType", () => field.DisplayType,
                    v => FormatObject(v), out ignoredField);
                Read(writer, "      IsCalculatedField", () => field.IsCalculatedField,
                    FormatBoolean, out ignoredField);
                Read(writer, "      IsCombinedParameterField", () => field.IsCombinedParameterField,
                    FormatBoolean, out ignoredField);
                Read(writer, "      IsOverridden", () => field.IsOverridden,
                    FormatBoolean, out ignoredField);

                if (hiddenOk && gridOk && !hidden)
                {
                    diagnostics.BodyVisibleGridWidthTotal += gridWidth;
                    diagnostics.HasBodyVisibleGridWidth = true;
                }
            }
        }

        private static void AppendDefinitionFlag(
            StreamWriter writer, ScheduleDefinition definition, string name, Func<bool> read)
        {
            bool ignored;
            Read(writer, "    " + name, read, FormatBoolean, out ignored);
        }

        private static void AppendTableData(
            StreamWriter writer, Document doc, TableData tableData,
            ScheduleDiagnostics diagnostics)
        {
            bool ignored;
            Read(writer, "    NumberOfSections", () => tableData.NumberOfSections,
                FormatInteger, out ignored);
            Read(writer, "    Width", () => tableData.Width,
                FormatDouble, out ignored);
            Read(writer, "    WidthInPixels", () => tableData.WidthInPixels,
                FormatInteger, out ignored);
            Read(writer, "    FreezeColumnsAndRows", () => tableData.FreezeColumnsAndRows,
                FormatBoolean, out ignored);
            Read(writer, "    ZoomLevel", () => tableData.ZoomLevel,
                FormatInteger, out ignored);

            IEnumerable<SectionType> sectionTypes = Enum.GetValues(typeof(SectionType))
                .Cast<SectionType>()
                .OrderBy(SectionOrder);
            foreach (SectionType sectionType in sectionTypes)
            {
                writer.WriteLine("    BEGIN SECTION " + sectionType);
                try
                {
                    bool sectionOk;
                    TableSectionData section = Read(writer,
                        "      GetSectionData(" + sectionType + ")",
                        () => tableData.GetSectionData(sectionType),
                        FormatPresent, out sectionOk);
                    if (sectionOk && section != null)
                    {
                        AppendSection(writer, doc, sectionType, section, diagnostics);
                    }
                    else
                    {
                        writer.WriteLine("      section : (null or unavailable)");
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.UnexpectedFailures++;
                    writer.WriteLine("      section dump : " + Failure(ex));
                }
                finally
                {
                    writer.WriteLine("    END SECTION " + sectionType);
                }
            }
        }

        private static void AppendSection(
            StreamWriter writer, Document doc, SectionType sectionType,
            TableSectionData section, ScheduleDiagnostics diagnostics)
        {
            SectionObservation observation = new SectionObservation(sectionType);
            diagnostics.Sections.Add(observation);
            bool rowCountOk;
            bool columnCountOk;
            bool firstRowOk;
            bool lastRowOk;
            bool firstColumnOk;
            bool lastColumnOk;
            observation.NumberOfRows = Read(writer, "      NumberOfRows",
                () => section.NumberOfRows, FormatInteger, out rowCountOk);
            observation.NumberOfColumns = Read(writer, "      NumberOfColumns",
                () => section.NumberOfColumns, FormatInteger, out columnCountOk);
            observation.FirstRow = Read(writer, "      FirstRowNumber",
                () => section.FirstRowNumber, FormatInteger, out firstRowOk);
            observation.LastRow = Read(writer, "      LastRowNumber",
                () => section.LastRowNumber, FormatInteger, out lastRowOk);
            observation.FirstColumn = Read(writer, "      FirstColumnNumber",
                () => section.FirstColumnNumber, FormatInteger, out firstColumnOk);
            observation.LastColumn = Read(writer, "      LastColumnNumber",
                () => section.LastColumnNumber, FormatInteger, out lastColumnOk);
            observation.BoundsComplete = rowCountOk && columnCountOk && firstRowOk
                && lastRowOk && firstColumnOk && lastColumnOk;
            bool ignored;
            Read(writer, "      HideSection", () => section.HideSection,
                FormatBoolean, out ignored);
            Read(writer, "      NeedsRefresh", () => section.NeedsRefresh,
                FormatBoolean, out ignored);
            writer.WriteLine("      section style getter : not exposed by Revit 2024 RevitAPI.xml; no table-style mutator is called");

            if (!observation.BoundsComplete)
            {
                writer.WriteLine("      dimensions : (incomplete; column/row progress skipped)");
                return;
            }

            AppendColumnWidths(writer, section, observation, diagnostics);
            AppendRowHeights(writer, section, observation, diagnostics);

            if (sectionType == SectionType.Header || sectionType == SectionType.Body)
            {
                AppendCells(writer, doc, section, observation, diagnostics);
            }
        }

        private static void AppendColumnWidths(
            StreamWriter writer, TableSectionData section, SectionObservation observation,
            ScheduleDiagnostics diagnostics)
        {
            long count = InclusiveCount(observation.FirstColumn, observation.LastColumn);
            for (long offset = 0; offset < count; offset++)
            {
                int column = checked((int)(observation.FirstColumn + offset));
                writer.WriteLine("      COLUMN progress col="
                    + column.ToString(CultureInfo.InvariantCulture));
                bool widthOk;
                double width = Read(writer, "        GetColumnWidth",
                    () => section.GetColumnWidth(column), FormatDouble, out widthOk);
                CheckDimension(diagnostics, "column width " + column, width, widthOk);
                if (widthOk && observation.SectionType == SectionType.Header)
                {
                    diagnostics.HeaderColumnWidthTotal += width;
                    diagnostics.HasHeaderColumnWidth = true;
                }
                bool pixelsOk;
                int pixels = Read(writer, "        GetColumnWidthInPixels",
                    () => section.GetColumnWidthInPixels(column), FormatInteger, out pixelsOk);
                CheckDimension(diagnostics, "column width pixels " + column, pixels, pixelsOk);
            }
        }

        private static void AppendRowHeights(
            StreamWriter writer, TableSectionData section, SectionObservation observation,
            ScheduleDiagnostics diagnostics)
        {
            long count = InclusiveCount(observation.FirstRow, observation.LastRow);
            for (long offset = 0; offset < count; offset++)
            {
                int row = checked((int)(observation.FirstRow + offset));
                writer.WriteLine("      ROW progress row="
                    + row.ToString(CultureInfo.InvariantCulture));
                bool heightOk;
                double height = Read(writer, "        GetRowHeight",
                    () => section.GetRowHeight(row), FormatDouble, out heightOk);
                CheckDimension(diagnostics, "row height " + row, height, heightOk);
                bool pixelsOk;
                int pixels = Read(writer, "        GetRowHeightInPixels",
                    () => section.GetRowHeightInPixels(row), FormatInteger, out pixelsOk);
                CheckDimension(diagnostics, "row height pixels " + row, pixels, pixelsOk);
            }
        }

        private static void AppendCells(
            StreamWriter writer, Document doc, TableSectionData section,
            SectionObservation observation, ScheduleDiagnostics diagnostics)
        {
            long rowCount = InclusiveCount(observation.FirstRow, observation.LastRow);
            long columnCount = InclusiveCount(observation.FirstColumn, observation.LastColumn);
            long rowsToRead = observation.SectionType == SectionType.Body
                ? Math.Min(5L, rowCount) : rowCount;
            long totalCells = rowCount * columnCount;
            long cellsToRead = rowsToRead * columnCount;
            bool boundedHeaderDump = observation.SectionType == SectionType.Header
                && cellsToRead > StyleIntegrityLimits.MaxHeaderCellsPerSchedule;
            if (boundedHeaderDump)
            {
                cellsToRead = StyleIntegrityLimits.MaxHeaderCellsPerSchedule;
            }
            long cellsRead = 0;
            for (long rowOffset = 0; rowOffset < rowsToRead && cellsRead < cellsToRead; rowOffset++)
            {
                int row = checked((int)(observation.FirstRow + rowOffset));
                for (long columnOffset = 0;
                    columnOffset < columnCount && cellsRead < cellsToRead;
                    columnOffset++)
                {
                    int column = checked((int)(observation.FirstColumn + columnOffset));
                    writer.WriteLine("      CELL progress section=" + observation.SectionType
                        + " row=" + row.ToString(CultureInfo.InvariantCulture)
                        + " col=" + column.ToString(CultureInfo.InvariantCulture));
                    AppendCell(writer, doc, section, observation, row, column, diagnostics);
                    cellsRead++;
                }
            }
            if (observation.SectionType == SectionType.Header)
            {
                diagnostics.HeaderCellDumped += cellsRead;
                diagnostics.HeaderCellDumpSkipped += Math.Max(0L, totalCells - cellsRead);
                writer.WriteLine("      detailed header cell dump complete : "
                    + FormatBoolean(totalCells <= StyleIntegrityLimits.MaxHeaderCellsPerSchedule));
                writer.WriteLine("      detailed header cells dumped : "
                    + cellsRead.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("      detailed header cells skipped : "
                    + Math.Max(0L, totalCells - cellsRead).ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendCell(
            StreamWriter writer, Document doc, TableSectionData section,
            SectionObservation observation, int row, int column,
            ScheduleDiagnostics diagnostics)
        {
            diagnostics.CellsRead++;
            bool ignored;
            bool typeOk;
            Read(writer, "        GetCellType", () => section.GetCellType(row, column),
                v => FormatObject(v), out typeOk);
            if (!typeOk)
            {
                diagnostics.CellTypeFailures++;
            }
            bool textOk;
            Read(writer, "        GetCellText", () => section.GetCellText(row, column),
                FormatCellText, out textOk);
            bool overriddenOk;
            Read(writer, "        IsCellOverridden",
                () => section.IsCellOverridden(row, column), FormatBoolean, out overriddenOk);
            bool allowOk;
            Read(writer, "        AllowOverrideCellStyle",
                () => section.AllowOverrideCellStyle(row, column), FormatBoolean, out allowOk);

            bool mergeCallOk;
            TableMergedCell merged = Read(writer, "        GetMergedCell",
                () => section.GetMergedCell(row, column), FormatPresent, out mergeCallOk);
            if (!mergeCallOk)
            {
                diagnostics.MergeReadFailures++;
            }
            if (merged != null)
            {
                bool topOk;
                bool leftOk;
                bool bottomOk;
                bool rightOk;
                int top = Read(writer, "          merged top", () => merged.Top,
                    FormatInteger, out topOk);
                int left = Read(writer, "          merged left", () => merged.Left,
                    FormatInteger, out leftOk);
                int bottom = Read(writer, "          merged bottom", () => merged.Bottom,
                    FormatInteger, out bottomOk);
                int right = Read(writer, "          merged right", () => merged.Right,
                    FormatInteger, out rightOk);
                if (!topOk || !leftOk || !bottomOk || !rightOk)
                {
                    diagnostics.MergeReadFailures++;
                }
                else
                {
                    MergeRegion region = new MergeRegion(
                        observation.SectionType, top, left, bottom, right);
                    if (!region.Contains(row, column))
                    {
                        diagnostics.MergeContainmentFailures++;
                        diagnostics.FirstMergeContainmentFailure =
                            "section=" + observation.SectionType + " cell=" + row + "," + column
                            + " region=" + region;
                    }
                    if (region.IsReal && !observation.Regions.Any(existing => existing.SameAs(region)))
                    {
                        observation.Regions.Add(region);
                    }
                }
            }

            bool styleOk;
            TableCellStyle style = Read(writer, "        GetTableCellStyle",
                () => section.GetTableCellStyle(row, column), FormatPresent, out styleOk);
            if (!styleOk)
            {
                diagnostics.CellStyleFailures++;
            }
            if (style != null)
            {
                AppendStyle(writer, doc, style, diagnostics);
            }

            Read(writer, "        GetCellCategoryId",
                () => section.GetCellCategoryId(row, column), FormatElementId, out ignored);
            Read(writer, "        GetCellParamId",
                () => section.GetCellParamId(row, column), FormatElementId, out ignored);
            Read(writer, "        GetCellFormatOptions",
                () => section.GetCellFormatOptions(row, column, doc), FormatPresent, out ignored);
        }

        private static void AppendStyle(
            StreamWriter writer, Document doc, TableCellStyle style,
            ScheduleDiagnostics diagnostics)
        {
            bool ignored;
            ReadStyle(writer, "          FontName", () => style.FontName,
                FormatString, diagnostics, out ignored);
            ReadStyle(writer, "          TextSize", () => style.TextSize,
                FormatDouble, diagnostics, out ignored);
            ReadStyle(writer, "          IsFontBold", () => style.IsFontBold,
                FormatBoolean, diagnostics, out ignored);
            ReadStyle(writer, "          IsFontItalic", () => style.IsFontItalic,
                FormatBoolean, diagnostics, out ignored);
            ReadStyle(writer, "          IsFontUnderline", () => style.IsFontUnderline,
                FormatBoolean, diagnostics, out ignored);
            ReadStyle(writer, "          TextColor", () => style.TextColor,
                FormatColor, diagnostics, out ignored);
            ReadStyle(writer, "          BackgroundColor", () => style.BackgroundColor,
                FormatColor, diagnostics, out ignored);
            ReadStyle(writer, "          SheetBackgroundColor", () => style.SheetBackgroundColor,
                FormatColor, diagnostics, out ignored);
            ReadStyle(writer, "          FontHorizontalAlignment", () => style.FontHorizontalAlignment,
                v => FormatObject(v), diagnostics, out ignored);
            ReadStyle(writer, "          FontVerticalAlignment", () => style.FontVerticalAlignment,
                v => FormatObject(v), diagnostics, out ignored);
            ReadStyle(writer, "          TextOrientation", () => style.TextOrientation,
                FormatInteger, diagnostics, out ignored);
            ReadStyle(writer, "          IsOverridden", () => style.IsOverridden,
                FormatBoolean, diagnostics, out ignored);
            ElementId top = ReadStyle(writer, "          BorderTopLineStyle",
                () => style.BorderTopLineStyle, FormatElementId, diagnostics, out ignored);
            CheckBorder(writer, doc, "top", top, diagnostics);
            ElementId bottom = ReadStyle(writer, "          BorderBottomLineStyle",
                () => style.BorderBottomLineStyle, FormatElementId, diagnostics, out ignored);
            CheckBorder(writer, doc, "bottom", bottom, diagnostics);
            ElementId left = ReadStyle(writer, "          BorderLeftLineStyle",
                () => style.BorderLeftLineStyle, FormatElementId, diagnostics, out ignored);
            CheckBorder(writer, doc, "left", left, diagnostics);
            ElementId right = ReadStyle(writer, "          BorderRightLineStyle",
                () => style.BorderRightLineStyle, FormatElementId, diagnostics, out ignored);
            CheckBorder(writer, doc, "right", right, diagnostics);

            bool optionsOk;
            TableCellStyleOverrideOptions options = ReadStyle(writer,
                "          GetCellStyleOverrideOptions",
                () => style.GetCellStyleOverrideOptions(), FormatPresent,
                diagnostics, out optionsOk);
            if (options == null)
            {
                return;
            }

            ReadOverrideFlag(writer, options, "IsValidObject", () => options.IsValidObject, diagnostics);
            ReadOverrideFlag(writer, options, "Font", () => options.Font, diagnostics);
            ReadOverrideFlag(writer, options, "FontSize", () => options.FontSize, diagnostics);
            ReadOverrideFlag(writer, options, "FontColor", () => options.FontColor, diagnostics);
            ReadOverrideFlag(writer, options, "BackgroundColor", () => options.BackgroundColor, diagnostics);
            ReadOverrideFlag(writer, options, "Bold", () => options.Bold, diagnostics);
            ReadOverrideFlag(writer, options, "Italics", () => options.Italics, diagnostics);
            ReadOverrideFlag(writer, options, "Underline", () => options.Underline, diagnostics);
            ReadOverrideFlag(writer, options, "HorizontalAlignment", () => options.HorizontalAlignment, diagnostics);
            ReadOverrideFlag(writer, options, "VerticalAlignment", () => options.VerticalAlignment, diagnostics);
            ReadOverrideFlag(writer, options, "TextOrientation", () => options.TextOrientation, diagnostics);
            ReadOverrideFlag(writer, options, "BorderLineStyle", () => options.BorderLineStyle, diagnostics);
            ReadOverrideFlag(writer, options, "BorderTopLineStyle", () => options.BorderTopLineStyle, diagnostics);
            ReadOverrideFlag(writer, options, "BorderBottomLineStyle", () => options.BorderBottomLineStyle, diagnostics);
            ReadOverrideFlag(writer, options, "BorderLeftLineStyle", () => options.BorderLeftLineStyle, diagnostics);
            ReadOverrideFlag(writer, options, "BorderRightLineStyle", () => options.BorderRightLineStyle, diagnostics);
        }

        private static T ReadStyle<T>(
            StreamWriter writer, string label, Func<T> read, Func<T, string> format,
            ScheduleDiagnostics diagnostics, out bool succeeded)
        {
            T value = Read(writer, label, read, format, out succeeded);
            if (!succeeded)
            {
                diagnostics.StylePropertyFailures++;
            }
            return value;
        }

        private static void ReadOverrideFlag(
            StreamWriter writer, TableCellStyleOverrideOptions options, string name,
            Func<bool> read, ScheduleDiagnostics diagnostics)
        {
            bool ignored;
            ReadStyle(writer, "            override." + name, read,
                FormatBoolean, diagnostics, out ignored);
        }

        private static void CheckBorder(
            StreamWriter writer, Document doc, string side, ElementId id,
            ScheduleDiagnostics diagnostics)
        {
            bool invalid = false;
            try
            {
                invalid = id != null && id == ElementId.InvalidElementId;
                writer.WriteLine("            border " + side + " invalidElementId : " +
                    invalid.ToString().ToLowerInvariant());
            }
            catch (Exception ex)
            {
                diagnostics.UnresolvedBorderCount++;
                writer.WriteLine("            border " + side + " invalidElementId : " + Failure(ex));
                return;
            }

            if (invalid)
            {
                return;
            }
            if (id == null)
            {
                diagnostics.UnresolvedBorderCount++;
                writer.WriteLine("            border " + side + " element : (null id; unresolved)");
                return;
            }

            Element element;
            try
            {
                element = doc.GetElement(id);
                writer.WriteLine("            border " + side + " element : "
                    + (element == null ? "(not found)" : "found"));
            }
            catch (Exception ex)
            {
                diagnostics.UnresolvedBorderCount++;
                writer.WriteLine("            border " + side + " element : " + Failure(ex));
                return;
            }
            if (element == null)
            {
                diagnostics.UnresolvedBorderCount++;
                return;
            }

            bool ignored;
            Read(writer, "              border " + side + " type",
                () => element.GetType().FullName, FormatString, out ignored);
            Read(writer, "              border " + side + " name",
                () => element.Name, FormatString, out ignored);
            Read(writer, "              border " + side + " category",
                () => element.Category == null ? null : element.Category.Name,
                FormatString, out ignored);
        }

        private static void AppendConsistencyCheck(
            StreamWriter writer, ScheduleDiagnostics diagnostics)
        {
            writer.WriteLine("  CONSISTENCY SUMMARY");
            writer.WriteLine("    MERGED REGIONS (distinct regions returned by GetMergedCell)");
            int mergedRegionCount = 0;
            foreach (SectionObservation section in diagnostics.Sections)
            {
                foreach (MergeRegion region in section.Regions)
                {
                    mergedRegionCount++;
                    writer.WriteLine("      section=" + section.SectionType + " region=" + region);
                }
            }
            if (mergedRegionCount == 0)
            {
                writer.WriteLine("      (none)");
            }
            SectionObservation header = diagnostics.Sections
                .FirstOrDefault(section => section.SectionType == SectionType.Header);
            long distinctMergedColumns = DistinctHeaderMergedColumns(diagnostics);
            writer.WriteLine("    header section column count : "
                + (header == null ? "unknown" : header.NumberOfColumns.ToString(CultureInfo.InvariantCulture)));
            writer.WriteLine("    distinct columns covered by header merges : "
                + distinctMergedColumns.ToString(CultureInfo.InvariantCulture));

            string headerTotal = diagnostics.HasHeaderColumnWidth
                ? FormatDouble(diagnostics.HeaderColumnWidthTotal) : "unknown";
            string bodyTotal = diagnostics.HasBodyVisibleGridWidth
                ? FormatDouble(diagnostics.BodyVisibleGridWidthTotal) : "unknown";
            writer.WriteLine("    header total column width : " + headerTotal);
            writer.WriteLine("    body visible GridColumnWidth total : " + bodyTotal);
            if (diagnostics.HasHeaderColumnWidth && diagnostics.HasBodyVisibleGridWidth)
            {
                double difference = diagnostics.HeaderColumnWidthTotal
                    - diagnostics.BodyVisibleGridWidthTotal;
                writer.WriteLine("    header minus body width difference : " + FormatDouble(difference));
                if (diagnostics.HeaderColumnWidthTotal > diagnostics.BodyVisibleGridWidthTotal)
                {
                    diagnostics.HeaderWider = true;
                    diagnostics.HeaderWiderDifference = difference;
                }
            }
            else
            {
                writer.WriteLine("    header minus body width difference : unknown");
            }

            List<string> issues = new List<string>();
            int outOfBounds = 0;
            int overlaps = 0;
            string firstOutOfBounds = null;
            string firstOverlap = null;
            foreach (SectionObservation section in diagnostics.Sections)
            {
                foreach (MergeRegion region in section.Regions)
                {
                    if (!section.Contains(region))
                    {
                        outOfBounds++;
                        if (firstOutOfBounds == null)
                        {
                            firstOutOfBounds = section.SectionType + " " + region;
                        }
                    }
                }
                for (int first = 0; first < section.Regions.Count; first++)
                {
                    for (int second = first + 1; second < section.Regions.Count; second++)
                    {
                        if (section.Regions[first].Overlaps(section.Regions[second]))
                        {
                            overlaps++;
                            if (firstOverlap == null)
                            {
                                firstOverlap = section.Regions[first] + " / "
                                    + section.Regions[second];
                            }
                        }
                    }
                }
            }
            if (outOfBounds > 0)
            {
                issues.Add("merged region out of bounds (" + outOfBounds
                    + "; first " + firstOutOfBounds + ")");
            }
            if (overlaps > 0)
            {
                issues.Add("overlapping merged regions (" + overlaps
                    + "; first " + firstOverlap + ")");
            }
            if (diagnostics.MergeContainmentFailures > 0)
            {
                issues.Add("GetMergedCell result does not contain requested cell ("
                    + diagnostics.MergeContainmentFailures + "; first "
                    + diagnostics.FirstMergeContainmentFailure + ")");
            }
            if (diagnostics.CellStyleFailures > 0 || diagnostics.StylePropertyFailures > 0)
            {
                issues.Add("style read failure(s): GetTableCellStyle="
                    + diagnostics.CellStyleFailures + ", style properties/overrides="
                    + diagnostics.StylePropertyFailures);
            }
            if (diagnostics.CellTypeFailures > 0)
            {
                issues.Add("cell type read failure(s): " + diagnostics.CellTypeFailures);
            }
            if (diagnostics.MergeReadFailures > 0)
            {
                issues.Add("merge read failure(s): " + diagnostics.MergeReadFailures);
            }
            if (diagnostics.UnresolvedBorderCount > 0)
            {
                issues.Add("non-invalid border id did not resolve to an element ("
                    + diagnostics.UnresolvedBorderCount + ")");
            }
            if (diagnostics.InvalidDimensionCount > 0)
            {
                issues.Add("non-positive, NaN, or infinite width/height ("
                    + diagnostics.InvalidDimensionCount + "; first "
                    + diagnostics.FirstInvalidDimension + ")");
            }
            if (diagnostics.HeaderWider)
            {
                issues.Add("header total column width is wider than body visible GridColumnWidth total by "
                    + FormatDouble(diagnostics.HeaderWiderDifference));
            }
            if (diagnostics.TableDataUnavailable)
            {
                issues.Add("table data could not be read");
            }
            if (diagnostics.HeaderCellDumpSkipped > 0)
            {
                issues.Add("detailed header cell dump was bounded (dumped "
                    + diagnostics.HeaderCellDumped + ", skipped "
                    + diagnostics.HeaderCellDumpSkipped + ")");
            }
            if (diagnostics.UnexpectedFailures > 0)
            {
                issues.Add("unexpected managed dump failure(s): " + diagnostics.UnexpectedFailures);
            }

            writer.WriteLine("    non-style text-type diagnostics: IsValidTextTypeId=false="
                + diagnostics.InvalidTextTypeIdCount
                + ", unresolved-or-unreadable=" + diagnostics.UnresolvedTextTypeCount);
            string result = issues.Count == 0
                ? "OK (non-style diagnostics)"
                : "NON-STYLE SUSPECT " + string.Join("; ", issues);
            writer.WriteLine("CHECK " + EscapeLine(diagnostics.Name) + ": " + result);
        }

        private static void AppendOtherSchedules(
            StreamWriter writer, TableStateContext context, IList<ViewSchedule> linked)
        {
            writer.WriteLine();
            writer.WriteLine(Rule);
            writer.WriteLine("OTHER NON-LINKED VIEWSCHEDULES (tables not dumped)");
            if (context.LinkedDiscoveryFailed)
            {
                writer.WriteLine("  (linked discovery failed; non-linked classification is incomplete)");
            }
            HashSet<long> linkedIds = new HashSet<long>(linked.Select(SafeElementIdValue));
            IEnumerable<ViewSchedule> schedules = (context.AllSchedules ?? new List<ViewSchedule>())
                .Where(schedule => schedule != null)
                .OrderBy(SafeElementIdValue);
            int count = 0;
            foreach (ViewSchedule schedule in schedules)
            {
                bool isTemplate;
                bool templateKnown = TryIsTemplate(schedule, out isTemplate);
                long id = SafeElementIdValue(schedule);
                if (linkedIds.Contains(id))
                {
                    continue;
                }
                count++;
                writer.WriteLine("  \"" + QuotePart(SafeScheduleName(schedule))
                    + "\" id=" + SafeElementId(schedule)
                    + (templateKnown ? (isTemplate ? " (template)" : "")
                        : " (template state unavailable)"));
            }
            if (count == 0)
            {
                writer.WriteLine("  (none)");
            }
        }

        private static int CountNonTemplate(
            IEnumerable<ViewSchedule> schedules, out int failures)
        {
            int count = 0;
            failures = 0;
            foreach (ViewSchedule schedule in schedules)
            {
                bool isTemplate;
                if (!TryIsTemplate(schedule, out isTemplate))
                {
                    failures++;
                    continue;
                }
                if (!isTemplate)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool TryIsTemplate(ViewSchedule schedule, out bool isTemplate)
        {
            try
            {
                isTemplate = schedule.IsTemplate;
                return true;
            }
            catch (Exception)
            {
                isTemplate = false;
                return false;
            }
        }

        private static T Read<T>(
            StreamWriter writer, string label, Func<T> read, Func<T, string> format,
            out bool succeeded)
        {
            try
            {
                T value = read();
                writer.WriteLine(label + " : " + (format == null ? FormatObject(value) : format(value)));
                succeeded = true;
                return value;
            }
            catch (Exception ex)
            {
                writer.WriteLine(label + " : " + Failure(ex));
                succeeded = false;
                return default(T);
            }
        }

        private static void CheckDimension(
            ScheduleDiagnostics diagnostics, string label, double value, bool readSucceeded)
        {
            if (!readSucceeded)
            {
                return;
            }
            if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                diagnostics.InvalidDimensionCount++;
                if (diagnostics.FirstInvalidDimension == null)
                {
                    diagnostics.FirstInvalidDimension = label + "=" + FormatDouble(value);
                }
            }
        }

        private static string FormatTicks(long ticks)
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

        private static string FormatPresent(object value)
        {
            return value == null ? "(null)" : "present (" + value.GetType().Name + ")";
        }

        private static string FormatObject(object value)
        {
            return value == null ? "(null)" : EscapeLine(value.ToString());
        }

        private static string FormatString(string value)
        {
            return value == null ? "(null)" : EscapeLine(value);
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static string FormatNullableBoolean(bool? value)
        {
            return value.HasValue ? FormatBoolean(value.Value) : "unavailable";
        }

        private static string FormatInteger(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatElementId(ElementId value)
        {
            return value == null ? "(null)" : value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatScheduleFieldId(ScheduleFieldId value)
        {
            return value == null
                ? "(null)"
                : value.IntegerValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatColor(Color value)
        {
            return value == null
                ? "(null)"
                : "r=" + value.Red.ToString(CultureInfo.InvariantCulture)
                + ",g=" + value.Green.ToString(CultureInfo.InvariantCulture)
                + ",b=" + value.Blue.ToString(CultureInfo.InvariantCulture)
                + ",valid=" + value.IsValid.ToString().ToLowerInvariant();
        }

        private static string FormatCellText(string value)
        {
            if (value == null)
            {
                return "(null)";
            }
            string truncated = value.Length <= 60 ? value : value.Substring(0, 60);
            return "\"" + QuotePart(truncated) + "\"";
        }

        private static string EscapeLine(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string QuotePart(string value)
        {
            return EscapeLine(value).Replace("\"", "\\\"");
        }

        private static string Failure(Exception ex)
        {
            string message = ex == null ? "unknown failure" : ex.Message;
            return "(failed: " + (ex == null ? "Exception" : ex.GetType().Name)
                + ": " + (string.IsNullOrEmpty(message) ? "(no message)" : EscapeLine(message)) + ")";
        }

        private static string SafeUsername(UIApplication uiapp)
        {
            try
            {
                string name = uiapp.Application.Username;
                return string.IsNullOrEmpty(name) ? "unknown" : name;
            }
            catch (Exception)
            {
                return "unknown";
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

        private static string SafeScheduleName(ViewSchedule schedule)
        {
            try
            {
                return schedule.Name ?? "(null)";
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }

        private static string SafeElementId(ViewSchedule schedule)
        {
            try
            {
                return schedule.Id.Value.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }

        private static long SafeElementIdValue(ViewSchedule schedule)
        {
            try
            {
                return schedule.Id.Value;
            }
            catch (Exception)
            {
                return long.MaxValue;
            }
        }

        private static string Sanitize(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0
                    ? '_'
                    : character);
            }
            string result = builder.ToString().Trim();
            return result.Length == 0 ? "unknown" : result;
        }

        private static long InclusiveCount(int first, int last)
        {
            return last < first ? 0L : (long)last - first + 1L;
        }

        private static int SectionOrder(SectionType sectionType)
        {
            switch (sectionType)
            {
                case SectionType.Header: return 0;
                case SectionType.Body: return 1;
                case SectionType.Summary: return 2;
                case SectionType.Footer: return 3;
                default: return 4;
            }
        }

        private sealed class TableStateContext
        {
            public IList<ViewSchedule> AllSchedules = new List<ViewSchedule>();
            public IList<ViewSchedule> LinkedAny = new List<ViewSchedule>();
            public IList<ViewSchedule> LinkedCrossCheck = new List<ViewSchedule>();
            public bool LinkedDiscoveryFailed;
            public int IntegrityHealthy;
            public int IntegrityAffected;
            public int IntegrityIndeterminate;
            public int IntegrityNotCheckedLimit;
            public long IntegrityHeaderCellsExpected;
            public long IntegrityHeaderCellsRead;
            public long IntegrityStylelessCells;

            public void RecordIntegrity(StyleIntegrityResult result)
            {
                if (result == null)
                {
                    IntegrityIndeterminate++;
                    return;
                }
                IntegrityHeaderCellsExpected += result.HeaderCellsExpected;
                IntegrityHeaderCellsRead += result.HeaderCellsRead;
                IntegrityStylelessCells += result.StylelessCellCount;
                if (result.ReasonCode == StyleIntegrityReasonCode.ScanLimit)
                {
                    IntegrityNotCheckedLimit += Math.Max(1,
                        result.SkippedScheduleCount);
                    return;
                }
                switch (result.State)
                {
                    case StyleIntegrityState.Healthy:
                        IntegrityHealthy++;
                        break;
                    case StyleIntegrityState.Affected:
                        IntegrityAffected++;
                        break;
                    default:
                        IntegrityIndeterminate++;
                        break;
                }
            }
        }

        private sealed class ScheduleDiagnostics
        {
            public ScheduleDiagnostics(string name)
            {
                Name = name;
            }

            public string Name;
            public List<SectionObservation> Sections = new List<SectionObservation>();
            public bool HasHeaderColumnWidth;
            public double HeaderColumnWidthTotal;
            public bool HasBodyVisibleGridWidth;
            public double BodyVisibleGridWidthTotal;
            public int CellsRead;
            public int CellTypeFailures;
            public int CellStyleFailures;
            public int StylePropertyFailures;
            public int MergeReadFailures;
            public int MergeContainmentFailures;
            public string FirstMergeContainmentFailure;
            public int UnresolvedBorderCount;
            public int InvalidDimensionCount;
            public string FirstInvalidDimension;
            public int UnexpectedFailures;
            public bool TableDataUnavailable;
            public int UnresolvedTextTypeCount;
            public int InvalidTextTypeIdCount;
            public long HeaderCellDumped;
            public long HeaderCellDumpSkipped;
            public bool HeaderWider;
            public double HeaderWiderDifference;
        }

        private sealed class SectionObservation
        {
            public SectionObservation(SectionType sectionType)
            {
                SectionType = sectionType;
            }

            public SectionType SectionType;
            public int NumberOfRows;
            public int NumberOfColumns;
            public int FirstRow;
            public int LastRow;
            public int FirstColumn;
            public int LastColumn;
            public bool BoundsComplete;
            public List<MergeRegion> Regions = new List<MergeRegion>();

            public bool Contains(MergeRegion region)
            {
                return region.Top >= FirstRow
                    && region.Left >= FirstColumn
                    && region.Bottom <= LastRow
                    && region.Right <= LastColumn;
            }
        }

        private sealed class MergeRegion
        {
            public MergeRegion(SectionType sectionType, int top, int left, int bottom, int right)
            {
                SectionType = sectionType;
                Top = top;
                Left = left;
                Bottom = bottom;
                Right = right;
            }

            public SectionType SectionType;
            public int Top;
            public int Left;
            public int Bottom;
            public int Right;

            public bool IsReal
            {
                get { return Top < Bottom || Left < Right; }
            }

            public bool Contains(int row, int column)
            {
                return row >= Top && row <= Bottom && column >= Left && column <= Right;
            }

            public bool SameAs(MergeRegion other)
            {
                return other != null && SectionType == other.SectionType
                    && Top == other.Top && Left == other.Left
                    && Bottom == other.Bottom && Right == other.Right;
            }

            public bool Overlaps(MergeRegion other)
            {
                return other != null && SectionType == other.SectionType
                    && Top <= other.Bottom && other.Top <= Bottom
                    && Left <= other.Right && other.Left <= Right;
            }

            public override string ToString()
            {
                return "[" + Top.ToString(CultureInfo.InvariantCulture) + ","
                    + Left.ToString(CultureInfo.InvariantCulture) + ".."
                    + Bottom.ToString(CultureInfo.InvariantCulture) + ","
                    + Right.ToString(CultureInfo.InvariantCulture) + "]";
            }
        }

        private static long DistinctHeaderMergedColumns(ScheduleDiagnostics diagnostics)
        {
            List<Tuple<int, int>> intervals = diagnostics.Sections
                .Where(section => section.SectionType == SectionType.Header)
                .SelectMany(section => section.Regions)
                .Where(region => region.IsReal)
                .Select(region => Tuple.Create(region.Left, region.Right))
                .OrderBy(interval => interval.Item1)
                .ThenBy(interval => interval.Item2)
                .ToList();
            if (intervals.Count == 0)
            {
                return 0L;
            }

            long total = 0L;
            long start = intervals[0].Item1;
            long end = intervals[0].Item2;
            foreach (Tuple<int, int> interval in intervals.Skip(1))
            {
                if (interval.Item1 <= end + 1L)
                {
                    end = Math.Max(end, interval.Item2);
                }
                else
                {
                    total += end - start + 1L;
                    start = interval.Item1;
                    end = interval.Item2;
                }
            }
            return total + end - start + 1L;
        }
    }
}
