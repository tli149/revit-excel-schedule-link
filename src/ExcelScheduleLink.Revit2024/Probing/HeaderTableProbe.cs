using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Builds a test table in the header section for diagnostic variants A and B.
    /// All row, column, and cell operations use FirstRowNumber and
    /// FirstColumnNumber rather than assuming fixed zero or one origins.
    /// </summary>
    internal static class HeaderTableProbe
    {
        public const string ScheduleNameA = "ESL_Step0_A_NoField";
        public const string ScheduleNameB = "ESL_Step0_B_DummyFilter";

        private const int TargetRows = 3;
        private const int TargetColumns = 3;
        private const int MaxLoop = 1000; // Guard against an API loop on failure.

        public static ViewSchedule FindByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == name);
        }

        /// <summary>Set ShowTitle=true and ShowHeaders=false for the header table.</summary>
        public static void ConfigureDefinition(ViewSchedule schedule, Step0Reporter r)
        {
            try
            {
                ScheduleDefinition def = schedule.Definition;
                def.ShowTitle = true;    // Required to display the title/header area.
                def.ShowHeaders = false; // Hide the body column header row.
                r.AddOk("ScheduleDefinition: ShowTitle=true / ShowHeaders=false");
            }
            catch (Exception ex)
            {
                r.AddFail("Setting ScheduleDefinition ShowTitle/ShowHeaders", ex);
            }
        }

        /// <summary>Reset the header section to 1x1 so a run can be repeated.</summary>
        public static void ClearHeaderTo1x1(TableSectionData tsd, Step0Reporter r)
        {
            try
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
                r.AddOk("Reduced header to 1x1 (RemoveRow/RemoveColumn); now "
                    + tsd.NumberOfRows + " rows x " + tsd.NumberOfColumns + " columns");
            }
            catch (Exception ex)
            {
                r.AddFail("Reducing the header to 1x1", ex);
            }
        }

        /// <summary>
        /// Build a 3x3 test table in the header and exercise widths, heights,
        /// and a merge. Each operation is reported independently.
        /// </summary>
        public static void BuildTestTable(ViewSchedule schedule, Step0Reporter r, string label)
        {
            TableSectionData tsd;
            try
            {
                tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
                r.AddOk("GetTableData().GetSectionData(SectionType.Header): now "
                    + tsd.NumberOfRows + " rows x " + tsd.NumberOfColumns + " columns"
                    + " (FirstRowNumber=" + tsd.FirstRowNumber
                    + ", FirstColumnNumber=" + tsd.FirstColumnNumber + ")");
            }
            catch (Exception ex)
            {
                r.AddFail("Getting the header section", ex);
                return;
            }

            if (tsd.NumberOfRows > 1 || tsd.NumberOfColumns > 1)
            {
                ClearHeaderTo1x1(tsd, r);
            }

            // Ensure rows by inserting at FirstRowNumber + 1.
            try
            {
                int guard = 0;
                while (tsd.NumberOfRows < TargetRows && guard++ < MaxLoop)
                {
                    tsd.InsertRow(tsd.FirstRowNumber + 1);
                }
                r.AddOk("InsertRow: expanded to " + tsd.NumberOfRows + " rows");
            }
            catch (Exception ex)
            {
                r.AddFail("Expanding rows with InsertRow", ex);
                return;
            }

            // Ensure columns.
            try
            {
                int guard = 0;
                while (tsd.NumberOfColumns < TargetColumns && guard++ < MaxLoop)
                {
                    tsd.InsertColumn(tsd.FirstColumnNumber + 1);
                }
                r.AddOk("InsertColumn: expanded to " + tsd.NumberOfColumns + " columns");
            }
            catch (Exception ex)
            {
                r.AddFail("Expanding columns with InsertColumn", ex);
                return;
            }

            int r0 = tsd.FirstRowNumber;
            int c0 = tsd.FirstColumnNumber;

            // Sample text includes labels and numeric display strings.
            string[,] texts =
            {
                { "ESL Probe", label, DateTime.Now.ToString("yyyy-MM-dd HH:mm") },
                { "Item A", "1,234.57", "12.35%" },
                { "Merged-cell test (3 columns)", "", "" }
            };
            for (int i = 0; i < TargetRows; i++)
            {
                for (int j = 0; j < TargetColumns; j++)
                {
                    string cellLabel = "SetCellText(R" + i + ",C" + j + ")=\"" + texts[i, j] + "\"";
                    try
                    {
                        tsd.SetCellText(r0 + i, c0 + j, texts[i, j]);
                        r.AddOk(cellLabel);
                    }
                    catch (Exception ex)
                    {
                        r.AddFail(cellLabel, ex);
                    }
                }
            }

            // Column widths in feet: 0.6 ft is approximately 183 mm;
            // 0.9 ft is approximately 274 mm; 1.2 ft is approximately 366 mm.
            double[] widthsFeet = { 0.6, 0.9, 1.2 };
            for (int j = 0; j < TargetColumns; j++)
            {
                try
                {
                    tsd.SetColumnWidth(c0 + j, widthsFeet[j]);
                    r.AddOk("SetColumnWidth(C" + j + ", " + widthsFeet[j] + "ft)");
                }
                catch (Exception ex)
                {
                    r.AddFail("SetColumnWidth(C" + j + ")", ex);
                }
            }

            // Row height in feet: 0.03 ft is approximately 9.1 mm.
            try
            {
                tsd.SetRowHeight(r0, 0.03);
                r.AddOk("SetRowHeight(R0, 0.03ft)");
            }
            catch (Exception ex)
            {
                r.AddFail("SetRowHeight(R0)", ex);
            }

            // Merge the bottom three columns with TableMergedCell(top, left,
            // bottom, right).
            try
            {
                TableMergedCell merge = new TableMergedCell(r0 + 2, c0, r0 + 2, c0 + 2);
                tsd.MergeCells(merge);
                r.AddOk("MergeCells(R2: C0..C2, three-column merge)");
            }
            catch (Exception ex)
            {
                r.AddFail("MergeCells(R2: C0..C2)", ex);
            }
        }
    }
}
