using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Creates the host ViewSchedule for a link using method B (confirmed in
    /// confirmed during the initial feasibility probe): a multi-category schedule with one dummy field and two
    /// contradictory filters so the body stays permanently empty, while the
    /// header section (the title area) hosts the rendered Excel table.
    /// Method B was chosen over the field-less method A because it keeps
    /// manual cell/column resizing available in the Revit UI.
    ///
    /// Known UI constraint: the schedule view shows a single column-letter
    /// strip ("A") below the table for the one body column. It is part of the
    /// Revit schedule editor UI only and does not appear on sheets or in
    /// printed/PDF output (verified during the initial feasibility probe).
    /// </summary>
    internal static class ScheduleFactory
    {
        private const string FilterValueX = "ESL_FILTER_X";
        private const string FilterValueY = "ESL_FILTER_Y";

        /// <summary>Creates and configures the schedule. Must run inside a Transaction.</summary>
        public static ViewSchedule Create(Document doc, string baseName)
        {
            ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
            SetUniqueName(doc, schedule, baseName);
            ConfigureEmptyBody(doc, schedule);
            schedule.Definition.ShowTitle = true;    // the header section is the title area
            schedule.Definition.ShowHeaders = false; // hide the body column header row
            return schedule;
        }

        /// <summary>
        /// Method B: adds one dummy field and two mutually exclusive Equal
        /// filters so no element can ever appear in the body. The dummy field
        /// is picked from GetSchedulableFields (never a hard-coded id),
        /// preferring well-known text parameters.
        /// </summary>
        private static void ConfigureEmptyBody(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition definition = schedule.Definition;
            IList<SchedulableField> fields = definition.GetSchedulableFields();

            SchedulableField chosen = null;
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
                    break;
                }
            }
            if (chosen == null)
            {
                chosen = fields.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Instance)
                    ?? fields.FirstOrDefault();
            }
            if (chosen == null)
            {
                throw new InvalidOperationException(UserMessages.NoSchedulableField);
            }

            ScheduleField field = definition.AddField(chosen);
            definition.AddFilter(new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, FilterValueX));
            definition.AddFilter(new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, FilterValueY));
        }

        /// <summary>Assigns a unique view name, appending " (n)" on collisions.</summary>
        private static void SetUniqueName(Document doc, ViewSchedule schedule, string baseName)
        {
            string name = baseName;
            for (int suffix = 2; suffix < 1000; suffix++)
            {
                try
                {
                    schedule.Name = name;
                    return;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Name already in use — try the next suffix.
                    name = baseName + " (" + suffix + ")";
                }
            }
            // Last resort: make it unique with the element id.
            schedule.Name = baseName + " [" + schedule.Id.Value + "]";
        }
    }
}
