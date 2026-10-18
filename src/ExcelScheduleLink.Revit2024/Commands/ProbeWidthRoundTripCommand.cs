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
    /// <summary>
    /// Probe-manifest-only diagnostic for Revit's native schedule width and
    /// height round trips. The command creates one temporary method-B schedule,
    /// runs every observation in one transaction, and never commits it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeWidthRoundTripCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Width and Height Round Trip";
        private const int CycleCount = 5;
        private const double CalibrationRelativeTolerance = 1e-9;
        private const double CalibrationResidualTolerance = 1e-6;
        private const double PixelIntegerTolerance = 1e-7;
        private const double FeetComparisonTolerance = 1e-12;

        private static readonly double[] WidthTargets = { 0.1, 0.5, 0.05 };
        private static readonly double[] RowTargets = { 0.025, 0.05, 0.1 };
        private static readonly double[] RowFallbackTargets = { 0.15, 0.2, 0.25, 0.3 };
        private const int RequiredAcceptedRowHeightTargets = 3;

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            ProbeReport report = new ProbeReport();
            Transaction transaction = null;
            TransactionStatus? finalStatus = null;

            try
            {
                AppendMetadata(commandData, doc, report);
                SheetModel sheet = CreateSyntheticSheet();

                transaction = new Transaction(
                    doc, "ESL Probe - width and height round trip (rolled back)");
                TransactionStatus start = transaction.Start();
                report.Add("transaction_start=" + start);
                if (start != TransactionStatus.Started)
                {
                    throw new InvalidOperationException(
                        "The probe transaction did not start: " + start);
                }

                ViewSchedule schedule = ScheduleFactory.Create(
                    doc, "ESL Width Round Trip Probe");
                report.Add("temporary_schedule_created=true id=" + schedule.Id.Value);
                report.Add("temporary_schedule_method=method-B");
                report.Add("synthetic_sheet=3 rows x 3 columns; one header merge");

                RunExperimentBoundary(
                    report, doc, schedule, sheet, "factory native default observation",
                    delegate { CaptureDimensions(report, schedule, "factory native default"); });
                RunExperimentBoundary(
                    report, doc, schedule, sheet, "real renderer",
                    delegate { RunInitialRenderer(report, schedule, sheet); });
                RunExperimentBoundary(
                    report, doc, schedule, sheet, "after real renderer observation",
                    delegate { CaptureDimensions(report, schedule, "after real renderer"); });
                report.Add("automatic_default_row_observation=recorded_above_before_explicit_row_writes");

                bool nativeDefaultReset = RunExperimentBoundary(
                    report, doc, schedule, sheet, "native default reset",
                    delegate
                    {
                        RebuildSyntheticHeader(
                            report, doc, schedule, sheet, "native default clear/grow");
                    });
                DimensionSnapshot nativeDefault = null;
                bool nativeDefaultCaptured = false;
                if (nativeDefaultReset)
                {
                    nativeDefaultCaptured = RunExperimentBoundary(
                        report, doc, schedule, sheet, "current native default observation",
                        delegate
                        {
                            nativeDefault = CaptureDimensions(
                                report, schedule, "current native default");
                        });
                    if (nativeDefaultCaptured)
                    {
                        RunExperimentBoundary(
                            report, doc, schedule, sheet, "current native default width",
                            delegate
                            {
                                RunWidthExperiment(
                                    report, doc, schedule,
                                    "current native default", null, nativeDefault);
                            });
                    }
                }
                else
                {
                    report.Add("current native default width=INCOMPLETE reset_failed");
                }

                foreach (double target in WidthTargets)
                {
                    string targetLabel = "width " + FormatR(target) + " ft";
                    bool reset = RunExperimentBoundary(
                        report, doc, schedule, sheet, targetLabel + " reset",
                        delegate
                        {
                            RebuildSyntheticHeader(
                                report, doc, schedule, sheet,
                                targetLabel + " reset");
                        });
                    if (reset)
                    {
                        RunExperimentBoundary(
                            report, doc, schedule, sheet, targetLabel,
                            delegate
                            {
                                RunWidthExperiment(
                                    report, doc, schedule,
                                    "requested " + FormatR(target) + " ft",
                                    target, null);
                            });
                    }
                    else
                    {
                        report.Add(targetLabel + "=INCOMPLETE reset_failed");
                    }
                }

                bool structuralReset = RunExperimentBoundary(
                    report, doc, schedule, sheet, "structural fixture reset",
                    delegate
                    {
                        RebuildSyntheticHeader(
                            report, doc, schedule, sheet, "structural fixture reset");
                    });
                if (structuralReset)
                {
                    RunExperimentBoundary(
                        report, doc, schedule, sheet, "structural experiments",
                        delegate { RunStructuralExperiments(report, doc, schedule, sheet); });
                }
                else
                {
                    report.Add("structural experiments=INCOMPLETE reset_failed");
                }
                RunExperimentBoundary(
                    report, doc, schedule, sheet, "row height experiments",
                    delegate { RunRowExperiments(report, doc, schedule, sheet); });
            }
            catch (Exception ex) when (ModelMutation.IsFatalRegeneration(ex))
            {
                ModelMutation.RecordFatalRegeneration();
                report.MarkFatalBoundary();
                report.Add("fatal_regeneration=" + ex.GetType().Name + ": "
                    + DescribeException(ex));
            }
            catch (Exception ex)
            {
                report.Add("ordinary_failure=" + ex.GetType().Name + ": "
                    + DescribeException(ex));
            }
            finally
            {
                // This is the only cleanup path. In particular, do not read the
                // document after a fatal regeneration exception.
                if (transaction != null)
                {
                    finalStatus = TransactionRollback.RollBackIfStarted(transaction);
                    transaction.Dispose();
                }
            }

            report.FinalRollbackStatus = finalStatus;
            report.Add("final_rollback_status="
                + (finalStatus.HasValue ? finalStatus.Value.ToString() : "not started"));
            report.Add("rollback_observed="
                + (finalStatus.HasValue
                    && finalStatus.Value == TransactionStatus.RolledBack));
            AppendSummary(report);

            string path = null;
            try
            {
                path = WriteReport(report);
            }
            catch (Exception ex)
            {
                string saveError = DescribeException(ex);
                string clipboardError;
                bool copied = TryCopyReportToClipboard(report, out clipboardError);
                path = null;

                string clipboardStatus = copied
                    ? "The collected report text was copied to the clipboard."
                    : "Clipboard copy failed: " + clipboardError;
                TaskDialog.Show(Title,
                    "The probe report could not be written: " + saveError
                    + "\n\n" + clipboardStatus);
                return Result.Failed;
            }

            bool rollbackObserved = finalStatus.HasValue
                && finalStatus.Value == TransactionStatus.RolledBack;
            string resultText = rollbackObserved
                ? "The temporary probe transaction was rolled back."
                : "Rollback was not observed as RolledBack; treat this report as incomplete evidence.";
            if (report.Fatal)
            {
                resultText = "The probe stopped at a fatal regeneration boundary. "
                    + (rollbackObserved
                        ? "Rollback was observed as RolledBack."
                        : "Rollback was not observed as RolledBack.");
            }
            TaskDialog.Show(Title,
                resultText + (path == null ? string.Empty : "\n\nReport:\n" + path));
            return Result.Succeeded;
        }

        private static bool RunExperimentBoundary(
            ProbeReport report, Document doc, ViewSchedule schedule,
            SheetModel sheet, string phase, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                report.RecordExperimentFailure(phase);
                report.Add("experiment_failure phase=" + phase + " type="
                    + ex.GetType().Name + " message=" + DescribeException(ex));
                try
                {
                    RebuildSyntheticHeader(
                        report, doc, schedule, sheet,
                        "recovery after " + phase);
                    report.Add("experiment_recovery phase=" + phase + "=restored");
                    return false;
                }
                catch (Exception recoveryException)
                    when (!ModelMutation.IsFatalRegeneration(recoveryException))
                {
                    report.Add("experiment_recovery phase=" + phase + "=failed type="
                        + recoveryException.GetType().Name + " message="
                        + DescribeException(recoveryException));
                    throw new ExperimentRecoveryException(phase, recoveryException);
                }
            }
        }

        private static void AppendMetadata(
            ExternalCommandData commandData, Document doc, ProbeReport report)
        {
            Autodesk.Revit.ApplicationServices.Application app =
                commandData.Application.Application;
            report.Add("revit_version=" + app.VersionNumber);
            report.Add("revit_build=" + app.VersionBuild);
            report.Add("revit_name=" + app.VersionName);
            report.Add("revit_username=" + (app.Username ?? "user"));
            report.Add("document_title=" + doc.Title);
            ForgeTypeId lengthUnit = doc.GetUnits()
                .GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
            report.Add("project_length_units="
                + (lengthUnit == null ? "(unavailable)" : lengthUnit.TypeId));
            report.Add("display_scaling=operator supplied value; the probe does not read screen DPI");
            report.Add("report_path=%APPDATA%\\ExcelScheduleLink\\WidthRoundTripProbe.txt");
        }

        private static SheetModel CreateSyntheticSheet()
        {
            string[,] cells = new string[3, 3]
            {
                { "Width probe A", "Width probe B", "Width probe C" },
                { "row 1", "value 1", "value 2" },
                { "row 2", "value 3", "value 4" }
            };
            List<MergedRange> merges = new List<MergedRange>
            {
                new MergedRange(0, 0, 0, 1)
            };
            List<ColumnInfo> columns = new List<ColumnInfo>
            {
                new ColumnInfo(0, 10.0),
                new ColumnInfo(1, 15.0),
                new ColumnInfo(2, 20.0)
            };
            return new SheetModel("ESL_WidthRoundTrip", cells, merges, columns, 3, 3);
        }

        private static void RunInitialRenderer(
            ProbeReport report, ViewSchedule schedule, SheetModel sheet)
        {
            try
            {
                HeaderTableRenderer.RenderResult render = HeaderTableRenderer.Render(
                    schedule, sheet, 1.0,
                    ColumnWidthMode.ApplyExcelWidths,
                    RowHeightMode.UseDefaultHeights);
                report.Add("real_renderer=completed");
                report.Add("real_renderer_columns_rejected=" + render.ColumnWidthsRejected);
                report.Add("real_renderer_body_width_applied=" + render.BodyWidthApplied);
                AppendIntegrity(report, "real_renderer_integrity", render.IntegrityResult);
            }
            catch (UnsafeScheduleStyleException ex)
            {
                report.Add("real_renderer=style_integrity_refusal");
                AppendIntegrity(report, "real_renderer_integrity", ex.Result);
                throw;
            }
        }

        private static void RebuildSyntheticHeader(
            ProbeReport report, Document doc, ViewSchedule schedule,
            SheetModel sheet, string label)
        {
            report.Add("BEGIN synthetic reset " + label);
            TableSectionData section = Header(schedule);
            HeaderTableRenderer.ClearTo1x1(section);
            GrowAndWriteSyntheticHeader(section, sheet);
            ApplySyntheticMerges(report, section, sheet, label);
            AppendSyntheticMergeReadback(report, section, sheet, label);

            double headerTotal;
            if (TrySumHeaderFeet(report, section, label, out headerTotal))
            {
                TrySyncBodyColumn(report, schedule, headerTotal, label);
            }
            else
            {
                report.Add(label + " body_sync=not_attempted_header_total_unavailable");
            }

            StyleIntegrityResult scan =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    doc, schedule, true);
            AppendIntegrity(report, label + " integrity", scan);
            report.Add("END synthetic reset " + label);
        }

        private static void GrowAndWriteSyntheticHeader(
            TableSectionData section, SheetModel sheet)
        {
            while (section.NumberOfRows < sheet.RowCount)
            {
                section.InsertRow(section.FirstRowNumber + 1);
            }
            while (section.NumberOfColumns < sheet.ColumnCount)
            {
                section.InsertColumn(section.FirstColumnNumber + 1);
            }

            int firstRow = section.FirstRowNumber;
            int firstColumn = section.FirstColumnNumber;
            for (int row = 0; row < sheet.RowCount; row++)
            {
                for (int column = 0; column < sheet.ColumnCount; column++)
                {
                    section.SetCellType(firstRow + row, firstColumn + column, CellType.Text);
                    section.SetCellText(
                        firstRow + row, firstColumn + column, sheet.GetText(row, column));
                }
            }
        }

        private static MergeOperationResult ApplySyntheticMerges(
            ProbeReport report, TableSectionData section, SheetModel sheet, string label)
        {
            MergeOperationResult result = new MergeOperationResult
            {
                Expected = sheet.MergedRanges.Count
            };
            int firstRow = section.FirstRowNumber;
            int firstColumn = section.FirstColumnNumber;
            foreach (MergedRange merge in sheet.MergedRanges)
            {
                try
                {
                    section.MergeCells(new TableMergedCell(
                        firstRow + merge.FirstRow,
                        firstColumn + merge.FirstColumn,
                        firstRow + merge.LastRow,
                        firstColumn + merge.LastColumn));
                    report.Add(label + " merge=accepted "
                        + merge.FirstRow + "," + merge.FirstColumn + "-"
                        + merge.LastRow + "," + merge.LastColumn);
                    result.Accepted++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    report.Add(label + " merge=native_refusal "
                        + DescribeException(ex) + " no_substitute=true");
                    result.Refused++;
                }
            }
            report.Add(label + " merge_operation expected=" + result.Expected
                + " accepted=" + result.Accepted
                + " refused=" + result.Refused);
            return result;
        }

        private static MergeReadbackResult AppendSyntheticMergeReadback(
            ProbeReport report, TableSectionData section, SheetModel sheet, string label)
        {
            MergeReadbackResult result = new MergeReadbackResult
            {
                Expected = sheet.MergedRanges.Count
            };
            int firstRow = section.FirstRowNumber;
            int firstColumn = section.FirstColumnNumber;
            foreach (MergedRange merge in sheet.MergedRanges)
            {
                try
                {
                    TableMergedCell actual = section.GetMergedCell(
                        firstRow + merge.FirstRow, firstColumn + merge.FirstColumn);
                    if (actual == null)
                    {
                        report.Add(label + " GetMergedCell=(null) no_substitute=true");
                        result.Unreadable++;
                    }
                    else
                    {
                        int expectedTop = firstRow + merge.FirstRow;
                        int expectedLeft = firstColumn + merge.FirstColumn;
                        int expectedBottom = firstRow + merge.LastRow;
                        int expectedRight = firstColumn + merge.LastColumn;
                        bool matches = actual.Top == expectedTop
                            && actual.Left == expectedLeft
                            && actual.Bottom == expectedBottom
                            && actual.Right == expectedRight;
                        result.Readable++;
                        if (matches)
                        {
                            result.Matches++;
                        }
                        else
                        {
                            result.Wrong++;
                        }
                        report.Add(label + " GetMergedCell=accepted top=" + actual.Top
                            + " left=" + actual.Left
                            + " bottom=" + actual.Bottom
                            + " right=" + actual.Right
                            + " expected_top=" + expectedTop
                            + " expected_left=" + expectedLeft
                            + " expected_bottom=" + expectedBottom
                            + " expected_right=" + expectedRight
                            + " matches=" + matches);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    report.Add(label + " GetMergedCell=native_refusal "
                        + DescribeException(ex) + " no_substitute=true");
                    result.Unreadable++;
                }
            }
            report.Add(label + " merge_readback expected=" + result.Expected
                + " readable=" + result.Readable
                + " matches=" + result.Matches
                + " wrong=" + result.Wrong
                + " unreadable=" + result.Unreadable);
            return result;
        }

        private static void RunWidthExperiment(
            ProbeReport report, Document doc, ViewSchedule schedule,
            string label, double? requestedFeet, DimensionSnapshot suppliedDefault)
        {
            report.Add("BEGIN COLUMN EXPERIMENT " + label);
            bool seedAccepted;
            if (requestedFeet.HasValue)
            {
                seedAccepted = SeedColumnsWithFeet(
                    report, schedule, requestedFeet.Value, label);
            }
            else
            {
                seedAccepted = SeedColumnsWithSnapshot(
                    report, schedule, suppliedDefault, label);
            }
            if (!seedAccepted)
            {
                report.MarkColumnExperimentIncomplete();
                report.Add("column_experiment=skipped_after_native_refusal no_substitute=true");
                report.Add("END COLUMN EXPERIMENT " + label);
                return;
            }

            DimensionSnapshot seed = CaptureDimensions(report, schedule, label + " raw seed");
            if (!HasCompleteColumns(seed))
            {
                report.MarkColumnExperimentIncomplete();
                report.Add("column_experiment=skipped_incomplete_seed no_substitute=true");
                report.Add("END COLUMN EXPERIMENT " + label);
                return;
            }

            RunFeetCycles(report, doc, schedule, seed, label);

            if (!ResetColumnsToPixels(report, schedule, seed, label + " integer reset"))
            {
                report.MarkColumnPixelExperimentIncomplete();
                report.Add("pixel_cycles=skipped_after_native_refusal no_substitute=true");
            }
            else
            {
                RunColumnPixelCycles(report, doc, schedule, seed, label);
            }

            if (!ResetColumnsToPixels(report, schedule, seed, label + " quantizer reset"))
            {
                report.MarkColumnPixelExperimentIncomplete();
                report.Add("quantized_pixel_cycles=skipped_after_native_refusal no_substitute=true");
            }
            else
            {
                List<double> requested = new List<double>();
                for (int index = 0; index < seed.Columns.Count; index++)
                {
                    requested.Add(requestedFeet.HasValue
                        ? requestedFeet.Value : seed.Columns[index].Feet);
                }
                RunQuantizedColumnExperiment(
                    report, doc, schedule, seed, requested, label);
            }
            report.Add("END COLUMN EXPERIMENT " + label);
        }

        private static bool SeedColumnsWithFeet(
            ProbeReport report, ViewSchedule schedule, double feet, string label)
        {
            TableSectionData section = Header(schedule);
            bool allAccepted = true;
            int firstColumn = section.FirstColumnNumber;
            for (int offset = 0; offset < section.NumberOfColumns; offset++)
            {
                allAccepted &= TrySetColumnFeet(
                    report, section, firstColumn + offset, feet,
                    label + " raw seed column " + offset);
            }
            return allAccepted;
        }

        private static bool SeedColumnsWithSnapshot(
            ProbeReport report, ViewSchedule schedule,
            DimensionSnapshot snapshot, string label)
        {
            if (!HasCompleteColumns(snapshot))
            {
                report.Add(label + " native default seed unavailable; no_substitute=true");
                return false;
            }
            TableSectionData section = Header(schedule);
            if (section.NumberOfColumns != snapshot.Columns.Count)
            {
                report.Add(label + " native default column count changed; no_substitute=true");
                return false;
            }
            bool allAccepted = true;
            foreach (PairObservation pair in snapshot.Columns)
            {
                allAccepted &= TrySetColumnFeet(
                    report, section, pair.Index, pair.Feet,
                    label + " raw default seed column " + pair.Index);
            }
            return allAccepted;
        }

        private static void RunFeetCycles(
            ProbeReport report, Document doc, ViewSchedule schedule,
            DimensionSnapshot seed, string label)
        {
            report.Add("BEGIN feet accumulation cycles " + label);
            foreach (PairObservation seedPair in seed.Columns)
            {
                double lastReadFeet = seedPair.Feet;
                for (int cycle = 1; cycle <= CycleCount; cycle++)
                {
                    TableSectionData section = Header(schedule);
                    if (!TrySetColumnFeet(
                        report, section, seedPair.Index, lastReadFeet,
                        label + " feet cycle " + cycle + " column " + seedPair.Index))
                    {
                        report.RecordFeetComparison(
                            "column", false, false);
                        report.RecordFeetComparison(
                            "column", false, false);
                        report.Add("feet cycle stopped after native refusal; no_substitute=true");
                        break;
                    }

                    PairObservation immediate = ReadColumnPair(schedule, seedPair.Index);
                    AppendPair(report, label + " feet cycle " + cycle + " immediate", immediate);
                    RecordFeetComparison(
                        report, "column", lastReadFeet,
                        immediate.Feet, immediate.FeetUsable);

                    doc.Regenerate();
                    PairObservation afterRegeneration = ReadColumnPair(schedule, seedPair.Index);
                    AppendPair(
                        report, label + " feet cycle " + cycle + " after_regeneration",
                        afterRegeneration);
                    RecordFeetComparison(
                        report, "column", lastReadFeet,
                        afterRegeneration.Feet, afterRegeneration.FeetUsable);
                    if (!afterRegeneration.FeetUsable)
                    {
                        report.Add("feet cycle stopped because the last read feet was unavailable; no_substitute=true");
                        break;
                    }
                    lastReadFeet = afterRegeneration.Feet;
                }
            }
            report.Add("END feet accumulation cycles " + label);
        }

        private static void RunColumnPixelCycles(
            ProbeReport report, Document doc, ViewSchedule schedule,
            DimensionSnapshot seed, string label)
        {
            report.Add("BEGIN integer pixel cycles " + label);
            foreach (PairObservation seedPair in seed.Columns)
            {
                for (int cycle = 1; cycle <= CycleCount; cycle++)
                {
                    TableSectionData section = Header(schedule);
                    if (!TrySetColumnPixels(
                        report, section, seedPair.Index, seedPair.Pixels,
                        label + " pixel cycle " + cycle + " column " + seedPair.Index))
                    {
                        report.RecordPixelObservation("column", "immediate", false, false);
                        report.RecordPixelObservation(
                            "column", "after_regeneration", false, false);
                        report.Add("pixel cycle stopped after native refusal; no_substitute=true");
                        break;
                    }

                    PairObservation immediate = ReadColumnPair(schedule, seedPair.Index);
                    AppendPair(report, label + " pixel cycle " + cycle + " immediate", immediate);
                    RecordPixelComparison(
                        report, seedPair.Pixels, immediate.Pixels,
                        immediate.PixelsUsable, "column", "immediate");

                    doc.Regenerate();
                    PairObservation afterRegeneration = ReadColumnPair(schedule, seedPair.Index);
                    AppendPair(
                        report, label + " pixel cycle " + cycle + " after_regeneration",
                        afterRegeneration);
                    RecordPixelComparison(
                        report, seedPair.Pixels, afterRegeneration.Pixels,
                        afterRegeneration.PixelsUsable, "column", "after_regeneration");
                }
            }
            report.Add("END integer pixel cycles " + label);
        }

        private static bool ResetColumnsToPixels(
            ProbeReport report, ViewSchedule schedule,
            DimensionSnapshot seed, string label)
        {
            TableSectionData section = Header(schedule);
            bool allAccepted = true;
            foreach (PairObservation pair in seed.Columns)
            {
                if (!pair.PixelsUsable)
                {
                    report.Add(label + " column " + pair.Index
                        + " seed pixel unavailable; no_substitute=true");
                    allAccepted = false;
                    continue;
                }
                allAccepted &= TrySetColumnPixels(
                    report, section, pair.Index, pair.Pixels,
                    label + " column " + pair.Index);
            }
            return allAccepted;
        }

        private static void RunQuantizedColumnExperiment(
            ProbeReport report, Document doc, ViewSchedule schedule,
            DimensionSnapshot seed, IList<double> requestedFeet, string label)
        {
            CalibrationResult calibration = Calibrate(seed.Columns);
            AppendCalibration(report, label + " runtime calibration", calibration);
            if (!calibration.Succeeded)
            {
                report.MarkColumnPixelExperimentIncomplete();
                report.Add("quantizer=skipped_after_calibration_failure no_substitute=true");
                return;
            }
            int[] expectedPixels = new int[requestedFeet.Count];
            for (int index = 0; index < requestedFeet.Count; index++)
            {
                string reason;
                if (!TryNearestPixel(
                    requestedFeet[index], calibration.FeetPerPixel,
                    out expectedPixels[index], out reason))
                {
                    report.MarkColumnPixelExperimentIncomplete();
                    report.Add(label + " quantizer requested_feet_R="
                        + FormatR(requestedFeet[index])
                        + " refusal=" + reason + " no_substitute=true");
                    return;
                }
            }
            report.Add(label + " quantizer=nearest_pixel expected_vector="
                + FormatPixels(expectedPixels));

            for (int round = 1; round <= 2; round++)
            {
                TableSectionData section = Header(schedule);
                bool allAccepted = true;
                for (int index = 0; index < expectedPixels.Length; index++)
                {
                    allAccepted &= TrySetColumnPixels(
                        report, section, seed.Columns[index].Index, expectedPixels[index],
                        label + " quantized round " + round + " column " + index);
                }

                List<PairObservation> immediate = ReadColumnVector(
                    report, schedule, label + " quantized round " + round + " immediate");
                AppendCalibrationForPairs(
                    report, label + " quantized round " + round + " immediate calibration",
                    immediate);
                ComparePixelVector(
                    report, expectedPixels, immediate, allAccepted,
                    label + " quantized round " + round + " immediate");

                doc.Regenerate();
                List<PairObservation> afterRegeneration = ReadColumnVector(
                    report, schedule,
                    label + " quantized round " + round + " after_regeneration");
                AppendCalibrationForPairs(
                    report,
                    label + " quantized round " + round + " after_regeneration calibration",
                    afterRegeneration);
                ComparePixelVector(
                    report, expectedPixels, afterRegeneration, allAccepted,
                    label + " quantized round " + round + " after_regeneration");
            }
        }

        private static List<PairObservation> ReadColumnVector(
            ProbeReport report, ViewSchedule schedule, string label)
        {
            TableSectionData section = Header(schedule);
            List<PairObservation> result = new List<PairObservation>();
            int firstColumn = section.FirstColumnNumber;
            for (int offset = 0; offset < section.NumberOfColumns; offset++)
            {
                PairObservation pair = ReadColumnPair(schedule, firstColumn + offset);
                result.Add(pair);
                AppendPair(report, label + " column " + offset, pair);
            }
            return result;
        }

        private static void ComparePixelVector(
            ProbeReport report, IList<int> expected,
            IList<PairObservation> actual, bool evaluate, string label)
        {
            bool immediate = label.IndexOf("immediate", StringComparison.OrdinalIgnoreCase) >= 0;
            string phase = immediate ? "immediate" : "after_regeneration";
            bool allEqual = evaluate
                && expected.Count == (actual == null ? 0 : actual.Count);
            for (int index = 0; index < expected.Count; index++)
            {
                PairObservation pair = actual != null && index < actual.Count
                    ? actual[index] : null;
                if (!evaluate || pair == null || !pair.PixelsUsable)
                {
                    allEqual = false;
                    report.RecordPixelObservation("column", phase, false, false);
                    report.Add(label + " column " + index
                        + " exact_pixel=unknown native_refusal="
                        + (pair == null
                            ? "unavailable"
                            : (pair.PixelsRefusal ?? "unavailable")));
                    continue;
                }
                bool equal = pair.Pixels == expected[index];
                report.RecordPixelObservation("column", phase, true, equal);
                if (!equal)
                {
                    allEqual = false;
                }
                report.Add(label + " column " + index
                    + " expected_pixels=" + expected[index]
                    + " actual_pixels=" + pair.Pixels
                    + " exact=" + equal);
            }
            report.Add(label + " exact_pixel_vector=" + allEqual
                + " expected=" + FormatPixels(expected));
        }

        private static void RunStructuralExperiments(
            ProbeReport report, Document doc, ViewSchedule schedule, SheetModel sheet)
        {
            report.Add("BEGIN clear/grow/restore, merge, body-sync and integrity experiments");
            DimensionSnapshot automaticDefault = CaptureDimensions(
                report, schedule, "structural automatic/default baseline");
            report.Add("structural_automatic_default_baseline_scope=row_heights_before_manual_seed");
            List<ManualRowSeed> manualRows = SeedManualStructuralRows(report, schedule);
            DimensionSnapshot before = CaptureDimensions(
                report, schedule, "structural manual-height baseline");
            AppendManualRowVector(
                report, "structural manual-height baseline", before, manualRows);
            report.Add("structural_manual_height_coverage=accepted=" + manualRows.Count
                + " required=" + RequiredAcceptedRowHeightTargets
                + " complete=" + (manualRows.Count >= RequiredAcceptedRowHeightTargets));
            report.StructuralManualCoverageIncomplete =
                manualRows.Count < RequiredAcceptedRowHeightTargets;
            RecordMovement(
                report, "manual_seed_vs_automatic_default", automaticDefault, before);

            TableSectionData section = Header(schedule);
            bool clearAccepted = false;
            HeaderTableRenderer.ClearTo1x1(section);
            clearAccepted = true;
            DimensionSnapshot afterClear = CaptureDimensions(report, schedule, "structural after clear");
            RecordMovement(report, "clear", before, afterClear);
            RecordManualVectorMovement(report, "clear", before, afterClear, manualRows);
            AppendManualRowVector(report, "structural after clear", afterClear, manualRows);
            bool clearReadbackSucceeded = HasCompleteDimensions(afterClear);
            RecordStructuralPhase(
                report, "clear", clearAccepted, clearReadbackSucceeded);

            bool growAccepted = false;
            GrowAndWriteSyntheticHeader(section, sheet);
            growAccepted = true;
            DimensionSnapshot afterGrow = CaptureDimensions(report, schedule, "structural after grow");
            RecordMovement(report, "grow", afterClear, afterGrow);
            RecordMovement(report, "clear_grow", before, afterGrow);
            RecordManualVectorMovement(report, "grow", afterClear, afterGrow, manualRows);
            RecordManualVectorMovement(report, "clear_grow", before, afterGrow, manualRows);
            AppendManualRowVector(report, "structural after grow", afterGrow, manualRows);
            bool growReadbackSucceeded = HasCompleteDimensions(afterGrow);
            RecordStructuralPhase(
                report, "grow", growAccepted, growReadbackSucceeded);
            RecordStructuralPhase(
                report, "clear_grow", clearAccepted && growAccepted,
                clearReadbackSucceeded && growReadbackSucceeded);

            bool restoreAccepted = RestoreSnapshotPixels(report, schedule, before, "structural restore");
            DimensionSnapshot afterRestore = CaptureDimensions(
                report, schedule, "structural after restore");
            RecordMovement(report, "restore", before, afterRestore);
            RecordManualVectorMovement(report, "restore", before, afterRestore, manualRows);
            AppendManualRowVector(report, "structural after restore", afterRestore, manualRows);
            report.Add("structural_restore_all_setters_accepted=" + restoreAccepted);
            RecordStructuralPhase(
                report, "restore", restoreAccepted, HasCompleteDimensions(afterRestore));

            section = Header(schedule);
            MergeOperationResult mergeOperation = ApplySyntheticMerges(
                report, section, sheet, "structural merge");
            MergeReadbackResult mergeReadback = AppendSyntheticMergeReadback(
                report, section, sheet, "structural merge");
            DimensionSnapshot afterMerge = CaptureDimensions(report, schedule, "structural after merge");
            RecordMovement(report, "merge", afterRestore, afterMerge);
            RecordManualVectorMovement(report, "merge", afterRestore, afterMerge, manualRows);
            AppendManualRowVector(report, "structural after merge", afterMerge, manualRows);
            bool mergeAccepted = mergeOperation.Expected > 0
                && mergeOperation.Accepted == mergeOperation.Expected
                && mergeOperation.Refused == 0;
            bool mergeReadbackSucceeded = mergeReadback.Expected > 0
                && mergeReadback.Readable == mergeReadback.Expected
                && mergeReadback.Matches == mergeReadback.Expected;
            RecordStructuralPhase(
                report, "merge", mergeAccepted, mergeReadbackSucceeded);

            BodyWidths bodyBefore = ReadBodyWidths(schedule);
            DimensionSnapshot beforeBody = CaptureDimensions(report, schedule, "body-sync before");
            AppendManualRowVector(report, "body-sync before", beforeBody, manualRows);
            double headerTotal;
            bool headerTotalRead = TrySumHeaderFeet(
                report, Header(schedule), "body-sync", out headerTotal);
            bool bodyApplied = headerTotalRead
                && TrySyncBodyColumn(report, schedule, headerTotal, "body-sync");
            BodyWidths bodyAfter = ReadBodyWidths(schedule);
            report.Add("body-sync applied=" + bodyApplied);
            AppendBodyWidths(report, "body-sync before", bodyBefore);
            AppendBodyWidths(report, "body-sync after", bodyAfter);
            double bodyTarget = headerTotalRead ? BodySyncTarget(headerTotal) : 0.0;
            bool bodyReadbackSucceeded = bodyApplied
                && BodyWidthsMatchTarget(bodyAfter, bodyTarget);
            report.Add("body-sync required_readback_succeeded="
                + bodyReadbackSucceeded
                + " expected_feet_R=" + (headerTotalRead ? FormatR(bodyTarget) : "(unavailable)"));
            DimensionSnapshot afterBody = CaptureDimensions(report, schedule, "body-sync after");
            RecordMovement(report, "body_sync", beforeBody, afterBody);
            RecordManualVectorMovement(report, "body_sync", beforeBody, afterBody, manualRows);
            AppendManualRowVector(report, "body-sync after", afterBody, manualRows);
            RecordStructuralPhase(
                report, "body_sync", bodyApplied, bodyReadbackSucceeded);

            StyleIntegrityResult scan =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    doc, schedule, true);
            AppendIntegrity(report, "structural post-mutation integrity", scan);
            DimensionSnapshot afterScan = CaptureDimensions(
                report, schedule, "integrity scan after regeneration");
            RecordMovement(report, "integrity_scan", afterBody, afterScan);
            RecordManualVectorMovement(report, "integrity_scan", afterBody, afterScan, manualRows);
            AppendManualRowVector(report, "integrity scan after regeneration", afterScan, manualRows);
            RecordStructuralPhase(
                report, "integrity_scan", scan != null,
                scan != null && HasCompleteDimensions(afterScan));
            report.Add("END clear/grow/restore, merge, body-sync and integrity experiments");
        }

        private static List<ManualRowSeed> SeedManualStructuralRows(
            ProbeReport report, ViewSchedule schedule)
        {
            List<ManualRowSeed> result = new List<ManualRowSeed>();
            TableSectionData section = Header(schedule);
            int rowCount = Math.Min(3, section.NumberOfRows);
            HashSet<double> usedTargets = new HashSet<double>();
            for (int offset = 0; offset < rowCount; offset++)
            {
                int row = section.FirstRowNumber + offset;
                bool accepted = false;
                foreach (double candidate in RowHeightCandidates())
                {
                    if (usedTargets.Contains(candidate))
                    {
                        continue;
                    }
                    string label = "structural manual row " + offset
                        + " candidate " + FormatR(candidate) + " ft";
                    if (!TrySetRowFeet(report, section, row, candidate, label))
                    {
                        continue;
                    }

                    PairObservation observed = ReadRowPair(schedule, row);
                    AppendPair(report, label + " observed seed", observed);
                    if (!observed.Complete)
                    {
                        report.Add(label
                            + " incomplete_after_accepted_setter; no_substitute=true");
                        continue;
                    }
                    usedTargets.Add(candidate);
                    result.Add(new ManualRowSeed
                    {
                        Index = row,
                        RequestedFeet = candidate,
                        Seed = observed
                    });
                    accepted = true;
                    break;
                }
                if (!accepted)
                {
                    report.Add("structural manual row " + offset
                        + "=INCOMPLETE no_accepted_candidate=true");
                }
            }
            return result;
        }

        private static IEnumerable<double> RowHeightCandidates()
        {
            foreach (double target in RowTargets)
            {
                yield return target;
            }
            foreach (double target in RowFallbackTargets)
            {
                yield return target;
            }
        }

        private static void AppendManualRowVector(
            ProbeReport report, string label, DimensionSnapshot snapshot,
            IList<ManualRowSeed> manualRows)
        {
            if (manualRows == null || manualRows.Count == 0)
            {
                report.Add(label + " manual_row_height_vector=(none) state=UNKNOWN");
                return;
            }
            foreach (ManualRowSeed seed in manualRows)
            {
                PairObservation actual = FindPair(snapshot == null ? null : snapshot.Rows, seed.Index);
                if (actual == null)
                {
                    report.Add(label + " manual_row index=" + seed.Index
                        + " requested_feet_R=" + FormatR(seed.RequestedFeet)
                        + " seed_pixels=" + seed.Seed.Pixels
                        + " actual=(unavailable) no_substitute=true");
                    continue;
                }
                AppendPair(report, label + " manual_row index=" + seed.Index, actual);
                report.Add(label + " manual_row index=" + seed.Index
                    + " requested_feet_R=" + FormatR(seed.RequestedFeet)
                    + " seed_pixels=" + seed.Seed.Pixels
                    + " actual_pixels="
                    + (actual.PixelsUsable ? actual.Pixels.ToString(CultureInfo.InvariantCulture) : "(unavailable)"));
            }
        }

        private static void RecordManualVectorMovement(
            ProbeReport report, string label, DimensionSnapshot before,
            DimensionSnapshot after, IList<ManualRowSeed> manualRows)
        {
            ManualVectorComparison comparison = CompareManualRowVector(
                before, after, manualRows);
            string state = ManualVectorState(comparison);
            if (report.StructuralManualCoverageIncomplete)
            {
                state = "INCOMPLETE";
            }
            report.Add(label + " manual_row_height_vector_moved=" + state
                + " expected=" + comparison.Expected
                + " comparable=" + comparison.Comparable
                + " unreadable=" + comparison.Unreadable
                + " mismatches=" + comparison.Mismatches);
            report.RecordManualVectorState(label, state);
        }

        private static ManualVectorComparison CompareManualRowVector(
            DimensionSnapshot before, DimensionSnapshot after,
            IList<ManualRowSeed> manualRows)
        {
            ManualVectorComparison result = new ManualVectorComparison();
            if (manualRows == null || manualRows.Count == 0)
            {
                return result;
            }
            result.Expected = manualRows.Count;
            foreach (ManualRowSeed seed in manualRows)
            {
                PairObservation left = FindPair(
                    before == null ? null : before.Rows, seed.Index);
                PairObservation right = FindPair(
                    after == null ? null : after.Rows, seed.Index);
                if (left == null || right == null
                    || !left.Complete || !right.Complete)
                {
                    result.Unreadable++;
                    continue;
                }
                result.Comparable++;
                if (left.Pixels != right.Pixels
                    || !NearlyEqualFeet(left.Feet, right.Feet))
                {
                    result.Mismatches++;
                }
            }
            return result;
        }

        private static PairObservation FindPair(
            IList<PairObservation> pairs, int index)
        {
            if (pairs == null)
            {
                return null;
            }
            foreach (PairObservation pair in pairs)
            {
                if (pair.Index == index)
                {
                    return pair;
                }
            }
            return null;
        }

        private static void RunRowExperiments(
            ProbeReport report, Document doc, ViewSchedule schedule, SheetModel sheet)
        {
            report.Add("BEGIN row height integer experiments");
            report.Add("row_height_proposed_targets_R=0.025,0.05,0.1");
            report.Add("row_height_fallback_targets_R=0.15,0.2,0.25,0.3");
            int acceptedTargetCount = 0;
            int attemptedTargetCount = 0;
            foreach (double requestedFeet in RowHeightCandidates())
            {
                if (acceptedTargetCount >= RequiredAcceptedRowHeightTargets)
                {
                    break;
                }
                attemptedTargetCount++;
                string targetLabel = "row height " + FormatR(requestedFeet) + " ft";
                bool reset = RunExperimentBoundary(
                    report, doc, schedule, sheet, targetLabel + " reset",
                    delegate
                    {
                        RebuildSyntheticHeader(
                            report, doc, schedule, sheet, targetLabel + " reset");
                    });
                if (!reset)
                {
                    report.Add(targetLabel + "=INCOMPLETE reset_failed");
                    continue;
                }

                int acceptedRows = 0;
                bool targetCompleted = RunExperimentBoundary(
                    report, doc, schedule, sheet, targetLabel,
                    delegate
                    {
                        acceptedRows = RunAcceptedRowHeightTarget(
                            report, doc, schedule, requestedFeet);
                    });
                if (targetCompleted && acceptedRows > 0)
                {
                    acceptedTargetCount++;
                }
                report.Add("row height target=" + FormatR(requestedFeet)
                    + " accepted_rows=" + acceptedRows
                    + " accepted_target=" + (targetCompleted && acceptedRows > 0));
            }
            report.AcceptedRowHeightTargets = acceptedTargetCount;
            report.RowHeightTargetsAttempted = attemptedTargetCount;
            if (acceptedTargetCount < RequiredAcceptedRowHeightTargets)
            {
                report.MarkRowExperimentIncomplete();
            }
            report.Add("row height target coverage=accepted=" + acceptedTargetCount
                + " required=" + RequiredAcceptedRowHeightTargets
                + " attempted=" + attemptedTargetCount
                + " complete=" + (acceptedTargetCount >= RequiredAcceptedRowHeightTargets));
            report.Add("END row height integer experiments");
        }

        private static int RunAcceptedRowHeightTarget(
            ProbeReport report, Document doc, ViewSchedule schedule, double requestedFeet)
        {
            TableSectionData section = Header(schedule);
            int firstRow = section.FirstRowNumber;
            int rowCount = Math.Min(3, section.NumberOfRows);
            int acceptedRows = 0;
            for (int offset = 0; offset < rowCount; offset++)
            {
                int row = firstRow + offset;
                string role = offset == 0
                    ? "first_surviving_row"
                    : "inserted_row_" + offset;
                if (!TrySetRowFeet(
                    report, section, row, requestedFeet,
                    "row " + FormatR(requestedFeet) + " ft " + role))
                {
                    continue;
                }

                PairObservation seed = ReadRowPair(schedule, row);
                AppendPair(
                    report,
                    "row " + FormatR(requestedFeet) + " ft " + role + " raw seed",
                    seed);
                RecordFeetComparison(
                    report, "row", requestedFeet, seed.Feet, seed.FeetUsable);
                if (!seed.FeetUsable || !seed.PixelsUsable)
                {
                    report.Add("row " + role
                        + " seed incomplete; no_substitute=true");
                    continue;
                }
                acceptedRows++;
                RunRowPixelCycles(report, doc, schedule, seed, role, requestedFeet);
            }
            return acceptedRows;
        }

        private static void RunRowPixelCycles(
            ProbeReport report, Document doc, ViewSchedule schedule,
            PairObservation seed, string role, double requestedFeet)
        {
            for (int cycle = 1; cycle <= CycleCount; cycle++)
            {
                TableSectionData section = Header(schedule);
                if (!TrySetRowPixels(
                    report, section, seed.Index, seed.Pixels,
                    "row " + FormatR(requestedFeet) + " ft " + role
                    + " pixel cycle " + cycle))
                {
                    report.RecordPixelObservation("row", "immediate", false, false);
                    report.RecordPixelObservation(
                        "row", "after_regeneration", false, false);
                    report.Add("row pixel cycle stopped after native refusal; no_substitute=true");
                    break;
                }

                PairObservation immediate = ReadRowPair(schedule, seed.Index);
                AppendPair(
                    report,
                    "row " + FormatR(requestedFeet) + " ft " + role
                    + " pixel cycle " + cycle + " immediate",
                    immediate);
                RecordPixelComparison(
                    report, seed.Pixels, immediate.Pixels,
                    immediate.PixelsUsable, "row", "immediate");

                doc.Regenerate();
                PairObservation afterRegeneration = ReadRowPair(schedule, seed.Index);
                AppendPair(
                    report,
                    "row " + FormatR(requestedFeet) + " ft " + role
                    + " pixel cycle " + cycle + " after_regeneration",
                    afterRegeneration);
                RecordPixelComparison(
                    report, seed.Pixels, afterRegeneration.Pixels,
                    afterRegeneration.PixelsUsable,
                    "row", "after_regeneration");
            }
        }

        private static bool RestoreSnapshotPixels(
            ProbeReport report, ViewSchedule schedule,
            DimensionSnapshot snapshot, string label)
        {
            TableSectionData section = Header(schedule);
            bool allAccepted = true;
            foreach (PairObservation pair in snapshot.Columns)
            {
                if (!pair.PixelsUsable)
                {
                    report.Add(label + " column " + pair.Index
                        + " seed pixel unavailable; no_substitute=true");
                    allAccepted = false;
                    continue;
                }
                allAccepted &= TrySetColumnPixels(
                    report, section, pair.Index, pair.Pixels,
                    label + " column " + pair.Index);
            }
            foreach (PairObservation pair in snapshot.Rows)
            {
                if (!pair.PixelsUsable)
                {
                    report.Add(label + " row " + pair.Index
                        + " seed pixel unavailable; no_substitute=true");
                    allAccepted = false;
                    continue;
                }
                allAccepted &= TrySetRowPixels(
                    report, section, pair.Index, pair.Pixels,
                    label + " row " + pair.Index);
            }
            return allAccepted;
        }

        private static bool TrySetColumnFeet(
            ProbeReport report, TableSectionData section, int column,
            double feet, string label)
        {
            try
            {
                section.SetColumnWidth(column, feet);
                report.Add(label + " SetColumnWidth(feet_R=" + FormatR(feet) + ")=accepted");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add(label + " SetColumnWidth(feet_R=" + FormatR(feet)
                    + ")=native_refusal " + DescribeException(ex)
                    + " no_substitute=true");
                return false;
            }
        }

        private static bool TrySetColumnPixels(
            ProbeReport report, TableSectionData section, int column,
            int pixels, string label)
        {
            try
            {
                section.SetColumnWidthInPixels(column, pixels);
                report.Add(label + " SetColumnWidthInPixels(pixels=" + pixels + ")=accepted");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add(label + " SetColumnWidthInPixels(pixels=" + pixels
                    + ")=native_refusal " + DescribeException(ex)
                    + " no_substitute=true");
                return false;
            }
        }

        private static bool TrySetRowFeet(
            ProbeReport report, TableSectionData section, int row,
            double feet, string label)
        {
            try
            {
                section.SetRowHeight(row, feet);
                report.Add(label + " SetRowHeight(feet_R=" + FormatR(feet) + ")=accepted");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add(label + " SetRowHeight(feet_R=" + FormatR(feet)
                    + ")=native_refusal " + DescribeException(ex)
                    + " no_substitute=true");
                return false;
            }
        }

        private static bool TrySetRowPixels(
            ProbeReport report, TableSectionData section, int row,
            int pixels, string label)
        {
            try
            {
                section.SetRowHeightInPixels(row, pixels);
                report.Add(label + " SetRowHeightInPixels(pixels=" + pixels + ")=accepted");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                report.Add(label + " SetRowHeightInPixels(pixels=" + pixels
                    + ")=native_refusal " + DescribeException(ex)
                    + " no_substitute=true");
                return false;
            }
        }

        private static bool TrySumHeaderFeet(
            ProbeReport report, TableSectionData section, string label, out double total)
        {
            total = 0.0;
            int firstColumn = section.FirstColumnNumber;
            for (int offset = 0; offset < section.NumberOfColumns; offset++)
            {
                int column = firstColumn + offset;
                double feet;
                try
                {
                    feet = section.GetColumnWidth(column);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    report.Add(label + " GetColumnWidth(column=" + column
                        + ")=native_refusal " + DescribeException(ex)
                        + " no_substitute=true");
                    total = 0.0;
                    return false;
                }
                if (!IsPositiveFinite(feet))
                {
                    report.Add(label + " GetColumnWidth(column=" + column
                        + ")=native_nonpositive feet_R=" + FormatR(feet)
                        + " no_substitute=true");
                    total = 0.0;
                    return false;
                }
                total += feet;
            }
            report.Add(label + " header_total_feet_R=" + FormatR(total));
            return true;
        }

        private static bool TrySyncBodyColumn(
            ProbeReport report, ViewSchedule schedule, double headerTotal, string label)
        {
            if (!IsPositiveFinite(headerTotal))
            {
                report.Add(label + " body_sync=native_call_not_attempted_invalid_total");
                return false;
            }
            ScheduleDefinition definition = schedule.Definition;
            int fieldCount = definition.GetFieldCount();
            if (fieldCount != 1)
            {
                report.Add(label + " body_sync=not_method_B_field_count=" + fieldCount);
                return false;
            }
            ScheduleField field = definition.GetField(0);
            double target = BodySyncTarget(headerTotal);
            try
            {
                field.GridColumnWidth = target;
                report.Add(label + " ScheduleField.GridColumnWidth(feet_R="
                    + FormatR(target) + ")=accepted");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                when (!ModelMutation.IsFatalRegeneration(ex))
            {
                report.Add(label + " ScheduleField.GridColumnWidth(feet_R="
                    + FormatR(target) + ")=native_refusal " + DescribeException(ex)
                    + " no_substitute=true");
                return false;
            }
        }

        private static double BodySyncTarget(double headerTotal)
        {
            return headerTotal
                + LayoutMetrics.BodyWidthMarginInches / LayoutMetrics.InchesPerFoot;
        }

        private static BodyWidths ReadBodyWidths(ViewSchedule schedule)
        {
            ScheduleDefinition definition = schedule.Definition;
            if (definition.GetFieldCount() != 1)
            {
                return new BodyWidths { Failure = "field_count_not_one" };
            }
            ScheduleField field = definition.GetField(0);
            try
            {
                return new BodyWidths
                {
                    GridReadable = true,
                    SheetReadable = true,
                    Grid = field.GridColumnWidth,
                    Sheet = field.SheetColumnWidth
                };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return new BodyWidths
                {
                    Failure = "native_refusal: " + DescribeException(ex)
                        + " no_substitute=true"
                };
            }
        }

        private static void AppendBodyWidths(
            ProbeReport report, string label, BodyWidths widths)
        {
            if (widths == null || !widths.GridReadable || !widths.SheetReadable)
            {
                report.Add(label + " GridColumnWidth/SheetColumnWidth=unavailable failure="
                    + (widths == null ? "unknown" : widths.Failure));
                return;
            }
            report.Add(label + " GridColumnWidth_feet_R=" + FormatR(widths.Grid)
                + " SheetColumnWidth_feet_R=" + FormatR(widths.Sheet));
        }

        private static bool BodyWidthsMatchTarget(BodyWidths widths, double target)
        {
            return widths != null
                && widths.GridReadable
                && IsPositiveFinite(target)
                && NearlyEqualFeet(widths.Grid, target);
        }

        private static DimensionSnapshot CaptureDimensions(
            ProbeReport report, ViewSchedule schedule, string label)
        {
            TableSectionData section = Header(schedule);
            DimensionSnapshot snapshot = new DimensionSnapshot
            {
                Label = label,
                NumberOfRows = section.NumberOfRows,
                NumberOfColumns = section.NumberOfColumns,
                FirstRow = section.FirstRowNumber,
                FirstColumn = section.FirstColumnNumber
            };
            report.Add("BEGIN DIMENSIONS " + label
                + " rows=" + snapshot.NumberOfRows
                + " columns=" + snapshot.NumberOfColumns
                + " first_row=" + snapshot.FirstRow
                + " first_column=" + snapshot.FirstColumn);

            for (int offset = 0; offset < snapshot.NumberOfColumns; offset++)
            {
                PairObservation pair = ReadColumnPair(
                    schedule, snapshot.FirstColumn + offset);
                snapshot.Columns.Add(pair);
                AppendPair(report, label + " column_offset=" + offset, pair);
            }
            for (int offset = 0; offset < snapshot.NumberOfRows; offset++)
            {
                PairObservation pair = ReadRowPair(
                    schedule, snapshot.FirstRow + offset);
                snapshot.Rows.Add(pair);
                AppendPair(report, label + " row_offset=" + offset, pair);
            }
            report.Add("END DIMENSIONS " + label);
            return snapshot;
        }

        private static PairObservation ReadColumnPair(ViewSchedule schedule, int column)
        {
            TableSectionData section = Header(schedule);
            PairObservation pair = new PairObservation
            {
                Index = column,
                IsRow = false
            };
            try
            {
                pair.Feet = section.GetColumnWidth(column);
                pair.FeetGetterSucceeded = true;
                pair.FeetUsable = IsPositiveFinite(pair.Feet);
                if (!pair.FeetUsable)
                {
                    pair.FeetRefusal = "native_nonpositive no_substitute=true";
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                pair.FeetRefusal = "native_refusal: " + DescribeException(ex)
                    + " no_substitute=true";
            }
            try
            {
                pair.Pixels = section.GetColumnWidthInPixels(column);
                pair.PixelsGetterSucceeded = true;
                pair.PixelsUsable = pair.Pixels > 0;
                if (!pair.PixelsUsable)
                {
                    pair.PixelsRefusal = "native_nonpositive no_substitute=true";
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                pair.PixelsRefusal = "native_refusal: " + DescribeException(ex)
                    + " no_substitute=true";
            }
            return pair;
        }

        private static PairObservation ReadRowPair(ViewSchedule schedule, int row)
        {
            TableSectionData section = Header(schedule);
            PairObservation pair = new PairObservation
            {
                Index = row,
                IsRow = true
            };
            try
            {
                pair.Feet = section.GetRowHeight(row);
                pair.FeetGetterSucceeded = true;
                pair.FeetUsable = IsPositiveFinite(pair.Feet);
                if (!pair.FeetUsable)
                {
                    pair.FeetRefusal = "native_nonpositive no_substitute=true";
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                pair.FeetRefusal = "native_refusal: " + DescribeException(ex)
                    + " no_substitute=true";
            }
            try
            {
                pair.Pixels = section.GetRowHeightInPixels(row);
                pair.PixelsGetterSucceeded = true;
                pair.PixelsUsable = pair.Pixels > 0;
                if (!pair.PixelsUsable)
                {
                    pair.PixelsRefusal = "native_nonpositive no_substitute=true";
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                pair.PixelsRefusal = "native_refusal: " + DescribeException(ex)
                    + " no_substitute=true";
            }
            return pair;
        }

        private static void AppendPair(
            ProbeReport report, string label, PairObservation pair)
        {
            report.RecordScaleObservation(pair);
            StringBuilder line = new StringBuilder();
            line.Append(label);
            line.Append(" index=").Append(pair.Index);
            line.Append(" feet_R=").Append(
                pair.FeetGetterSucceeded ? FormatR(pair.Feet) : "(unavailable)");
            line.Append(" mm_R=").Append(
                pair.FeetUsable
                    ? FormatR(UnitUtils.ConvertFromInternalUnits(pair.Feet, UnitTypeId.Millimeters))
                    : "(unavailable)");
            line.Append(" in_R=").Append(
                pair.FeetUsable
                    ? FormatR(UnitUtils.ConvertFromInternalUnits(pair.Feet, UnitTypeId.Inches))
                    : "(unavailable)");
            line.Append(" pixels=").Append(
                pair.PixelsGetterSucceeded ? pair.Pixels.ToString(CultureInfo.InvariantCulture) : "(unavailable)");
            line.Append(" ratio_feet_per_pixel_R=").Append(
                pair.Complete ? FormatR(pair.Feet / pair.Pixels) : "(unavailable)");
            if (!string.IsNullOrEmpty(pair.FeetRefusal))
            {
                line.Append(" feet_note=").Append(pair.FeetRefusal);
            }
            if (!string.IsNullOrEmpty(pair.PixelsRefusal))
            {
                line.Append(" pixel_note=").Append(pair.PixelsRefusal);
            }
            report.Add(line.ToString());
        }

        private static void AppendIntegrity(
            ProbeReport report, string label, StyleIntegrityResult result)
        {
            if (result == null)
            {
                report.Add(label + "=(none)");
                return;
            }
            report.Add(label + "=" + result.State + "/" + result.ReasonCode
                + " styleless=" + result.StylelessCellCount
                + " first_error=" + (result.FirstReadError ?? string.Empty));
        }

        // Diagnostic-only calibration for this probe; it is not production conversion logic.
        private static CalibrationResult Calibrate(IList<PairObservation> pairs)
        {
            return CalibrateValues(pairs);
        }

        private static CalibrationResult CalibrateValues(IList<PairObservation> pairs)
        {
            CalibrationResult result = new CalibrationResult();
            if (pairs == null || pairs.Count == 0)
            {
                result.Failure = "no_pairs";
                return result;
            }
            List<double> ratios = new List<double>(pairs.Count);
            for (int index = 0; index < pairs.Count; index++)
            {
                if (pairs[index] == null || !pairs[index].Complete)
                {
                    result.Failure = "incomplete_feet_pixel_pair_at_" + index;
                    return result;
                }
                double ratio = pairs[index].Feet / pairs[index].Pixels;
                if (!IsPositiveFinite(ratio))
                {
                    result.Failure = "invalid_ratio_at_" + index;
                    return result;
                }
                ratios.Add(ratio);
            }

            ratios.Sort();
            double q = ratios[(ratios.Count - 1) / 2];
            double maxRatioResidual = 0.0;
            double maxPixelResidual = 0.0;
            for (int index = 0; index < pairs.Count; index++)
            {
                PairObservation pair = pairs[index];
                double ratio = pair.Feet / pair.Pixels;
                double ratioResidual = Math.Abs(ratio - q);
                double relativeLimit = Math.Max(Math.Abs(ratio), Math.Abs(q))
                    * CalibrationRelativeTolerance;
                double pixelResidual = Math.Abs(pair.Feet / q - pair.Pixels);
                maxRatioResidual = Math.Max(maxRatioResidual, ratioResidual);
                maxPixelResidual = Math.Max(maxPixelResidual, pixelResidual);
                if (ratioResidual > relativeLimit
                    || pixelResidual > CalibrationResidualTolerance)
                {
                    result.Failure = "residual_check_failed_at_" + index;
                    result.FeetPerPixel = q;
                    result.MaxRatioResidual = maxRatioResidual;
                    result.MaxPixelResidual = maxPixelResidual;
                    return result;
                }
            }
            result.Succeeded = true;
            result.FeetPerPixel = q;
            result.MaxRatioResidual = maxRatioResidual;
            result.MaxPixelResidual = maxPixelResidual;
            return result;
        }

        private static void AppendCalibration(
            ProbeReport report, string label, CalibrationResult calibration)
        {
            if (calibration == null || !calibration.Succeeded)
            {
                report.CalibrationFailures++;
                report.Add(label + "=failed reason="
                    + (calibration == null ? "unknown" : calibration.Failure)
                    + (calibration != null && calibration.FeetPerPixel > 0.0
                        ? " observed_q_R=" + FormatR(calibration.FeetPerPixel)
                        : string.Empty)
                    + (calibration == null ? string.Empty
                        : " max_ratio_residual_R=" + FormatR(calibration.MaxRatioResidual)
                        + " max_pixel_residual_R=" + FormatR(calibration.MaxPixelResidual)));
                return;
            }
            report.Add(label + "=accepted observed_q_feet_per_pixel_R="
                + FormatR(calibration.FeetPerPixel)
                + " max_ratio_residual_R=" + FormatR(calibration.MaxRatioResidual)
                + " max_pixel_residual_R=" + FormatR(calibration.MaxPixelResidual));
        }

        private static void AppendCalibrationForPairs(
            ProbeReport report, string label, IList<PairObservation> pairs)
        {
            CalibrationResult calibration = Calibrate(pairs);
            AppendCalibration(report, label, calibration);
        }

        private static bool TryNearestPixel(
            double feet, double feetPerPixel, out int pixels, out string reason)
        {
            pixels = 0;
            reason = string.Empty;
            if (!IsPositiveFinite(feet) || !IsPositiveFinite(feetPerPixel))
            {
                reason = "feet_or_scale_not_positive_finite";
                return false;
            }
            double continuous = feet / feetPerPixel;
            if (!IsPositiveFinite(continuous))
            {
                reason = "continuous_pixel_value_not_positive_finite";
                return false;
            }
            double rounded = Math.Round(continuous, MidpointRounding.AwayFromZero);
            if (Math.Abs(continuous - rounded) <= PixelIntegerTolerance)
            {
                continuous = rounded;
            }
            rounded = Math.Round(continuous, MidpointRounding.AwayFromZero);
            if (rounded < 1.0 || rounded > Int32.MaxValue)
            {
                reason = "nearest_pixel_out_of_int32_range";
                return false;
            }
            pixels = checked((int)rounded);
            return pixels > 0;
        }

        private static void RecordFeetComparison(
            ProbeReport report, string axis, double expected,
            double actual, bool actualUsable)
        {
            bool drift = actualUsable && !NearlyEqualFeet(expected, actual);
            report.RecordFeetComparison(axis, actualUsable, drift);
            if (actualUsable && drift)
            {
                report.Add(axis + " feet_drift_observed expected_R=" + FormatR(expected)
                    + " actual_R=" + FormatR(actual));
            }
        }

        private static void RecordPixelComparison(
            ProbeReport report, int expected, int actual, bool actualReadable,
            string axis, string phase)
        {
            bool equal = actualReadable && expected == actual;
            report.RecordPixelObservation(axis, phase, actualReadable, equal);
            report.Add(axis + " pixel_identity=" + phase + ":"
                + (actualReadable ? equal.ToString() : "INCOMPLETE")
                + " expected=" + expected
                + " actual=" + (actualReadable ? actual.ToString(CultureInfo.InvariantCulture) : "(unavailable)"));
        }

        private static void RecordMovement(
            ProbeReport report, string label,
            DimensionSnapshot before, DimensionSnapshot after)
        {
            DimensionComparison comparison = CompareDimensionSnapshots(before, after);
            string columnState = MovementState(
                comparison.Columns, comparison.ColumnShapeChanged);
            string rowState = MovementState(
                comparison.Rows, comparison.RowShapeChanged);
            string overallState = CombineStates(columnState, rowState);
            report.Add(label + " measured_header_dimension_values_moved=" + overallState);
            report.Add(label + " column_dimension_values_moved=" + columnState
                + " expected=" + comparison.Columns.Expected
                + " comparable=" + comparison.Columns.Comparable
                + " unreadable=" + comparison.Columns.Unreadable
                + " mismatches=" + comparison.Columns.Mismatches);
            report.Add(label + " row_dimension_values_moved=" + rowState
                + " expected=" + comparison.Rows.Expected
                + " comparable=" + comparison.Rows.Comparable
                + " unreadable=" + comparison.Rows.Unreadable
                + " mismatches=" + comparison.Rows.Mismatches);
            report.Add(label + " header_shape_changed=" + comparison.ShapeChanged);
            report.Add(label + " column_shape_changed=" + comparison.ColumnShapeChanged
                + " row_shape_changed=" + comparison.RowShapeChanged);
            report.RecordMovement(label, overallState);
        }

        private static void RecordStructuralPhase(
            ProbeReport report, string label,
            bool operationAccepted, bool readbackSucceeded)
        {
            string measuredState = report.MeasuredMovementState(label);
            string phaseState = report.StructuralManualCoverageIncomplete
                || !operationAccepted || !readbackSucceeded
                ? "INCOMPLETE" : measuredState;
            report.Add(label + " structural_operation_accepted=" + operationAccepted);
            report.Add(label + " structural_readback_succeeded=" + readbackSucceeded);
            report.Add(label + " structural_phase_evidence=" + phaseState);
            report.RecordStructuralPhase(label, phaseState);
        }

        private static DimensionComparison CompareDimensionSnapshots(
            DimensionSnapshot before, DimensionSnapshot after)
        {
            return new DimensionComparison
            {
                Columns = ComparePairValues(
                    before == null ? null : before.Columns,
                    after == null ? null : after.Columns),
                Rows = ComparePairValues(
                    before == null ? null : before.Rows,
                    after == null ? null : after.Rows),
                ColumnShapeChanged = ColumnShapeChanged(before, after),
                RowShapeChanged = RowShapeChanged(before, after),
                ShapeChanged = ShapeChanged(before, after)
            };
        }

        private static PairMovement ComparePairValues(
            IList<PairObservation> before, IList<PairObservation> after)
        {
            PairMovement result = new PairMovement();
            if (before == null || after == null)
            {
                result.Expected = 1;
                result.Unreadable = 1;
                return result;
            }
            int count = Math.Max(before.Count, after.Count);
            result.Expected = count;
            for (int index = 0; index < count; index++)
            {
                if (index >= before.Count || index >= after.Count)
                {
                    continue;
                }
                PairObservation left = before[index];
                PairObservation right = after[index];
                if (!left.Complete || !right.Complete)
                {
                    result.Unreadable++;
                    continue;
                }
                result.Comparable++;
                if (left.Pixels != right.Pixels
                    || !NearlyEqualFeet(left.Feet, right.Feet))
                {
                    result.Mismatches++;
                }
            }
            return result;
        }

        private static bool ShapeChanged(DimensionSnapshot before, DimensionSnapshot after)
        {
            return RowShapeChanged(before, after) || ColumnShapeChanged(before, after);
        }

        private static bool ColumnShapeChanged(
            DimensionSnapshot before, DimensionSnapshot after)
        {
            return before == null || after == null
                || before.NumberOfColumns != after.NumberOfColumns
                || before.FirstColumn != after.FirstColumn;
        }

        private static bool RowShapeChanged(
            DimensionSnapshot before, DimensionSnapshot after)
        {
            return before == null || after == null
                || before.NumberOfRows != after.NumberOfRows
                || before.FirstRow != after.FirstRow;
        }

        private static void AppendSummary(ProbeReport report)
        {
            report.Add("================================================================================");
            report.Add("SUMMARY");
            report.Add("feet_based_drift_reproduced="
                + DriftConclusion(report));
            report.Add("column_feet_based_drift_reproduced="
                + FeetDriftState(report.ColumnFeetExpected,
                    report.ColumnFeetComparable,
                    report.ColumnFeetUnreadable,
                    report.ColumnFeetDriftObservations,
                    report.ColumnFeetIncompletePhase));
            report.Add("row_feet_based_drift_reproduced="
                + FeetDriftState(report.RowFeetExpected,
                    report.RowFeetComparable,
                    report.RowFeetUnreadable,
                    report.RowFeetDriftObservations,
                    report.RowFeetIncompletePhase));
            report.Add("accepted_column_pixel_writes_read_back_identically_immediately="
                + ComparisonState(report.ColumnPixelImmediate));
            report.Add("accepted_column_pixel_writes_read_back_identically_after_regeneration="
                + ComparisonState(report.ColumnPixelAfterRegeneration));
            report.Add("accepted_row_pixel_writes_read_back_identically_immediately="
                + ComparisonState(report.RowPixelImmediate));
            report.Add("accepted_row_pixel_writes_read_back_identically_after_regeneration="
                + ComparisonState(report.RowPixelAfterRegeneration));
            AppendComparisonCounts(
                report, "column_pixel_immediate", report.ColumnPixelImmediate);
            AppendComparisonCounts(
                report, "column_pixel_after_regeneration",
                report.ColumnPixelAfterRegeneration);
            AppendComparisonCounts(
                report, "row_pixel_immediate", report.RowPixelImmediate);
            AppendComparisonCounts(
                report, "row_pixel_after_regeneration",
                report.RowPixelAfterRegeneration);
            report.Add("one_proportional_feet_per_pixel_scale_fits_all_observed_pairs="
                + ScaleConclusion(report));
            CalibrationResult allColumnScale = Calibrate(report.WidthCalibrationPairs);
            report.Add("observed_all_column_scale_median_feet_per_pixel_R="
                + (allColumnScale != null && allColumnScale.FeetPerPixel > 0.0
                    ? FormatR(allColumnScale.FeetPerPixel) : "(unavailable)")
                + " max_pixel_residual_R="
                + (allColumnScale == null ? "(unavailable)"
                    : FormatR(allColumnScale.MaxPixelResidual))
                + " observation_only=true no_conversion_constant_used=true");
            report.Add("observed_column_feet_per_pixel_values_R="
                + FormatScales(report.WidthScales)
                + " observation_only=true no_conversion_constant_used=true");
            report.Add("observed_row_feet_per_pixel_values_R="
                + FormatScales(report.RowScales)
                + " excluded_from_width_calibration=true");
            report.Add("column_scale_observations=logged=" + report.WidthScaleObservations
                + " complete=" + report.WidthScales.Count
                + " incomplete=" + report.WidthScaleIncompleteObservations
                + " calibration_pairs=" + report.WidthCalibrationPairs.Count);
            report.Add("row_scale_observations=logged=" + report.RowScaleObservations
                + " complete=" + report.RowScales.Count
                + " incomplete=" + report.RowScaleIncompleteObservations);
            report.Add("calibration_failures=" + report.CalibrationFailures);
            report.Add("body_sync_measured_header_dimension_values_moved="
                + report.MeasuredMovementState("body_sync"));
            report.Add("body_sync_structural_phase_evidence="
                + report.StructuralState("body_sync"));
            report.Add("body_sync_moved_any_header_dimension="
                + report.StructuralState("body_sync"));
            report.Add("merges_measured_header_dimension_values_moved="
                + report.MeasuredMovementState("merge"));
            report.Add("merges_structural_phase_evidence="
                + report.StructuralState("merge"));
            report.Add("merges_moved_any_header_dimension="
                + report.StructuralState("merge"));
            report.Add("clear_grow_measured_header_dimension_values_moved="
                + report.MeasuredMovementState("clear_grow"));
            report.Add("clear_grow_structural_phase_evidence="
                + report.StructuralState("clear_grow"));
            report.Add("clear_grow_moved_any_header_dimension="
                + report.StructuralState("clear_grow"));
            report.Add("structural_manual_vector_clear="
                + report.ManualVectorState("clear"));
            report.Add("structural_manual_vector_grow="
                + report.ManualVectorState("grow"));
            report.Add("structural_manual_vector_restore="
                + report.ManualVectorState("restore"));
            report.Add("structural_manual_vector_merge="
                + report.ManualVectorState("merge"));
            report.Add("structural_manual_vector_body_sync="
                + report.ManualVectorState("body_sync"));
            report.Add("structural_manual_vector_integrity_scan="
                + report.ManualVectorState("integrity_scan"));
            report.Add("accepted_row_height_targets=" + report.AcceptedRowHeightTargets);
            report.Add("row_height_target_coverage="
                + (!report.Fatal
                    && report.AcceptedRowHeightTargets >= RequiredAcceptedRowHeightTargets
                    ? "YES" : "INCOMPLETE")
                + " accepted=" + report.AcceptedRowHeightTargets
                + " required=" + RequiredAcceptedRowHeightTargets
                + " attempted=" + report.RowHeightTargetsAttempted);
            report.Add("fatal_boundary_reached=" + report.Fatal);
            report.Add("successful_probe_evidence_requires_rollback_status=RolledBack");
            report.Add("================================================================================");
        }

        private static void AppendComparisonCounts(
            ProbeReport report, string label, ComparisonCounters counters)
        {
            report.Add(label + " expected=" + counters.Expected
                + " comparable=" + counters.Comparable
                + " unreadable=" + counters.Unreadable
                + " mismatches=" + counters.Failures
                + " incomplete_phase=" + counters.IncompletePhase);
        }

        private static string ComparisonState(ComparisonCounters counters)
        {
            if (counters == null)
            {
                return "UNKNOWN";
            }
            if (counters.IncompletePhase || counters.Unreadable > 0)
            {
                return "INCOMPLETE";
            }
            if (counters.Expected == 0)
            {
                return "UNKNOWN";
            }
            if (counters.Comparable < counters.Expected)
            {
                return "INCOMPLETE";
            }
            return counters.Failures == 0 ? "YES" : "NO";
        }

        private static string DriftConclusion(ProbeReport report)
        {
            string column = FeetDriftState(
                report.ColumnFeetExpected,
                report.ColumnFeetComparable,
                report.ColumnFeetUnreadable,
                report.ColumnFeetDriftObservations,
                report.ColumnFeetIncompletePhase);
            string row = FeetDriftState(
                report.RowFeetExpected,
                report.RowFeetComparable,
                report.RowFeetUnreadable,
                report.RowFeetDriftObservations,
                report.RowFeetIncompletePhase);
            return CombineStates(column, row);
        }

        private static string FeetDriftState(
            int expected, int comparable, int unreadable,
            int driftObservations, bool incompletePhase)
        {
            if (incompletePhase || unreadable > 0)
            {
                return "INCOMPLETE";
            }
            if (expected == 0)
            {
                return "UNKNOWN";
            }
            if (comparable < expected)
            {
                return "INCOMPLETE";
            }
            return driftObservations > 0 ? "YES" : "NO";
        }

        private static string ScaleConclusion(ProbeReport report)
        {
            if (report.WidthScaleIncompleteObservations > 0)
            {
                return "INCOMPLETE";
            }
            if (report.WidthScaleObservations == 0)
            {
                return "UNKNOWN";
            }
            CalibrationResult calibration = Calibrate(report.WidthCalibrationPairs);
            if (calibration == null || !calibration.Succeeded)
            {
                return "NO";
            }
            return "YES";
        }

        private static string MovementState(
            PairMovement movement, bool shapeChanged)
        {
            if (movement == null || movement.Expected == 0)
            {
                return "UNKNOWN";
            }
            if (movement.Unreadable > 0)
            {
                return "INCOMPLETE";
            }
            if (shapeChanged || movement.Mismatches > 0)
            {
                return "YES";
            }
            return movement.Comparable == movement.Expected ? "NO" : "UNKNOWN";
        }

        private static string ManualVectorState(ManualVectorComparison comparison)
        {
            if (comparison == null || comparison.Expected == 0)
            {
                return "UNKNOWN";
            }
            if (comparison.Unreadable > 0)
            {
                return "INCOMPLETE";
            }
            return comparison.Mismatches > 0 ? "YES" : "NO";
        }

        private static string CombineStates(string left, string right)
        {
            if (left == "INCOMPLETE" || right == "INCOMPLETE")
            {
                return "INCOMPLETE";
            }
            if (left == "UNKNOWN" || right == "UNKNOWN")
            {
                return "UNKNOWN";
            }
            if (left == "YES" || right == "YES")
            {
                return "YES";
            }
            if (left == "NO" || right == "NO")
            {
                return "NO";
            }
            return "UNKNOWN";
        }

        private static string FormatScales(IList<double> scales)
        {
            if (scales == null || scales.Count == 0)
            {
                return "(none)";
            }
            StringBuilder text = new StringBuilder();
            for (int index = 0; index < scales.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(",");
                }
                text.Append(FormatR(scales[index]));
            }
            return text.ToString();
        }

        private static string FormatPixels(IList<int> pixels)
        {
            StringBuilder text = new StringBuilder("[");
            for (int index = 0; index < pixels.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(",");
                }
                text.Append(pixels[index].ToString(CultureInfo.InvariantCulture));
            }
            text.Append("]");
            return text.ToString();
        }

        private static bool HasCompleteColumns(DimensionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Columns.Count < 3)
            {
                return false;
            }
            foreach (PairObservation pair in snapshot.Columns)
            {
                if (!pair.Complete)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasCompleteDimensions(DimensionSnapshot snapshot)
        {
            if (snapshot == null
                || snapshot.NumberOfColumns <= 0
                || snapshot.NumberOfRows <= 0
                || snapshot.Columns.Count != snapshot.NumberOfColumns
                || snapshot.Rows.Count != snapshot.NumberOfRows)
            {
                return false;
            }
            foreach (PairObservation pair in snapshot.Columns)
            {
                if (!pair.Complete)
                {
                    return false;
                }
            }
            foreach (PairObservation pair in snapshot.Rows)
            {
                if (!pair.Complete)
                {
                    return false;
                }
            }
            return true;
        }

        private static TableSectionData Header(ViewSchedule schedule)
        {
            return schedule.GetTableData().GetSectionData(SectionType.Header);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool NearlyEqualFeet(double left, double right)
        {
            double limit = Math.Max(Math.Abs(left), Math.Abs(right)) * FeetComparisonTolerance;
            return Math.Abs(left - right) <= Math.Max(limit, FeetComparisonTolerance);
        }

        private static string FormatR(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string DescribeException(Exception ex)
        {
            return (ex == null ? "unknown" : ex.Message ?? string.Empty)
                .Replace("\r", " ").Replace("\n", " ");
        }

        private static string WriteReport(ProbeReport report)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directory = Path.Combine(appData, "ExcelScheduleLink");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "WidthRoundTripProbe.txt");
            File.WriteAllText(path, report.ToText(), new UTF8Encoding(true));
            return path;
        }

        private static bool TryCopyReportToClipboard(
            ProbeReport report, out string failure)
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

        private sealed class ProbeReport
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<double> WidthScales = new List<double>();
            public readonly List<PairObservation> WidthCalibrationPairs =
                new List<PairObservation>();
            public readonly List<double> RowScales = new List<double>();
            public readonly Dictionary<string, string> StructuralStates =
                new Dictionary<string, string>();
            public readonly Dictionary<string, string> MeasuredMovementStates =
                new Dictionary<string, string>();
            public readonly Dictionary<string, string> ManualVectorStates =
                new Dictionary<string, string>();
            public readonly ComparisonCounters ColumnPixelImmediate =
                new ComparisonCounters();
            public readonly ComparisonCounters ColumnPixelAfterRegeneration =
                new ComparisonCounters();
            public readonly ComparisonCounters RowPixelImmediate =
                new ComparisonCounters();
            public readonly ComparisonCounters RowPixelAfterRegeneration =
                new ComparisonCounters();
            public int ColumnFeetExpected;
            public int ColumnFeetComparable;
            public int ColumnFeetUnreadable;
            public int ColumnFeetDriftObservations;
            public int RowFeetExpected;
            public int RowFeetComparable;
            public int RowFeetUnreadable;
            public int RowFeetDriftObservations;
            public int WidthScaleObservations;
            public int WidthScaleIncompleteObservations;
            public int RowScaleObservations;
            public int RowScaleIncompleteObservations;
            public int CalibrationFailures;
            public int AcceptedRowHeightTargets;
            public int RowHeightTargetsAttempted;
            public bool StructuralManualCoverageIncomplete;
            public bool StructuralExperimentIncomplete;
            public bool Fatal;
            public TransactionStatus? FinalRollbackStatus;

            public void Add(string line)
            {
                Lines.Add(line ?? string.Empty);
            }

            public string ToText()
            {
                return string.Join(Environment.NewLine, Lines.ToArray())
                    + Environment.NewLine;
            }

            public void RecordScaleObservation(PairObservation pair)
            {
                if (pair == null)
                {
                    return;
                }
                if (pair.IsRow)
                {
                    RowScaleObservations++;
                    if (pair.Complete)
                    {
                        RowScales.Add(pair.Feet / pair.Pixels);
                    }
                    else
                    {
                        RowScaleIncompleteObservations++;
                    }
                }
                else
                {
                    WidthScaleObservations++;
                    if (pair.Complete)
                    {
                        WidthScales.Add(pair.Feet / pair.Pixels);
                        WidthCalibrationPairs.Add(pair);
                    }
                    else
                    {
                        WidthScaleIncompleteObservations++;
                    }
                }
            }

            public void RecordFeetComparison(
                string axis, bool readable, bool drift)
            {
                if (axis == "row")
                {
                    RowFeetExpected++;
                    if (!readable)
                    {
                        RowFeetUnreadable++;
                    }
                    else
                    {
                        RowFeetComparable++;
                        if (drift)
                        {
                            RowFeetDriftObservations++;
                        }
                    }
                }
                else
                {
                    ColumnFeetExpected++;
                    if (!readable)
                    {
                        ColumnFeetUnreadable++;
                    }
                    else
                    {
                        ColumnFeetComparable++;
                        if (drift)
                        {
                            ColumnFeetDriftObservations++;
                        }
                    }
                }
            }

            public void RecordPixelObservation(
                string axis, string phase, bool readable, bool equal)
            {
                ComparisonCounters counters = PixelCounters(axis, phase);
                counters.Expected++;
                if (!readable)
                {
                    counters.Unreadable++;
                }
                else
                {
                    counters.Comparable++;
                    if (!equal)
                    {
                        counters.Failures++;
                    }
                }
            }

            public void RecordExperimentFailure(string phase)
            {
                if (phase.IndexOf("structural", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    StructuralExperimentIncomplete = true;
                }
                if (phase.IndexOf("row", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MarkRowExperimentIncomplete();
                }
                if (phase.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0
                    || phase.IndexOf("native default", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MarkColumnExperimentIncomplete();
                }
            }

            public void MarkColumnExperimentIncomplete()
            {
                WidthScaleIncompleteObservations++;
                ColumnFeetIncompletePhase = true;
                MarkColumnPixelExperimentIncomplete();
            }

            public void MarkColumnPixelExperimentIncomplete()
            {
                ColumnPixelImmediate.IncompletePhase = true;
                ColumnPixelAfterRegeneration.IncompletePhase = true;
            }

            public void MarkRowExperimentIncomplete()
            {
                RowScaleIncompleteObservations++;
                RowFeetIncompletePhase = true;
                RowPixelImmediate.IncompletePhase = true;
                RowPixelAfterRegeneration.IncompletePhase = true;
            }

            public void MarkFatalBoundary()
            {
                Fatal = true;
                ColumnFeetIncompletePhase = true;
                RowFeetIncompletePhase = true;
                ColumnPixelImmediate.IncompletePhase = true;
                ColumnPixelAfterRegeneration.IncompletePhase = true;
                RowPixelImmediate.IncompletePhase = true;
                RowPixelAfterRegeneration.IncompletePhase = true;
                WidthScaleIncompleteObservations++;
                RowScaleIncompleteObservations++;
                StructuralExperimentIncomplete = true;
            }

            public void RecordMovement(string label, string state)
            {
                MeasuredMovementStates[label] = state;
            }

            public void RecordStructuralPhase(string label, string state)
            {
                StructuralStates[label] = state;
            }

            public string MeasuredMovementState(string label)
            {
                string state;
                if (MeasuredMovementStates.TryGetValue(label, out state))
                {
                    return state;
                }
                return StructuralExperimentIncomplete ? "INCOMPLETE" : "UNKNOWN";
            }

            public string StructuralState(string label)
            {
                string state;
                if (Fatal)
                {
                    return "INCOMPLETE";
                }
                if (StructuralManualCoverageIncomplete)
                {
                    return "INCOMPLETE";
                }
                if (StructuralStates.TryGetValue(label, out state))
                {
                    return state;
                }
                return StructuralExperimentIncomplete ? "INCOMPLETE" : "UNKNOWN";
            }

            public string ManualVectorState(string label)
            {
                string state;
                if (Fatal)
                {
                    return "INCOMPLETE";
                }
                if (ManualVectorStates.TryGetValue(label, out state))
                {
                    return state;
                }
                return StructuralExperimentIncomplete ? "INCOMPLETE" : "UNKNOWN";
            }

            public void RecordManualVectorState(string label, string state)
            {
                ManualVectorStates[label] = state;
            }

            private ComparisonCounters PixelCounters(string axis, string phase)
            {
                bool immediate = phase == "immediate";
                if (axis == "row")
                {
                    return immediate ? RowPixelImmediate : RowPixelAfterRegeneration;
                }
                return immediate ? ColumnPixelImmediate : ColumnPixelAfterRegeneration;
            }

            public bool ColumnFeetIncompletePhase;
            public bool RowFeetIncompletePhase;
        }

        private sealed class ComparisonCounters
        {
            public int Expected;
            public int Comparable;
            public int Unreadable;
            public int Failures;
            public bool IncompletePhase;
        }

        private sealed class PairMovement
        {
            public int Expected;
            public int Comparable;
            public int Unreadable;
            public int Mismatches;
        }

        private sealed class DimensionComparison
        {
            public PairMovement Columns;
            public PairMovement Rows;
            public bool ColumnShapeChanged;
            public bool RowShapeChanged;
            public bool ShapeChanged;
        }

        private sealed class ManualVectorComparison
        {
            public int Expected;
            public int Comparable;
            public int Unreadable;
            public int Mismatches;
        }

        private sealed class ManualRowSeed
        {
            public int Index;
            public double RequestedFeet;
            public PairObservation Seed;
        }

        private sealed class MergeOperationResult
        {
            public int Expected;
            public int Accepted;
            public int Refused;
        }

        private sealed class MergeReadbackResult
        {
            public int Expected;
            public int Readable;
            public int Matches;
            public int Wrong;
            public int Unreadable;
        }

        private sealed class ExperimentRecoveryException : Exception
        {
            public ExperimentRecoveryException(string phase, Exception innerException)
                : base("Could not restore the temporary probe fixture after " + phase,
                    innerException)
            {
            }
        }

        private sealed class DimensionSnapshot
        {
            public string Label;
            public int NumberOfRows;
            public int NumberOfColumns;
            public int FirstRow;
            public int FirstColumn;
            public readonly List<PairObservation> Columns = new List<PairObservation>();
            public readonly List<PairObservation> Rows = new List<PairObservation>();
        }

        private sealed class PairObservation
        {
            public int Index;
            public bool IsRow;
            public bool FeetGetterSucceeded;
            public bool FeetUsable;
            public double Feet;
            public bool PixelsGetterSucceeded;
            public bool PixelsUsable;
            public int Pixels;
            public string FeetRefusal;
            public string PixelsRefusal;

            public bool Complete
            {
                get { return FeetUsable && PixelsUsable; }
            }
        }

        private sealed class CalibrationResult
        {
            public bool Succeeded;
            public double FeetPerPixel;
            public double MaxRatioResidual;
            public double MaxPixelResidual;
            public string Failure;
        }

        private sealed class BodyWidths
        {
            public bool GridReadable;
            public bool SheetReadable;
            public double Grid;
            public double Sheet;
            public string Failure;
        }
    }
}
