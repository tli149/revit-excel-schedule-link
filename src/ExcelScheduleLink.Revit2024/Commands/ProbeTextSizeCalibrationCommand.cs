using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Runs the probe-only Revit text-size calibration.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeTextSizeCalibrationCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Text Size Calibration";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "No active document is available.");
                return Result.Cancelled;
            }

            ViewSchedule schedule;
            try
            {
                schedule = TextSizeCalibrationReport.PickTarget(uidoc);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(Title, "The linked schedule could not be selected.\n\n" + ex);
                message = ex.Message;
                return Result.Failed;
            }
            if (schedule == null)
            {
                TaskDialog.Show(Title,
                    "No linked schedule was found. Select exactly one linked schedule in the "
                    + "Project Browser, or create a linked schedule first.");
                return Result.Cancelled;
            }

            TextSizeCalibrationResult result;
            try
            {
                result = TextSizeCalibrationReport.Run(uiapp, uidoc.Document, schedule);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(Title,
                    "The text-size calibration report could not be written.\n\n" + ex);
                message = ex.Message;
                return Result.Failed;
            }

            TaskDialog dialog = new TaskDialog(Title);
            dialog.MainInstruction = "Text size calibration recorded.";
            dialog.MainContent = "Evidence file:\n" + result.Path;
            dialog.ExpandedContent = result.RollbackVerified
                ? "Nothing was changed; the calibration transaction was rolled back."
                : "Rollback completion could not be verified. The observed final status was: "
                    + result.FinalRollbackStatus;
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
