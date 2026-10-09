using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Layout;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Measures the text already rendered in a linked schedule's header so
    /// "Fit Content" can size columns without re-reading the workbook (design
    /// v0.8.0 5.1.1): it uses what the user currently sees, works when the source
    /// file is missing, and costs no file I/O.
    ///
    /// Scope: only the rows/columns of the header section are visited - with
    /// method B the whole header section IS the table the add-in rendered
    /// (HeaderTableRenderer clears it to 1x1 and grows it to exactly rows x
    /// columns), so no Revit-owned title row can leak into the measurement.
    /// Merged cells are handled once at their top-left cell; a horizontally
    /// merged cell becomes a span requirement instead of inflating one column.
    /// Cells with line breaks are measured by their longest line.
    /// </summary>
    internal static class HeaderContentMeasurer
    {
        internal sealed class Measurement
        {
            /// <summary>Longest single-cell content per column, in half-width units.</summary>
            public double[] MaxUnitsPerColumn;
            /// <summary>Horizontal merges and the width their content needs (feet, scaled).</summary>
            public List<MergeRequirement> Merges;
        }

        /// <summary>
        /// Measures the header content. <paramref name="widthScale"/> is applied to
        /// the merge requirements so they are comparable with the scaled column
        /// widths produced by ColumnWidthMath.
        /// </summary>
        public static Measurement Measure(ViewSchedule schedule, double widthScale)
        {
            double scale = widthScale <= 0 ? 1.0 : widthScale;
            TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);

            int r0 = tsd.FirstRowNumber;
            int c0 = tsd.FirstColumnNumber;
            int rows = tsd.NumberOfRows;
            int columns = tsd.NumberOfColumns;

            Measurement m = new Measurement
            {
                MaxUnitsPerColumn = new double[columns < 0 ? 0 : columns],
                Merges = new List<MergeRequirement>()
            };
            if (rows <= 0 || columns <= 0)
            {
                return m;
            }

            HashSet<string> handledMerges = new HashSet<string>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int row = r0 + r;
                    int col = c0 + c;

                    TableMergedCell merged = TryGetMergedCell(tsd, row, col);
                    bool spansColumns = merged != null && merged.Right > merged.Left;
                    bool spansRows = merged != null && merged.Bottom > merged.Top;

                    if (spansColumns || spansRows)
                    {
                        // Process a merged range once, at its top-left cell.
                        if (row != merged.Top || col != merged.Left)
                        {
                            continue;
                        }
                        string key = merged.Top + ":" + merged.Left + ":" + merged.Bottom + ":" + merged.Right;
                        if (!handledMerges.Add(key))
                        {
                            continue;
                        }

                        double mergedUnits = CharWidth.MeasureLongestLineUnits(GetText(schedule, tsd, row, col));
                        if (spansColumns)
                        {
                            // Requirement over the span (design 5.1: span total >= required).
                            m.Merges.Add(new MergeRequirement
                            {
                                FirstColumn = merged.Left - c0,
                                LastColumn = merged.Right - c0,
                                RequiredFeet = ColumnWidthMath.ContentFeetForUnits(mergedUnits) * scale
                            });
                        }
                        else
                        {
                            // Vertical-only merge occupies a single column: normal content.
                            Accumulate(m.MaxUnitsPerColumn, merged.Left - c0, mergedUnits);
                        }
                        continue;
                    }

                    double units = CharWidth.MeasureLongestLineUnits(GetText(schedule, tsd, row, col));
                    Accumulate(m.MaxUnitsPerColumn, c, units);
                }
            }

            return m;
        }

        private static void Accumulate(double[] maxUnits, int index, double units)
        {
            if (index < 0 || index >= maxUnits.Length) return;
            if (units > maxUnits[index]) maxUnits[index] = units;
        }

        /// <summary>
        /// Cell text, preferring ViewSchedule.GetCellText (the displayed string)
        /// and falling back to the section data when that is unavailable.
        /// </summary>
        private static string GetText(ViewSchedule schedule, TableSectionData tsd, int row, int column)
        {
            try
            {
                string text = schedule.GetCellText(SectionType.Header, row, column);
                if (text != null) return text;
            }
            catch (Exception)
            {
                // fall through to the section data
            }
            try { return tsd.GetCellText(row, column) ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        private static TableMergedCell TryGetMergedCell(TableSectionData tsd, int row, int column)
        {
            try { return tsd.GetMergedCell(row, column); }
            catch (Exception) { return null; }
        }
    }
}
