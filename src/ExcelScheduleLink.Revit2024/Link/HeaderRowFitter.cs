using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal static class HeaderRowFitter
    {
        internal static bool UsesTextFit(RowHeightMode mode)
        {
            return mode != RowHeightMode.KeepManualHeights;
        }

        internal static StyleIntegrityResult FitAndValidateInOpenTransaction(
            ViewSchedule schedule, RowHeightMode mode,
            StyleIntegrityResult healthyScan, bool requireTextCells,
            out int? rowsMayHideText)
        {
            rowsMayHideText = 0;
            if (healthyScan == null
                || healthyScan.State != StyleIntegrityState.Healthy)
            {
                throw new UnsafeScheduleStyleException(healthyScan);
            }
            if (!UsesTextFit(mode))
            {
                return healthyScan;
            }

            RowFitResult fit;
            double[] currentHeights;
            if (!TryPrepareRowsInOpenTransaction(
                schedule, healthyScan, out fit, out currentHeights))
            {
                rowsMayHideText = null;
                return healthyScan;
            }

            HashSet<int> affectedRows = new HashSet<int>();
            for (int index = 0; index < fit.TooNarrowRows.Count; index++)
            {
                affectedRows.Add(fit.TooNarrowRows[index]);
            }

            TableSectionData section;
            try
            {
                section = schedule.GetTableData()
                    .GetSectionData(SectionType.Header);
                if (section == null || !section.IsValidObject
                    || section.NumberOfRows != currentHeights.Length
                    || section.NumberOfColumns != healthyScan.Facts.NumberOfColumns
                    || section.FirstRowNumber != healthyScan.Facts.FirstRowNumber)
                {
                    throw new InvalidOperationException(
                        "The header dimensions changed during row-fit preparation.");
                }
            }
            catch (Exception exception)
                when (!ModelMutation.IsFatalRegeneration(exception))
            {
                if (!IsExpectedMeasurementReadFailure(exception))
                {
                    throw;
                }
                rowsMayHideText = null;
                return healthyScan;
            }

            int firstRow = healthyScan.Facts.FirstRowNumber;
            for (int offset = 0; offset < fit.RequiredFeet.Count; offset++)
            {
                double requiredFeet = fit.RequiredFeet[offset];
                if (Math.Abs(currentHeights[offset] - requiredFeet)
                    <= LayoutOps.Tolerance)
                {
                    continue;
                }

                try
                {
                    section.SetRowHeight(firstRow + offset, requiredFeet);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    affectedRows.Add(offset);
                }
            }

            StyleIntegrityResult finalIntegrity =
                ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                    schedule.Document, schedule, requireTextCells);
            if (finalIntegrity == null
                || finalIntegrity.State != StyleIntegrityState.Healthy)
            {
                throw new UnsafeScheduleStyleException(finalIntegrity);
            }

            rowsMayHideText = CountRowsThatMayHideText(
                schedule, fit, affectedRows);
            return finalIntegrity;
        }

        private static bool TryPrepareRowsInOpenTransaction(
            ViewSchedule schedule, StyleIntegrityResult healthyScan,
            out RowFitResult fit, out double[] currentHeights)
        {
            fit = null;
            currentHeights = null;
            try
            {
                if (healthyScan == null || healthyScan.Facts == null
                    || healthyScan.Facts.Cells == null)
                {
                    throw new InvalidOperationException(
                        "The healthy scan did not include complete style facts.");
                }

                HeaderTableSnapshot snapshot =
                    ScheduleRepairService.CaptureSnapshotInOpenTransaction(schedule);
                if (snapshot == null)
                {
                    throw new InvalidOperationException(
                        "The header snapshot was unavailable.");
                }

                FitContentMeasurements measurements =
                    HeaderContentMeasurer.PrepareSnapshot(snapshot, 1.0);
                if (snapshot.ColumnWidths == null
                    || snapshot.RowHeights == null
                    || snapshot.ColumnWidths.Count != snapshot.Columns
                    || snapshot.RowHeights.Count != snapshot.Rows)
                {
                    throw new ArgumentException(
                        "The header snapshot dimensions were incomplete.");
                }

                ScheduleStyleFacts facts = healthyScan.Facts;
                if (facts.NumberOfRows != snapshot.Rows
                    || facts.NumberOfColumns != snapshot.Columns
                    || facts.FirstRowNumber != snapshot.FirstRowNumber
                    || facts.FirstColumnNumber != snapshot.FirstColumnNumber
                    || facts.Cells.Count != snapshot.Rows * snapshot.Columns)
                {
                    throw new InvalidOperationException(
                        "The healthy scan dimensions did not match the header snapshot.");
                }

                Dictionary<Tuple<int, int>, CellStyleFacts> factsByCoordinate =
                    new Dictionary<Tuple<int, int>, CellStyleFacts>();
                for (int index = 0; index < facts.Cells.Count; index++)
                {
                    CellStyleFacts cell = facts.Cells[index];
                    if (cell == null)
                    {
                        throw new InvalidOperationException(
                            "The healthy scan contained an unreadable cell.");
                    }
                    Tuple<int, int> key = Tuple.Create(cell.Row, cell.Column);
                    if (factsByCoordinate.ContainsKey(key))
                    {
                        throw new InvalidOperationException(
                            "The healthy scan contained duplicate cell coordinates.");
                    }
                    factsByCoordinate.Add(key, cell);
                }

                List<RowFitTextMetrics> textMetrics =
                    new List<RowFitTextMetrics>(measurements.Cells.Count);
                for (int index = 0; index < measurements.Cells.Count; index++)
                {
                    FitContentMeasuredCell measured = measurements.Cells[index];
                    CellStyleFacts cell;
                    if (!factsByCoordinate.TryGetValue(
                        Tuple.Create(measured.Row, measured.Column), out cell))
                    {
                        throw new InvalidOperationException(
                            "The healthy scan did not cover every fitting anchor.");
                    }
                    textMetrics.Add(new RowFitTextMetrics(
                        ScheduleStyleIntegrityScanner.GetConservativeTextSizeFeet(cell)));
                }

                for (int index = 0; index < snapshot.ColumnWidths.Count; index++)
                {
                    double width = snapshot.ColumnWidths[index];
                    if (width <= 0.0 || double.IsNaN(width)
                        || double.IsInfinity(width))
                    {
                        throw new ArgumentException(
                            "The header contained an unreadable column width.");
                    }
                }

                currentHeights = new double[snapshot.RowHeights.Count];
                for (int index = 0; index < snapshot.RowHeights.Count; index++)
                {
                    double height = snapshot.RowHeights[index];
                    if (height <= 0.0 || double.IsNaN(height)
                        || double.IsInfinity(height))
                    {
                        throw new ArgumentException(
                            "The header contained an unreadable row height.");
                    }
                    currentHeights[index] = height;
                }

                fit = RowHeightMath.FitRows(
                    measurements, snapshot.ColumnWidths, textMetrics);
                return true;
            }
            catch (Exception exception)
                when (!ModelMutation.IsFatalRegeneration(exception))
            {
                if (!IsExpectedMeasurementReadFailure(exception))
                {
                    throw;
                }
                fit = null;
                currentHeights = null;
                return false;
            }
        }

        private static int? CountRowsThatMayHideText(
            ViewSchedule schedule, RowFitResult fit, HashSet<int> affectedRows)
        {
            try
            {
                TableSectionData section = schedule.GetTableData()
                    .GetSectionData(SectionType.Header);
                if (section == null || !section.IsValidObject
                    || section.NumberOfRows != fit.RequiredFeet.Count)
                {
                    return null;
                }

                int firstRow = section.FirstRowNumber;
                for (int offset = 0; offset < fit.RequiredFeet.Count; offset++)
                {
                    if (affectedRows.Contains(offset))
                    {
                        continue;
                    }

                    double actualHeight;
                    try
                    {
                        actualHeight = section.GetRowHeight(firstRow + offset);
                    }
                    catch (Exception exception)
                        when (!ModelMutation.IsFatalRegeneration(exception))
                    {
                        if (!IsExpectedMeasurementReadFailure(exception))
                        {
                            throw;
                        }
                        affectedRows.Add(offset);
                        continue;
                    }

                    if (actualHeight <= 0.0
                        || double.IsNaN(actualHeight)
                        || double.IsInfinity(actualHeight)
                        || actualHeight < fit.RequiredFeet[offset]
                            - LayoutOps.Tolerance)
                    {
                        affectedRows.Add(offset);
                    }
                }
                return affectedRows.Count;
            }
            catch (Exception exception)
                when (!ModelMutation.IsFatalRegeneration(exception))
            {
                if (!IsExpectedMeasurementReadFailure(exception))
                {
                    throw;
                }
                return null;
            }
        }

        private static bool IsExpectedMeasurementReadFailure(Exception exception)
        {
            return exception is Autodesk.Revit.Exceptions.ArgumentException
                || exception is Autodesk.Revit.Exceptions.InvalidOperationException
                || exception is System.ArgumentException
                || exception is System.InvalidOperationException;
        }
    }
}
