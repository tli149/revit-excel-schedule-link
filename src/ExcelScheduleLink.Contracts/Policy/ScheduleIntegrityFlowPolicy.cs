// Revit-free decision logic, kept in Contracts so the net8 tests can exercise it.
using System.Collections.Generic;

namespace ExcelScheduleLink.Contracts
{
    /// <summary>Pure decision used immediately before an ESL-owned activation.</summary>
    public enum ScheduleActivationDecision
    {
        Activate,
        OfferWorkbookRepair,
        RefuseUnverified
    }

    /// <summary>Pure decisions shared by activation and repair orchestration.</summary>
    public static class ScheduleIntegrityFlowDecisions
    {
        /// <summary>
        /// The general v0.9.1 advisory is only truthful for a non-detached
        /// workshared document and is suppressed once it was already shown.
        /// </summary>
        public static bool ShouldShowWorksharingAdvisory(
            bool isWorkshared, bool isDetached, bool alreadyShown)
        {
            return isWorkshared && !isDetached && !alreadyShown;
        }

        /// <summary>
        /// The only observations after which the ordinary reload path may use
        /// the locally cached ownership advisory. Pending, committed, and
        /// unreadable states deliberately remain unqualified.
        /// </summary>
        public enum ReloadFailureTransactionObservation
        {
            NotStarted,
            RolledBack,
            Pending,
            Committed,
            Unreadable
        }

        public const int AutomaticNotificationMaxAttempts = 3;

        public static bool MayQueryOwnershipAfterReloadFailure(
            ReloadFailureTransactionObservation observation)
        {
            return observation == ReloadFailureTransactionObservation.NotStarted
                || observation == ReloadFailureTransactionObservation.RolledBack;
        }

        public static bool MayQueryOwnershipAfterMutationFailure(
            bool regenerationFailure,
            ReloadFailureTransactionObservation observation)
        {
            return !regenerationFailure
                && MayQueryOwnershipAfterReloadFailure(observation);
        }

        /// <summary>
        /// Detached workshared documents are edited as independent copies. They
        /// must use the detached-copy boundary, not central-owner guidance.
        /// </summary>
        public static bool MayUseWorksharingOwnershipAdvisory(
            bool isDetached, bool isWorkshared)
        {
            return isWorkshared && !isDetached;
        }

        public static bool IsSuccessfulTargetedReload(
            bool workbookReloadSucceeded,
            bool fallbackSucceeded,
            StyleIntegrityResult after)
        {
            return (workbookReloadSucceeded || fallbackSucceeded)
                && after != null
                && after.State == StyleIntegrityState.Healthy;
        }

        public static ScheduleActivationDecision ForActivation(
            StyleIntegrityResult result)
        {
            if (result == null || result.State == StyleIntegrityState.Indeterminate)
            {
                return ScheduleActivationDecision.RefuseUnverified;
            }
            return result.State == StyleIntegrityState.Healthy
                ? ScheduleActivationDecision.Activate
                : ScheduleActivationDecision.OfferWorkbookRepair;
        }

        public static bool CountsAsWorkbookRepair(
            StyleIntegrityResult before,
            bool reloadCommitted,
            StyleIntegrityResult after)
        {
            return before != null
                && before.State == StyleIntegrityState.Affected
                && reloadCommitted
                && after != null
                && after.State == StyleIntegrityState.Healthy;
        }

