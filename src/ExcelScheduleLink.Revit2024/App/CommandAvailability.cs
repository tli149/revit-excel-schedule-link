using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.App
{
    /// <summary>Available whenever a project document is open.</summary>
    public class DocumentOpenAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            return applicationData != null && applicationData.ActiveUIDocument != null;
        }
    }

    /// <summary>
    /// Like <see cref="LinkedScheduleAvailability"/>, but a schedule placed on a
    /// sheet (ScheduleSheetInstance) also counts, so the Layout commands can be
    /// started straight from the sheet. Still a type check only - no extensible
    /// storage is read here; the commands themselves verify the Excel link.
    /// </summary>
    public class ScheduleOrInstanceAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            UIDocument uidoc = applicationData != null ? applicationData.ActiveUIDocument : null;
            if (uidoc == null)
            {
                return false;
            }
            if (uidoc.ActiveView is ViewSchedule)
            {
                return true;
            }
            Document doc = uidoc.Document;
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (element is ViewSchedule || element is ScheduleSheetInstance)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Available when a document is open and the active view is a ViewSchedule,
    /// or a ViewSchedule is selected. Deliberately cheap (a type check only) - it
    /// does NOT read extensible storage on every UI poll. The Reload command
    /// itself does the exact "is this an Excel link?" check and reports when there
    /// is no valid target, so over-enabling here is harmless.
    /// </summary>
    public class LinkedScheduleAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            UIDocument uidoc = applicationData != null ? applicationData.ActiveUIDocument : null;
            if (uidoc == null)
            {
                return false;
            }
            if (uidoc.ActiveView is ViewSchedule)
            {
                return true;
            }
            Document doc = uidoc.Document;
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                if (doc.GetElement(id) is ViewSchedule)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
