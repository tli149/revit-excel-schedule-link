using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Shared runner for diagnostic variants A (no field) and B (dummy field
    /// plus contradictory filters). Both can be run for comparison and are
    /// distinguished by schedule name. A repeated run reuses an existing
    /// schedule and resets its header to 1x1 first.
    /// </summary>
    internal static class ScheduleProbeRunner
    {
        public static Result Run(
            ExternalCommandData commandData,
            string commandName,
            string scheduleName,
            bool fallbackMode)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Step0Reporter r = new Step0Reporter();
            string reportPath = null;
            ViewSchedule schedule = null;

            if (uidoc == null)
            {
                r.AddFail("No document is open. Open a disposable RVT before running the diagnostic.", null);
                reportPath = r.Flush(uiapp, commandName);
                TaskDialog.Show("Excel Schedule Link diagnostics", "No document is open.\nDetails: " + reportPath);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            r.Add("Document: " + (string.IsNullOrEmpty(doc.PathName) ? "(unsaved)" : doc.PathName));
            r.Add("Diagnostic variant: " + (fallbackMode
                ? "B: empty body with a dummy field and contradictory filters"
                : "A: multi-category schedule without fields"));

            try
            {
                using (Transaction t = new Transaction(doc, commandName))
                {
                    t.Start();

                    schedule = HeaderTableProbe.FindByName(doc, scheduleName);
                    if (schedule != null)
                    {
                        r.AddOk("Reusing existing schedule: " + scheduleName + " (Id=" + schedule.Id.Value + ")");
                    }
                    else
                    {
                        schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                        schedule.Name = scheduleName;
                        r.AddOk("ViewSchedule.CreateSchedule(multi-category) + name: " + scheduleName
                            + " (Id=" + schedule.Id.Value + ")");

                        if (fallbackMode)
                        {
                            ConfigureFallbackBodyEmpty(doc, schedule, r);
                        }
                    }

                    HeaderTableProbe.ConfigureDefinition(schedule, r);
                    HeaderTableProbe.BuildTestTable(schedule, r, fallbackMode ? "Variant B" : "Variant A");

                    // Extensible Storage check (read existing, write new, read back).
                    Schema schema = ProbeStorage.EnsureSchema(r);
                    ProbeStorage.ReportExistingEntity(schedule, schema, r);
                    ProbeStorage.WriteAndVerifyEntity(schedule, schema, r);

                    t.Commit();
                    r.AddOk("Transaction Commit");
                }
            }
            catch (Exception ex)
            {
                r.AddFail("Diagnostic transaction failed (the transaction is rolled back)", ex);
            }
            finally
            {
                reportPath = r.Flush(uiapp, commandName);
            }

            // Open the schedule for manual inspection; ignore an activation failure.
            if (schedule != null && schedule.IsValidObject)
            {
                try
                {
                    uidoc.ActiveView = schedule;
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }

            TaskDialog.Show("Excel Schedule Link diagnostics",
                commandName + " completed." + (r.HasFailure ? "\nFailures were recorded; review the report." : "\nAll operations succeeded.")
                + "\n\nDetailed report:\n" + reportPath);
            return Result.Succeeded;
        }

        /// <summary>
        /// Variant B: add one dummy field and empty the body with two
        /// contradictory Equal filters. Select a string-like field from
        /// GetSchedulableFields rather than assuming a fixed ID, and record it.
        /// </summary>
        private static void ConfigureFallbackBodyEmpty(Document doc, ViewSchedule schedule, Step0Reporter r)
        {
            try
            {
                ScheduleDefinition def = schedule.Definition;
                IList<SchedulableField> fields = def.GetSchedulableFields();
                r.Add("GetSchedulableFields: " + fields.Count + " field(s)");

                SchedulableField chosen = null;
                string reason = null;

                // Prefer string parameters: comments, mark, and type comments.
                BuiltInParameter[] preferred =
                {
                    BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                    BuiltInParameter.ALL_MODEL_MARK,
                    BuiltInParameter.ALL_MODEL_TYPE_COMMENTS
                };
                foreach (BuiltInParameter bip in preferred)
                {
                    ElementId pid = new ElementId(bip);
                    chosen = fields.FirstOrDefault(f => f.ParameterId == pid);
                    if (chosen != null)
                    {
                        reason = "preferred string parameter " + bip;
                        break;
                    }
                }
                if (chosen == null)
                {
                    chosen = fields.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Instance);
                    reason = "no preferred candidate; first Instance field";
                }
                if (chosen == null)
                {
                    chosen = fields.FirstOrDefault();
                    reason = "no Instance field; first available field";
                }
                if (chosen == null)
                {
                    r.AddFail("No SchedulableField is available", null);
                    return;
                }

                string fieldName;
                try
                {
                    fieldName = chosen.GetName(doc);
                }
                catch (Exception)
                {
                    fieldName = "(name lookup failed)";
                }
                r.AddOk("Selected dummy field: \"" + fieldName + "\""
                    + " (ParameterId=" + chosen.ParameterId.Value
                    + ", FieldType=" + chosen.FieldType
                    + ", reason: " + reason + ")");

                ScheduleField field = def.AddField(chosen);
                r.AddOk("ScheduleDefinition.AddField(SchedulableField)");

                def.AddFilter(new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, "ESL_STEP0_X"));
                def.AddFilter(new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, "ESL_STEP0_Y"));
                r.AddOk("Added two contradictory filters (Equal \"ESL_STEP0_X\" AND Equal \"ESL_STEP0_Y\"); body is empty");
            }
            catch (Exception ex)
            {
                r.AddFail("Configuring variant B dummy field and filters", ex);
            }
        }
    }
}
