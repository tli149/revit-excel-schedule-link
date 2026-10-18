using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Layout;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Reads the text already rendered in a linked schedule's header so Fit
    /// Content can size columns and provide heading hints without re-reading
    /// the workbook. Snapshot capture owns the Revit read and rollback; this
    /// type only converts that one snapshot into the shared pure table.
    /// </summary>
    internal static class HeaderContentMeasurer
    {
        internal sealed class FitContentMeasurementOutcome
        {
            public FitContentMeasurements Measurements;
            public string[] ColumnHeadings;
            public string UserMessage = string.Empty;
            public bool Success { get { return Measurements != null; } }
        }

        public static FitContentMeasurementOutcome ReadMeasurements(
            Document doc, ViewSchedule schedule, double widthScale)
        {
            SnapshotReadOutcome read =
                ScheduleRepairService.CaptureSnapshotWithRolledBackTransaction(
                    doc, schedule);
            if (!read.Success)
            {
                return new FitContentMeasurementOutcome
                {
                    UserMessage = UserMessages.LayoutFitContentReadFailed
                };
            }

            try
            {
                FitContentTable table = CreateTable(read.Snapshot);
                FitContentMeasurements measurements = PrepareTable(table, widthScale);
                return new FitContentMeasurementOutcome
                {
                    Measurements = measurements,
                    ColumnHeadings = FitContentText.GetColumnHeadings(table)
                };
            }
            catch (ArgumentException)
            {
                return new FitContentMeasurementOutcome
                {
                    UserMessage = UserMessages.LayoutFitContentReadFailed
                };
            }
        }

        internal static FitContentMeasurements PrepareSnapshot(
            HeaderTableSnapshot snapshot, double widthScale)
        {
            return PrepareTable(CreateTable(snapshot), widthScale);
        }

        private static FitContentTable CreateTable(HeaderTableSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            // Snapshot structure is checked here; anchor geometry (overlaps,
            // gaps, spans) is left to FitContentText.Prepare, its one owner.
            if (snapshot.Rows < 0 || snapshot.Columns < 0
                || snapshot.Cells == null || snapshot.Merges == null)
            {
                throw new ArgumentException(
                    "The table snapshot is incomplete.", "snapshot");
            }
            long gridSize = (long)snapshot.Rows * snapshot.Columns;
            if (gridSize > int.MaxValue)
            {
                throw new ArgumentException(
                    "The table snapshot is too large.", "snapshot");
            }
            int expectedCellCount = (int)gridSize;
            if (snapshot.Cells.Count != expectedCellCount)
            {
                throw new ArgumentException(
                    "The table snapshot cell count does not match its shape.",
                    "snapshot");
            }
            for (int index = 0; index < snapshot.Cells.Count; index++)
            {
                HeaderTableCellSnapshot cell = snapshot.Cells[index];
                if (cell == null || cell.Text == null)
                {
                    throw new ArgumentException(
                        "The table snapshot has an unreadable cell.",
                        "snapshot");
                }
            }

            List<FitContentCell> anchors = new List<FitContentCell>();
            bool[] covered = new bool[expectedCellCount];
            for (int i = 0; i < snapshot.Merges.Count; i++)
            {
                HeaderTableMergeSnapshot merge = snapshot.Merges[i];
                if (merge == null)
                {
                    throw new ArgumentException(
                        "The table snapshot has an unreadable merge.",
                        "snapshot");
                }

                bool inBounds = merge.Top >= 0 && merge.Left >= 0
                    && merge.Top <= merge.Bottom
                    && merge.Left <= merge.Right
                    && merge.Bottom < snapshot.Rows
                    && merge.Right < snapshot.Columns;
                if (!inBounds)
                {
                    // Prepare rejects the out-of-range anchor.
                    anchors.Add(new FitContentCell(
                        merge.Top, merge.Left,
                        InclusiveSpanOrZero(merge.Top, merge.Bottom),
                        InclusiveSpanOrZero(merge.Left, merge.Right),
                        string.Empty));
                    continue;
                }

                if (merge.Top == merge.Bottom
                    && merge.Left == merge.Right)
                {
                    continue;
                }

                int rowSpan = checked(merge.Bottom - merge.Top + 1);
                int columnSpan = checked(merge.Right - merge.Left + 1);
                int topLeftIndex = checked(
                    merge.Top * snapshot.Columns + merge.Left);
                anchors.Add(new FitContentCell(
                    merge.Top, merge.Left, rowSpan, columnSpan,
                    snapshot.Cells[topLeftIndex].Text));
                for (int row = merge.Top; row <= merge.Bottom; row++)
                {
                    for (int column = merge.Left;
                        column <= merge.Right; column++)
                    {
                        covered[row * snapshot.Columns + column] = true;
                    }
                }
            }

            for (int row = 0; row < snapshot.Rows; row++)
            {
                for (int column = 0; column < snapshot.Columns; column++)
                {
                    int index = row * snapshot.Columns + column;
                    if (!covered[index])
                    {
                        anchors.Add(new FitContentCell(
                            row, column, 1, 1,
                            snapshot.Cells[index].Text));
                    }
                }
            }

            return new FitContentTable(snapshot.Rows, snapshot.Columns, anchors);
        }

        private static FitContentMeasurements PrepareTable(
            FitContentTable table, double widthScale)
        {
            return FitContentText.Prepare(
                table, widthScale,
                LayoutMetrics.FitContentHeadingRowCount,
                LayoutMetrics.MinColumnFeet, LayoutMetrics.MaxColumnFeet);
        }

        private static int InclusiveSpanOrZero(int first, int last)
        {
            if (last < first)
            {
                return 0;
            }
            long span = (long)last - first + 1L;
            return span > int.MaxValue ? 0 : (int)span;
        }

    }
}
