using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal sealed class CheckAndRepairOutcome
    {
        public StyleIntegrityBulkSummary Summary = new StyleIntegrityBulkSummary();
        public readonly List<string> Details = new List<string>();
        public bool FatalRegenerationFailure;
    }

    /// <summary>
    /// Owns explicit repair consent, worksharing editability advisories, and
    /// post-repair rescans. Ordinary Reload remains in ReloadService and does
    /// not pass through this coordinator's style-repair prompt.
    /// </summary>
    internal static class ScheduleRepairCoordinator
    {
        public static RepairAttemptOutcome TryFallback(
            Document doc, ViewSchedule schedule, string unavailableReason)
        {
            if (!CanStartExplicitRepair(doc, schedule, out string refusal))
            {
                TaskDialog refusalDialog = new TaskDialog(UserMessages.DialogTitle);
                refusalDialog.MainInstruction = refusal;
                AddReason(refusalDialog, unavailableReason);
                refusalDialog.Show();
                return Failed(refusal);
            }

            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = UserMessages.FallbackInstruction;
            dialog.MainContent = UserMessages.FallbackContent
                + (doc.IsDetached
                    ? "\n\n" + UserMessages.DetachedRepairSentence
                    : string.Empty);
            AddReason(dialog, unavailableReason);
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UserMessages.FallbackRebuild,
                UserMessages.FallbackRebuildHint);
            dialog.DefaultButton = TaskDialogResult.Cancel;
            if (dialog.Show() != TaskDialogResult.CommandLink1)
            {
                return Declined();
            }

            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage != null && !storage.IsV2
                && !ReloadDialogs.ConfirmLegacyMigration(doc, schedule.Name))
            {
                return Declined();
            }

            RepairAttemptOutcome repair =
                ScheduleRepairService.RebuildFromCurrentTable(doc, schedule);
            if (repair == null || !repair.Success)
            {
                RepairAttemptOutcome failed = repair
                    ?? Failed(UserMessages.FallbackSnapshotFailedInstruction);
                TaskDialog.Show(UserMessages.DialogTitle,
                    string.IsNullOrEmpty(failed.Detail)
                        ? UserMessages.FallbackSnapshotFailedInstruction
                        : failed.Detail);
                return failed;
            }

            StyleIntegrityResult after =
                ScheduleIntegritySessionCache.Rescan(doc, schedule);
            if (after == null || after.State != StyleIntegrityState.Healthy)
            {
                repair.Success = false;
                repair.Detail = UserMessages.FallbackPostRepairFailed;
                TaskDialog.Show(UserMessages.DialogTitle, repair.Detail);
                return repair;
            }
            return repair;
        }

        public static CheckAndRepairOutcome CheckAndRepair(Document doc)
        {
            return CheckAndRepair(doc, null, null);
        }

        /// <summary>
        /// Starts the same per-schedule review phase used by Link Manager,
        /// using an already completed automatic document-open scan. The
        /// caller owns the preceding warning; this method therefore skips the
        /// fresh-summary and bulk-choice dialogs and goes straight to the
        /// existing one-at-a-time review.
        /// </summary>
        public static CheckAndRepairOutcome ReviewAffectedSchedules(
            Document doc,
            IList<ViewSchedule> affected,
            IDictionary<long, StyleIntegrityResult> before)
        {
            return CheckAndRepair(doc, affected, before);
        }

        private static CheckAndRepairOutcome CheckAndRepair(
            Document doc,
            IList<ViewSchedule> suppliedAffected,
            IDictionary<long, StyleIntegrityResult> suppliedResults)
        {
            CheckAndRepairOutcome outcome = new CheckAndRepairOutcome();
            bool suppliedReviewSet = suppliedAffected != null;
            IDictionary<long, StyleIntegrityResult> before = suppliedResults
                ?? ScheduleIntegritySessionCache.ScanManual(doc);
            IList<ViewSchedule> schedules = suppliedReviewSet
                ? new List<ViewSchedule>()
                : LinkedScheduleFinder.FindAll(doc);
            List<ViewSchedule> affected = suppliedReviewSet
                ? new List<ViewSchedule>(suppliedAffected)
                : new List<ViewSchedule>();
            StyleIntegrityBulkSummary freshSummary = new StyleIntegrityBulkSummary();
            foreach (ViewSchedule schedule in schedules)
            {
                StyleIntegrityResult result;
                if (!before.TryGetValue(schedule.Id.Value, out result)
                    || result == null || result.State == StyleIntegrityState.Indeterminate)
                {
                    freshSummary.NotChecked++;
                }
                else if (result.State == StyleIntegrityState.Affected)
                {
                    affected.Add(schedule);
                    freshSummary.Unsafe++;
                }
                else
                {
                    freshSummary.Safe++;
                }
            }

            bool reviewRepairs = suppliedReviewSet;
            if (!suppliedReviewSet)
            {
                // The fresh state summary is shown before any repair decision.
                // Its repair counters are deliberately zero because no mutation
                // has happened yet. When there is something to repair, the
                // summary is part of the repair question itself; a separate
                // "check finished" dialog would interrupt the choice and make
                // the repair flow harder to complete.
                if (affected.Count == 0)
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.CheckAndRepairSummary(freshSummary, null));
                }
                else
                {
                    TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
                    dialog.MainInstruction = UserMessages.BulkRepairInstruction(affected.Count);
                    dialog.MainContent = UserMessages.BulkRepairContent(affected.Count)
                        + "\n\n" + UserMessages.CheckAndRepairSummary(freshSummary, null);
                    dialog.AllowCancellation = true;
                    dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                    dialog.AddCommandLink(
                        TaskDialogCommandLinkId.CommandLink1,
                        UserMessages.ReviewRepairsOneAtATime,
                        UserMessages.ReviewRepairsOneAtATimeHint);
                    dialog.AddCommandLink(
                        TaskDialogCommandLinkId.CommandLink2,
                        UserMessages.BulkRepairClose,
                        string.Empty);
                    dialog.DefaultButton = TaskDialogResult.CommandLink2;
                    reviewRepairs = dialog.Show() == TaskDialogResult.CommandLink1;
                }
            }

            bool stopReview = !reviewRepairs && affected.Count > 0;
            foreach (ViewSchedule schedule in affected)
            {
                StyleIntegrityResult prior = null;
                if (before != null && schedule != null && schedule.Id != null)
                {
                    before.TryGetValue(schedule.Id.Value, out prior);
                }
                prior = prior ?? ScheduleIntegritySessionCache.GetResult(
                    doc, schedule.Id);
                if (stopReview)
                {
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary,
                        ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Declined);
                    outcome.Details.Add(UserMessages.RepairDeclined(schedule.Name));
                    continue;
                }

                if (!ReviewOneSchedule(doc, schedule))
                {
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary,
                        ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Declined);
                    outcome.Details.Add(UserMessages.RepairDeclined(schedule.Name));
                    continue;
                }

                if (!CanStartExplicitRepair(doc, schedule, out string refusal))
                {
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary,
                        ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed);
                    outcome.Details.Add(UserMessages.RepairFailed(schedule.Name));
                    TaskDialog.Show(UserMessages.DialogTitle, refusal);
                    continue;
                }

                TargetedReloadFlowResult flow = TargetedReloadFlow.Run(
                    doc, schedule, ReloadScope.SingleLink, prior);
                ReloadOutcome reload = flow.Reload;
                if (flow.FatalRegeneration)
                {
                    outcome.FatalRegenerationFailure = true;
                    if (reload.IsFatalRegenerationFailure)
                    {
                        TaskDialog.Show(UserMessages.DialogTitle, reload.Detail);
                    }
                    return outcome;
                }
                if (reload.IsSuccess)
                {
                    StyleIntegrityResult after = flow.After;
                    ScheduleIntegrityFlowDecisions.BulkRepairOutcome repairOutcome =
                        ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                            ScheduleIntegrityFlowDecisions.BulkRepairChoice.ReloadFromWorkbook,
                            prior, true, after);
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary, repairOutcome);
                    if (repairOutcome == ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed)
                    {
                        outcome.Details.Add(
                            UserMessages.RepairFailedPostRepairScan(schedule.Name));
                    }
                    if (flow.FinalHealthy)
                    {
                        string details = UserMessages.AppendRenderNotes(
                            UserMessages.ReloadColumnCountNote(reload),
                            0, 0, false, false, 0,
                            reload.RowHeightsRejected, reload.ScheduleName,
                            rowsMayHideText: reload.RowsMayHideText);
                        if (!string.IsNullOrEmpty(details))
                        {
                            outcome.Details.Add(details);
                        }
                    }
                }
                else if (reload.IsWorkbookUnavailable)
                {
                    RepairAttemptOutcome fallback =
                        flow.FallbackOffered
                        ? flow.Fallback
                        : Failed(reload.Detail);
                    StyleIntegrityResult after = flow.After;
                    if (fallback != null && fallback.Success
                        && after != null && after.State == StyleIntegrityState.Healthy)
                    {
                        ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                            outcome.Summary,
                            ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                                ScheduleIntegrityFlowDecisions.BulkRepairChoice.RebuildFromCurrentTable,
                                prior, true, after));
                    }
                    else if (fallback != null && fallback.ConsentDeclined)
                    {
                        ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                            outcome.Summary,
                            ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Declined);
                        outcome.Details.Add(UserMessages.RepairDeclined(schedule.Name));
                    }
                    else
                    {
                        ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                            outcome.Summary,
                            ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed);
                        outcome.Details.Add(UserMessages.RepairFailed(schedule.Name));
                    }
                }
                else if (reload.Kind == ReloadResultKind.Cancelled
                    || reload.Kind == ReloadResultKind.ConfirmationRequired)
                {
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary,
                        ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Declined);
                    outcome.Details.Add(UserMessages.RepairDeclined(schedule.Name));
                }
                else
                {
                    ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(
                        outcome.Summary,
                        ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed);
                    outcome.Details.Add(UserMessages.RepairFailed(schedule.Name));
                    if (!string.IsNullOrEmpty(reload.Detail))
                    {
                        outcome.Details.Add(reload.Detail);
                    }
                }
            }

            IDictionary<long, StyleIntegrityResult> finalResults =
                ScheduleIntegritySessionCache.ScanManual(doc);
            foreach (ViewSchedule schedule in LinkedScheduleFinder.FindAll(doc))
            {
                StyleIntegrityResult result;
                if (!finalResults.TryGetValue(schedule.Id.Value, out result)
                    || result == null || result.State == StyleIntegrityState.Indeterminate)
                {
                    outcome.Summary.NotChecked++;
                    outcome.Details.Add(UserMessages.RepairNotChecked(schedule.Name));
                }
                else if (result.State == StyleIntegrityState.Affected)
                {
                    outcome.Summary.Unsafe++;
                    outcome.Details.Add(UserMessages.RepairUnsafe(schedule.Name));
                }
                else
                {
                    outcome.Summary.Safe++;
                }
            }
            return outcome;
        }

        private static bool ReviewOneSchedule(Document doc, ViewSchedule schedule)
        {
            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = UserMessages.ReviewRepairInstruction(schedule.Name);
            dialog.MainContent = UserMessages.ReviewRepairContent(
                schedule.Name, doc != null && doc.IsDetached);
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UserMessages.ReviewRepairReload,
                UserMessages.ActivationReloadFromWorkbookHint);
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                UserMessages.ReviewRepairSkip,
                string.Empty);
            dialog.DefaultButton = TaskDialogResult.CommandLink2;
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static bool CanStartExplicitRepair(
            Document doc, ViewSchedule schedule, out string refusal)
        {
            refusal = string.Empty;
            if (doc == null || schedule == null || !schedule.IsValidObject)
            {
                refusal = UserMessages.FallbackSnapshotFailedInstruction;
                return false;
            }
            bool ownedByOtherUser = false;
            string owner = string.Empty;
            if (ScheduleIntegrityFlowDecisions.MayUseWorksharingOwnershipAdvisory(
                doc.IsDetached, doc.IsWorkshared))
            {
                try
                {
                    CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(
                        doc, schedule.Id, out owner);
                    ownedByOtherUser = status == CheckoutStatus.OwnedByOtherUser;
                }
                catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
                {
                    // The API documents this value as a local advisory. If it
                    // cannot be read, the transaction remains authoritative.
                }
            }

            ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision policy =
                ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                    doc.IsReadOnly, doc.IsReadOnlyFile, doc.IsDetached,
                    doc.IsWorkshared, ownedByOtherUser);
            switch (policy)
            {
                case ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnly:
                    refusal = UserMessages.AutomaticReadOnlyMessage;
                    return false;
                case ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnlyFile:
                    refusal = UserMessages.ReadOnlyFileRepairMessage;
                    return false;
                case ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseWorksharedNotEditable:
                    // No transaction was started, so this message may truthfully
                    // include the unchanged-model sentence.
                    refusal = UserMessages.RepairOwnershipMessage(
                        schedule.Name, owner, true);
                    return false;
            }
            return true;
        }

        private static RepairAttemptOutcome Failed(string detail)
        {
            return new RepairAttemptOutcome { Detail = detail ?? string.Empty };
        }

        private static RepairAttemptOutcome Declined()
        {
            return new RepairAttemptOutcome
            {
                Detail = "Repair was declined.",
                ConsentDeclined = true
            };
        }

        private static void AddReason(TaskDialog dialog, string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                dialog.ExpandedContent = reason;
            }
        }
    }
}
