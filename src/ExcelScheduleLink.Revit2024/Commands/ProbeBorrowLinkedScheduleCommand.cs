using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Probe-only ownership request. WorksharingUtils.CheckoutElements
    /// is called without opening a Revit transaction, as documented by the
    /// Revit API for this overload.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeBorrowLinkedScheduleCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Borrow Linked Schedule";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp == null ? null : uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "No active document is available. Nothing was changed.");
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!doc.IsWorkshared)
            {
                TaskDialog.Show(
                    Title,
                    "The active document is not workshared. No checkout was attempted and "
                    + "nothing was changed.");
                return Result.Cancelled;
            }

            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count != 1)
            {
                TaskDialog.Show(
                    Title,
                    "Select exactly one Excel-linked schedule in the Project Browser. "
                    + "No checkout was attempted.");
                return Result.Cancelled;
            }

            ElementId selectedId = null;
            foreach (ElementId id in selectedIds)
            {
                selectedId = id;
                break;
            }

            ViewSchedule schedule = selectedId == null
                ? null
                : doc.GetElement(selectedId) as ViewSchedule;
            if (schedule == null || schedule.IsTemplate
                || !LinkedScheduleFinder.IsLinked(schedule))
            {
                TaskDialog.Show(
                    Title,
                    "The single selected Project Browser item is not an Excel-linked schedule. "
                    + "No checkout was attempted.");
                return Result.Cancelled;
            }

            ICollection<ElementId> checkedOut = null;
            string checkoutResult;
            try
            {
                checkedOut = WorksharingUtils.CheckoutElements(
                    doc, new List<ElementId> { schedule.Id });
                checkoutResult = checkedOut != null
                    && checkedOut.Contains(schedule.Id)
                    ? "requested element is in the returned owned set"
                    : "requested element is not in the returned owned set";
            }
            catch (Exception ex)
            {
                checkoutResult = "CheckoutElements failed: "
                    + ex.GetType().Name + ": " + ex.Message;
            }

            string statusText;
            string statusOwner = string.Empty;
            try
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(
                    doc, schedule.Id, out statusOwner);
                statusText = status.ToString();
            }
            catch (Exception ex)
            {
                statusText = "failed: " + ex.GetType().Name + ": " + ex.Message;
            }

            string tooltipOwner;
            try
            {
                WorksharingTooltipInfo tooltip =
                    WorksharingUtils.GetWorksharingTooltipInfo(doc, schedule.Id);
                tooltipOwner = tooltip == null || string.IsNullOrEmpty(tooltip.Owner)
                    ? "(empty)"
                    : tooltip.Owner;
            }
            catch (Exception ex)
            {
                tooltipOwner = "failed: " + ex.GetType().Name + ": " + ex.Message;
            }

            TaskDialog dialog = new TaskDialog(Title);
            dialog.MainInstruction = "Linked schedule checkout probe completed.";
            dialog.MainContent =
                "Schedule: " + schedule.Name + " (element id "
                + schedule.Id.Value + ")\n"
                + "CheckoutElements result: " + checkoutResult + "\n"
                + "Checkout status: " + statusText + "\n"
                + "Owner from GetCheckoutStatus: "
                + (string.IsNullOrEmpty(statusOwner) ? "(empty)" : statusOwner) + "\n"
                + "Owner from GetWorksharingTooltipInfo: " + tooltipOwner;
            dialog.ExpandedContent =
                "No Revit transaction was opened. CheckoutElements may change worksharing "
                + "ownership even though it does not use a document transaction."
                + "\nThe status value is the locally cached Revit worksharing result.";
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
