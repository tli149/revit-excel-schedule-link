using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Read-only persistence diagnostic. After saving an RVT and restarting
    /// Revit, this command checks whether the previous probe's header cell text
    /// and Extensible Storage entity remain without changing the model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeReadStorageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Step0Reporter r = new Step0Reporter();
            string reportPath = null;

            try
            {
                if (uidoc == null)
                {
                    r.AddFail("No document is open.", null);
                    return Result.Cancelled;
                }
                Document doc = uidoc.Document;
                r.Add("Document: " + (string.IsNullOrEmpty(doc.PathName) ? "(unsaved)" : doc.PathName));
                r.Add("This command does not change the model (no transaction). ");

                Schema schema = Schema.Lookup(ProbeStorage.SchemaGuid);
                if (schema == null)
                {
                    r.Add("Schema.Lookup: not registered (normal when this model has no probe entity)");
                }
                else
                {
                    r.AddOk("Schema.Lookup: registered (" + ProbeStorage.SchemaGuid + ")");
                }

                string[] names = { HeaderTableProbe.ScheduleNameA, HeaderTableProbe.ScheduleNameB };
                foreach (string name in names)
                {
                    r.Add("");
                    r.Add("--- Schedule: " + name + " ---");
                    ViewSchedule schedule = HeaderTableProbe.FindByName(doc, name);
                    if (schedule == null)
                    {
                        r.Add("Not found (not created or already deleted)");
                        continue;
                    }
                    r.AddOk("Schedule found (Id=" + schedule.Id.Value + ")");

                    // Check the header table without changing it.
                    try
                    {
                        TableSectionData tsd = schedule.GetTableData()
                            .GetSectionData(SectionType.Header);
                        int r0 = tsd.FirstRowNumber;
                        int c0 = tsd.FirstColumnNumber;
                        r.AddOk("Header section: " + tsd.NumberOfRows + " rows x "
                            + tsd.NumberOfColumns + " columns");
                        r.Add("  First cell GetCellText: \"" + tsd.GetCellText(r0, c0) + "\"");
                    }
                    catch (Exception ex)
                    {
                        r.AddFail("Reading the header table", ex);
                    }

                    // Check whether the entity remains.
                    if (schema != null)
                    {
                        ProbeStorage.ReportExistingEntity(schedule, schema, r);
                    }
                }
            }
            catch (Exception ex)
            {
                r.AddFail("Read-storage diagnostic failed", ex);
            }
            finally
            {
                reportPath = r.Flush(uiapp, "Probe_ReadStorage");
            }

            TaskDialog.Show("Excel Schedule Link diagnostics",
                "Read-storage diagnostic completed." + (r.HasFailure ? "\nFailures were recorded; review the report." : "")
                + "\n\nDetailed report:\n" + reportPath);
            return Result.Succeeded;
        }
    }
}
