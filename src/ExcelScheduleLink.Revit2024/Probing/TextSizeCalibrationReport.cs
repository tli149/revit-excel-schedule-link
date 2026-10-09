using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Probing
{
    internal sealed class TextSizeCalibrationResult
    {
        public string Path = string.Empty;
        public string FinalRollbackStatus = string.Empty;
        public bool RollbackVerified;
    }

    /// <summary>
    /// Probe-only calibration of Revit's TableCellStyle.TextSize representation.
    /// Every API mutation is contained in one transaction that is never committed.
    /// </summary>
    internal static class TextSizeCalibrationReport
    {
        private sealed class SampleSize
        {
            public string Label;
            public double Inches;
        }

        private struct SampleCell
        {
            public int Row;
            public int Column;

            public SampleCell(int row, int column)
            {
                Row = row;
                Column = column;
            }
        }

        private static readonly SampleCell[] Cells =
        {
            new SampleCell(0, 0),
            new SampleCell(0, 1),
            new SampleCell(1, 0)
        };

        public static ViewSchedule PickTarget(UIDocument uidoc)
        {
            if (uidoc == null || uidoc.Document == null)
            {
                return null;
            }

            Document doc = uidoc.Document;
            List<ViewSchedule> selected = new List<ViewSchedule>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                ViewSchedule schedule = doc.GetElement(id) as ViewSchedule;
                if (schedule != null && !schedule.IsTemplate
                    && LinkedScheduleFinder.IsLinkedAny(schedule))
                {
                    selected.Add(schedule);
                }
            }
            if (selected.Count == 1)
            {
                return selected[0];
            }

            IList<ViewSchedule> linked = LinkedScheduleFinder.FindAllAny(doc);
            return linked != null && linked.Count > 0 ? linked[0] : null;
        }

        public static TextSizeCalibrationResult Run(
            UIApplication uiapp, Document doc, ViewSchedule schedule)
        {
            if (doc == null)
            {
                throw new ArgumentNullException("doc");
            }
            if (schedule == null || !schedule.IsValidObject)
            {
                throw new InvalidOperationException("The linked schedule was null or invalid.");
            }

            DateTime nowUtc = DateTime.UtcNow;
            string evidenceSource;
            string directory = WorksharingStatusReport.ResolveEvidenceDirectory(
                out evidenceSource);
            Directory.CreateDirectory(directory);
            string user = SafeUsername(uiapp);
            string path = Path.Combine(directory,
                "ESL_TextSizeCalibration_"
                + nowUtc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                + "_" + Sanitize(user) + ".txt");

            TextSizeCalibrationResult result = new TextSizeCalibrationResult
            {
                Path = path
            };
            using (StreamWriter writer = new StreamWriter(
                path, false, new UTF8Encoding(false)))
            {
                writer.AutoFlush = true;
                WriteLine(writer, "ESL Text Size Calibration");
                WriteLine(writer, "utc=" + nowUtc.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                WriteLine(writer, "user=" + user);
                WriteLine(writer, "evidence_directory_source=" + evidenceSource);
                WriteLine(writer, "schedule=" + SafeScheduleName(schedule));
                WriteLine(writer, "schedule_id=" + SafeElementId(schedule.Id));

                Transaction transaction = null;
                try
                {
                    transaction = new Transaction(
                        doc, "ESL Probe - Text Size Calibration (rolled back)");
                    TransactionStatus startStatus;
                    try
                    {
                        startStatus = transaction.Start();
                        WriteLine(writer, "transaction_start=" + startStatus);
                    }
                    catch (Exception ex)
                    {
                        WriteLine(writer, "transaction_start_exception=" + FormatException(ex));
                        startStatus = TransactionStatus.Uninitialized;
                    }

                    if (startStatus == TransactionStatus.Started)
                    {
                        foreach (SampleSize sample in Sizes())
                        {
                            CaptureSize(writer, doc, schedule, sample, nowUtc);
                        }
                    }
                }
                finally
                {
                    TransactionStatus? finalStatus = null;
                    if (transaction != null)
                    {
                        finalStatus = TransactionRollback.RollBackIfStarted(transaction);
                    }
                    result.FinalRollbackStatus = finalStatus.HasValue
                        ? finalStatus.Value.ToString() : "NotStartedOrUnreadable";
                    result.RollbackVerified = finalStatus == TransactionStatus.RolledBack;
                    WriteLine(writer, "final_rollback_status=" + result.FinalRollbackStatus);
                    WriteLine(writer, "rollback_verified=" + result.RollbackVerified);
                    if (transaction != null)
                    {
                        transaction.Dispose();
                    }
                }
            }
            return result;
        }

        private static void CaptureSize(
            StreamWriter writer, Document doc, ViewSchedule schedule,
            SampleSize sample, DateTime runUtc)
        {
            double expectedTableUnits = sample.Inches * 96.0;
            WriteLine(writer, "size=" + sample.Label
                + " inches=" + FormatDouble(sample.Inches)
                + " expected_table_units=" + FormatDouble(expectedTableUnits));
            try
            {
                ElementId currentId = schedule.HeaderTextTypeId;
                TextNoteType current = doc.GetElement(currentId) as TextNoteType;
                if (current == null)
                {
                    throw new InvalidOperationException(
                        "HeaderTextTypeId did not resolve to TextNoteType.");
                }

                string duplicateName = "ESL Text Size Calibration "
                    + runUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                    + " " + Guid.NewGuid().ToString("N");
                TextNoteType duplicate = current.Duplicate(duplicateName) as TextNoteType;
                if (duplicate == null)
                {
                    throw new InvalidOperationException(
                        "TextNoteType.Duplicate did not return a TextNoteType.");
                }
                ElementId duplicateId = duplicate.Id;

                Parameter sizeParameter = duplicate.get_Parameter(
                    BuiltInParameter.TEXT_SIZE);
                if (sizeParameter == null)
                {
                    throw new InvalidOperationException("TEXT_SIZE was unavailable.");
                }
                if (sizeParameter.StorageType != StorageType.Double)
                {
                    throw new InvalidOperationException(
                        "TEXT_SIZE storage type was " + sizeParameter.StorageType + ".");
                }
                sizeParameter.Set(UnitUtils.ConvertToInternalUnits(
                    sample.Inches, UnitTypeId.Inches));

                if (!schedule.IsValidTextTypeId(duplicateId))
                {
                    throw new InvalidOperationException(
                        "The duplicated text type was not valid for the schedule.");
                }
                schedule.TitleTextTypeId = duplicateId;
                schedule.HeaderTextTypeId = duplicateId;
                doc.Regenerate();

                TableSectionData header = schedule.GetTableData()
                    .GetSectionData(SectionType.Header);
                foreach (SampleCell cell in Cells)
                {
                    RecordCell(writer, header, cell, expectedTableUnits);
                }
            }
            catch (Exception ex)
            {
                WriteLine(writer, "size_exception=" + FormatException(ex));
            }
        }

        private static void RecordCell(
            StreamWriter writer, TableSectionData header, SampleCell cell,
            double expectedTableUnits)
        {
            string prefix = "cell=(" + cell.Row + "," + cell.Column + ")";
            try
            {
                if (cell.Row < 0 || cell.Column < 0
                    || cell.Row >= header.NumberOfRows
                    || cell.Column >= header.NumberOfColumns)
                {
                    throw new InvalidOperationException("The requested cell was outside the header.");
                }
                TableCellStyle style = header.GetTableCellStyle(
                    header.FirstRowNumber + cell.Row,
                    header.FirstColumnNumber + cell.Column);
                if (style == null)
                {
                    throw new InvalidOperationException("GetTableCellStyle returned null.");
                }
                WriteLine(writer, prefix
                    + " FontName=" + FormatText(style.FontName)
                    + " TextSize=" + FormatDouble(style.TextSize)
                    + " expected_inches_x_96=" + FormatDouble(expectedTableUnits));
            }
            catch (Exception ex)
            {
                WriteLine(writer, prefix + " exception=" + FormatException(ex)
                    + " expected_inches_x_96=" + FormatDouble(expectedTableUnits));
            }
        }

        private static IEnumerable<SampleSize> Sizes()
        {
            yield return new SampleSize { Label = "2.5 mm", Inches = 2.5 / 25.4 };
            yield return new SampleSize { Label = "3 mm", Inches = 3.0 / 25.4 };
            yield return new SampleSize { Label = "3/32\"", Inches = 3.0 / 32.0 };
            yield return new SampleSize { Label = "1/8\"", Inches = 1.0 / 8.0 };
            yield return new SampleSize { Label = "3.5 mm", Inches = 3.5 / 25.4 };
            yield return new SampleSize { Label = "5 mm", Inches = 5.0 / 25.4 };
        }

        private static void WriteLine(StreamWriter writer, string line)
        {
            writer.WriteLine(line ?? string.Empty);
            writer.Flush();
        }

        private static string SafeUsername(UIApplication uiapp)
        {
            try
            {
                return string.IsNullOrEmpty(uiapp.Application.Username)
                    ? "unknown" : uiapp.Application.Username;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string SafeScheduleName(ViewSchedule schedule)
        {
            try { return schedule.Name ?? "(unnamed)"; }
            catch (Exception ex) { return "(unavailable: " + ex.GetType().Name + ")"; }
        }

        private static string SafeElementId(ElementId id)
        {
            try { return id.Value.ToString(CultureInfo.InvariantCulture); }
            catch (Exception) { return "unavailable"; }
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatText(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string FormatException(Exception ex)
        {
            return ex == null ? "unknown failure" : ex.GetType().Name + ": " + ex.Message;
        }

        private static string Sanitize(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in value ?? string.Empty)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            string result = sb.ToString().Trim();
            return result.Length == 0 ? "unknown" : result;
        }
    }
}
