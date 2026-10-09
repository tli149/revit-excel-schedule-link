using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Opens the Column Widths dialog for the linked schedule and applies what it
    /// returns. The dialog only edits candidates; this command owns every Revit
    /// side effect (transaction, extensible storage, ActiveView), and re-verifies
    /// the target at the apply boundary because the model may have changed while
    /// the modal dialog was open.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LayoutDialogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            LayoutTarget target = LayoutService.ResolveTarget(uidoc);
            if (target.Kind != LayoutTargetKind.Resolved)
            {
                TaskDialog.Show(UserMessages.DialogTitle, target.Message);
                return Result.Cancelled;
            }

            ViewSchedule schedule = target.Schedule;
            ElementId scheduleId = schedule.Id; // re-fetched after the dialog closes

            double[] currentFeet = LayoutService.GetCurrentWidths(schedule);
            if (currentFeet.Length == 0)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.LayoutNoColumns);
                return Result.Cancelled;
            }

            ColumnWidthMode currentMode = LayoutService.GetColumnWidthMode(schedule);
            double widthScale = LayoutService.GetWidthScale(schedule);
            LengthUnitChoice unit = LengthUnitHelper.Resolve(doc);
            int columnCount = currentFeet.Length;

            // "Apply" inside the dialog: same re-verification and all-or-nothing
            // transaction as the closing path, just without closing. The dialog runs
            // inside this command's Revit API context, so the transaction is valid.
            // Revit discards every transaction committed during a command that
            // returns Cancelled or Failed. Once an in-dialog Apply has committed,
            // the command must therefore end with Succeeded whatever the user
            // does afterwards; Cancel and Discard only ever drop unapplied edits.
            bool appliedInDialog = false;
            Func<double[], ColumnWidthMode, LayoutSourceSnapshot, LayoutApplyResult> applyNow =
                delegate (double[] feet, ColumnWidthMode policy, LayoutSourceSnapshot sourceSnapshot)
                {
                    ViewSchedule live = doc.GetElement(scheduleId) as ViewSchedule;
                    string issue = ReVerify(doc, live, feet);
                    if (issue != null)
                    {
                        return LayoutApplyResult.Failed(issue, null);
                    }
                    if (!LayoutMutationAuthorization.Authorize(doc, live, sourceSnapshot))
                    {
                        return LayoutApplyResult.Failed(UserMessages.ReloadCancelled, null);
                    }
                    LayoutApplyResult inDialog = LayoutService.Apply(doc, live, feet, policy, null, true);
                    if (inDialog != null && inDialog.Success)
                    {
                        appliedInDialog = true;
                    }
                    return inDialog;
                };

            // ShowDialog() without an explicit Revit owner, and no thread DPI
            // switching anywhere: verified on a 4K machine that this is what keeps
            // the text crisp. Making the thread Per-Monitor-aware and parenting the
            // dialog to Revit's System-aware main window produced a bitmap-scaled
            // (blurry) dialog instead.
            LayoutDialogOutcome outcome;
            using (LayoutDialog dialog = new LayoutDialog(
                schedule.Name,
                currentFeet,
                currentMode,
                unit,
                delegate { return LayoutService.ComputeFitContent(schedule, widthScale); },
                delegate { return ExcelWidthReader.ReadColumnWidths(doc, schedule, columnCount); },
                applyNow))
            {
                dialog.ShowDialog();
                outcome = dialog.Outcome;
            }

            if (outcome == null || outcome.Action == LayoutDialogAction.Cancelled)
            {
                return appliedInDialog ? Result.Succeeded : Result.Cancelled;
            }

            bool wantsApply = outcome.Action == LayoutDialogAction.ApplyAndClose
                              || outcome.Action == LayoutDialogAction.ApplyAndOpenSchedule;
            bool wantsOpenView = outcome.Action == LayoutDialogAction.ApplyAndOpenSchedule
                                 || outcome.Action == LayoutDialogAction.DiscardAndOpenSchedule;

            // Re-fetch by id: the dialog was modal, but never assume the model stood still.
            ViewSchedule fresh = doc.GetElement(scheduleId) as ViewSchedule;

            if (wantsApply)
            {
                string problem = ReVerify(doc, fresh, outcome.CandidateFeet);
                if (problem != null)
                {
                    TaskDialog.Show(UserMessages.DialogTitle, problem);
                    return appliedInDialog ? Result.Succeeded : Result.Cancelled; // this apply changed nothing
                }

                if (!LayoutMutationAuthorization.Authorize(doc, fresh, outcome.SourceSnapshot))
                {
                    return appliedInDialog ? Result.Succeeded : Result.Cancelled;
                }

                LayoutApplyResult applied = LayoutService.Apply(
                    doc, fresh, outcome.CandidateFeet, outcome.ReloadPolicy, null, true);
                TaskDialog.Show(UserMessages.DialogTitle, applied.UserMessage);
                if (!applied.Success)
                {
                    message = applied.UserMessage;
                    // Never switch views after a failed apply. Failed would also undo
                    // an earlier in-dialog Apply, so only use it when nothing was committed.
                    return appliedInDialog ? Result.Succeeded : Result.Failed;
                }
            }

            if (wantsOpenView && fresh != null && fresh.IsValidObject)
            {
                if (ScheduleActivationGuard.CheckAndOfferRepair(uidoc, fresh))
                {
                    try { uidoc.ActiveView = fresh; }
                    catch (Exception) { /* best effort */ }
                }
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Apply-boundary re-verification: valid schedule, same document, still an
        /// Excel link, and a header column count that still matches the candidates.
        /// Returns null when everything is fine, otherwise the message to show.
        /// </summary>
        private static string ReVerify(Document doc, ViewSchedule schedule, double[] candidateFeet)
        {
            if (schedule == null || !schedule.IsValidObject)
            {
                return UserMessages.LayoutInvalidTarget;
            }
            try
            {
                if (schedule.Document == null || !schedule.Document.Equals(doc))
                {
                    return UserMessages.LayoutInvalidTarget;
                }
            }
            catch (Exception)
            {
                return UserMessages.LayoutInvalidTarget;
            }
            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return UserMessages.LayoutNotLinked;
            }
            if (candidateFeet == null)
            {
                return UserMessages.LayoutNoCandidates;
            }

            int headerColumns;
            try
            {
                headerColumns = schedule.GetTableData().GetSectionData(SectionType.Header).NumberOfColumns;
            }
            catch (Exception)
            {
                return UserMessages.LayoutInvalidTarget;
            }
            if (headerColumns <= 0)
            {
                return UserMessages.LayoutNoColumns;
            }
            if (headerColumns != candidateFeet.Length)
            {
                return UserMessages.LayoutColumnCountMismatch(candidateFeet.Length, headerColumns);
            }
            return null;
        }
    }
}
