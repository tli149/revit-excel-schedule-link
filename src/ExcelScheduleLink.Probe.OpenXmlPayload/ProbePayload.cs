using System;
using System.Linq;
using System.Reflection;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelScheduleLink.Excel.OpenXml.Internal;

namespace ExcelScheduleLink.Probe.OpenXmlPayload
{
    /// <summary>
    /// Diagnostic payload loaded by the Revit add-in through reflection
    /// (Assembly.LoadFrom + GetType().GetMethod("Run").Invoke).
    /// Arguments and return values are strings only to avoid shared-type
    /// identity issues. The first report line is PayloadResult=OK or
    /// PayloadResult=FAIL for machine-readable status.
    /// </summary>
    public static class ProbePayload
    {
        public static string Run(string xlsxPath)
        {
            StringBuilder body = new StringBuilder();
            bool ok = true;
            try
            {
                ReportOpenXmlAssemblies(body);
                ReadWorkbook(xlsxPath, body);
            }
            catch (Exception ex)
            {
                ok = false;
                body.AppendLine("[Payload] Exception:");
                body.AppendLine(ex.ToString());
            }

            StringBuilder result = new StringBuilder();
            result.AppendLine(ok ? "PayloadResult=OK" : "PayloadResult=FAIL");
            result.Append(body);
            return result.ToString();
        }

        private static void ReportOpenXmlAssemblies(StringBuilder sb)
        {
            // Identify the assemblies that are actually bound through typeof.
            // If Revit or another add-in loaded an older Open XML assembly
            // first, this reports a version other than the referenced 3.5.1.
            Assembly typedAsm = typeof(SpreadsheetDocument).Assembly;
            Assembly frameworkAsm = typeof(OpenXmlElement).Assembly;

            sb.AppendLine("[Payload] typeof(SpreadsheetDocument).Assembly:");
            AppendAssemblyInfo(sb, typedAsm);
            sb.AppendLine("[Payload] typeof(OpenXmlElement).Assembly:");
            AppendAssemblyInfo(sb, frameworkAsm);
        }

        private static void AppendAssemblyInfo(StringBuilder sb, Assembly asm)
        {
            sb.AppendLine("  FullName : " + asm.FullName);
            string location;
            try
            {
                location = asm.Location;
            }
            catch (Exception ex)
            {
                location = "(read failed: " + ex.GetType().Name + ")";
            }
            sb.AppendLine("  Location : " + location);
        }

        private static void ReadWorkbook(string xlsxPath, StringBuilder sb)
        {
            sb.AppendLine("[Payload] Workbook: " + xlsxPath);
            WorkbookReadLimits.ValidateZip(xlsxPath);
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(
                xlsxPath, false, WorkbookReadLimits.CreateOpenSettings()))
            {
                WorkbookReadLimits.ValidateDocument(doc, null, null);
                WorkbookPart wbPart = doc.WorkbookPart;
                if (wbPart == null)
                {
                    throw new InvalidOperationException("WorkbookPart is missing.");
                }

                Sheet[] sheets = wbPart.Workbook.Descendants<Sheet>().ToArray();
                sb.AppendLine("[Payload] Sheet count: " + sheets.Length);
                foreach (Sheet sheet in sheets)
                {
                    string name = sheet.Name != null ? sheet.Name.Value : "(unnamed)";
                    string a1 = ReadCellRaw(wbPart, sheet, "A1");
                    sb.AppendLine("  Sheet '" + name + "' A1 raw value: " + a1);
                }
            }
            sb.AppendLine("[Payload] Workbook read succeeded");
        }

        /// <summary>Returns CellValue/InnerText and DataType without display formatting.</summary>
        private static string ReadCellRaw(WorkbookPart wbPart, Sheet sheet, string cellReference)
        {
            if (sheet.Id == null)
            {
                return "(sheet.Id missing)";
            }
            WorksheetPart wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);
            Cell cell = wsPart.Worksheet
                .Descendants<Cell>()
                .FirstOrDefault(c => c.CellReference != null && c.CellReference.Value == cellReference);
            if (cell == null)
            {
                return "(cell missing)";
            }

            // Open XML 3.x EnumValue.ToString does not return the value name,
            // so use the raw attribute string (s/b/str/inlineStr, and so on).
            string dataType = cell.DataType != null ? cell.DataType.InnerText : "(number/date)";
            string raw = cell.CellValue != null ? cell.CellValue.InnerText : cell.InnerText;

            // A shared string is an index, so append the resolved value for reference.
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                SharedStringTablePart sst = wbPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                int index;
                if (sst != null && int.TryParse(raw, out index))
                {
                    raw = raw + " -> \"" + sst.SharedStringTable.ElementAt(index).InnerText + "\"";
                }
            }
            return "DataType=" + dataType + ", Value=" + raw;
        }
    }
}