        public static bool NeedsAutomaticNotification(
            IEnumerable<StyleIntegrityResult> results)
        {
            if (results == null)
            {
                return false;
            }
            foreach (StyleIntegrityResult result in results)
            {
                if (result == null
                    || result.State == StyleIntegrityState.Affected
                    || result.State == StyleIntegrityState.Indeterminate)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns whether an affected schedule may offer the explicit
        /// current-table fallback after its workbook read failed.</summary>
        public static bool ShouldOfferCurrentTableFallback(
            StyleIntegrityState? priorState,
            bool workbookReadable)
        {
            return priorState.HasValue
                && priorState.Value == StyleIntegrityState.Affected
                && !workbookReadable;
        }

        /// <summary>
        /// The document-open warning only needs to know whether an affected
        /// schedule is present and whether the document itself is read-only.
        /// File/read-detached policy is handled by the repair coordinator.
        /// </summary>
        public static bool ShouldOfferReviewRepairsNow(
            bool anyAffected,
            bool isReadOnly)
        {
            return anyAffected && !isReadOnly;
        }

        public enum BulkRepairChoice
        {
            ReloadFromWorkbook,
            RebuildFromCurrentTable,
            Decline
        }

        public enum BulkRepairOutcome
        {
            RepairedFromWorkbook,
            RebuiltFromCurrentTable,
            Failed,
            Declined
        }

        /// <summary>
        /// Classifies one explicitly reviewed affected schedule. A single
        /// review choice can authorize at most this one result; callers
        /// must invoke it again for the next schedule.
        /// </summary>
        public static BulkRepairOutcome ClassifyBulkRepair(
            BulkRepairChoice choice,
            StyleIntegrityResult before,
            bool mutationCommitted,
            StyleIntegrityResult after)
        {
            if (choice == BulkRepairChoice.Decline)
            {
                return BulkRepairOutcome.Declined;
            }
            if (after == null || after.State != StyleIntegrityState.Healthy
                || !mutationCommitted)
            {
                return BulkRepairOutcome.Failed;
            }
            if (choice == BulkRepairChoice.RebuildFromCurrentTable)
            {
                return BulkRepairOutcome.RebuiltFromCurrentTable;
            }
            return CountsAsWorkbookRepair(before, true, after)
                ? BulkRepairOutcome.RepairedFromWorkbook
                : BulkRepairOutcome.Failed;
        }

        public static void ApplyBulkRepairOutcome(
            StyleIntegrityBulkSummary summary, BulkRepairOutcome outcome)
        {
            if (summary == null)
            {
                return;
            }
            switch (outcome)
            {
                case BulkRepairOutcome.RepairedFromWorkbook:
                    summary.RepairedFromWorkbook++;
                    break;
                case BulkRepairOutcome.RebuiltFromCurrentTable:
                    summary.RebuiltFromCurrentTable++;
                    break;
                case BulkRepairOutcome.Declined:
                    summary.RepairDeclined++;
                    break;
                default:
                    summary.RepairFailed++;
                    break;
            }
        }

        public enum ExplicitRepairDocumentDecision
        {
            Allow,
            RefuseReadOnly,
            RefuseReadOnlyFile,
            RequireDetachedConfirmation,
            RefuseWorksharedNotEditable
        }

        /// <summary>
        /// Pure policy for the explicitly consented Check-and-repair and
        /// current-table fallback flows. Ordinary reload deliberately does not
        /// use this policy as a new preflight gate.
        /// </summary>
        public static ExplicitRepairDocumentDecision ForExplicitRepairDocument(
            bool isReadOnly,
            bool isReadOnlyFile,
            bool isDetached,
            bool isWorkshared,
            bool worksharedNotEditable)
        {
            if (isReadOnly)
            {
                return ExplicitRepairDocumentDecision.RefuseReadOnly;
            }
            // Detached is checked before the read-only-file flag: Revit reports a
            // document detached from its central as opened from a read-only file
            // even when the central file is writable, and a
            // detached copy is exactly the editable document the read-only-file
            // advice would ask the user to create.
            if (isDetached)
            {
                return ExplicitRepairDocumentDecision.RequireDetachedConfirmation;
            }
            if (isReadOnlyFile)
            {
                return ExplicitRepairDocumentDecision.RefuseReadOnlyFile;
            }
            if (isWorkshared && worksharedNotEditable)
            {
                return ExplicitRepairDocumentDecision.RefuseWorksharedNotEditable;
            }
            return ExplicitRepairDocumentDecision.Allow;
        }
    }

    /// <summary>
    /// Pure summary of the results that drive the automatic integrity warning.
    /// </summary>
    public sealed class ScheduleIntegrityResultSummary
    {
        private ScheduleIntegrityResultSummary(
            IReadOnlyList<long> affectedScheduleIds,
            bool hasIndeterminate,
            bool hasAffected)
        {
            AffectedScheduleIds = affectedScheduleIds;
            HasIndeterminate = hasIndeterminate;
            HasAffected = hasAffected;
        }

        public IReadOnlyList<long> AffectedScheduleIds { get; private set; }
        public bool HasIndeterminate { get; private set; }
        public bool HasAffected { get; private set; }

        public static ScheduleIntegrityResultSummary From(
            IDictionary<long, StyleIntegrityResult> results)
        {
            List<long> affectedScheduleIds = new List<long>();
            bool hasIndeterminate = false;
            bool hasAffected = false;
            if (results != null)
            {
                foreach (KeyValuePair<long, StyleIntegrityResult> pair in results)
                {
                    StyleIntegrityResult result = pair.Value;
                    if (result == null
                        || result.State == StyleIntegrityState.Indeterminate)
                    {
                        hasIndeterminate = true;
                    }
                    else if (result.State == StyleIntegrityState.Affected)
                    {
                        hasAffected = true;
                        affectedScheduleIds.Add(pair.Key);
                    }
                }
            }

            return new ScheduleIntegrityResultSummary(
                affectedScheduleIds.AsReadOnly(), hasIndeterminate, hasAffected);
        }
    }

    /// <summary>
    /// Owns the retry and automatic-notification state for one document-open
    /// session. It contains no Revit or UI dependencies.
    /// </summary>
    public sealed class AutomaticIntegrityNotificationState
    {
        private bool _notificationPending;
        private int _notificationAttempts;
        private bool _notificationInProgress;
        private bool _notificationShown;
        private bool _retryPending;
        private bool _retryAttempted;
        private bool _readOnlyAtOpen;

        public bool HasPendingWork
        {
            get { return _retryPending || _notificationPending; }
        }

        public bool NotificationPending
        {
            get { return _notificationPending; }
        }

        public int NotificationAttempts
        {
            get { return _notificationAttempts; }
        }

        public bool NotificationInProgress
        {
            get { return _notificationInProgress; }
        }

        public bool NotificationShown
        {
            get { return _notificationShown; }
        }

        public bool RetryPending
        {
            get { return _retryPending; }
        }

        public bool RetryAttempted
        {
            get { return _retryAttempted; }
        }

        public bool ReadOnlyAtOpen
        {
            get { return _readOnlyAtOpen; }
        }

        public void ScheduleRetry()
        {
            _retryPending = true;
        }

        public bool TryBeginRetry()
        {
            if (!_retryPending || _retryAttempted)
            {
                return false;
            }
            _retryAttempted = true;
            return true;
        }

        public void CompleteRetry()
        {
            _retryPending = false;
        }

        public void FailRetry()
        {
            _retryPending = false;
        }

        public void Queue(bool needsNotification)
        {
            _notificationPending = needsNotification;
        }

        public bool TryBeginNotification()
        {
            if (_notificationInProgress)
            {
                return false;
            }
            if (_notificationShown)
            {
                _notificationPending = false;
                return false;
            }
            if (!_notificationPending)
            {
                return false;
            }
            if (!CanAttemptNotification())
            {
                _notificationPending = false;
                return false;
            }

            _notificationAttempts++;
            _notificationInProgress = true;
            return true;
        }

        public void CompleteNotificationShown()
        {
            _notificationShown = true;
            _notificationPending = false;
            _notificationAttempts = 0;
        }

        public void NotificationAttemptFailed()
        {
            _notificationInProgress = false;
            if (!CanAttemptNotification())
            {
                _notificationPending = false;
            }
        }

        public void EndNotificationAttempt()
        {
            _notificationInProgress = false;
        }

        public void MarkReadOnlyAtOpen()
        {
            _readOnlyAtOpen = true;
        }

        private bool CanAttemptNotification()
        {
            return _notificationPending
                && !_notificationShown
                && _notificationAttempts >= 0
                && _notificationAttempts
                    < ScheduleIntegrityFlowDecisions.AutomaticNotificationMaxAttempts;
        }
    }

    /// <summary>Counts used by the final Check-and-repair message.</summary>
    public sealed class StyleIntegrityBulkSummary
    {
        public int Safe;
        public int Unsafe;
        public int NotChecked;
        public int RepairedFromWorkbook;
        public int RebuiltFromCurrentTable;
        public int RepairFailed;
        public int RepairDeclined;

        public string Format()
        {
            return "Schedule style check finished.\n\n"
                + "Safe: " + Safe + "\n"
                + "Unsafe: " + Unsafe + "\n"
                + "Not checked: " + NotChecked + "\n"
                + "Repaired from workbook: " + RepairedFromWorkbook + "\n"
                + "Rebuilt from current table: " + RebuiltFromCurrentTable + "\n"
                + "Repair failed: " + RepairFailed + "\n"
                + "Repair declined: " + RepairDeclined;
        }
    }

    /// <summary>Stable Link Manager display strings for the three scan states.</summary>
    public static class StyleIntegrityDisplay
    {
        public const string Safe = "Safe";
        public const string Unsafe = "Unsafe - reload required";
        public const string NotChecked = "Not checked - do not open";

        public static string For(StyleIntegrityState state)
        {
            switch (state)
            {
                case StyleIntegrityState.Healthy:
                    return Safe;
                case StyleIntegrityState.Affected:
                    return Unsafe;
                default:
                    return NotChecked;
            }
        }
    }
}
