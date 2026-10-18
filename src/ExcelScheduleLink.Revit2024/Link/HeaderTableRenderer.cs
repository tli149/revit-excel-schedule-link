using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Raised when the in-transaction render guard cannot prove that every
    /// rendered header cell has a safe style. Callers catch this only at their
    /// existing transaction boundary.
    /// </summary>
    internal sealed class UnsafeScheduleStyleException : Exception
    {
        public UnsafeScheduleStyleException(StyleIntegrityResult result)
            : base("The rendered schedule failed style-integrity validation.")
        {
            Result = result;
        }

        public StyleIntegrityResult Result { get; private set; }
    }

    /// <summary>
    /// Renders a SheetModel into the header section of a linked schedule.
    /// All row/column/cell operations are relative to FirstRowNumber /
    /// FirstColumnNumber (never hard-coded 0 or 1).
    /// Renders display text, basic column widths (Excel units → feet via
    /// ColumnWidthConverter × WidthScale) and merged cells.
    ///
    /// On reload the layout modes are honored:
    ///   - ColumnWidthMode.KeepManualWidths preserves widths the user set in
    ///     Revit (captured before the section is cleared, restored by index).
    ///   - RowHeightMode.KeepManualHeights preserves manually-set row heights.
    ///   - WidthScale always multiplies the Excel/content-derived widths.
    ///
    /// After the header widths are applied, the single method-B body column is
    /// widened to the rendered table total (see SetBodyColumnWidth) so the
    /// schedule VIEW can show the table at its real proportions.
    ///
    /// Any exception must make the caller roll back the whole transaction.
    /// </summary>
    internal static class HeaderTableRenderer
    {
        private const int MaxLoop = 100000; // safety guard against API anomalies

        internal sealed class RenderResult
        {
            public int RowsRendered;
            public int ColumnsRendered;
            public int? PreviousColumns;
            public bool Truncated;

            /// <summary>Header columns whose width Revit refused (0 = all applied).</summary>
            public int ColumnWidthsRejected;

            /// <summary>Keep row heights Revit refused or did not retain (0 = all retained).</summary>
            public int RowHeightsRejected;

            /// <summary>Distinct header rows whose fitted text may remain hidden.</summary>
            public int? RowsMayHideText = 0;

            /// <summary>False when the body column could not be matched to the table width.</summary>
            public bool BodyWidthApplied;

            /// <summary>True when the schedule view may not show the full table width.</summary>
            public bool HasViewWidthProblem
            {
                get { return ColumnWidthsRejected > 0 || !BodyWidthApplied; }
            }

            public StyleIntegrityResult IntegrityResult;
        }

        /// <summary>Initial link rendering (defaults: apply Excel widths/heights).</summary>
        public static RenderResult Render(ViewSchedule schedule, SheetModel sheet, double widthScale)
        {
            return Render(schedule, sheet, widthScale,
                ColumnWidthMode.ApplyExcelWidths, RowHeightMode.UseDefaultHeights);
        }

        /// <summary>
        /// Renders honoring the column-width and row-height modes. For the
        /// "keep manual" modes, the current widths/heights are captured before
        /// the section is cleared and restored (by index) afterwards.
        /// </summary>
        public static RenderResult Render(
            ViewSchedule schedule, SheetModel sheet, double widthScale,
            ColumnWidthMode columnWidthMode, RowHeightMode rowHeightMode)
        {
            TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
            int previousColumns = tsd.NumberOfColumns;

            // Capture existing layout before clearing (only when preserving it).
            double[] keptWidths = columnWidthMode == ColumnWidthMode.KeepManualWidths
                ? CaptureColumnWidths(tsd)
                : null;
            double[] keptHeights = rowHeightMode == RowHeightMode.KeepManualHeights
                ? CaptureRowHeights(tsd)
                : null;

            ClearTo1x1(tsd);

            StyleIntegrityLimits.RenderedSize renderedSize =
                StyleIntegrityLimits.GetEffectiveRenderedSize(
                    sheet.SourceRowCount, sheet.SourceColumnCount);
            int rows = renderedSize.Rows;
            int columns = renderedSize.Columns;

            // Grow the section (the official sample pattern inserts at First + 1)
            int guard = 0;
            while (tsd.NumberOfRows < rows && guard++ < MaxLoop)
            {
                tsd.InsertRow(tsd.FirstRowNumber + 1);
            }
            guard = 0;
            while (tsd.NumberOfColumns < columns && guard++ < MaxLoop)
            {
                tsd.InsertColumn(tsd.FirstColumnNumber + 1);
            }

            int r0 = tsd.FirstRowNumber;
            int c0 = tsd.FirstColumnNumber;

            // Make every cell an ordinary text cell before assigning its text.
            // In particular, an empty top-left source cell must not retain the
            // schedule title Parameter cell type.
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    string text = sheet.GetText(r, c) ?? string.Empty;
                    tsd.SetCellType(r0 + r, c0 + c, CellType.Text);
                    tsd.SetCellText(r0 + r, c0 + c, text);
                }
            }

            int widthsRejected =
                ApplyColumnWidths(tsd, sheet, widthScale, columns, c0, columnWidthMode, keptWidths);
            bool[] restoredRows;
            int rowHeightsRejected =
                ApplyRowHeights(tsd, rows, r0, rowHeightMode, keptHeights, out restoredRows);

            // Merged cells (TableMergedCell takes top, left, bottom, right).
            // Ranges that cross the truncation boundary are skipped.
            foreach (MergedRange merge in sheet.MergedRanges)
            {
                if (merge.LastRow >= rows || merge.LastColumn >= columns)
                {
                    continue;
                }
                TableMergedCell cell = new TableMergedCell(
                    r0 + merge.FirstRow, c0 + merge.FirstColumn,
                    r0 + merge.LastRow, c0 + merge.LastColumn);
                tsd.MergeCells(cell);
            }

            // Widths are read back rather than re-derived: Revit may have refused
            // a value, and the body column must match what the table became.
            double headerTotal;
            bool bodyApplied = TryGetHeaderWidthTotal(schedule, out headerTotal)
                && SetBodyColumnWidth(schedule, headerTotal);

            StyleIntegrityResult integrity =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    schedule.Document, schedule, true);
            // The injector is compiled into Release because probe commands share
            // this DLL. It remains inert unless the probe-manifest command arms
            // its one-shot static flag; production code and the production
            // ribbon never arm or expose it.
            if (DebugStyleIntegrityFailureInjector.ConsumeNextValidationFailure())
            {
                integrity = new StyleIntegrityResult
                {
                    State = StyleIntegrityState.Affected,
                    ReasonCode = StyleIntegrityReasonCode.StylelessCellDetected
                };
            }
            if (integrity == null || integrity.State != StyleIntegrityState.Healthy)
            {
                throw new UnsafeScheduleStyleException(integrity);
            }

            int? rowsMayHideText;
            integrity = HeaderRowFitter.FitAndValidateInOpenTransaction(
                schedule, rowHeightMode, integrity, true, out rowsMayHideText);

            rowHeightsRejected +=
                CountChangedRestoredRows(schedule, keptHeights, restoredRows);

            RenderResult result = new RenderResult();
            result.RowsRendered = rows;
            result.ColumnsRendered = columns;
            result.PreviousColumns = previousColumns > 0 ? (int?)previousColumns : null;
            result.Truncated = sheet.SourceRowCount > rows
                || sheet.SourceColumnCount > columns;
            result.ColumnWidthsRejected = widthsRejected;
            result.RowHeightsRejected = rowHeightsRejected;
            result.RowsMayHideText = rowsMayHideText;
            result.BodyWidthApplied = bodyApplied;
            result.IntegrityResult = integrity;
            return result;
        }

        /// <summary>Applies every column width; returns how many Revit refused.</summary>
        private static int ApplyColumnWidths(
            TableSectionData tsd, SheetModel sheet, double widthScale, int columns, int c0,
            ColumnWidthMode mode, double[] keptWidths)
        {
            double[] excelFeet = ColumnWidthConverter.ComputeColumnFeet(sheet, widthScale, columns);
            int rejected = 0;
            for (int c = 0; c < columns; c++)
            {
                // KeepManualWidths: restore the previously captured width for
                // columns that existed before; fall back to the Excel-derived
                // width for newly added columns.
                double feet = (mode == ColumnWidthMode.KeepManualWidths
                               && keptWidths != null && c < keptWidths.Length && keptWidths[c] > 0)
                    ? keptWidths[c]
                    : excelFeet[c];
                if (!TrySetColumnWidth(tsd, c0 + c, feet))
                {
                    rejected++;
                }
            }
            return rejected;
        }

        private static int ApplyRowHeights(
            TableSectionData tsd, int rows, int r0, RowHeightMode mode,
            double[] keptHeights, out bool[] restoredRows)
        {
            restoredRows = new bool[rows];
            // Non-Keep rows are fitted after widths, merges, body sync and the
            // initial Healthy scan. KeepManualHeights still restores the heights
            // the user set (captured before the section was cleared).
            if (mode != RowHeightMode.KeepManualHeights || keptHeights == null)
            {
                return 0;
            }
            int rejected = 0;
            for (int r = 0; r < rows && r < keptHeights.Length; r++)
            {
                double feet = keptHeights[r];
                if (feet > 0.0)
                {
                    if (TrySetRowHeight(tsd, r0 + r, feet))
                    {
                        restoredRows[r] = true;
                    }
                    else
                    {
                        rejected++;
                    }
                }
            }
            return rejected;
        }

        private static double[] CaptureColumnWidths(TableSectionData tsd)
        {
            int n = tsd.NumberOfColumns;
            int c0 = tsd.FirstColumnNumber;
            double[] widths = new double[n];
            for (int c = 0; c < n; c++)
            {
                try { widths[c] = tsd.GetColumnWidth(c0 + c); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { widths[c] = 0; }
            }
            return widths;
        }

        private static double[] CaptureRowHeights(TableSectionData tsd)
        {
            int n = tsd.NumberOfRows;
            int r0 = tsd.FirstRowNumber;
            double[] heights = new double[n];
            for (int r = 0; r < n; r++)
            {
                try { heights[r] = tsd.GetRowHeight(r0 + r); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { heights[r] = 0; }
            }
            return heights;
        }

        /// <summary>
        /// Attempts the supplied native feet value. A rejected value leaves the
        /// column as it is and returns false, so the caller can tell the user
        /// instead of leaving a squeezed table that looks like a failed load.
        /// </summary>
        private static bool TrySetColumnWidth(TableSectionData tsd, int column, double feet)
        {
            try
            {
                tsd.SetColumnWidth(column, feet);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Matches the single method-B body column to the rendered table width.
        ///
        /// Revit 2024 can stretch header columns when GridColumnWidth is set.
        /// The body therefore targets the exact actual header total; positive
        /// padding would enlarge the header on every write. If the body is
        /// narrower than the header, the editor can still squeeze a column, but
        /// matching the actual total removes that known deficit.
        ///
        /// An already-equal target is left alone, and only GridColumnWidth (the
        /// schedule-view width) is written.
        /// SheetColumnWidth is deliberately left alone: sheet output is already
        /// correct and must not be disturbed.
        ///
        /// Returns false when the method-B field is absent or Revit rejects the
        /// requested width. That is cosmetic, so link/reload can show their
        /// existing view-width warning. Unexpected exceptions propagate: layout
        /// apply must roll back rather than hide an unrecognized failure.
        /// </summary>
        internal static bool SetBodyColumnWidth(ViewSchedule schedule, double tableFeet)
        {
            if (double.IsNaN(tableFeet) || double.IsInfinity(tableFeet) || tableFeet <= 0.0)
            {
                return false;
            }
            double target = tableFeet
                + LayoutMetrics.BodyWidthMarginInches / LayoutMetrics.InchesPerFoot;
            try
            {
                ScheduleDefinition definition = schedule.Definition;
                if (definition.GetFieldCount() != 1)
                {
                    return false;
                }
                ScheduleField field = definition.GetField(0);
                if (field == null)
                {
                    return false;
                }
                if (Math.Abs(field.GridColumnWidth - target) <= LayoutOps.Tolerance)
                {
                    return true;
                }
                field.GridColumnWidth = target;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                when (!ModelMutation.IsFatalRegeneration(ex))
            {
                // Revit refused the total (its own column-width limit). The link
                // itself is valid; only the schedule view stays narrow.
                return false;
            }
        }

        /// <summary>Reads back the actual header widths after a mutation.</summary>
        internal static bool TryGetHeaderWidthTotal(ViewSchedule schedule, out double total)
        {
            total = 0.0;
            try
            {
                TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
                int columns = tsd.NumberOfColumns;
                if (columns <= 0)
                {
                    return false;
                }
                int firstColumn = tsd.FirstColumnNumber;
                for (int column = 0; column < columns; column++)
                {
                    double width = tsd.GetColumnWidth(firstColumn + column);
                    if (width <= 0.0 || double.IsNaN(width) || double.IsInfinity(width))
                    {
                        total = 0.0;
                        return false;
                    }
                    total += width;
                    if (double.IsNaN(total) || double.IsInfinity(total))
                    {
                        total = 0.0;
                        return false;
                    }
                }
                if (total <= 0.0 || double.IsNaN(total) || double.IsInfinity(total))
                {
                    total = 0.0;
                    return false;
                }
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                total = 0.0;
                return false;
            }
        }

        private static bool TrySetRowHeight(TableSectionData tsd, int row, double feet)
        {
            try
            {
                tsd.SetRowHeight(row, feet);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return false;
            }
        }

        private static int CountChangedRestoredRows(
            ViewSchedule schedule, double[] keptHeights, bool[] restoredRows)
        {
            if (keptHeights == null || restoredRows == null)
            {
                return 0;
            }

            TableSectionData tsd =
                schedule.GetTableData().GetSectionData(SectionType.Header);
            int rows = tsd.NumberOfRows;
            int r0 = tsd.FirstRowNumber;
            int rejected = 0;
            int limit = Math.Min(restoredRows.Length, keptHeights.Length);
            for (int offset = 0; offset < limit && offset < rows; offset++)
            {
                if (!restoredRows[offset])
                {
                    continue;
                }

                double actual;
                try
                {
                    actual = tsd.GetRowHeight(r0 + offset);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    rejected++;
                    continue;
                }

                if (actual <= 0.0
                    || double.IsNaN(actual)
                    || double.IsInfinity(actual)
                    || Math.Abs(actual - keptHeights[offset]) > LayoutOps.Tolerance)
                {
                    rejected++;
                }
            }
            return rejected;
        }

        /// <summary>Shrinks the header section back to a single empty cell.</summary>
        public static void ClearTo1x1(TableSectionData tsd)
        {
            int guard = 0;
            while (tsd.NumberOfRows > 1 && guard++ < MaxLoop)
            {
                tsd.RemoveRow(tsd.LastRowNumber);
            }
            guard = 0;
            while (tsd.NumberOfColumns > 1 && guard++ < MaxLoop)
            {
                tsd.RemoveColumn(tsd.LastColumnNumber);
            }
            tsd.SetCellText(tsd.FirstRowNumber, tsd.FirstColumnNumber, string.Empty);
        }
    }
}
