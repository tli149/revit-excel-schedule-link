using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace UiHarness
{
    internal static class Program
    {
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2024";
        private static string _outDir;

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRevit;
            _outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            Directory.CreateDirectory(_outDir);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm", 1.0f);
            Render(1, LengthUnitChoice.Inches, "layout_1col_in", 1.0f);
            Render(50, LengthUnitChoice.Millimeters, "layout_50col_mm", 1.0f);
            // Font-scaled runs stand in for high-DPI: controls grow while the form
            // keeps its designed size - the same stress that clipped the real dialog.
            Render(1, LengthUnitChoice.Inches, "layout_1col_in_150", 1.5f);
            Render(1, LengthUnitChoice.Inches, "layout_1col_in_200", 2.0f);
            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm_150", 1.5f);
            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm_200", 2.0f);
            Render(50, LengthUnitChoice.Millimeters, "layout_50col_mm_150", 1.5f);
            Render(50, LengthUnitChoice.Millimeters, "layout_50col_mm_200", 2.0f);
            RenderConstrained(LengthUnitChoice.Millimeters, "layout_constrained_mm", 1.0f);
            RenderConstrained(LengthUnitChoice.Inches, "layout_constrained_in_150", 1.5f);
            RenderConstrained(LengthUnitChoice.Millimeters, "layout_constrained_mm_200", 2.0f);
            CheckPolicyCombinations();
            CheckFitContentInteractions();
            CheckFitContentFailureState();
            CheckFitContentMessages();
            CheckColumnHeadingLabels();
            CheckWidthRangeMessages();
            CheckLiveEqualNarrowWidthStillAccepted();
            CheckLengthUnitMapping();
            CheckFatalLayoutResultShapes();
            CheckUnchangedDisplayEdits();
            CheckPolicyOnlyNarrowApply();
            CheckRowHeightRenderNotes();
            CheckRowFitPolicyHint();
            CheckKeepRowNoticeFormatting();
            CheckRowFitRenderNotes();
            CheckRowFitWarningApplySucceeds();
            RenderLinkExcel("link-dialog", 1.0f);
            RenderLinkExcel("link-dialog_200", 2.0f);
            RenderLinkManagerPreview("link-manager");
            CheckLinkExcelFallback();
            Console.WriteLine("done");
        }

        private static Assembly ResolveRevit(object sender, ResolveEventArgs e)
        {
            try
            {
                string name = new AssemblyName(e.Name).Name;
                string path = Path.Combine(RevitDir, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch (Exception) { return null; }
        }

        private static LayoutDialog CreateLayoutDialog(
            int columns, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode)
        {
            double[] feet = new double[columns];
            for (int i = 0; i < columns; i++)
            {
                feet[i] = 0.4 + (i % 5) * 0.15; // varied, inside [0.10, 2.0]
            }

            return CreateLayoutDialogWithFeet(
                feet, unit, columnMode, rowMode,
                BuildHarnessHeadings(columns),
                delegate (double[] current) { return ComputeHarnessFit(current); },
                delegate (double[] f, ColumnWidthMode m, RowHeightMode r, LayoutSourceSnapshot s)
                {
                    return new LayoutApplyResult { Success = true, UserMessage = "harness: applied" };
                });
        }

        private static LayoutDialog CreateLayoutDialogWithFeet(
            double[] feet, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            Func<double[], ColumnWidthMode, RowHeightMode, LayoutSourceSnapshot, LayoutApplyResult> apply)
        {
            return CreateLayoutDialogWithFeet(feet, unit, columnMode, rowMode,
                BuildHarnessHeadings(feet != null ? feet.Length : 0),
                delegate (double[] current) { return ComputeHarnessFit(current); },
                apply);
        }

        private static LayoutDialog CreateLayoutDialogWithFeet(
            double[] feet, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            Func<double[], FitContentResult> fitContent,
            Func<double[], ColumnWidthMode, RowHeightMode, LayoutSourceSnapshot, LayoutApplyResult> apply)
        {
            return CreateLayoutDialogWithFeet(
                feet, unit, columnMode, rowMode,
                BuildHarnessHeadings(feet != null ? feet.Length : 0),
                fitContent, apply);
        }

        private static LayoutDialog CreateLayoutDialogWithFeet(
            double[] feet, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            IReadOnlyList<string> headings,
            Func<double[], FitContentResult> fitContent,
            Func<double[], ColumnWidthMode, RowHeightMode, LayoutSourceSnapshot, LayoutApplyResult> apply)
        {
            return CreateLayoutDialogCore(
                feet, unit, columnMode, rowMode, headings, fitContent, apply);
        }

        private static LayoutDialog CreateLayoutDialogCore(
            double[] feet, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            IReadOnlyList<string> headings,
            Func<double[], FitContentResult> fitContent,
            Func<double[], ColumnWidthMode, RowHeightMode, LayoutSourceSnapshot, LayoutApplyResult> apply)
        {
            return new LayoutDialog(
                "Sample Workbook - Data",
                feet,
                columnMode,
                rowMode,
                unit,
                headings,
                fitContent,
                delegate { return ExcelWidthResult.Failed("harness: no workbook"); },
                apply);
        }

        private static LayoutDialog CreateLayoutDialogWithHeadings(
            double[] feet, LengthUnitChoice unit,
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            IReadOnlyList<string> headings)
        {
            return CreateLayoutDialogCore(
                feet, unit, columnMode, rowMode, headings,
                delegate (double[] current) { return ComputeHarnessFit(current); },
                delegate (double[] f, ColumnWidthMode m, RowHeightMode r,
                    LayoutSourceSnapshot s)
                {
                    return new LayoutApplyResult
                    {
                        Success = true,
                        UserMessage = "harness: applied"
                    };
                });
        }

        private static string[] BuildHarnessHeadings(int columns)
        {
            if (columns < 0) columns = 0;
            FitContentTable table = BuildHarnessTable(columns);
            FitContentText.Prepare(
                table, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            return FitContentText.GetColumnHeadings(table);
        }

        private static FitContentTable BuildHarnessTable(int columns)
        {
            if (columns >= 5)
            {
                return BuildHeadingTable(columns);
            }

            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    string text;
                    if (row == 0)
                    {
                        text = column % 2 == 0
                            ? "Number"
                            : new string('\u6F22', 24) + " Description";
                    }
                    else if (column % 2 == 0)
                    {
                        text = (10 + row).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        text = row == 3
                            ? new string('\u6F22', 40)
                            : "Description row " + row;
                    }
                    cells.Add(new FitContentCell(row, column, 1, 1, text));
                }
            }
            return new FitContentTable(4, columns, cells);
        }

        private static FitContentTable BuildHeadingTable(int columns)
        {
            if (columns < 5)
            {
                throw new ArgumentOutOfRangeException("columns");
            }

            List<FitContentCell> cells = new List<FitContentCell>();
            for (int row = 0; row < 4; row++)
            {
                if (row == 0)
                {
                    cells.Add(new FitContentCell(
                        row, 0, 1, 2, "Merged title"));
                }
                else
                {
                    cells.Add(new FitContentCell(
                        row, 0, 1, 1, row == 1 ? "Description" : "data"));
                    cells.Add(new FitContentCell(
                        row, 1, 1, 1, row == 1 ? "Code" : "data"));
                }

                if (row == 0)
                {
                    cells.Add(new FitContentCell(row, 2, 1, 1, string.Empty));
                }
                else if (row == 1)
                {
                    cells.Add(new FitContentCell(
                        row, 2, 3, 1, "Vertical merge"));
                }

                cells.Add(new FitContentCell(
                    row, 3, 1, 2,
                    row == 0 ? "Merged-only title" : "Merged-only data"));

                for (int column = 5; column < columns; column++)
                {
                    string text;
                    if (row == 0 && column == 5)
                    {
                        text = new string('L', 48);
                    }
                    else if (row == 0 && column == 6)
                    {
                        text = new string('\u6F22', 32);
                    }
                    else if (row == 0 && column == 25)
                    {
                        text = "Z column";
                    }
                    else if (row == 0 && column == 26)
                    {
                        text = "AA column";
                    }
                    else if (row == 0 && column == 49)
                    {
                        text = "AX column";
                    }
                    else
                    {
                        text = row == 0
                            ? "Heading " + column
                            : "data";
                    }
                    cells.Add(new FitContentCell(row, column, 1, 1, text));
                }
            }
            return new FitContentTable(4, columns, cells);
        }

        private static string[] BuildHeadingArray(int columns)
        {
            FitContentTable table = BuildHeadingTable(columns);
            FitContentText.Prepare(
                table, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            return FitContentText.GetColumnHeadings(table);
        }

        private static FitContentResult ComputeHarnessFit(double[] currentFeet)
        {
            if (currentFeet == null || currentFeet.Length == 0)
            {
                return FitContentResult.Failed(UserMessages.LayoutNoCandidates);
            }

            FitContentTable table = BuildHarnessTable(currentFeet.Length);
            FitContentMeasurements measurements = FitContentText.Prepare(
                table, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            double total = 0.0;
            foreach (double feet in currentFeet) total += feet;
            FixedTotalFitResult fit = LayoutOps.FitContentToTotal(
                measurements, total);
            return new FitContentResult
            {
                Success = fit.CanApply,
                Feet = ToArray(fit.Feet),
                FixedTotal = fit,
                ColumnHeadings = FitContentText.GetColumnHeadings(table)
            };
        }

        private static double[] ToArray(IReadOnlyList<double> values)
        {
            double[] result = new double[values != null ? values.Count : 0];
            if (values != null)
            {
                for (int i = 0; i < result.Length; i++) result[i] = values[i];
            }
            return result;
        }

        private static void CheckFitContentInteractions()
        {
            double[] current = { 0.45, 0.60, 0.75 };
            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                current, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] feet, ColumnWidthMode columnMode,
                    RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                {
                    return new LayoutApplyResult
                    {
                        Success = true,
                        UserMessage = UserMessages.AppendLayoutRowPolicyNote(
                            "harness: applied", rowMode)
                    };
                }))
            {
                TextBox target = GetPrivateField<TextBox>(dialog, "_targetTotal");
                target.Text = "1.0";
                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                double beforeEditTotal = SumGridFeet(grid);
                double firstFeet = GetRowFeet(grid.Rows[0].Tag);
                double editedDisplay = LengthUnitHelper.FromFeet(
                    firstFeet, LengthUnitChoice.Millimeters) + 5.0;
                grid.Rows[0].Cells["width"].Value = editedDisplay.ToString(
                    "F" + LengthUnitHelper.Decimals(LengthUnitChoice.Millimeters),
                    CultureInfo.CurrentCulture);
                InvokeCellEndEdit(dialog, grid, 0);
                double beforeFitTotal = SumGridFeet(grid);

                InvokePrivate(dialog, "OnFitContent", dialog, EventArgs.Empty);
                double afterFitTotal = SumGridFeet(grid);
                if (Math.Abs(beforeFitTotal - afterFitTotal) > LayoutOps.Tolerance)
                {
                    throw new InvalidOperationException(
                        "Fit Content did not use the candidate total instead of the target text.");
                }
                if (Math.Abs(beforeEditTotal - beforeFitTotal) < LayoutOps.Tolerance)
                {
                    throw new InvalidOperationException(
                        "The manual candidate edit did not reach Fit Content.");
                }
                if (GetPrivateField<FixedTotalFitResult>(
                        dialog, "_fitContentDiagnostics") == null)
                {
                    throw new InvalidOperationException(
                        "Fit Content did not retain its pure diagnostics.");
                }
                if (GetPrivateField<FixedTotalFitResult>(
                        dialog, "_fitContentDiagnostics").Stage
                    != FitContentStage.Comfortable)
                {
                    throw new InvalidOperationException(
                        "The ordinary Fit Content fixture was not comfortable.");
                }

                double[] firstResult = CaptureGridFeet(grid);
                InvokePrivate(dialog, "OnFitContent", dialog, EventArgs.Empty);
                double[] secondResult = CaptureGridFeet(grid);
                if (!EqualFeet(firstResult, secondResult))
                {
                    throw new InvalidOperationException(
                        "Repeated Fit Content was not deterministic.");
                }
                if (!Convert.ToString(grid.Rows[0].Cells["label"].Value)
                    .StartsWith("A  Number", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The grid did not use the shared letter-plus-heading formatter.");
                }

                RadioButton rowKeep = FindRadioButton(
                    dialog, UserMessages.RowModeKeepManual);
                rowKeep.Checked = true;
                Label status = GetPrivateField<Label>(dialog, "_status");
                if (status.Text.IndexOf(UserMessages.LayoutKeepRowHeightsNote,
                    StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Keep did not refresh the Fit Content preview notice.");
                }
                InvokePrivate(dialog, "OnApplyNow", dialog, EventArgs.Empty);
                if (GetPrivateField<bool>(dialog, "_dirty")
                    || CountOccurrences(status.Text,
                        UserMessages.LayoutKeepRowHeightsNote) != 1)
                {
                    throw new InvalidOperationException(
                        "A successful Keep-row Apply did not show its notice once.");
                }
                double[] keepResult = CaptureGridFeet(grid);
                // Select the other radio as a user would; clearing one radio
                // programmatically leaves neither selected.
                FindRadioButton(dialog, UserMessages.RowModeFitToText).Checked = true;
                if (!EqualFeet(keepResult, CaptureGridFeet(grid))
                    || status.Text.IndexOf(UserMessages.LayoutKeepRowHeightsNote,
                        StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "Changing row policy changed widths or left a stale Keep notice.");
                }
            }
        }

        private static void CheckFitContentMessages()
        {
            FitContentTable table = BuildHarnessTable(3);
            FitContentMeasurements measurements = FitContentText.Prepare(
                table, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            FixedTotalFitResult constrained = LayoutOps.FitContentToTotal(
                measurements, 3.0 * LayoutMetrics.MinColumnFeet);
            string[] headings = FitContentText.GetColumnHeadings(table);
            string metric = LengthUnitHelper.FitContentNotes(
                constrained, LengthUnitChoice.Millimeters, headings);
            string imperial = LengthUnitHelper.FitContentNotes(
                constrained, LengthUnitChoice.Inches, headings);
            if (metric.IndexOf("mm", StringComparison.Ordinal) < 0
                || imperial.IndexOf("in", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Fit Content notes did not use the selected project unit.");
            }

            string metricRange = LengthUnitHelper.InvalidNumbersMessage(
                new[] { 0 }, LengthUnitChoice.Millimeters, headings);
            string imperialRange = LengthUnitHelper.InvalidNumbersMessage(
                new[] { 0 }, LengthUnitChoice.Inches, headings);
            if (metricRange.IndexOf("30.5 to 609.6 mm",
                    StringComparison.Ordinal) < 0
                || imperialRange.IndexOf("1.20 to 24.00 in",
                    StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "The unit-aware changed-width range was not formatted correctly.");
            }

            FitContentTable longTable = new FitContentTable(2, 1, new[]
            {
                new FitContentCell(0, 0, 1, 1, "ID"),
                new FitContentCell(1, 0, 1, 1, new string('A', 80))
            });
            FitContentMeasurements longMeasurements = FitContentText.Prepare(
                longTable, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            FixedTotalFitResult limited = LayoutOps.FitContentToTotal(
                longMeasurements, LayoutMetrics.MinColumnFeet);
            if (!limited.ComfortableEstimateLimited
                || LengthUnitHelper.FitContentComfort(
                    limited, LengthUnitChoice.Inches).IndexOf(
                        "limited estimate", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "The limited comfortable-width estimate was not exposed.");
            }

            FitContentTable protectedTable = new FitContentTable(4, 1, new[]
            {
                new FitContentCell(0, 0, 1, 1, "ID"),
                new FitContentCell(1, 0, 1, 1, "12345678901234567890"),
                new FitContentCell(2, 0, 1, 1, "12345678901234567890"),
                new FitContentCell(3, 0, 1, 1, "12345678901234567890")
            });
            FitContentMeasurements protectedMeasurements = FitContentText.Prepare(
                protectedTable, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            FixedTotalFitResult protectionFailure = LayoutOps.FitContentToTotal(
                protectedMeasurements, LayoutMetrics.MinColumnFeet);
            if (protectionFailure.ProtectionFailedColumns.Count == 0)
            {
                throw new InvalidOperationException(
                    "The constrained fixture did not report its protected column.");
            }

            string warning = UserMessages.LayoutFitToTotalNotReached(
                UserMessages.LayoutFitContentAboveMaximum, 24.0, "in");
            if (warning.IndexOf("Closest supported total: about 24.0 in",
                    StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "The out-of-range Fit Content warning was not readable.");
            }
        }

        private static void CheckFitContentFailureState()
        {
            double[] current = { 0.45, 0.60, 0.75 };
            LayoutSourceSnapshot sentinel = new LayoutSourceSnapshot
            {
                ResolvedPath = "harness-fit-failure"
            };
            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                current, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] feet)
                {
                    return FitContentResult.Failed("harness fit failure");
                },
                delegate (double[] feet, ColumnWidthMode mode,
                    RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                {
                    return new LayoutApplyResult
                    {
                        Success = true,
                        UserMessage = "harness: applied"
                    };
                }))
            {
                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                SetPrivateField(dialog, "_fitContentDiagnostics",
                    ComputeHarnessFit(current).FixedTotal);
                Label comfort = GetPrivateField<Label>(dialog, "_comfortableWidth");
                comfort.Text = "harness estimate";
                SetPrivateField(dialog, "_sourceSnapshot", sentinel);
                SetPrivateField(dialog, "_dirty", true);
                RadioButton columnKeep = FindRadioButton(
                    dialog, UserMessages.LayoutPolicyKeep);
                RadioButton rowKeep = FindRadioButton(
                    dialog, UserMessages.RowModeKeepManual);
                columnKeep.Checked = true;
                rowKeep.Checked = true;
                double[] before = CaptureGridFeet(grid);
                bool dirtyBefore = GetPrivateField<bool>(dialog, "_dirty");
                FixedTotalFitResult feedbackBefore =
                    GetPrivateField<FixedTotalFitResult>(dialog, "_fitContentDiagnostics");

                // Drive the real handler: its failure path shows one MessageBox.
                int boxes = RunDismissingMessageBoxes(delegate
                {
                    InvokePrivate(dialog, "OnFitContent", dialog, EventArgs.Empty);
                });
                Label failureStatus = GetPrivateField<Label>(dialog, "_status");
                if (boxes != 1
                    || failureStatus.Text.IndexOf("harness fit failure",
                        StringComparison.Ordinal) < 0
                    || !ReferenceEquals(feedbackBefore, GetPrivateField<FixedTotalFitResult>(
                        dialog, "_fitContentDiagnostics"))
                    || comfort.Text != "harness estimate"
                    || !EqualFeet(before, CaptureGridFeet(grid))
                    || !ReferenceEquals(sentinel,
                        GetPrivateField<LayoutSourceSnapshot>(dialog, "_sourceSnapshot"))
                    || GetPrivateField<bool>(dialog, "_dirty") != dirtyBefore
                    || !columnKeep.Checked || !rowKeep.Checked)
                {
                    throw new InvalidOperationException(
                        "A failed Fit calculation changed candidates or dialog state.");
                }

                InvokePrivate(dialog, "ClearFitContentFeedback");
                if (GetPrivateField<FixedTotalFitResult>(
                        dialog, "_fitContentDiagnostics") != null
                    || comfort.Text.Length != 0
                    || !EqualFeet(before, CaptureGridFeet(grid))
                    || !ReferenceEquals(sentinel,
                        GetPrivateField<LayoutSourceSnapshot>(dialog, "_sourceSnapshot"))
                    || GetPrivateField<bool>(dialog, "_dirty") != dirtyBefore
                    || !columnKeep.Checked || !rowKeep.Checked)
                {
                    throw new InvalidOperationException(
                        "Clearing Fit feedback changed candidates or dialog state.");
                }
            }
        }

        private static void CheckColumnHeadingLabels()
        {
            const int columns = 53;
            string[] headings = BuildHeadingArray(columns);
            string[] diagnosticHeadings = (string[])headings.Clone();
            double[] feet = new double[columns];
            for (int i = 0; i < feet.Length; i++) feet[i] = 0.4;

            using (LayoutDialog dialog = CreateLayoutDialogWithHeadings(
                feet, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights, headings))
            {
                CheckInitiallyClean(dialog);
                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                double[] before = CaptureGridFeet(grid);
                RadioButton columnKeep = FindRadioButton(
                    dialog, UserMessages.LayoutPolicyKeep);
                RadioButton rowKeep = FindRadioButton(
                    dialog, UserMessages.RowModeKeepManual);
                bool columnKeepBefore = columnKeep.Checked;
                bool rowKeepBefore = rowKeep.Checked;

                ExpectGridLabel(grid, 0, "A  Description");
                ExpectGridLabel(grid, 1, "B  Code");
                ExpectGridLabel(grid, 2, "C  Vertical merge");
                ExpectGridLabel(grid, 3, "D");
                ExpectGridLabel(grid, 4, "E");
                ExpectGridLabel(grid, 5, UserMessages.LayoutColumnName(5, headings));
                ExpectGridLabel(grid, 6, "G  " + new string('\u6F22', 32));
                ExpectGridLabel(grid, 25, "Z  Z column");
                ExpectGridLabel(grid, 26, "AA  AA column");
                ExpectGridLabel(grid, 49, "AX  AX column");
                for (int row = 0; row < grid.Rows.Count; row++)
                {
                    if (GetRowColumnIndex(grid.Rows[row].Tag) != row)
                    {
                        throw new InvalidOperationException(
                            "A grid row lost its immutable schedule-column identity.");
                    }
                }

                headings[0] = "Changed after construction";
                if (Convert.ToString(grid.Rows[0].Cells["label"].Value)
                    != "A  Description")
                {
                    throw new InvalidOperationException(
                        "Dialog labels were not defensively copied at construction.");
                }
                if (!EqualFeet(before, CaptureGridFeet(grid))
                    || columnKeep.Checked != columnKeepBefore
                    || rowKeep.Checked != rowKeepBefore
                    || GetPrivateField<LayoutSourceSnapshot>(
                        dialog, "_sourceSnapshot") != null
                    || GetPrivateField<bool>(dialog, "_dirty"))
                {
                    throw new InvalidOperationException(
                        "Formatter-only heading diagnostics changed dialog state.");
                }

                string protection = UserMessages.LayoutFitContentProtectionFailed(
                    new[] { 1 }, diagnosticHeadings);
                if (protection.IndexOf("B  Code", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Protection diagnostics did not use the grid column name.");
                }

                FitContentTable mergeTable = new FitContentTable(1, 2, new[]
                {
                    new FitContentCell(0, 0, 1, 2, new string('M', 80))
                });
                FitContentMeasurements mergeMeasurements = FitContentText.Prepare(
                    mergeTable, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                    LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
                FixedTotalFitResult mergeFit = LayoutOps.FitContentToTotal(
                    mergeMeasurements, 2.0 * LayoutMetrics.MinColumnFeet);
                if (mergeFit.UnmetMerges.Count == 0
                    || UserMessages.LayoutFitContentMerges(
                        mergeFit.UnmetMerges, diagnosticHeadings).IndexOf(
                            "A  Description", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Merged-cell diagnostics did not use the shared column names.");
                }

                string parse = LengthUnitHelper.InvalidNumbersMessage(
                    new[] { 1 }, LengthUnitChoice.Millimeters, diagnosticHeadings);
                if (parse.IndexOf("B  Code", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Parse diagnostics did not use the shared column name.");
                }

                ValidationResult validation = LayoutOps.ValidateAll(
                    new double[] { 0.05, 0.5 }, null);
                string range = LengthUnitHelper.ValidationMessage(
                    validation, LengthUnitChoice.Millimeters, diagnosticHeadings);
                if (range.IndexOf("A  Description", StringComparison.Ordinal) < 0
                    || range.IndexOf("30.5 to 609.6 mm", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Range diagnostics did not use the shared heading formatter.");
                }
            }

            using (LayoutDialog refreshed = CreateLayoutDialogWithHeadings(
                feet, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights, headings))
            {
                DataGridView grid = GetPrivateField<DataGridView>(refreshed, "_grid");
                ExpectGridLabel(grid, 0, "A  Changed after construction");
            }

            using (LayoutDialog missing = CreateLayoutDialogWithHeadings(
                new double[] { 0.4, 0.4, 0.4 }, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights, null))
            {
                DataGridView grid = GetPrivateField<DataGridView>(missing, "_grid");
                ExpectGridLabel(grid, 0, "A");
                ExpectGridLabel(grid, 1, "B");
                ExpectGridLabel(grid, 2, "C");
            }

            using (LayoutDialog wrongCount = CreateLayoutDialogWithHeadings(
                new double[] { 0.4, 0.4, 0.4 }, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights, new[] { "Only one" }))
            {
                DataGridView grid = GetPrivateField<DataGridView>(wrongCount, "_grid");
                ExpectGridLabel(grid, 0, "A");
                ExpectGridLabel(grid, 1, "B");
                ExpectGridLabel(grid, 2, "C");
            }
        }

        private static void CheckWidthRangeMessages()
        {
            CultureInfo oldCulture = CultureInfo.CurrentCulture;
            CultureInfo oldUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                string[] headings = { "Description", "Code" };

                ValidationResult changed = LayoutOps.ValidateAll(
                    new double[] { 0.05, 2.5 }, null);
                string metric = LengthUnitHelper.ValidationMessage(
                    changed, LengthUnitChoice.Millimeters, headings);
                string imperial = LengthUnitHelper.ValidationMessage(
                    changed, LengthUnitChoice.Inches, headings);
                if (metric.IndexOf("A  Description", StringComparison.Ordinal) < 0
                    || metric.IndexOf("B  Code", StringComparison.Ordinal) < 0
                    || CountOccurrences(metric, "30.5 to 609.6 mm") != 1
                    || metric.IndexOf(" ft", StringComparison.Ordinal) >= 0
                    || imperial.IndexOf("1.20 to 24.00 in", StringComparison.Ordinal) < 0
                    || CountOccurrences(imperial, "1.20 to 24.00 in") != 1
                    || imperial.IndexOf(" ft", StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "Changed-width range messages were not complete or unit-aware.");
                }

                ValidationResult nonFinite = LayoutOps.ValidateAll(
                    new double[] { double.NaN, double.NegativeInfinity }, null);
                string nonFiniteMessage = LengthUnitHelper.ValidationMessage(
                    nonFinite, LengthUnitChoice.Millimeters, headings);
                if (nonFiniteMessage.IndexOf("A  Description", StringComparison.Ordinal) < 0
                    || nonFiniteMessage.IndexOf("B  Code", StringComparison.Ordinal) < 0
                    || CountOccurrences(nonFiniteMessage, "30.5 to 609.6 mm") != 1)
                {
                    throw new InvalidOperationException(
                        "Non-finite width messages lost typed column or range details.");
                }

                ValidationResult totalOnly = LayoutOps.ValidateAll(
                    new double[] { 1.0, 1.0 }, 1.5);
                string totalMessage = LengthUnitHelper.ValidationMessage(
                    totalOnly, LengthUnitChoice.Millimeters, headings);
                if (totalMessage.IndexOf("safe limit", StringComparison.Ordinal) < 0
                    || totalMessage.IndexOf("Changed column widths must",
                        StringComparison.Ordinal) >= 0
                    || totalMessage.IndexOf("609.6 mm", StringComparison.Ordinal) < 0
                    || totalMessage.IndexOf("457.2 mm", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "A total-limit-only message was formatted as a range failure.");
                }

                ValidationResult mixed = LayoutOps.ValidateAll(
                    new double[] { double.NaN, 0.05, 2.5 }, 2.0);
                string mixedMessage = LengthUnitHelper.ValidationMessage(
                    mixed, LengthUnitChoice.Inches, headings);
                if (mixedMessage.IndexOf("do not have finite widths",
                        StringComparison.Ordinal) < 0
                    || mixedMessage.IndexOf("outside the allowed width range",
                        StringComparison.Ordinal) < 0
                    || mixedMessage.IndexOf("safe limit", StringComparison.Ordinal) < 0
                    || CountOccurrences(mixedMessage, "1.20 to 24.00 in") != 1)
                {
                    throw new InvalidOperationException(
                        "Mixed typed validation failures were not combined once.");
                }

                string parse = LengthUnitHelper.InvalidNumbersMessage(
                    new[] { 1 }, LengthUnitChoice.Inches, headings);
                string letterFallback = LengthUnitHelper.InvalidNumbersMessage(
                    new[] { 1 }, LengthUnitChoice.Millimeters, null);
                if (CountOccurrences(parse, "1.20 to 24.00 in") != 1
                    || parse.IndexOf("B  Code", StringComparison.Ordinal) < 0
                    || letterFallback.IndexOf("B", StringComparison.Ordinal) < 0
                    || letterFallback.IndexOf("Description", StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "Parse/range formatter did not preserve heading fallback rules.");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCulture;
                CultureInfo.CurrentUICulture = oldUiCulture;
            }
        }

        private static void CheckLiveEqualNarrowWidthStillAccepted()
        {
            double[] live = { 0.05, 0.75 };
            ValidationResult preserved = LayoutOps.ValidateAll(
                (IList<double>)new double[] { 0.05, 0.75 }, null, live);
            if (!preserved.IsValid || preserved.NormalizedFeet[0] != 0.05)
            {
                throw new InvalidOperationException(
                    "A live-equivalent narrow native width was rejected.");
            }

            ValidationResult changed = LayoutOps.ValidateAll(
                new double[] { 0.06, 0.75 }, null, live);
            if (changed.IsValid
                || changed.OutOfRangeColumns.Length != 1
                || changed.OutOfRangeColumns[0] != 0)
            {
                throw new InvalidOperationException(
                    "A genuinely changed narrow width was not rejected.");
            }

            LayoutSourceSnapshot sentinel = new LayoutSourceSnapshot
            {
                ResolvedPath = "harness-live-baseline"
            };
            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                live, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] feet, ColumnWidthMode mode,
                    RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                {
                    ValidationResult result = LayoutOps.ValidateAll(
                        feet, null, live);
                    return result.IsValid
                        ? new LayoutApplyResult
                        {
                            Success = true,
                            UserMessage = "harness: applied"
                        }
                        : new LayoutApplyResult
                        {
                            Success = false,
                            UserMessage = LengthUnitHelper.ValidationMessage(
                                result, LengthUnitChoice.Millimeters, null),
                            InvalidColumns = result.InvalidColumns
                        };
                }))
            {
                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                grid.Rows[0].Cells["width"].Value =
                    InvokeFormatDisplay(dialog, live[0]);
                InvokeCellEndEdit(dialog, grid, 0);
                SetPrivateField(dialog, "_sourceSnapshot", sentinel);
                InvokePrivate(dialog, "OnApplyNow", dialog, EventArgs.Empty);
                if (GetRowFeet(grid.Rows[0].Tag) != live[0]
                    || GetPrivateField<LayoutSourceSnapshot>(
                        dialog, "_sourceSnapshot") != null
                    || GetPrivateField<bool>(dialog, "_dirty")
                    || GetPrivateField<FixedTotalFitResult>(
                        dialog, "_fitContentDiagnostics") != null)
                {
                    throw new InvalidOperationException(
                        "The live-equal narrow Apply path changed dialog state.");
                }
            }

            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                live, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] feet, ColumnWidthMode mode,
                    RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                {
                    ValidationResult result = LayoutOps.ValidateAll(
                        feet, null, live);
                    return result.IsValid
                        ? new LayoutApplyResult
                        {
                            Success = true,
                            UserMessage = "harness: applied"
                        }
                        : new LayoutApplyResult
                        {
                            Success = false,
                            UserMessage = LengthUnitHelper.ValidationMessage(
                                result, LengthUnitChoice.Millimeters, null),
                            InvalidColumns = result.InvalidColumns
                        };
                }))
            {
                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                grid.Rows[0].Cells["width"].Value =
                    LengthUnitHelper.FromFeet(0.06, LengthUnitChoice.Millimeters)
                        .ToString("F1", CultureInfo.InvariantCulture);
                InvokeCellEndEdit(dialog, grid, 0);
                SetPrivateField(dialog, "_sourceSnapshot", sentinel);
                bool dirtyBefore = GetPrivateField<bool>(dialog, "_dirty");
                double[] feetBefore = CaptureGridFeet(grid);

                // Drive the real handler: one MessageBox, the refused column
                // highlighted, and every candidate/state value kept.
                int boxes = RunDismissingMessageBoxes(delegate
                {
                    InvokePrivate(dialog, "OnApplyNow", dialog, EventArgs.Empty);
                });
                DataGridViewCell refused = grid.Rows[0].Cells["width"];
                DataGridViewCell accepted = grid.Rows[1].Cells["width"];
                Label applyStatus = GetPrivateField<Label>(dialog, "_status");
                if (boxes != 1
                    || refused.Style.BackColor != Color.MistyRose
                    || string.IsNullOrEmpty(refused.ErrorText)
                    || accepted.Style.BackColor == Color.MistyRose
                    || applyStatus.Text.IndexOf("30.5", StringComparison.Ordinal) < 0
                    || !EqualFeet(feetBefore, CaptureGridFeet(grid))
                    || !ReferenceEquals(sentinel,
                        GetPrivateField<LayoutSourceSnapshot>(dialog, "_sourceSnapshot"))
                    || GetPrivateField<bool>(dialog, "_dirty") != dirtyBefore)
                {
                    throw new InvalidOperationException(
                        "A failed changed-width Apply did not preserve candidates and state.");
                }
            }
        }

        private static void CheckLengthUnitMapping()
        {
            if (LengthUnitHelper.FromLengthUnit(null)
                != LengthUnitChoice.Millimeters
                || LengthUnitHelper.FromLengthUnit(
                    Autodesk.Revit.DB.UnitTypeId.Inches)
                    != LengthUnitChoice.Inches
                || LengthUnitHelper.FromLengthUnit(
                    Autodesk.Revit.DB.UnitTypeId.Millimeters)
                    != LengthUnitChoice.Millimeters)
            {
                throw new InvalidOperationException(
                    "Document-free length-unit mapping changed.");
            }
        }

        private static void CheckFatalLayoutResultShapes()
        {
            FitContentResult fit = FitContentResult.RegenerationFailed();
            if (!fit.FatalRegenerationFailure || fit.Success || fit.Feet != null
                || fit.FixedTotal != null || fit.ColumnHeadings != null
                || fit.UserMessage != UserMessages.RegenerationFailure)
            {
                throw new InvalidOperationException(
                    "Fit Content fatal result shape changed.");
            }

            ExcelWidthResult restore = ExcelWidthResult.RegenerationFailed();
            if (!restore.FatalRegenerationFailure || restore.Success
                || restore.Feet != null
                || restore.UserMessage != UserMessages.RegenerationFailure)
            {
                throw new InvalidOperationException(
                    "Restore fatal result shape changed.");
            }

            LayoutApplyResult apply = LayoutApplyResult.RegenerationFailed();
            if (!apply.FatalRegenerationFailure || apply.Success
                || apply.UserMessage != UserMessages.RegenerationFailure)
            {
                throw new InvalidOperationException(
                    "Apply fatal result shape changed.");
            }

            FitContentResult ordinary = FitContentResult.Failed("ordinary");
            if (ordinary.FatalRegenerationFailure || ordinary.Success
                || ordinary.Feet != null || ordinary.FixedTotal != null
                || ordinary.ColumnHeadings != null)
            {
                throw new InvalidOperationException(
                    "Ordinary Fit Content failure was classified as fatal.");
            }

            ExcelWidthResult ordinaryRestore =
                ExcelWidthResult.Failed("ordinary restore");
            LayoutApplyResult ordinaryApply =
                LayoutApplyResult.Failed("ordinary apply", new[] { 0 });
            if (ordinaryRestore.FatalRegenerationFailure
                || ordinaryRestore.Success || ordinaryApply.FatalRegenerationFailure
                || ordinaryApply.Success || ordinaryApply.InvalidColumns == null)
            {
                throw new InvalidOperationException(
                    "Ordinary Restore/Apply failures were classified as fatal.");
            }
        }

        private static void ExpectGridLabel(
            DataGridView grid, int rowIndex, string expected)
        {
            string actual = Convert.ToString(
                grid.Rows[rowIndex].Cells["label"].Value);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Unexpected grid label at row " + rowIndex + ": ["
                    + actual + "] expected [" + expected + "]");
            }
        }

        private static int GetRowColumnIndex(object state)
        {
            if (state == null) throw new InvalidOperationException("Missing row state.");
            FieldInfo field = state.GetType().GetField(
                "ColumnIndex", BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new InvalidOperationException("Row identity field was not found.");
            }
            return (int)field.GetValue(state);
        }

        private static double SumGridFeet(DataGridView grid)
        {
            double total = 0.0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                total += GetRowFeet(row.Tag);
            }
            return total;
        }

        private static double[] CaptureGridFeet(DataGridView grid)
        {
            double[] result = new double[grid.Rows.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = GetRowFeet(grid.Rows[i].Tag);
            }
            return result;
        }

        private static bool EqualFeet(double[] left, double[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (Math.Abs(left[i] - right[i]) > LayoutOps.Tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private static void CheckUnchangedDisplayEdits()
        {
            double[] metricFeet = { 0.1, 0.05, 2.5 };
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Millimeters, metricFeet, 0, true, false);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Millimeters, metricFeet, 0, true, true);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Millimeters, metricFeet, 1, false, false);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Millimeters, metricFeet, 2, false, false);

            double[] imperialFeet = { 0.123456, 0.05, 2.5 };
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Inches, imperialFeet, 0, false, false);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Inches, imperialFeet, 0, false, true);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Inches, imperialFeet, 1, false, false);
            CheckUnchangedDisplayCase(
                LengthUnitChoice.Inches, imperialFeet, 2, false, false);
        }

        private static void CheckUnchangedDisplayCase(
            LengthUnitChoice unit, double[] feet, int rowIndex,
            bool useEquivalentSpelling, bool initiallyDirty)
        {
            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                feet, unit, ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] f, ColumnWidthMode m, RowHeightMode r,
                    LayoutSourceSnapshot s)
                {
                    return new LayoutApplyResult { Success = true, UserMessage = "harness: applied" };
                }))
            {
                RadioButton columnKeep = FindRadioButton(
                    dialog, UserMessages.LayoutPolicyKeep);
                RadioButton rowKeep = FindRadioButton(
                    dialog, UserMessages.RowModeKeepManual);
                if (initiallyDirty)
                {
                    columnKeep.Checked = true;
                    rowKeep.Checked = true;
                }

                bool dirtyBefore = GetPrivateField<bool>(dialog, "_dirty");
                bool columnKeepBefore = columnKeep.Checked;
                bool rowKeepBefore = rowKeep.Checked;
                LayoutSourceSnapshot sentinel = new LayoutSourceSnapshot
                {
                    ResolvedPath = "harness-sentinel"
                };
                SetPrivateField(dialog, "_sourceSnapshot", sentinel);

                DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
                object state = grid.Rows[rowIndex].Tag;
                double originalFeet = GetRowFeet(state);
                string formatted = InvokeFormatDisplay(dialog, originalFeet);
                grid.Rows[rowIndex].Cells["width"].Value =
                    useEquivalentSpelling ? formatted + "0" : formatted;
                InvokeCellEndEdit(dialog, grid, rowIndex);

                if (Math.Abs(GetRowFeet(grid.Rows[rowIndex].Tag) - originalFeet) > 1e-12)
                {
                    throw new InvalidOperationException(
                        "An unchanged displayed width changed its precise feet value.");
                }
                if (GetPrivateField<bool>(dialog, "_dirty") != dirtyBefore
                    || !ReferenceEquals(
                        sentinel, GetPrivateField<LayoutSourceSnapshot>(
                            dialog, "_sourceSnapshot"))
                    || columnKeep.Checked != columnKeepBefore
                    || rowKeep.Checked != rowKeepBefore)
                {
                    throw new InvalidOperationException(
                        "An unchanged displayed width changed dialog state.");
                }

                double changedDisplay =
                    LengthUnitHelper.FromFeet(originalFeet, unit) + 1.0;
                string changedText = changedDisplay.ToString(
                    "F" + LengthUnitHelper.Decimals(unit),
                    CultureInfo.CurrentCulture);
                grid.Rows[rowIndex].Cells["width"].Value = changedText;
                InvokeCellEndEdit(dialog, grid, rowIndex);

                // The dialog parses the text it was given, so compare against that
                // text's value, not the unrounded number used to build it.
                double changedFeet = LengthUnitHelper.ToFeet(
                    double.Parse(changedText, NumberStyles.Float, CultureInfo.CurrentCulture),
                    unit);
                if (Math.Abs(GetRowFeet(grid.Rows[rowIndex].Tag) - changedFeet) > 1e-12
                    || !GetPrivateField<bool>(dialog, "_dirty")
                    || GetPrivateField<LayoutSourceSnapshot>(
                        dialog, "_sourceSnapshot") != null
                    || !columnKeep.Checked
                    || rowKeep.Checked != rowKeepBefore)
                {
                    throw new InvalidOperationException(
                        "A changed valid width did not follow the existing dirty/Keep behavior.");
                }
            }
        }

        private static void CheckPolicyOnlyNarrowApply()
        {
            double[] current = { 0.05, 0.75 };
            double[] capturedFeet = null;
            ColumnWidthMode appliedColumnMode = ColumnWidthMode.KeepManualWidths;
            RowHeightMode appliedRowMode = RowHeightMode.UseDefaultHeights;

            using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                current, LengthUnitChoice.Millimeters,
                ColumnWidthMode.ApplyExcelWidths, RowHeightMode.UseDefaultHeights,
                delegate (double[] feet, ColumnWidthMode columnMode,
                    RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                {
                    capturedFeet = (double[])feet.Clone();
                    appliedColumnMode = columnMode;
                    appliedRowMode = rowMode;
                    return new LayoutApplyResult
                    {
                        Success = true,
                        UserMessage = "harness: applied"
                    };
                }))
            {
                RadioButton rowKeep = FindRadioButton(
                    dialog, UserMessages.RowModeKeepManual);
                rowKeep.Checked = true;
                InvokePrivate(dialog, "OnApplyNow", dialog, EventArgs.Empty);
            }

            if (capturedFeet == null
                || capturedFeet.Length != current.Length
                || capturedFeet[0] != current[0]
                || capturedFeet[1] != current[1]
                || appliedColumnMode != ColumnWidthMode.ApplyExcelWidths
                || appliedRowMode != RowHeightMode.KeepManualHeights)
            {
                throw new InvalidOperationException(
                    "Policy-only Apply did not preserve the narrow width or row policy.");
            }
        }

        private static void CheckRowHeightRenderNotes()
        {
            ReloadOutcome success = NewSuccessfulOutcome();
            success.RowHeightsRejected = 0;
            string noRows = UserMessages.ReloadOutcomeMessage(success);
            if (noRows.IndexOf("Revit did not keep the saved height",
                StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("A zero row count produced a row note.");
            }

            success.RowHeightsRejected = 2;
            string rowNote = UserMessages.RowHeightsNotKept(2, success.ScheduleName);
            string twoRows = UserMessages.ReloadOutcomeMessage(success);
            if (CountOccurrences(twoRows, rowNote) != 1)
            {
                throw new InvalidOperationException(
                    "The success message did not contain the named row note exactly once.");
            }

            success.PreviousColumns = 1;
            success.Columns = 2;
            string combined = UserMessages.ReloadOutcomeMessage(success);
            string columnNote = UserMessages.ReloadColumnCountNote(success);
            if (CountOccurrences(combined, columnNote) != 1
                || CountOccurrences(combined, rowNote) != 1)
            {
                throw new InvalidOperationException(
                    "Combined reload notes were missing or duplicated.");
            }

            ReloadOutcome failed = NewSuccessfulOutcome();
            failed.Kind = ReloadResultKind.RenderFailed;
            failed.RowHeightsRejected = 2;
            failed.Detail = "harness failure";
            if (UserMessages.ReloadOutcomeMessage(failed) != failed.Detail)
            {
                throw new InvalidOperationException(
                    "A failed reload exposed a success row note.");
            }

            ReloadOutcome cancelled = NewSuccessfulOutcome();
            cancelled.Kind = ReloadResultKind.Cancelled;
            cancelled.RowHeightsRejected = 2;
            if (!string.IsNullOrEmpty(UserMessages.ReloadOutcomeMessage(cancelled)))
            {
                throw new InvalidOperationException(
                    "A cancelled reload exposed a success row note.");
            }

            string rowOnly = UserMessages.AppendRenderNotes(
                string.Empty, 0, 0, false, false, 0, 2, success.ScheduleName);
            if (rowOnly != rowNote || rowOnly.StartsWith("\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The bulk row-only note was not named or had a leading separator.");
            }
        }

        private static void CheckRowFitPolicyHint()
        {
            string expected =
                "Fit-to-text also fits rows when you apply column widths. Keep preserves current heights.";
            if (UserMessages.LayoutRowPolicyHint != expected)
            {
                throw new InvalidOperationException(
                    "The row-policy hint did not use the shared fit-to-text wording.");
            }
        }

        private static void CheckKeepRowNoticeFormatting()
        {
            string summary = "Applied layout.";
            string notice = UserMessages.LayoutKeepRowHeightsNote;
            string withNotice = UserMessages.AppendLayoutRowPolicyNote(
                summary, RowHeightMode.KeepManualHeights);
            if (withNotice != summary + "\n\n" + notice
                || CountOccurrences(withNotice, notice) != 1)
            {
                throw new InvalidOperationException(
                    "The Keep row-height notice was missing or duplicated.");
            }
            if (UserMessages.AppendLayoutRowPolicyNote(
                summary, RowHeightMode.UseDefaultHeights) != summary)
            {
                throw new InvalidOperationException(
                    "The Keep row-height notice appeared for Fit-to-text.");
            }
        }

        private static void ExpectExactNote(string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Fitted-row note mismatch. Expected: [" + expected
                    + "] Actual: [" + actual + "]");
            }
        }

        private static void CheckRowFitRenderNotes()
        {
            // Literal wording, independent of the formatter the later checks reuse.
            ExpectExactNote(UserMessages.RowsMayHideTextNote(0), string.Empty);
            ExpectExactNote(UserMessages.RowsMayHideTextNote(0, "Panel Loads"),
                string.Empty);
            ExpectExactNote(UserMessages.RowsMayHideTextNote(2),
                "Text may be hidden in 2 row(s): the rows could not be fitted to their text.");
            ExpectExactNote(UserMessages.RowsMayHideTextNote(2, "Panel Loads"),
                "\"Panel Loads\": Text may be hidden in 2 row(s): "
                + "the rows could not be fitted to their text.");
            ExpectExactNote(UserMessages.RowsMayHideTextNote(null),
                "Rows could not be fitted to their text because measurements were "
                + "unavailable. Check for hidden text.");
            ExpectExactNote(UserMessages.RowsMayHideTextNote(null, "Panel Loads"),
                "\"Panel Loads\": Rows could not be fitted to their text because "
                + "measurements were unavailable. Check for hidden text.");

            ReloadOutcome success = NewSuccessfulOutcome();
            string zero = UserMessages.ReloadOutcomeMessage(success);
            if (zero.IndexOf("Text may be hidden in ", StringComparison.Ordinal) >= 0
                || zero.IndexOf("measurements were unavailable", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "A zero fitted-row count produced a success note.");
            }

            success.RowsMayHideText = 2;
            string positiveNote = UserMessages.RowsMayHideTextNote(
                2, success.ScheduleName);
            string positive = UserMessages.ReloadOutcomeMessage(success);
            if (CountOccurrences(positive, positiveNote) != 1)
            {
                throw new InvalidOperationException(
                    "The positive fitted-row note was missing or duplicated.");
            }

            success.RowsMayHideText = null;
            string unavailableNote = UserMessages.RowsMayHideTextNote(
                null, success.ScheduleName);
            string unavailable = UserMessages.ReloadOutcomeMessage(success);
            if (CountOccurrences(unavailable, unavailableNote) != 1
                || unavailable.IndexOf("2 row(s)", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "The unavailable fitted-row note invented a count.");
            }

            success.RowsMayHideText = 3;
            success.RowHeightsRejected = 1;
            success.PreviousColumns = 1;
            success.Columns = 2;
            success.Truncated = true;
            success.ViewWidthProblem = true;
            success.ColumnWidthsRejected = 1;
            string combined = UserMessages.ReloadOutcomeMessage(success);
            string positiveThreeNote = UserMessages.RowsMayHideTextNote(
                3, success.ScheduleName);
            string rowNote = UserMessages.RowHeightsNotKept(
                1, success.ScheduleName);
            string columnNote = UserMessages.ReloadColumnCountNote(success);
            string truncationNote = UserMessages.TruncationNote(
                success.SourceRows, success.SourceColumns);
            string viewNote = UserMessages.ViewWidthNote(1);
            if (CountOccurrences(combined, positiveThreeNote) != 1
                || CountOccurrences(combined, rowNote) != 1
                || CountOccurrences(combined, columnNote) != 1
                || CountOccurrences(combined, truncationNote) != 1
                || CountOccurrences(combined, viewNote) != 1)
            {
                throw new InvalidOperationException(
                    "Fitted-row, truncation, view, column and Keep notes did not coexist once each.");
            }

            string bulk = UserMessages.AppendRenderNotes(
                string.Empty, 0, 0, false, false, 0, 0,
                success.ScheduleName, rowsMayHideText: 4);
            string bulkNote = UserMessages.RowsMayHideTextNote(
                4, success.ScheduleName);
            if (bulk != bulkNote || bulk.StartsWith("\n", StringComparison.Ordinal)
                || CountOccurrences(bulk, bulkNote) != 1)
            {
                throw new InvalidOperationException(
                    "The bulk row-only fitted-row note was not formatted independently.");
            }

            ReloadOutcome failed = NewSuccessfulOutcome();
            failed.Kind = ReloadResultKind.RenderFailed;
            failed.RowsMayHideText = 4;
            failed.Detail = "harness failure";
            if (UserMessages.ReloadOutcomeMessage(failed) != failed.Detail)
            {
                throw new InvalidOperationException(
                    "A failed reload exposed a fitted-row success note.");
            }

            ReloadOutcome cancelled = NewSuccessfulOutcome();
            cancelled.Kind = ReloadResultKind.Cancelled;
            cancelled.RowsMayHideText = null;
            if (!string.IsNullOrEmpty(UserMessages.ReloadOutcomeMessage(cancelled)))
            {
                throw new InvalidOperationException(
                    "A cancelled reload exposed an unavailable fitted-row note.");
            }

            ReloadOutcome fatal = NewSuccessfulOutcome();
            fatal.Kind = ReloadResultKind.RegenerationFailed;
            fatal.RowsMayHideText = 5;
            fatal.Detail = "fatal harness failure";
            if (UserMessages.ReloadOutcomeMessage(fatal) != fatal.Detail)
            {
                throw new InvalidOperationException(
                    "A fatal reload exposed a fitted-row success note.");
            }

            string initialLink = UserMessages.AppendRenderNotes(
                "Created linked schedule \"Sample\".", 3, 2,
                false, false, 0, scheduleName: "Sample",
                rowsMayHideText: 2);
            string initialNote = UserMessages.RowsMayHideTextNote(2, "Sample");
            if (CountOccurrences(initialLink, initialNote) != 1)
            {
                throw new InvalidOperationException(
                    "The initial-Link-style formatter call did not show its fitted-row note once.");
            }
        }

        private static void CheckRowFitWarningApplySucceeds()
        {
            int?[] counts = { 2, null };
            foreach (int? count in counts)
            {
                double[] current = { 0.4, 0.75 };
                string message = UserMessages.AppendRenderNotes(
                    "harness: applied", 0, 0, false, false, 0, 0,
                    "Sample Workbook - Data", rowsMayHideText: count);
                using (LayoutDialog dialog = CreateLayoutDialogWithFeet(
                    current, LengthUnitChoice.Millimeters,
                    ColumnWidthMode.ApplyExcelWidths,
                    RowHeightMode.UseDefaultHeights,
                    delegate (double[] feet, ColumnWidthMode columnMode,
                        RowHeightMode rowMode, LayoutSourceSnapshot snapshot)
                    {
                        return new LayoutApplyResult
                        {
                            Success = true,
                            UserMessage = message
                        };
                    }))
                {
                    RadioButton columnKeep = FindRadioButton(
                        dialog, UserMessages.LayoutPolicyKeep);
                    columnKeep.Checked = true;
                    if (!GetPrivateField<bool>(dialog, "_dirty"))
                    {
                        throw new InvalidOperationException(
                            "The warning Apply check did not create a dirty dialog state.");
                    }

                    InvokePrivate(dialog, "OnApplyNow", dialog, EventArgs.Empty);
                    Label status = GetPrivateField<Label>(dialog, "_status");
                    if (GetPrivateField<bool>(dialog, "_dirty")
                        || status.Text != message
                        || CountOccurrences(status.Text,
                            UserMessages.RowsMayHideTextNote(
                                count, "Sample Workbook - Data")) != 1)
                    {
                        throw new InvalidOperationException(
                            "A successful warning Apply did not clear dirty state or show its note once.");
                    }
                }
            }
        }

        private static ReloadOutcome NewSuccessfulOutcome()
        {
            return new ReloadOutcome
            {
                Kind = ReloadResultKind.Success,
                ScheduleName = "Sample Workbook - Data",
                Identity = SourceIdentityState.Match,
                Rows = 3,
                Columns = 2,
                SourceRows = 3,
                SourceColumns = 2
            };
        }

        private static void InvokeCellEndEdit(
            LayoutDialog dialog, DataGridView grid, int rowIndex)
        {
            InvokePrivate(
                dialog, "OnCellEndEdit", grid,
                new DataGridViewCellEventArgs(
                    grid.Columns["width"].Index, rowIndex));
        }

        private static string InvokeFormatDisplay(LayoutDialog dialog, double feet)
        {
            return (string)typeof(LayoutDialog).GetMethod(
                "FormatDisplay", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(dialog, new object[] { feet });
        }

        private static double GetRowFeet(object state)
        {
            FieldInfo field = state.GetType().GetField(
                "Feet", BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("Layout row state was not found.");
            }
            return (double)field.GetValue(state);
        }

        // ---- harness-only message box handling ----
        // A real failure handler shows a modal MessageBox. A WinForms timer still
        // ticks inside that modal loop, so it closes each box and counts it; the
        // checks can then drive the real handlers instead of their delegates.

        private delegate bool EnumThreadWindowsProc(IntPtr window, IntPtr parameter);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId,
            EnumThreadWindowsProc callback, IntPtr parameter);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [System.Runtime.InteropServices.DllImport("user32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name,
            int capacity);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message,
            IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const uint WmClose = 0x0010;

        private static int RunDismissingMessageBoxes(Action action)
        {
            int dismissed = 0;
            List<IntPtr> seen = new List<IntPtr>();
            using (Timer timer = new Timer { Interval = 50 })
            {
                timer.Tick += delegate
                {
                    EnumThreadWindows(GetCurrentThreadId(),
                        delegate (IntPtr window, IntPtr parameter)
                        {
                            StringBuilder name = new StringBuilder(32);
                            GetClassName(window, name, name.Capacity);
                            if (name.ToString() == "#32770"
                                && IsWindowVisible(window)
                                && !seen.Contains(window))
                            {
                                seen.Add(window);
                                dismissed++;
                                PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
                            }
                            return true;
                        }, IntPtr.Zero);
                };
                timer.Start();
                action();
                timer.Stop();
            }
            return dismissed;
        }

        private static void InvokePrivate(
            object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Private harness target was not found: " + name);
            }
            method.Invoke(target, arguments);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    "Private harness field was not found: " + name);
            }
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(
            object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    "Private harness field was not found: " + name);
            }
            field.SetValue(target, value);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(value, start, StringComparison.Ordinal);
                if (index < 0) return count;
                count++;
                start = index + value.Length;
            }
        }

        private static void Render(int columns, LengthUnitChoice unit, string baseName, float fontScale)
        {
            RenderDialog(CreateLayoutDialog(
                columns, unit, ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights), baseName, fontScale, false);
        }

        /// <summary>
        /// A narrow total over long protected numbers, capped prose and a merged
        /// title: Fit Content returns Constrained with protection failures, an
        /// unmet merge and a limited estimate, so the estimate label and the long
        /// notes are actually on screen in every capture.
        /// </summary>
        private static void RenderConstrained(LengthUnitChoice unit, string baseName,
            float fontScale)
        {
            double[] narrow = { 0.11, 0.11, 0.11 };
            RenderDialog(CreateLayoutDialogWithFeet(
                narrow, unit, ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.UseDefaultHeights,
                delegate (double[] current) { return ComputeStressFit(current); },
                delegate (double[] f, ColumnWidthMode m, RowHeightMode r, LayoutSourceSnapshot s)
                {
                    return new LayoutApplyResult { Success = true, UserMessage = "harness: applied" };
                }), baseName, fontScale, true);
        }

        private static FitContentResult ComputeStressFit(double[] currentFeet)
        {
            string number = "12345678901234567890";
            string prose = new string('A', 70);
            List<FitContentCell> cells = new List<FitContentCell>
            {
                new FitContentCell(0, 0, 1, 1, "Number"),
                new FitContentCell(0, 1, 1, 1, "Description"),
                new FitContentCell(0, 2, 1, 1, "Code")
            };
            for (int row = 1; row <= 3; row++)
            {
                cells.Add(new FitContentCell(row, 0, 1, 1, number));
                cells.Add(new FitContentCell(row, 1, 1, 1, prose));
                cells.Add(new FitContentCell(row, 2, 1, 1, "AB-" + number));
            }
            cells.Add(new FitContentCell(4, 0, 1, 3,
                "A merged title that is much longer than the narrow table width"));
            FitContentTable table = new FitContentTable(5, 3, cells);
            FitContentMeasurements measurements = FitContentText.Prepare(
                table, 1.0, LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
            double total = 0.0;
            foreach (double feet in currentFeet) total += feet;
            FixedTotalFitResult fit = LayoutOps.FitContentToTotal(measurements, total);
            return new FitContentResult
            {
                Success = fit.CanApply,
                Feet = ToArray(fit.Feet),
                FixedTotal = fit,
                ColumnHeadings = FitContentText.GetColumnHeadings(table)
            };
        }

        private static void RenderDialog(LayoutDialog dialog, string baseName,
            float fontScale, bool expectConstrained)
        {
            CheckPolicyGroups(dialog);
            InvokePrivate(dialog, "OnFitContent", dialog, EventArgs.Empty);
            if (expectConstrained)
            {
                FixedTotalFitResult fit = GetPrivateField<FixedTotalFitResult>(
                    dialog, "_fitContentDiagnostics");
                Label comfort = GetPrivateField<Label>(dialog, "_comfortableWidth");
                if (fit == null || fit.Stage != FitContentStage.Constrained
                    || fit.ProtectionFailedColumns.Count == 0
                    || fit.UnmetMerges.Count == 0
                    || !fit.ComfortableEstimateLimited
                    || comfort.Text.Length == 0)
                {
                    throw new InvalidOperationException(
                        "The constrained render did not show its estimate and diagnostics: "
                        + baseName);
                }
            }

            if (Math.Abs(fontScale - 1.0f) > 0.01f)
            {
                dialog.Font = new Font(dialog.Font.FontFamily, dialog.Font.Size * fontScale);
            }
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(0, 0);
            dialog.Show();
            // Control.Visible reports effective visibility, so check once shown.
            if (expectConstrained
                && !GetPrivateField<Label>(dialog, "_comfortableWidth").Visible)
            {
                throw new InvalidOperationException(
                    "The constrained estimate label is hidden: " + baseName);
            }
            CaptureLayout(dialog, baseName, false);

            dialog.Width = Math.Max(dialog.MinimumSize.Width, dialog.Width - 24);
            // A slightly narrower dialog may or may not overflow: require a
            // scrollbar only when content is actually cut off vertically.
            CaptureLayout(dialog, baseName + "_resize", null);

            if (fontScale > 1.01f)
            {
                dialog.Size = dialog.MinimumSize;
                CaptureLayout(dialog, baseName + "_min", true);
            }

            if (Math.Abs(fontScale - 2.0f) < 0.01f)
            {
                Size minimum = dialog.MinimumSize;
                // Harness-only constraint: allow an outer height below the production minimum.
                dialog.MinimumSize = new Size(minimum.Width, 1);
                dialog.Height = 560;
                CaptureLayout(dialog, baseName + "_short", true);
            }

            SetPrivateField(dialog, "_dirty", false);
            dialog.Close();
            dialog.Dispose();
        }

        private static void CaptureLayout(
            LayoutDialog dialog, string baseName, bool expectVerticalScroll)
        {
            CaptureLayout(dialog, baseName, (bool?)expectVerticalScroll);
        }

        /// <summary>
        /// expectVerticalScroll: true = must scroll, false = must fit,
        /// null = may scroll, but vertically cut-off content needs a scrollbar.
        /// </summary>
        private static void CaptureLayout(
            LayoutDialog dialog, string baseName, bool? expectVerticalScroll)
        {
            Application.DoEvents();
            dialog.Refresh();
            Application.DoEvents();

            TableLayoutPanel root = dialog.Controls.Count > 0
                ? dialog.Controls[0] as TableLayoutPanel
                : null;
            bool verticalScroll = root != null && root.VerticalScroll.Visible;
            CheckGridCells(dialog, baseName);

            using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                bmp.Save(Path.Combine(_outDir, baseName + ".png"));
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("form: " + dialog.Width + "x" + dialog.Height
                + "  client=" + dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            if (root != null)
            {
                sb.AppendLine("root scroll: auto=" + root.AutoScroll
                    + " vertical=" + verticalScroll
                    + " minimum=" + root.AutoScrollMinSize.Height
                    + " client=" + root.ClientSize.Height);
            }
            DumpControls(dialog, dialog, sb, 0);
            string report = sb.ToString();
            File.WriteAllText(Path.Combine(_outDir, baseName + ".txt"), report);

            if (report.IndexOf("<<< OVERLAP", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "Layout render reported sibling overlap: " + baseName);
            }
            if (expectVerticalScroll == true && (root == null || !verticalScroll))
            {
                throw new InvalidOperationException(
                    "Constrained layout render did not show a vertical scrollbar: " + baseName);
            }
            if (report.IndexOf("HORIZONTAL CLIPPED", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "Layout render horizontally clipped a visible control: "
                    + baseName);
            }
            bool verticallyClipped =
                report.IndexOf("VERTICAL CLIPPED", StringComparison.Ordinal) >= 0;
            if (verticallyClipped && (expectVerticalScroll == false
                || (expectVerticalScroll == null && !verticalScroll)))
            {
                throw new InvalidOperationException(
                    "Layout render vertically clipped a visible control: " + baseName);
            }
        }

        private static void CheckGridCells(LayoutDialog dialog, string baseName)
        {
            DataGridView grid = GetPrivateField<DataGridView>(dialog, "_grid");
            DataGridViewColumn label = grid.Columns["label"];
            DataGridViewColumn width = grid.Columns["width"];
            if (label.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill
                || label.DefaultCellStyle.WrapMode != DataGridViewTriState.True
                || width.AutoSizeMode != DataGridViewAutoSizeColumnMode.AllCells
                || width.DefaultCellStyle.WrapMode != DataGridViewTriState.False)
            {
                throw new InvalidOperationException(
                    "The layout grid did not use the required Fill/AllCells wrapping modes.");
            }

            foreach (Control child in grid.Controls)
            {
                if (child is HScrollBar && child.Visible)
                {
                    throw new InvalidOperationException(
                        "Heading labels introduced a visible horizontal grid scrollbar: "
                        + baseName);
                }
            }

            // The grid's own preferred width already includes the header cell's
            // padding and borders; a squeezed column would fall below it.
            int numericHeaderWidth = width.GetPreferredWidth(
                DataGridViewAutoSizeColumnMode.AllCells, true);
            if (width.Width < numericHeaderWidth)
            {
                throw new InvalidOperationException(
                    "The numeric width header is not fully visible: " + baseName
                    + " (column " + width.Width + " px, needed "
                    + numericHeaderWidth + " px)");
            }

            bool foundLongHeading = false;
            for (int rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                DataGridViewRow row = grid.Rows[rowIndex];
                DataGridViewCell labelCell = row.Cells["label"];
                DataGridViewCell widthCell = row.Cells["width"];
                string labelText = Convert.ToString(labelCell.Value);
                string widthText = Convert.ToString(widthCell.Value);
                foundLongHeading = foundLongHeading || labelText.Length > 20;

                int numericWidth = TextRenderer.MeasureText(
                    widthText, grid.Font).Width + 12;
                if (width.Width < numericWidth)
                {
                    throw new InvalidOperationException(
                        "A numeric width value is clipped: " + baseName);
                }

                Size labelSize = TextRenderer.MeasureText(
                    labelText, grid.Font,
                    new Size(Math.Max(1, label.Width - 8), 10000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                if (row.Height + 4 < labelSize.Height)
                {
                    throw new InvalidOperationException(
                        "A wrapped heading row is shorter than its label: " + baseName);
                }
            }

            if (foundLongHeading && label.Width <= 0)
            {
                throw new InvalidOperationException(
                    "The wrapped heading column has no visible width: " + baseName);
            }

            if (grid.Rows.Count > 10)
            {
                try
                {
                    grid.FirstDisplayedScrollingRowIndex = grid.Rows.Count - 1;
                    Application.DoEvents();
                    Rectangle last = grid.GetRowDisplayRectangle(
                        grid.Rows.Count - 1, true);
                    if (last.Bottom <= grid.ColumnHeadersHeight
                        || last.Top >= grid.ClientSize.Height)
                    {
                        throw new InvalidOperationException(
                            "The last of 50 heading rows was not reachable: " + baseName);
                    }
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException(
                        "The grid could not scroll to its last heading row: " + baseName,
                        exception);
                }
            }
        }

        private static void CheckPolicyCombinations()
        {
            CheckPolicyCombination(
                ColumnWidthMode.KeepManualWidths,
                RowHeightMode.KeepManualHeights,
                true, true);
            CheckPolicyCombination(
                ColumnWidthMode.ApplyExcelWidths,
                RowHeightMode.KeepManualHeights,
                false, true);
            CheckPolicyCombination(
                ColumnWidthMode.ApplyExcelWidths,
                (RowHeightMode)99,
                false, false);
        }

        private static void CheckPolicyCombination(
            ColumnWidthMode columnMode, RowHeightMode rowMode,
            bool expectedColumnKeep, bool expectedRowKeep)
        {
            using (LayoutDialog dialog = CreateLayoutDialog(
                3, LengthUnitChoice.Millimeters, columnMode, rowMode))
            {
                // Harness-only reflection check: construction must not mark the dialog dirty.
                CheckInitiallyClean(dialog);
                CheckPolicySelection(dialog, expectedColumnKeep, expectedRowKeep);

                RadioButton columnKeep = FindRadioButton(dialog, UserMessages.LayoutPolicyKeep);
                RadioButton columnRecalculate = FindRadioButton(dialog, UserMessages.LayoutPolicyRecalculate);
                RadioButton rowDefault = FindRadioButton(dialog, UserMessages.RowModeFitToText);
                RadioButton rowKeep = FindRadioButton(dialog, UserMessages.RowModeKeepManual);

                RadioButton rowToToggle = expectedRowKeep ? rowDefault : rowKeep;
                rowToToggle.Checked = true;
                CheckPolicySelection(dialog, expectedColumnKeep, !expectedRowKeep);

                RadioButton columnToToggle = expectedColumnKeep ? columnRecalculate : columnKeep;
                columnToToggle.Checked = true;
                CheckPolicySelection(dialog, !expectedColumnKeep, !expectedRowKeep);
            }
        }

        private static void CheckInitiallyClean(LayoutDialog dialog)
        {
            FieldInfo dirtyField = typeof(LayoutDialog).GetField(
                "_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            if (dirtyField == null)
            {
                throw new InvalidOperationException("Layout dialog dirty field was not found.");
            }
            object value = dirtyField.GetValue(dialog);
            if (!(value is bool) || (bool)value)
            {
                throw new InvalidOperationException(
                    "Layout dialog was dirty immediately after construction.");
            }
        }

        private static void RenderLinkExcel(string baseName, float fontScale)
        {
            LinkExcelDialog dialog = new LinkExcelDialog(
                @"C:\Projects\Sample\Schedule Data.xlsx",
                new List<string> { "Data" },
                "Data",
                string.Empty);
            if (dialog.SelectedSheetName != "Data"
                || dialog.RangeAddressOrNull != null)
            {
                throw new InvalidOperationException("Link Excel did not restore the successful defaults.");
            }

            if (Math.Abs(fontScale - 1.0f) > 0.01f)
            {
                dialog.Font = new Font(dialog.Font.FontFamily, dialog.Font.Size * fontScale);
            }
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(0, 0);
            dialog.Show();
            Application.DoEvents();
            dialog.Refresh();
            Application.DoEvents();

            using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                bmp.Save(Path.Combine(_outDir, baseName + ".png"));
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("form: " + dialog.Width + "x" + dialog.Height
                + "  client=" + dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            DumpControls(dialog, dialog, sb, 0);
            File.WriteAllText(Path.Combine(_outDir, baseName + ".txt"), sb.ToString());
            if (sb.ToString().Contains("OVERLAP"))
            {
                throw new InvalidOperationException(
                    "Link Excel controls overlap in " + baseName + ".");
            }

            dialog.Close();
            dialog.Dispose();
        }

        /// <summary>
        /// The production LinkManagerDialog needs a live Revit Document to load
        /// linked schedules. This preview keeps the screenshot harness runnable
        /// outside Revit while exercising the same public-facing sample rows.
        /// It is deliberately harness-only and is never compiled into the add-in.
        /// </summary>
        private static void RenderLinkManagerPreview(string baseName)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = UserMessages.ManagerTitle;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(0, 0);
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(1200, 640);

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Padding = new Padding(12)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                ListView list = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    HideSelection = false,
                    GridLines = true,
                    MultiSelect = false
                };
                list.Columns.Add(UserMessages.ManagerColSchedule, 170);
                list.Columns.Add(UserMessages.ManagerColWorksheet, 95);
                list.Columns.Add(UserMessages.ManagerColRange, 85);
                list.Columns.Add(UserMessages.ManagerColSource, 400);
                list.Columns.Add(UserMessages.ManagerColStatus, 150);
                list.Columns.Add(UserMessages.ManagerIntegrityColumn, 125);

                ListViewItem present = new ListViewItem(new[]
                {
                    "Sample Workbook - Data",
                    "Data",
                    "(used range)",
                    @"C:\Projects\Sample\Schedule Data.xlsx",
                    UserMessages.ManagerStatusProtected,
                    StyleIntegrityDisplay.For(StyleIntegrityState.Healthy)
                });
                ListViewItem missing = new ListViewItem(new[]
                {
                    "Schedule - Data",
                    "Data",
                    "B2:C3",
                    @"C:\Proj\Missing Book.xlsx",
                    UserMessages.ManagerStatusMissing,
                    StyleIntegrityDisplay.For(StyleIntegrityState.Indeterminate)
                });
                missing.ForeColor = Color.Firebrick;
                list.Items.Add(present);
                list.Items.Add(missing);
                layout.Controls.Add(list, 0, 0);

                FlowLayoutPanel buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    Height = 40,
                    Margin = new Padding(0, 8, 0, 0)
                };
                buttons.Controls.Add(new Button
                {
                    Text = UserMessages.ManagerClose,
                    DialogResult = DialogResult.Cancel,
                    AutoSize = true,
                    MinimumSize = new Size(90, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                buttons.Controls.Add(new Button
                {
                    Text = UserMessages.ManagerReloadAll,
                    AutoSize = true,
                    MinimumSize = new Size(110, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                buttons.Controls.Add(new Button
                {
                    Text = UserMessages.ManagerReloadSelected,
                    AutoSize = true,
                    MinimumSize = new Size(120, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                buttons.Controls.Add(new Button
                {
                    Text = UserMessages.ManagerCheckAndRepair,
                    AutoSize = true,
                    MinimumSize = new Size(150, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);

                dialog.Show();
                Application.DoEvents();
                present.Selected = true;
                list.Focus();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                    bmp.Save(Path.Combine(_outDir, baseName + ".png"));
                }
                dialog.Close();
            }
        }

        private static void CheckPolicyGroups(LayoutDialog dialog)
        {
            RadioButton columnKeep;
            RadioButton columnRecalculate;
            RadioButton rowDefault;
            RadioButton rowKeep;
            FindPolicyButtons(dialog, out columnKeep, out columnRecalculate,
                out rowDefault, out rowKeep);
            CheckPolicyGroupStructure(
                columnKeep, columnRecalculate, rowDefault, rowKeep);
            if (!columnRecalculate.Checked || !rowDefault.Checked)
            {
                throw new InvalidOperationException(
                    "Layout policy radio groups are not independent or did not load their defaults.");
            }
        }

        private static void CheckPolicySelection(
            LayoutDialog dialog, bool expectedColumnKeep, bool expectedRowKeep)
        {
            RadioButton columnKeep;
            RadioButton columnRecalculate;
            RadioButton rowDefault;
            RadioButton rowKeep;
            FindPolicyButtons(dialog, out columnKeep, out columnRecalculate,
                out rowDefault, out rowKeep);
            CheckPolicyGroupStructure(
                columnKeep, columnRecalculate, rowDefault, rowKeep);
            if (columnKeep.Checked != expectedColumnKeep
                || columnRecalculate.Checked != !expectedColumnKeep
                || rowDefault.Checked != !expectedRowKeep
                || rowKeep.Checked != expectedRowKeep)
            {
                throw new InvalidOperationException(
                    "Layout policy radio groups did not select the expected values.");
            }
        }

        private static void FindPolicyButtons(
            LayoutDialog dialog,
            out RadioButton columnKeep, out RadioButton columnRecalculate,
            out RadioButton rowDefault, out RadioButton rowKeep)
        {
            columnKeep = FindRadioButton(dialog, UserMessages.LayoutPolicyKeep);
            columnRecalculate = FindRadioButton(dialog, UserMessages.LayoutPolicyRecalculate);
            rowDefault = FindRadioButton(dialog, UserMessages.RowModeFitToText);
            rowKeep = FindRadioButton(dialog, UserMessages.RowModeKeepManual);
        }

        private static void CheckPolicyGroupStructure(
            RadioButton columnKeep, RadioButton columnRecalculate,
            RadioButton rowDefault, RadioButton rowKeep)
        {
            if (columnKeep == null || columnRecalculate == null
                || rowDefault == null || rowKeep == null)
            {
                throw new InvalidOperationException("Layout dialog did not render both policy groups.");
            }
            if (!ReferenceEquals(columnKeep.Parent, columnRecalculate.Parent)
                || !ReferenceEquals(rowDefault.Parent, rowKeep.Parent)
                || ReferenceEquals(columnKeep.Parent, rowDefault.Parent))
            {
                throw new InvalidOperationException(
                    "Layout policy radio groups are not independent.");
            }
        }

        private static RadioButton FindRadioButton(Control parent, string text)
        {
            foreach (Control child in parent.Controls)
            {
                RadioButton radio = child as RadioButton;
                if (radio != null && radio.Text == text)
                {
                    return radio;
                }
                RadioButton nested = FindRadioButton(child, text);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static void CheckLinkExcelFallback()
        {
            using (LinkExcelDialog dialog = new LinkExcelDialog(
                @"C:\Projects\Another.xlsx",
                new List<string> { "First", "Second" },
                "Worksheet no longer present",
                string.Empty))
            {
                if (dialog.SelectedSheetName != "First")
                {
                    throw new InvalidOperationException(
                        "Link Excel must select the first worksheet when the remembered name is absent.");
                }
            }
        }

        /// <summary>Reports clipping and visible sibling overlap in layout panels.</summary>
        private static void DumpControls(Control parent, Form form, StringBuilder sb, int depth)
        {
            foreach (Control c in parent.Controls)
            {
                Rectangle b = c.Bounds;
                Size client = parent.ClientSize;
                bool horizontalClipped = b.Right > client.Width + 1 || b.X < -1;
                bool verticalClipped = b.Bottom > client.Height + 1 || b.Y < -1;
                bool internalScrollBar = c is HScrollBar || c is VScrollBar;
                string overlap = FindSiblingOverlap(parent, c);
                sb.AppendLine(new string(' ', depth * 2)
                    + c.GetType().Name + " '" + Shorten(c.Text) + "' "
                    + b.X.ToString(CultureInfo.InvariantCulture) + ","
                    + b.Y.ToString(CultureInfo.InvariantCulture) + " "
                    + b.Width + "x" + b.Height
                    + " (parent client " + client.Width + "x" + client.Height + ")"
                    + (horizontalClipped && !internalScrollBar
                        ? "   <<< HORIZONTAL CLIPPED" : "")
                    + (verticalClipped && !internalScrollBar
                        ? "   <<< VERTICAL CLIPPED" : "")
                    + overlap
                    + (c.Visible ? "" : "   [hidden]"));
                DumpControls(c, form, sb, depth + 1);
            }
        }

        private static string FindSiblingOverlap(Control parent, Control control)
        {
            if (!control.Visible
                || (!(parent is TableLayoutPanel) && !(parent is FlowLayoutPanel)))
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder();
            foreach (Control other in parent.Controls)
            {
                if (other == control || !other.Visible)
                {
                    continue;
                }

                Rectangle intersection = Rectangle.Intersect(
                    control.Bounds, other.Bounds);
                if (intersection.Width > 1 && intersection.Height > 1)
                {
                    string otherText = Shorten(other.Text);
                    if (string.IsNullOrEmpty(otherText))
                    {
                        otherText = other.GetType().Name;
                    }
                    result.Append("   <<< OVERLAP with '")
                        .Append(otherText)
                        .Append("'");
                }
            }
            return result.ToString();
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", " ").Replace("\r", " ");
            return text.Length > 34 ? text.Substring(0, 34) + "..." : text;
        }
    }
}
