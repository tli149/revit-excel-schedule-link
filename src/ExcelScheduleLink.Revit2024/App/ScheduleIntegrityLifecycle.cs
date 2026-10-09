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
        private const string AutomaticNotificationKey = "automatic-style-integrity";
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
                    state.ReadOnlyAtOpen = true;
                    QueueNotification(state);
                    return;
                }

                if (doc.IsModifiable)
                {
                    // An active transaction is not ours. Idling gets one retry.
                    state.RetryPending = true;
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
                state.Results.Clear();
                state.Results[-1L] = new StyleIntegrityResult
                {
                    State = StyleIntegrityState.Indeterminate,
                    ReasonCode = StyleIntegrityReasonCode.TableReadFailed,
                    FirstReadError = ex.GetType().Name + ": " + ex.Message
                };
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
            if (state.RetryPending && !state.RetryAttempted)
            {
                try
                {
                    state.RetryAttempted = true;
                    ScheduleIntegritySessionCache.ScanAutomatic(state.Document);
                    state.RetryPending = false;
                    QueueNotification(state);
                }
                catch (Exception ex)
                {
                    state.RetryAttempted = true;
                    state.RetryPending = false;
                    RecordIdlingFailure(state, ex);
                }
            }

            // Notification construction includes resolving schedule names from
            // the document and TaskDialog.Show itself, both of which may throw
            // during teardown. Contain the entire branch per state.
            if (state.AutomaticNotificationPending)
            {
                if (state.AutomaticNotificationInProgress)
                {
                    return;
                }
                try
                {
                    if (!state.Notifications.HasBeenShown(AutomaticNotificationKey))
                    {
                        if (!ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                            true, false, state.AutomaticNotificationAttempts))
                        {
                            state.AutomaticNotificationPending = false;
                            return;
                        }
                        state.AutomaticNotificationAttempts++;
                        TaskDialogResult notificationResult =
                            ShowAutomaticNotification(state);
                        if (notificationResult == TaskDialogResult.CommandLink1)
                        {
                            RunAutomaticRepairs(state);
                        }
                    }
                    else
                    {
                        state.AutomaticNotificationPending = false;
                    }
                }
                catch (Exception)
                {
                    if (!ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                        state.AutomaticNotificationPending, false,
                        state.AutomaticNotificationAttempts))
                    {
                        state.AutomaticNotificationPending = false;
                    }
                }
            }
        }

        private static void RecordIdlingFailure(
            ScheduleIntegrityDocumentState state, Exception exception)
        {
            state.Results.Clear();
            state.Results[-1L] = new StyleIntegrityResult
            {
                State = StyleIntegrityState.Indeterminate,
                ReasonCode = StyleIntegrityReasonCode.TableReadFailed,
                FirstReadError = exception.GetType().Name + ": " + exception.Message
            };
            try
            {
                QueueNotification(state);
            }
            catch (Exception)
            {
                state.AutomaticNotificationPending = false;
            }
        }

        private static void QueueNotification(ScheduleIntegrityDocumentState state)
        {
            state.AutomaticNotificationPending =
                ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                    state.Results.Values);
        }

        private static string BuildUnsafeStylesContent(
            ScheduleIntegrityDocumentState state, bool offersReviewRepairs)
        {
            List<string> affectedNames = new List<string>();
            bool hasIndeterminate = false;
            foreach (KeyValuePair<long, StyleIntegrityResult> pair in state.Results)
            {
                StyleIntegrityResult result = pair.Value;
                if (result != null && result.State == StyleIntegrityState.Affected)
                {
                    ViewSchedule schedule = state.Document.GetElement(
                        new ElementId(pair.Key)) as ViewSchedule;
                    affectedNames.Add(schedule == null
                        ? "(schedule unavailable)" : schedule.Name);
                }
                else if (result == null
                    || result.State == StyleIntegrityState.Indeterminate)
                {
                    hasIndeterminate = true;
                }
            }

            string content = UserMessages.AutomaticUnsafeStylesContent(
                affectedNames, offersReviewRepairs);
            return hasIndeterminate
                ? content + "\n\n" + UserMessages.AutomaticUnverifiedContent
                : content;
        }

        private static void RunAutomaticRepairs(
            ScheduleIntegrityDocumentState state)
        {
            try
            {
                List<ViewSchedule> affected = new List<ViewSchedule>();
                foreach (KeyValuePair<long, StyleIntegrityResult> pair in state.Results)
                {
                    if (pair.Value == null
                        || pair.Value.State != StyleIntegrityState.Affected)
                    {
                        continue;
                    }
                    ViewSchedule schedule = state.Document.GetElement(
                        new ElementId(pair.Key)) as ViewSchedule;
                    if (schedule != null)
                    {
                        affected.Add(schedule);
                    }
                }

                CheckAndRepairOutcome outcome =
                    ScheduleRepairCoordinator.ReviewAffectedSchedules(
                        state.Document,
                        affected,
                        new Dictionary<long, StyleIntegrityResult>(state.Results));
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
                state.Results[-1L] = new StyleIntegrityResult
                {
                    State = StyleIntegrityState.Indeterminate,
                    ReasonCode = StyleIntegrityReasonCode.TableReadFailed,
                    FirstReadError = ex.GetType().Name + ": " + ex.Message
                };
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
            if (state.AutomaticNotificationInProgress)
            {
                return TaskDialogResult.Cancel;
            }
            state.AutomaticNotificationInProgress = true;
            try
            {
                if (state.Notifications.HasBeenShown(AutomaticNotificationKey))
                {
                    state.AutomaticNotificationPending = false;
                    return TaskDialogResult.Cancel;
                }

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
                    List<string> affectedNames = new List<string>();
                    bool hasIndeterminate = false;
                    foreach (KeyValuePair<long, StyleIntegrityResult> pair in state.Results)
                    {
                        StyleIntegrityResult result = pair.Value;
                        if (result != null && result.State == StyleIntegrityState.Affected)
                        {
                            ViewSchedule schedule = state.Document.GetElement(
                                new ElementId(pair.Key)) as ViewSchedule;
                            affectedNames.Add(schedule == null
                                ? "(schedule unavailable)" : schedule.Name);
                        }
                        else if (result == null
                            || result.State == StyleIntegrityState.Indeterminate)
                        {
                            hasIndeterminate = true;
                        }
                    }

                    if (affectedNames.Count > 0)
                    {
                        string content = UserMessages.AutomaticUnsafeStylesContent(affectedNames);
                        if (hasIndeterminate)
                        {
                            content += "\n\n" + UserMessages.AutomaticUnverifiedContent;
                        }
                        dialogContent = UserMessages.AutomaticUnsafeStylesInstruction
                            + "\n\n" + content;
                    }
                    else if (hasIndeterminate)
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

                bool hasAffected = false;
                foreach (StyleIntegrityResult result in state.Results.Values)
                {
                    if (result != null && result.State == StyleIntegrityState.Affected)
                    {
                        hasAffected = true;
                        break;
                    }
                }

                TaskDialogResult dialogResult = TaskDialogResult.Cancel;
                if (ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(
                    hasAffected,
                    state.Document.IsReadOnly))
                {
                    TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
                    dialog.MainInstruction = UserMessages.AutomaticUnsafeStylesInstruction;
                    dialog.MainContent = BuildUnsafeStylesContent(state, true);
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
                state.Notifications.TryMarkShown(AutomaticNotificationKey);
                state.AutomaticNotificationPending = false;
                state.AutomaticNotificationAttempts = 0;
                return dialogResult;
            }
            finally
            {
                state.AutomaticNotificationInProgress = false;
            }
        }
    }
}
