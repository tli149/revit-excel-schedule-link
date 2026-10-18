using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ProbeRowTextFitCommand : IExternalCommand
    {
        private const int FixtureRows = 18;
        private const int FixtureColumns = 3;
        private const string BaseScheduleName = "ESL_QA_RowFit_";
        private const string MutationName = "ESL Probe - Row Text Fit Calibration";

        private const string LegendLabel = "LEGEND";
        private const string LegendLatin =
            "Latin pitch groups end with END1, END2 or END3.";
        private const string LegendJapanese =
            "\u65e5\u672c\u8A9E\u8868\u793A END markers.";
        private const string ControlLabel = "CONTROL";
        private const string ControlLatin = "ABC 123 END_CONTROL";
        private const string ControlJapanese =
            "\u65e5\u672c\u8A9E\u8868\u793A END_CONTROL";

        private const string LatinEnd1 = "ABC 123 END1";
        private const string LatinEnd2 = "ABC 123 END2\nABC 123 END2";
        private const string LatinEnd3 =
            "ABC 123 END3\nABC 123 END3\nABC 123 END3";
        private const string JapaneseEnd1 =
            "\u65e5\u672c\u8A9E\u8868\u793A END1";
        private const string JapaneseEnd2 =
            "\u65e5\u672c\u8A9E\u8868\u793A END2\n\u65e5\u672c\u8A9E\u8868\u793A END2";
        private const string JapaneseEnd3 =
            "\u65e5\u672c\u8A9E\u8868\u793A END3\n\u65e5\u672c\u8A9E\u8868\u793A END3\n\u65e5\u672c\u8A9E\u8868\u793A END3";

        private const string CandidateLabel11 = "PITCH 1.15 / 1 LINE";
        private const string CandidateLabel12 = "PITCH 1.15 / 2 LINES";
        private const string CandidateLabel13 = "PITCH 1.15 / 3 LINES";
        private const string CandidateLabel21 = "PITCH 1.35 / 1 LINE";
        private const string CandidateLabel22 = "PITCH 1.35 / 2 LINES";
        private const string CandidateLabel23 = "PITCH 1.35 / 3 LINES";
        private const string CandidateLabel31 = "PITCH 1.55 / 1 LINE";
        private const string CandidateLabel32 = "PITCH 1.55 / 2 LINES";
        private const string CandidateLabel33 = "PITCH 1.55 / 3 LINES";

        private const string ProseLabel = "LONG PROSE";
        private const string ProseLatin =
            "This ordinary Latin description deliberately exceeds the narrow "
            + "column capacity and ends with END_LATIN.";
        private const string ProseJapanese =
            "\u3053\u308C\u306F\u72ED\u3044\u5217\u3067\u6298\u308A\u8FD4\u3059\u9577\u3044\u65E5\u672C\u8A9E\u306E\u8AAC\u660E\u3067\u3059 "
            + "\u5E45\u306E\u72ED\u3044\u5217\u3067\u5185\u5BB9\u304C\u3069\u306E\u3088\u3046\u306B\u6298\u308A\u8FD4\u3055\u308C\u308B\u304B\u3092\u78BA\u8A8D\u3059\u308B\u305F\u3081\u306E\u6587\u7AE0\u3067\u3059 END_JP";
        private const string CodeLabel = "NO-SPACE CODE";
        private const string LongNoSpaceCode =
            "ESL_ROW_FIT_CALIBRATION_CODE_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_END_CODE";
        private const string CodeJapanese =
            "\u30B3\u30FC\u30C9\u306E\u78BA\u8A8D END_CODE";
        private const string LongLabel = ">60 UNITS";
        private const string LongLatinOver60 =
            "LLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLLL END_LONG";
        private const string JapanesePhrase =
            "\u65E5\u672C\u8A9E\u8868\u793A";
        private const string LongJapaneseOver60 =
            JapanesePhrase + JapanesePhrase + JapanesePhrase + JapanesePhrase
            + JapanesePhrase + JapanesePhrase + JapanesePhrase + " END_LONG";
        private const string TrailingLabel = "TRAILING BLANKS";
        private const string TrailingLatin = "Trailing Latin line END_TRAILING\n\n\n";
        private const string TrailingJapanese =
            "\u65E5\u672C\u8A9E\u306E\u884C END_TRAILING\n\n\n";
        private const string HorizontalMergeLabel = "HORIZONTAL MERGE";
        private const string HorizontalMergeTitle =
            "Long horizontal merged title for row text fitting END_HMERGE";
        private const string VerticalMergeLabel = "VERTICAL MERGE";
        private const string VerticalMergeLatin =
            "Vertical line one\nVertical line two\nVertical line three END_VMERGE";
        private const string VerticalJapanese16 =
            "\u65e5\u672c\u8A9E\u8868\u793A ROW16";
        private const string VerticalJapanese17 =
            "\u65e5\u672c\u8A9E\u8868\u793A ROW17";
        private const string VerticalMergeEndLabel = "VERTICAL MERGE END";

        private static readonly double[] RequestedColumnFeet =
            { 0.25, 0.33, 0.33 };

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData == null
                || commandData.Application == null
                ? null : commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show(UserMessages.RowFitCalibrationTitle,
                    UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            CalibrationReport report = new CalibrationReport();
            bool mutationCommitted = false;
            bool mutationInvoked = false;
            bool mutationResultRecorded = false;

            try
            {
                AppendMetadata(commandData, doc, report);
                string[,] fixture = CreateFixtureTexts();
                AppendFixture(report, fixture);

                ElementId sourceTitleTextTypeId =
                    ReadActiveTitleTextType(doc, uidoc, report);
                string requestedName = BaseScheduleName
                    + DateTime.UtcNow.ToString(
                        "yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
                report.Add("requested_schedule_name=" + requestedName);

                mutationInvoked = true;
                ModelMutationResult<bool> mutation = ModelMutation.Run<bool>(
                    doc, MutationName,
                    delegate (ModelMutationContext context)
                    {
                        ViewSchedule schedule = ScheduleFactory.Create(
                            doc, requestedName);
                        report.ScheduleName = schedule.Name;
                        report.ScheduleId = schedule.Id.Value;
                        report.Add("schedule_name=" + schedule.Name);
                        report.Add("schedule_id=" + schedule.Id.Value);
                        BuildFixture(doc, schedule, sourceTitleTextTypeId,
                            fixture, report);
                        return true;
                    });

                AppendMutationResult(report, mutation);
                mutationResultRecorded = true;
                mutationCommitted = mutation.Status
                    == ModelMutationStatus.Committed;
            }
            catch (Exception ex) when (ModelMutation.IsFatalRegeneration(ex))
            {
                ModelMutation.RecordFatalRegeneration();
                report.Fatal = true;
                report.Add("fatal_regeneration=" + ex.GetType().Name + ": "
                    + DescribeException(ex));
            }
            catch (Exception ex)
            {
                report.Add("ordinary_failure=" + ex.GetType().Name + ": "
                    + DescribeException(ex));
            }

            if (!mutationResultRecorded)
            {
                report.Add("mutation_status=" + (mutationInvoked
                    ? "exception_before_result" : "not_started"));
            }

            return WriteReportAndShow(report, mutationCommitted);
        }

        private static ElementId ReadActiveTitleTextType(
            Document doc, UIDocument uidoc, CalibrationReport report)
        {
            ViewSchedule active = uidoc.ActiveView as ViewSchedule;
            if (active == null || !LinkedScheduleFinder.IsLinkedAny(active))
            {
                report.Add("active_linked_schedule=false");
                report.Add("title_text_type_source=native_default_no_active_link");
                return null;
            }

            report.Add("active_linked_schedule=true");
            report.Add("active_schedule_name=" + active.Name);
            report.Add("active_schedule_id=" + active.Id.Value);
            StyleIntegrityResult scan =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(
                    doc, active);
            AppendIntegrity(report, "active_schedule_scan", scan);
            if (scan == null
                || scan.ReadTransactionRollbackStatus
                    != TransactionStatus.RolledBack.ToString())
            {
                throw new PreliminaryScanException(
                    "The active linked schedule scan did not verify rollback.");
            }
            if (scan.State != StyleIntegrityState.Healthy)
            {
                report.Add("title_text_type_source=native_default_active_not_healthy");
                return null;
            }

            ElementId titleTextTypeId = active.TitleTextTypeId;
            if (titleTextTypeId == null
                || titleTextTypeId == ElementId.InvalidElementId)
            {
                report.Add("title_text_type_source=native_default_id_unavailable");
                return null;
            }
            report.Add("title_text_type_source=active_healthy_title_type");
            report.Add("title_text_type_id=" + titleTextTypeId.Value);
            return titleTextTypeId;
        }

        private static void BuildFixture(Document doc, ViewSchedule schedule,
            ElementId sourceTitleTextTypeId, string[,] fixture,
            CalibrationReport report)
        {
            TableSectionData section = schedule.GetTableData()
                .GetSectionData(SectionType.Header);
            HeaderTableRenderer.ClearTo1x1(section);
            while (section.NumberOfRows < FixtureRows)
            {
                section.InsertRow(section.FirstRowNumber + 1);
            }
            while (section.NumberOfColumns < FixtureColumns)
            {
                section.InsertColumn(section.FirstColumnNumber + 1);
            }

            if (sourceTitleTextTypeId != null
                && sourceTitleTextTypeId != ElementId.InvalidElementId
                && schedule.IsValidTextTypeId(sourceTitleTextTypeId))
            {
                schedule.TitleTextTypeId = sourceTitleTextTypeId;
                report.Add("new_schedule_title_text_type=copied");
            }
            else
            {
                report.Add("new_schedule_title_text_type=native_default");
            }

            int firstRow = section.FirstRowNumber;
            int firstColumn = section.FirstColumnNumber;
            for (int row = 0; row < FixtureRows; row++)
            {
                for (int column = 0; column < FixtureColumns; column++)
                {
                    section.SetCellType(firstRow + row,
                        firstColumn + column, CellType.Text);
                    section.SetCellText(firstRow + row,
                        firstColumn + column, fixture[row, column]);
                }
            }

            for (int column = 0; column < FixtureColumns; column++)
            {
                TrySetColumnWidth(section, firstColumn + column,
                    RequestedColumnFeet[column], column, report);
            }

            section.MergeCells(new TableMergedCell(
                firstRow + 15, firstColumn + 1,
                firstRow + 15, firstColumn + 2));
            section.MergeCells(new TableMergedCell(
                firstRow + 16, firstColumn + 1,
                firstRow + 17, firstColumn + 1));
            report.Add("fixture_merges=horizontal row15 columns1-2; vertical row16-17 column1");

            double headerTotal;
            if (!HeaderTableRenderer.TryGetHeaderWidthTotal(
                schedule, out headerTotal))
            {
                throw new BodyColumnWidthSyncException();
            }
            report.Add("header_total_feet_before_body_sync=" + FormatR(headerTotal));
            if (!HeaderTableRenderer.SetBodyColumnWidth(schedule, headerTotal))
            {
                throw new BodyColumnWidthSyncException();
            }
            report.Add("body_width_sync=accepted_zero_margin");

            StyleIntegrityResult initialScan =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    doc, schedule, true);
            AppendIntegrity(report, "initial_fixture_scan", initialScan);
            if (initialScan == null
                || initialScan.State != StyleIntegrityState.Healthy)
            {
                throw new UnsafeScheduleStyleException(initialScan);
            }

            double nativeControlHeight = section.GetRowHeight(firstRow + 1);
            report.Add("native_one_line_control_height_feet="
                + FormatR(nativeControlHeight));

            HeaderTableSnapshot snapshot =
                ScheduleRepairService.CaptureSnapshotInOpenTransaction(schedule);
            report.Add("snapshot_rows=" + snapshot.Rows);
            report.Add("snapshot_columns=" + snapshot.Columns);
            for (int column = 0; column < snapshot.ColumnWidths.Count; column++)
            {
                report.Add("column_width_requested_feet[" + column + "]="
                    + FormatR(RequestedColumnFeet[column])
                    + " readback_feet=" + FormatR(snapshot.ColumnWidths[column]));
            }

            FitContentMeasurements measurements =
                HeaderContentMeasurer.PrepareSnapshot(snapshot, 1.0);
            List<RowFitTextMetrics> metrics = BuildMetrics(
                measurements, initialScan.Facts, report);
            RowFitTextMetrics[] metricArray = metrics.ToArray();
            RowFitResult fit = RowHeightMath.FitRows(
                measurements, snapshot.ColumnWidths, metricArray);
            report.Add("shared_fit_can_fit=" + fit.CanFit);
            report.Add("shared_fit_too_narrow_cells="
                + fit.TooNarrowCells.Count);
            report.Add("shared_fit_too_narrow_rows="
                + FormatRows(fit.TooNarrowRows));

            AppendAnchorPlans(measurements, snapshot.ColumnWidths,
                metricArray, report);
            AppendRowPlans(fit, report);

            // Immediate observations are diagnostics only; the authoritative
            // usable flags are read after the final regenerating scan.
            RowHeightObservation[] observations =
                new RowHeightObservation[FixtureRows];
            for (int row = 0; row < FixtureRows; row++)
            {
                if (row == 1)
                {
                    report.Add("row_plan row=1 path=native_one_line_control applied=false");
                    continue;
                }
                if (row >= 2 && row <= 10)
                {
                    int lineCount = CandidateLineCount(row);
                    double factor = CandidatePitch(row);
                    double textSizeFeet = MaxTextSizeForRow(
                        measurements, metricArray, row);
                    double requested = RowHeightMath.RequiredFeetForLines(
                        lineCount, textSizeFeet, factor);
                    observations[row] = SetRowHeight(
                        section, firstRow + row, requested,
                        "candidate_pitch=" + FormatR(factor)
                        + " lines=" + lineCount, row, report);
                    report.Add("candidate row=" + row
                        + " pitch_factor=" + FormatR(factor)
                        + " explicit_lines=" + lineCount
                        + " text_size_feet=" + FormatR(textSizeFeet)
                        + " immediate_usable=" + observations[row].Usable);
                    continue;
                }

                observations[row] = SetRowHeight(
                    section, firstRow + row, fit.RequiredFeet[row],
                    "shared_fit", row, report);
                report.Add("shared_row row=" + row
                    + " predicted_lines=" + fit.RequiredLines[row]
                    + " requested_feet=" + FormatR(fit.RequiredFeet[row])
                    + " immediate_usable=" + observations[row].Usable);
            }

            StyleIntegrityResult finalScan =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    doc, schedule, true);
            AppendIntegrity(report, "final_fixture_scan", finalScan);
            if (finalScan == null
                || finalScan.State != StyleIntegrityState.Healthy)
            {
                throw new UnsafeScheduleStyleException(finalScan);
            }
            report.Add("fixture_final_healthy=true");
            report.Add("fixture_height_writes_completed=true");
            AppendFinalRowHeights(schedule, observations, report);
        }

        private static void AppendFinalRowHeights(ViewSchedule schedule,
            RowHeightObservation[] observations, CalibrationReport report)
        {
            TableSectionData section = schedule.GetTableData()
                .GetSectionData(SectionType.Header);
            int firstRow = section.FirstRowNumber;
            for (int row = 0; row < FixtureRows; row++)
            {
                double finalFeet = double.NaN;
                bool readable = false;
                try
                {
                    finalFeet = section.GetRowHeight(firstRow + row);
                    readable = true;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    report.Add("final_row_readback_unavailable row=" + row
                        + " detail=" + DescribeException(ex));
                }

                RowHeightObservation observation = observations[row];
                if (observation == null)
                {
                    report.Add("final_row_height row=" + row
                        + " kind=native_one_line_control final_feet="
                        + FormatR(finalFeet));
                    continue;
                }
                bool usable = observation.SetterAccepted && readable
                    && IsPositiveFinite(finalFeet)
                    && finalFeet >= observation.RequestedFeet
                        - LayoutOps.Tolerance;
                report.Add("final_row_height row=" + row
                    + " requested_feet=" + FormatR(observation.RequestedFeet)
                    + " final_feet=" + FormatR(finalFeet)
                    + " setter_accepted=" + observation.SetterAccepted
                    + " final_usable=" + usable);
            }
        }

        private static List<RowFitTextMetrics> BuildMetrics(
            FitContentMeasurements measurements, ScheduleStyleFacts facts,
            CalibrationReport report)
        {
            if (facts == null)
            {
                throw new InvalidOperationException(
                    "The style scan did not return cell facts.");
            }
            List<RowFitTextMetrics> metrics =
                new List<RowFitTextMetrics>(measurements.Cells.Count);
            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell measured = measurements.Cells[i];
                CellStyleFacts cell = FindCellFacts(facts,
                    measured.Row, measured.Column);
                if (cell == null)
                {
                    throw new InvalidOperationException(
                        "A measured anchor had no matching style facts.");
                }
                double textSizeFeet =
                    ScheduleStyleIntegrityScanner.GetConservativeTextSizeFeet(cell);
                metrics.Add(new RowFitTextMetrics(textSizeFeet));
                report.Add("anchor_style row=" + measured.Row
                    + " column=" + measured.Column
                    + " font=" + (cell.FontName ?? string.Empty)
                    + " raw_text_size_table_units=" + FormatR(cell.TextSize)
                    + " conservative_text_size_feet=" + FormatR(textSizeFeet));
            }
            return metrics;
        }

        private static CellStyleFacts FindCellFacts(
            ScheduleStyleFacts facts, int row, int column)
        {
            if (facts.Cells == null || row < 0 || column < 0
                || row >= facts.NumberOfRows
                || column >= facts.NumberOfColumns)
            {
                return null;
            }
            int index = checked(row * facts.NumberOfColumns + column);
            return index < facts.Cells.Count ? facts.Cells[index] : null;
        }

        private static double MaxTextSizeForRow(
            FitContentMeasurements measurements,
            IReadOnlyList<RowFitTextMetrics> metrics, int row)
        {
            double maximum = 0.0;
            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                if (row >= cell.Row && row < cell.Row + cell.RowSpan
                    && metrics[i].TextSizeFeet > maximum)
                {
                    maximum = metrics[i].TextSizeFeet;
                }
            }
            if (maximum <= 0.0 || double.IsNaN(maximum)
                || double.IsInfinity(maximum))
            {
                throw new InvalidOperationException(
                    "A candidate row had no usable text size.");
            }
            return maximum;
        }

        private static void AppendAnchorPlans(
            FitContentMeasurements measurements,
            IReadOnlyList<double> columnFeet,
            IReadOnlyList<RowFitTextMetrics> metrics,
            CalibrationReport report)
        {
            for (int i = 0; i < measurements.Cells.Count; i++)
            {
                FitContentMeasuredCell cell = measurements.Cells[i];
                double spanFeet = 0.0;
                for (int column = cell.Column;
                    column < cell.Column + cell.ColumnSpan; column++)
                {
                    spanFeet += columnFeet[column];
                }
                double halfUnitFeet = metrics[i].TextSizeFeet
                    * LayoutMetrics.RowFitHalfUnitEm;
                long fullLines = FitContentText.EstimateRequiredLineCount(
                    cell, spanFeet, halfUnitFeet,
                    LayoutMetrics.CellPaddingInches
                    / LayoutMetrics.InchesPerFoot);
                // Per-row distributed lines and heights come from the shared
                // FitRows result in the row_plan lines; not recomputed here.
                report.Add("anchor_plan row=" + cell.Row
                    + " column=" + cell.Column
                    + " row_span=" + cell.RowSpan
                    + " column_span=" + cell.ColumnSpan
                    + " span_width_feet=" + FormatR(spanFeet)
                    + " max_glyph_units=" + FormatR(cell.MaxGlyphUnits)
                    + " predicted_full_lines=" + fullLines);
            }
        }

        private static void AppendRowPlans(RowFitResult fit,
            CalibrationReport report)
        {
            for (int row = 0; row < fit.RequiredFeet.Count; row++)
            {
                report.Add("row_plan row=" + row
                    + " shared_predicted_lines=" + fit.RequiredLines[row]
                    + " shared_required_feet=" + FormatR(fit.RequiredFeet[row]));
            }
        }

        private static RowHeightObservation SetRowHeight(
            TableSectionData section, int row, double requestedFeet,
            string kind, int relativeRow, CalibrationReport report)
        {
            RowHeightObservation observation = new RowHeightObservation
            {
                RequestedFeet = requestedFeet,
                ReadbackFeet = double.NaN,
                SetterAccepted = true,
                ReadbackSucceeded = false
            };
            try
            {
                section.SetRowHeight(row, requestedFeet);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                observation.SetterAccepted = false;
                report.Add("refused_setter row=" + relativeRow
                    + " kind=" + kind + " requested_feet="
                    + FormatR(requestedFeet) + " detail="
                    + DescribeException(ex));
            }

            try
            {
                observation.ReadbackFeet = section.GetRowHeight(row);
                observation.ReadbackSucceeded = true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add("row_readback_unavailable row=" + relativeRow
                    + " kind=" + kind + " detail="
                    + DescribeException(ex));
            }
            report.Add("row_height kind=" + kind
                + " row=" + relativeRow
                + " requested_feet=" + FormatR(requestedFeet)
                + " readback_feet=" + FormatR(observation.ReadbackFeet)
                + " setter_accepted=" + observation.SetterAccepted
                + " readback_succeeded=" + observation.ReadbackSucceeded);
            observation.Usable = observation.SetterAccepted
                && observation.ReadbackSucceeded
                && IsPositiveFinite(observation.ReadbackFeet)
                && observation.ReadbackFeet >= requestedFeet - LayoutOps.Tolerance;
            return observation;
        }

        private static void TrySetColumnWidth(TableSectionData section,
            int column, double requestedFeet, int relativeColumn,
            CalibrationReport report)
        {
            bool accepted = true;
            try
            {
                section.SetColumnWidth(column, requestedFeet);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                accepted = false;
                report.Add("refused_setter column=" + relativeColumn
                    + " requested_feet=" + FormatR(requestedFeet)
                    + " detail=" + DescribeException(ex));
            }

            double readback = double.NaN;
            bool readbackSucceeded = false;
            try
            {
                readback = section.GetColumnWidth(column);
                readbackSucceeded = true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add("column_readback_unavailable column="
                    + relativeColumn + " detail=" + DescribeException(ex));
            }
            report.Add("column_width column=" + relativeColumn
                + " requested_feet=" + FormatR(requestedFeet)
                + " readback_feet=" + FormatR(readback)
                + " setter_accepted=" + accepted
                + " readback_succeeded=" + readbackSucceeded);
        }

        private static double CandidatePitch(int row)
        {
            if (row >= 2 && row <= 4)
            {
                return 1.15;
            }
            if (row >= 5 && row <= 7)
            {
                return 1.35;
            }
            return 1.55;
        }

        private static int CandidateLineCount(int row)
        {
            return ((row - 2) % 3) + 1;
        }

        private static string[,] CreateFixtureTexts()
        {
            string[,] cells = new string[FixtureRows, FixtureColumns];
            cells[0, 0] = LegendLabel;
            cells[0, 1] = LegendLatin;
            cells[0, 2] = LegendJapanese;
            cells[1, 0] = ControlLabel;
            cells[1, 1] = ControlLatin;
            cells[1, 2] = ControlJapanese;

            cells[2, 0] = CandidateLabel11;
            cells[2, 1] = LatinEnd1;
            cells[2, 2] = JapaneseEnd1;
            cells[3, 0] = CandidateLabel12;
            cells[3, 1] = LatinEnd2;
            cells[3, 2] = JapaneseEnd2;
            cells[4, 0] = CandidateLabel13;
            cells[4, 1] = LatinEnd3;
            cells[4, 2] = JapaneseEnd3;
            cells[5, 0] = CandidateLabel21;
            cells[5, 1] = LatinEnd1;
            cells[5, 2] = JapaneseEnd1;
            cells[6, 0] = CandidateLabel22;
            cells[6, 1] = LatinEnd2;
            cells[6, 2] = JapaneseEnd2;
            cells[7, 0] = CandidateLabel23;
            cells[7, 1] = LatinEnd3;
            cells[7, 2] = JapaneseEnd3;
            cells[8, 0] = CandidateLabel31;
            cells[8, 1] = LatinEnd1;
            cells[8, 2] = JapaneseEnd1;
            cells[9, 0] = CandidateLabel32;
            cells[9, 1] = LatinEnd2;
            cells[9, 2] = JapaneseEnd2;
            cells[10, 0] = CandidateLabel33;
            cells[10, 1] = LatinEnd3;
            cells[10, 2] = JapaneseEnd3;

            cells[11, 0] = ProseLabel;
            cells[11, 1] = ProseLatin;
            cells[11, 2] = ProseJapanese;
            cells[12, 0] = CodeLabel;
            cells[12, 1] = LongNoSpaceCode;
            cells[12, 2] = CodeJapanese;
            cells[13, 0] = LongLabel;
            cells[13, 1] = LongLatinOver60;
            cells[13, 2] = LongJapaneseOver60;
            cells[14, 0] = TrailingLabel;
            cells[14, 1] = TrailingLatin;
            cells[14, 2] = TrailingJapanese;
            cells[15, 0] = HorizontalMergeLabel;
            cells[15, 1] = HorizontalMergeTitle;
            cells[15, 2] = string.Empty;
            cells[16, 0] = VerticalMergeLabel;
            cells[16, 1] = VerticalMergeLatin;
            cells[16, 2] = VerticalJapanese16;
            cells[17, 0] = VerticalMergeEndLabel;
            cells[17, 1] = string.Empty;
            cells[17, 2] = VerticalJapanese17;
            return cells;
        }

        private static void AppendFixture(CalibrationReport report,
            string[,] fixture)
        {
            report.Add("fixture_shape=18 rows x 3 columns");
            report.Add("fixture_columns=labels,Latin,Japanese");
            report.Add("fixture_widths_requested_feet=0.25,0.33,0.33");
            report.Add("fixture_row_map=0 legend; 1 untouched control; "
                + "2-4 pitch 1.15 lines 1-3; 5-7 pitch 1.35 lines 1-3; "
                + "8-10 pitch 1.55 lines 1-3; 11 prose; 12 code; "
                + "13 over-60; 14 trailing blanks; 15 horizontal merge; "
                + "16-17 vertical Latin merge");
            for (int row = 0; row < FixtureRows; row++)
            {
                for (int column = 0; column < FixtureColumns; column++)
                {
                    report.Add("fixture_text row=" + row
                        + " column=" + column + " value="
                        + EscapeFixtureText(fixture[row, column]));
                }
            }
        }

        private static void AppendMetadata(
            ExternalCommandData commandData, Document doc,
            CalibrationReport report)
        {
            Autodesk.Revit.ApplicationServices.Application app =
                commandData.Application.Application;
            report.Add("revit_version=" + app.VersionNumber);
            report.Add("revit_build=" + app.VersionBuild);
            report.Add("revit_name=" + app.VersionName);
            report.Add("revit_username=" + (app.Username ?? "user"));
            report.Add("document_title=" + doc.Title);
            report.Add("report_path=%APPDATA%\\ExcelScheduleLink\\RowTextFitCalibration.txt");
            report.Add("row_fit_half_unit_em="
                + FormatR(LayoutMetrics.RowFitHalfUnitEm));
            report.Add("row_fit_default_pitch_factor="
                + FormatR(LayoutMetrics.RowFitLinePitchFactor));
            report.Add("row_fit_vertical_padding_inches="
                + FormatR(LayoutMetrics.RowFitVerticalPaddingInches));
            report.Add("row_fit_wrapped_line_allowance="
                + LayoutMetrics.RowFitWrappedLineAllowance);
        }

        private static void AppendIntegrity(CalibrationReport report,
            string label, StyleIntegrityResult result)
        {
            if (result == null)
            {
                report.Add(label + "=(null)");
                return;
            }
            report.Add(label + "=" + result.State + "/" + result.ReasonCode);
            report.Add(label + "_read_transaction_rollback="
                + (result.ReadTransactionRollbackStatus ?? string.Empty));
            if (result.Facts != null)
            {
                report.Add(label + "_facts_dimensions="
                    + result.Facts.NumberOfRows + "x"
                    + result.Facts.NumberOfColumns);
                AppendTextStyle(report, label + "_title",
                    result.Facts.TitleTextStyle);
                AppendTextStyle(report, label + "_header",
                    result.Facts.HeaderTextStyle);
            }
        }

        private static void AppendTextStyle(CalibrationReport report,
            string label, TextStyleFacts facts)
        {
            if (facts == null)
            {
                report.Add(label + "=(null)");
                return;
            }
            report.Add(label + "_font=" + (facts.FontName ?? string.Empty)
                + " raw_size=" + FormatR(facts.RawSize)
                + " resolved_inches=" + FormatR(facts.SizeInches)
                + " scanner_projection_table_units="
                + FormatR(facts.SizeTableUnits));
        }

        private static void AppendMutationResult(CalibrationReport report,
            ModelMutationResult<bool> mutation)
        {
            if (mutation == null)
            {
                report.Add("mutation_status=(null)");
                return;
            }
            report.Add("mutation_status=" + mutation.Status);
            report.Add("mutation_transaction_started="
                + mutation.TransactionStarted);
            report.Add("mutation_start_status="
                + FormatStatus(mutation.StartStatus));
            report.Add("mutation_commit_status="
                + FormatStatus(mutation.CommitStatus));
            report.Add("mutation_rollback_status="
                + FormatStatus(mutation.RollbackStatus));
            report.Add("mutation_rollback_verified="
                + mutation.RollbackVerified);
            if (mutation.Exception != null)
            {
                report.Add("mutation_exception="
                    + mutation.Exception.GetType().Name + ": "
                    + DescribeException(mutation.Exception));
            }
            if (mutation.Status == ModelMutationStatus.FatalRegeneration)
            {
                report.Fatal = true;
                report.Add("fatal_mutation_boundary=true");
            }
            report.Add("fixture_visible="
                + (mutation.Status == ModelMutationStatus.Committed));
        }

        private static Result WriteReportAndShow(CalibrationReport report,
            bool mutationCommitted)
        {
            string path = null;
            try
            {
                path = WriteReport(report);
            }
            catch (Exception ex)
            {
                string clipboardFailure;
                bool copied = TryCopyReportToClipboard(
                    report, out clipboardFailure);
                if (copied)
                {
                    TaskDialog.Show(UserMessages.RowFitCalibrationTitle,
                        UserMessages.RowFitCalibrationReportFailed);
                }
                else
                {
                    string detail = DescribeException(ex) + "\n\n"
                        + clipboardFailure;
                    TaskDialog.Show(UserMessages.RowFitCalibrationTitle,
                        detail);
                }
                // A failed result would make Revit undo the committed fixture.
                return mutationCommitted ? Result.Succeeded : Result.Failed;
            }

            if (mutationCommitted)
            {
                TaskDialog.Show(UserMessages.RowFitCalibrationTitle,
                    UserMessages.RowFitCalibrationCreated(
                        report.ScheduleName ?? BaseScheduleName, path));
                return Result.Succeeded;
            }

            string failureMessage = report.Fatal
                ? UserMessages.RegenerationFailure
                : UserMessages.ReloadTransactionNotCommitted;
            TaskDialog.Show(UserMessages.RowFitCalibrationTitle,
                failureMessage + "\n\nReport:\n" + path);
            return Result.Failed;
        }

        private static string WriteReport(CalibrationReport report)
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            string directory = Path.Combine(appData, "ExcelScheduleLink");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                "RowTextFitCalibration.txt");
            File.WriteAllText(path, report.ToText(), new UTF8Encoding(true));
            return path;
        }

        private static bool TryCopyReportToClipboard(
            CalibrationReport report, out string failure)
        {
            failure = null;
            try
            {
                System.Windows.Forms.Clipboard.SetText(report.ToText());
                return true;
            }
            catch (Exception ex)
            {
                failure = DescribeException(ex);
                return false;
            }
        }

        private static string EscapeFixtureText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string FormatRows(IReadOnlyList<int> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append(rows[i]);
            }
            return builder.ToString();
        }

        private static string FormatStatus(TransactionStatus? status)
        {
            return status.HasValue ? status.Value.ToString() : "none";
        }

        private static string FormatR(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0.0 && !double.IsNaN(value)
                && !double.IsInfinity(value);
        }

        private static string DescribeException(Exception exception)
        {
            return (exception == null ? "unknown" : exception.Message ?? string.Empty)
                .Replace("\r", " ").Replace("\n", " ");
        }

        private sealed class RowHeightObservation
        {
            public double RequestedFeet;
            public double ReadbackFeet;
            public bool SetterAccepted;
            public bool ReadbackSucceeded;
            public bool Usable;
        }

        private sealed class CalibrationReport
        {
            private readonly List<string> _lines = new List<string>();

            public string ScheduleName;
            public long ScheduleId;
            public bool Fatal;

            public void Add(string line)
            {
                _lines.Add(line ?? string.Empty);
            }

            public string ToText()
            {
                return string.Join(Environment.NewLine, _lines.ToArray())
                    + Environment.NewLine;
            }
        }

        private sealed class PreliminaryScanException : Exception
        {
            public PreliminaryScanException(string message)
                : base(message)
            {
            }
        }
    }
}
