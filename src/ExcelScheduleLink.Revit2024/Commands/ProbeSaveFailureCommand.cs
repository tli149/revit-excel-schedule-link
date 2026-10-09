using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Debug-only harness for the settings-save failure regression check (failures are
    /// reported truthfully). It exists because the natural way to make a save
    /// fail - another user holding the element in a workshared model - needs a
    /// second person, while the regression check actually asserts something narrower: when the
    /// settings transaction does not commit, the Link Manager must not claim the
    /// settings were saved and must not go on to reload.
    ///
    /// This command opens a transaction and holds it open for the whole Link
    /// Manager session. Revit does not allow a second transaction in the same
    /// document, so every save the manager attempts fails at Transaction.Start,
    /// LinkManagerService.SaveSettings returns false, and the failure path under
    /// test runs on demand instead of by luck.
    ///
    /// The failure CAUSE differs from the worksharing case (nested transaction
    /// rather than a borrowed element), but the code path under test is the
    /// same one. Real worksharing behavior still has to be checked in a real
    /// workshared model; this does not replace that.
    ///
    /// The blocking transaction is always rolled back, so the probe cannot leave
    /// anything in the model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeSaveFailureCommand : IExternalCommand
    {
        private const string Title = "ESL Probe - settings save failure";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            TaskDialog.Show(Title,
                "The Link Manager will open with settings saving forced to fail.\n\n"
                + "Expected (settings-save failure check):\n"
                + "1. Change a setting, then click Apply settings.\n"
                + "   The manager must report that the settings could not be saved,\n"
                + "   must NOT say \"Settings saved\", and the edit must stay pending\n"
                + "   (heading still says unsaved changes, Apply still enabled).\n"
                + "2. With the edit still pending, click Reload selected.\n"
                + "   It must report the same failure and must NOT reload.\n\n"
                + "Do not use Reload selected or Reload all with NO pending edit:\n"
                + "the reload transaction is blocked too, which is an artifact of\n"
                + "this probe rather than the settings-save failure check.\n\n"
                + "Nothing is written to the model either way.");

            using (Transaction blocker = new Transaction(doc, "ESL Probe - block settings save"))
            {
                TransactionStatus started;
                try
                {
                    started = blocker.Start();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show(Title, "Could not open the blocking transaction: " + ex.Message);
                    return Result.Cancelled;
                }
                if (started != TransactionStatus.Started)
                {
                    TaskDialog.Show(Title,
                        "Could not open the blocking transaction (status " + started + ").");
                    return Result.Cancelled;
                }

                try
                {
                    using (LinkManagerDialog dialog = new LinkManagerDialog(doc))
                    {
                        dialog.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    // An escaping exception is itself a result worth seeing: it
                    // means some path reported the blocked transaction by
                    // throwing rather than by telling the user.
                    TaskDialog.Show(Title,
                        "The Link Manager let an exception escape:\n\n" + ex);
                }
                finally
                {
                    RollBackIfStarted(blocker);
                }
            }

            TaskDialog.Show(Title,
                "Probe finished. The blocking transaction was rolled back, so this "
                + "command changed nothing in the model.");
            return Result.Succeeded;
        }

        private static void RollBackIfStarted(Transaction transaction)
        {
            try
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }
            }
            catch (Exception)
            {
                // Disposing an uncommitted transaction also discards it.
            }
        }
    }
}
