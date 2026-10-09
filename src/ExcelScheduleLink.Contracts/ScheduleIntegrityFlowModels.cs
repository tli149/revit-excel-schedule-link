using System;
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

        public static bool CanAttemptAutomaticNotification(
            bool pending, bool alreadyShown, int failedAttempts)
        {
            return pending
                && !alreadyShown
                && failedAttempts >= 0
                && failedAttempts < AutomaticNotificationMaxAttempts;
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
            // even when the central file is writable (journal.0240), and a
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
    /// Session-only once-per-document bookkeeping. The caller supplies a
    /// document identity and is responsible for clearing it when that document
    /// closes; no document or model data is persisted here.
    /// </summary>
    public sealed class OncePerDocumentNotificationBookkeeping
    {
        private readonly HashSet<string> _shown = new HashSet<string>(
            StringComparer.Ordinal);

        public bool TryMarkShown(string documentKey)
        {
            if (string.IsNullOrEmpty(documentKey))
            {
                return false;
            }
            return _shown.Add(documentKey);
        }

        public bool HasBeenShown(string documentKey)
        {
            return !string.IsNullOrEmpty(documentKey) && _shown.Contains(documentKey);
        }

        public void Clear(string documentKey)
        {
            if (!string.IsNullOrEmpty(documentKey))
            {
                _shown.Remove(documentKey);
            }
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
