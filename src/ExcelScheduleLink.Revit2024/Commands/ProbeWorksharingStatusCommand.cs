using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Debug-only evidence command for worksharing verification. Every checklist
    /// item that needs an evidence record can use this command, so each check can be judged
    /// from the written files instead of from screenshots taken at the right
    /// moment. The Revit-Word pair run lost Tester A's ownership evidence exactly
    /// because there was no such record to point at afterwards.
    ///
    /// Read-only by construction: it opens no transaction and calls only query
    /// APIs, so an operator can run it at any point in the relay, including
    /// immediately before deliberately causing an ownership conflict.
    ///
    /// It stays in the debug manifest. It is verification tooling, not a feature,
    /// and the production ribbon should not grow a button whose output only the
    /// test procedure knows how to read.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ProbeWorksharingStatusCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - Worksharing Status";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            string path;
            try
            {
                path = WorksharingStatusReport.Run(uiapp, doc);
            }
            catch (Exception ex)
            {
                // A probe that cannot write its evidence must say so loudly: a
                // silently missing record is indistinguishable from a step the
                // operator forgot, and that ambiguity is what this command exists
                // to remove.
                TaskDialog.Show(Title,
                    "The worksharing status report could not be written.\n\n"
                    + "Record this failure in the result sheet rather than repeating\n"
                    + "the step: a missing evidence file must not be read as a step\n"
                    + "that was skipped.\n\n"
                    + ex);
                message = ex.Message;
                return Result.Failed;
            }

            TaskDialog dialog = new TaskDialog(Title);
            dialog.MainInstruction = "Worksharing status recorded.";
            dialog.MainContent = Summary(doc) + "\n\nEvidence file:\n" + path;
            dialog.ExpandedContent =
                "Nothing was changed in the model - this command opens no transaction.\n\n"
                + "For a relay run, set " + WorksharingStatusReport.EvidenceDirectoryVariable
                + " before launching Revit so\nevidence from both machines collects on the QA kit "
                + "drive. Without it,\nfiles go to %APPDATA%\\ExcelScheduleLink\\worksharing on "
                + "each machine\nand have to be gathered by hand.";
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
            return Result.Succeeded;
        }

        /// <summary>
        /// The two facts an operator needs on screen to decide whether to carry
        /// on with the current relay step. Everything else is in the file.
        /// </summary>
        private static string Summary(Document doc)
        {
            string workshared;
            try
            {
                workshared = doc.IsWorkshared
                    ? "Workshared: yes"
                    : "Workshared: NO - this is not the model the WS-xx checks are about.";
            }
            catch (Exception)
            {
                workshared = "Workshared: (could not be determined)";
            }

            string links;
            try
            {
                links = "Excel-linked schedules found: "
                    + LinkedScheduleFinder.FindAll(doc).Count
                    + " (links in a closed workset are not counted)";
            }
            catch (Exception ex)
            {
                links = "Excel-linked schedules found: (failed: " + ex.GetType().Name + ")";
            }

            return workshared + "\n" + links;
        }
    }
}
