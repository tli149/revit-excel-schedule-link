using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.App
{
    /// <summary>
    /// Session lifecycle for bounded style-integrity scans. The open event
    /// never displays UI and never performs repair; Idling owns the warning,
    /// the one retry allowed after an active transaction, and any explicitly
    /// requested one-at-a-time repair flow.
    /// </summary>
    internal sealed class ScheduleIntegrityLifecycle
    {
        private readonly UIControlledApplication _app;

        public ScheduleIntegrityLifecycle(UIControlledApplication app)
        {
            _app = app;
        }

        public void Subscribe()
        {
            _app.ControlledApplication.DocumentOpened += OnDocumentOpened;
            _app.ControlledApplication.DocumentClosing += OnDocumentClosing;
            _app.ControlledApplication.DocumentClosed += OnDocumentClosed;
            _app.Idling += OnIdling;
        }

        public void Unsubscribe()
        {
            _app.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            _app.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            _app.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            _app.Idling -= OnIdling;
            ScheduleIntegritySessionCache.ClearAll();
        }

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs args)
        {
            // Revit raises DocumentOpened even after a failed/cancelled open.
            // Status is checked before touching the document or creating cache.
            if (args == null || args.Status != RevitAPIEventStatus.Succeeded)
            {
                return;
            }

            Document doc = args.Document;
            if (doc == null)
            {
                return;
            }

            // A linked RVT also raises DocumentOpened but can never be edited in
            // this session, so every link in it would be reported "not checked"
            // for no actionable reason; its schedules cannot be opened from the
            // host anyway. Family documents carry no Excel-linked schedules.
            if (doc.IsLinked || doc.IsFamilyDocument)
            {
                return;
            }

            ScheduleIntegrityDocumentState state =
                ScheduleIntegritySessionCache.BeginAutomatic(doc);
            try
            {
                if (doc.IsReadOnly)
                {
                    // ScanAutomatic creates one indeterminate record per link
                    // without attempting a transaction; it is intentionally not
                    // a repair or a persistent write.
                    ScheduleIntegritySessionCache.ScanAutomatic(doc);
                    state.MarkReadOnlyAtOpen();
                    QueueNotification(state);
                    return;
                }

                if (doc.IsModifiable)
                {
                    // An active transaction is not ours. Idling gets one retry.
                    state.ScheduleRetry();
                    return;
                }

                ScheduleIntegritySessionCache.ScanAutomatic(doc);
                QueueNotification(state);
            }
            catch (Exception ex)
            {
                // Opening a document must not be interrupted by an add-in scan.
                // Preserve an honest not-checked state for the later Idling
                // warning, without showing a dialog in DocumentOpened.
                state.ClearResults();
                state.RecordDocumentLevelFailure(ex);
                QueueNotification(state);
            }
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs args)
        {
            if (args != null)
            {
                // DocumentClosing is cancellable. Keep the state until the
                // paired DocumentClosed event reports a successful close.
                ScheduleIntegritySessionCache.MarkClosing(
                    args.DocumentId, args.Document);
            }
        }

        private void OnDocumentClosed(object sender, DocumentClosedEventArgs args)
        {
            if (args == null)
            {
                return;
            }
            if (args.Status == RevitAPIEventStatus.Succeeded)
            {
                ScheduleIntegritySessionCache.RemoveAfterSuccessfulClose(
                    args.DocumentId);
            }
            else
            {
                // A cancelled or failed close leaves the live state intact;
                // only the pending event correlation is cleared.
                ScheduleIntegritySessionCache.PreserveAfterCancelledClose(
                    args.DocumentId);
            }
        }

        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs args)
        {
            // This is the common idle path in a Revit session. Avoid both the
            // state-list allocation and any document walk when there is no
            // retry or pending notification.
            if (!ScheduleIntegritySessionCache.HasPendingWork())
            {
                return;
            }

            ScheduleIntegritySessionCache.PruneInvalidDocuments();
            if (!ScheduleIntegritySessionCache.HasPendingWork())
            {
                return;
            }

            foreach (ScheduleIntegrityDocumentState state in
                ScheduleIntegritySessionCache.GetPendingStates())
            {
                ProcessPendingState(state);
            }
        }

        private static void ProcessPendingState(ScheduleIntegrityDocumentState state)
        {
            if (state == null || state.Document == null)
            {
                return;
            }

            // Keep retry failures local to this document. A bad wrapper or a
            // transient Revit API failure must not abort Idling for other docs.
            if (state.TryBeginRetry())
            {
                try
                {
                    ScheduleIntegritySessionCache.ScanAutomatic(state.Document);
                    state.CompleteRetry();
                    QueueNotification(state);
                }
                catch (Exception ex)
                {
                    state.FailRetry();
                    RecordIdlingFailure(state, ex);
                }
            }

            // Notification construction includes resolving schedule names from
            // the document and TaskDialog.Show itself, both of which may throw
            // during teardown. Contain the entire branch per state.
            if (state.NotificationPending)
            {
                if (state.NotificationInProgress)
                {
                    return;
                }
                try
                {
                    TaskDialogResult notificationResult =
                        ShowAutomaticNotification(state);
                    if (notificationResult == TaskDialogResult.CommandLink1)
                    {
                        RunAutomaticRepairs(state);
                    }
                }
                catch (Exception)
                {
                    state.NotificationAttemptFailed();
                }
            }
        }

        private static void RecordIdlingFailure(
            ScheduleIntegrityDocumentState state, Exception exception)
        {
            state.ClearResults();
            state.RecordDocumentLevelFailure(exception);
            try
            {
                QueueNotification(state);
            }
            catch (Exception)
            {
                state.QueueNotification(false);
            }
        }

        private static void QueueNotification(ScheduleIntegrityDocumentState state)
        {
            IDictionary<long, StyleIntegrityResult> results =
                state.GetResultsSnapshot();
            state.QueueNotification(
                ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                    results.Values));
        }

        private static string BuildUnsafeStylesContent(
            ScheduleIntegrityDocumentState state,
            ScheduleIntegrityResultSummary summary,
            bool offersReviewRepairs)
        {
            List<string> affectedNames = ResolveAffectedScheduleNames(
                state.Document, summary);
            string content = UserMessages.AutomaticUnsafeStylesContent(
                affectedNames, offersReviewRepairs);
            return summary.HasIndeterminate
                ? content + "\n\n" + UserMessages.AutomaticUnverifiedContent
                : content;
        }

        private static List<string> ResolveAffectedScheduleNames(
            Document document, ScheduleIntegrityResultSummary summary)
        {
            List<string> affectedNames = new List<string>();
            foreach (long scheduleId in summary.AffectedScheduleIds)
            {
                ViewSchedule schedule = document.GetElement(
                    new ElementId(scheduleId)) as ViewSchedule;
                affectedNames.Add(schedule == null
                    ? "(schedule unavailable)" : schedule.Name);
            }
            return affectedNames;
        }

        private static void RunAutomaticRepairs(
            ScheduleIntegrityDocumentState state)
        {
            try
            {
                ScheduleIntegrityResultSummary summary =
                    state.GetResultSummary();
                List<ViewSchedule> affected = new List<ViewSchedule>();
                foreach (long scheduleId in summary.AffectedScheduleIds)
                {
                    ViewSchedule schedule = state.Document.GetElement(
                        new ElementId(scheduleId)) as ViewSchedule;
                    if (schedule != null)
                    {
                        affected.Add(schedule);
                    }
                }

                CheckAndRepairOutcome outcome =
                    ScheduleRepairCoordinator.ReviewAffectedSchedules(
                        state.Document,
                        affected,
                        state.GetResultsSnapshot());
                if (outcome != null && outcome.FatalRegenerationFailure)
                {
                    // The coordinator already showed the one fatal message.
                    // Do not rescan, update the session cache, resolve another
                    // element, or continue the repair loop for this document.
                    return;
                }
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.CheckAndRepairSummary(
                        outcome.Summary, outcome.Details));
            }
            catch (Exception ex)
            {
                // A repair exception must not escape Idling. Do not convert a
                // partially completed flow into success or silently discard
                // the diagnostic; the next action remains an explicit check.
                state.RecordDocumentLevelFailure(ex);
                try
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.AutomaticRepairFlowFailed(ex));
                }
                catch (Exception)
                {
                    // Revit may be tearing down the document/UI. The repair
                    // failure remains in the session state and never escapes
                    // the Idling callback.
                }
            }
        }

        private static TaskDialogResult ShowAutomaticNotification(
            ScheduleIntegrityDocumentState state)
        {
            if (!state.TryBeginNotification())
            {
                return TaskDialogResult.Cancel;
            }
            try
            {
                ScheduleIntegrityResultSummary summary =
                    state.GetResultSummary();

                // Build the complete dialog before consuming the once-per-
                // document token. Both name resolution and TaskDialog.Show can
                // throw while a document is being torn down.
                string dialogContent;
                if (state.ReadOnlyAtOpen)
                {
                    dialogContent = UserMessages.AutomaticReadOnlyMessage;
                }
                else
                {
                    if (summary.HasAffected)
                    {
                        List<string> affectedNames = ResolveAffectedScheduleNames(
                            state.Document, summary);
                        string content = UserMessages.AutomaticUnsafeStylesContent(affectedNames);
                        if (summary.HasIndeterminate)
                        {
                            content += "\n\n" + UserMessages.AutomaticUnverifiedContent;
                        }
                        dialogContent = UserMessages.AutomaticUnsafeStylesInstruction
                            + "\n\n" + content;
                    }
                    else if (summary.HasIndeterminate)
                    {
                        dialogContent = UserMessages.AutomaticUnverifiedInstruction
                            + "\n\n" + UserMessages.AutomaticUnverifiedContent;
                    }
                    else
                    {
                        // QueueNotification should not create this state, but
                        // keep the invariant explicit if its inputs change.
                        return TaskDialogResult.Cancel;
                    }
                }

                TaskDialogResult dialogResult = TaskDialogResult.Cancel;
                if (ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(
                    summary.HasAffected,
                    state.Document.IsReadOnly))
                {
                    TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
                    dialog.MainInstruction = UserMessages.AutomaticUnsafeStylesInstruction;
                    dialog.MainContent = BuildUnsafeStylesContent(
                        state, summary, true);
                    dialog.AllowCancellation = true;
                    dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                    dialog.AddCommandLink(
                        TaskDialogCommandLinkId.CommandLink1,
                        UserMessages.AutomaticReviewRepairsNow,
                        UserMessages.AutomaticReviewRepairsNowHint);
                    dialog.AddCommandLink(
                        TaskDialogCommandLinkId.CommandLink2,
                        UserMessages.AutomaticNotNow,
                        string.Empty);
                    dialog.DefaultButton = TaskDialogResult.CommandLink2;
                    dialogResult = dialog.Show();
                }
                else
                {
                    dialogResult = TaskDialog.Show(UserMessages.DialogTitle, dialogContent);
                }

                // Consume the once-per-document notification only after the
                // dialog returned. Repair may now run, but it must not cause a
                // second automatic warning for the same document.
                state.CompleteNotificationShown();
                return dialogResult;
            }
            finally
            {
                state.EndNotificationAttempt();
            }
        }
    }
}
