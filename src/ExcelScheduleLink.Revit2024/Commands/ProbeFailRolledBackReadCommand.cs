using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Arms the one-shot regeneration-failure equivalent at a rolled-back style
    /// read outside ModelMutation, so the exception path through
    /// FatalRegenerationBoundary can be exercised in Revit.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeFailRolledBackReadCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Rolled-Back Read Failure";

        public Result Execute(
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            int linkManagerOpenReads = uidoc == null
                ? -1 : LinkManagerOpenReads(uidoc.Document);

            TaskDialog dialog = new TaskDialog(Title);
            dialog.MainInstruction = "Which rolled-back style read should fail?";
            dialog.MainContent =
                "The chosen read throws a private marker treated identically to Revit's "
                + "RegenerationFailedException (the Revit exception cannot be constructed "
                + "by the probe). Opening Link Manager uses one read per linked schedule.\n\n"
                + "This probe-manifest-only hook changes no model state. Close the model "
                + "without saving after the fatal message.";
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "The next read",
                "Column Widths Apply, or Reload Link's final check.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "The second read (one read passes first)",
                "An Apply after an earlier Apply.");
            if (linkManagerOpenReads >= 0)
            {
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "The first read after Link Manager opens",
                    "Link Manager's Check and repair. Opening Link Manager passes "
                    + linkManagerOpenReads + " read(s) first, one per linked schedule.");
            }
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.DefaultButton = TaskDialogResult.Cancel;
            dialog.AllowCancellation = true;

            int readsToPass;
            switch (dialog.Show())
            {
                case TaskDialogResult.CommandLink1:
                    readsToPass = 0;
                    break;
                case TaskDialogResult.CommandLink2:
                    readsToPass = 1;
                    break;
                case TaskDialogResult.CommandLink3:
                    readsToPass = linkManagerOpenReads;
                    break;
                default:
                    return Result.Cancelled;
            }
            DebugRegenerationFailureInjector.ArmRolledBackReadFailure(readsToPass);
            return Result.Succeeded;
        }

        // The reads LinkManagerCommand's Manual scan makes in a writable document
        // before its dialog shows: one per linked schedule, up to the Manual limit.
        private static int LinkManagerOpenReads(Document doc)
        {
            return Math.Min(LinkedScheduleFinder.FindAll(doc).Count,
                StyleIntegrityLimits.MaxSchedules(StyleIntegrityScanScope.Manual));
        }
    }
}
