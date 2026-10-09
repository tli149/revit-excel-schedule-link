using ExcelScheduleLink.Contracts;
using Xunit;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    public sealed class ScheduleIntegrityFlowTests
    {
        [Fact]
        public void AT95_ActivationDecision_OnlyHealthyMayActivate()
        {
            Assert.Equal(
                ScheduleActivationDecision.Activate,
                ScheduleIntegrityFlowDecisions.ForActivation(Result(StyleIntegrityState.Healthy)));
            Assert.Equal(
                ScheduleActivationDecision.OfferWorkbookRepair,
                ScheduleIntegrityFlowDecisions.ForActivation(Result(StyleIntegrityState.Affected)));
            Assert.Equal(
                ScheduleActivationDecision.RefuseUnverified,
                ScheduleIntegrityFlowDecisions.ForActivation(Result(StyleIntegrityState.Indeterminate)));
            Assert.Equal(
                ScheduleActivationDecision.RefuseUnverified,
                ScheduleIntegrityFlowDecisions.ForActivation(null));
        }

        [Fact]
        public void AT89_WorkbookRepairCount_RequiresKnownAffectedAndHealthyPostScan()
        {
            StyleIntegrityResult affected = Result(StyleIntegrityState.Affected);
            StyleIntegrityResult healthy = Result(StyleIntegrityState.Healthy);

            Assert.True(ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                affected, true, healthy));
            Assert.False(ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                affected, false, healthy));
            Assert.False(ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                affected, true, Result(StyleIntegrityState.Indeterminate)));
            Assert.False(ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                Result(StyleIntegrityState.Healthy), true, healthy));
        }

        [Fact]
        public void AT89_WorkbookRepairCount_CoversEveryStateAndNullCombination()
        {
            StyleIntegrityState?[] states =
            {
                null,
                StyleIntegrityState.Healthy,
                StyleIntegrityState.Affected,
                StyleIntegrityState.Indeterminate
            };

            foreach (StyleIntegrityState? beforeState in states)
            {
                foreach (StyleIntegrityState? afterState in states)
                {
                    foreach (bool committed in new[] { false, true })
                    {
                        StyleIntegrityResult before = beforeState.HasValue
                            ? Result(beforeState.Value) : null;
                        StyleIntegrityResult after = afterState.HasValue
                            ? Result(afterState.Value) : null;
                        bool expected = beforeState == StyleIntegrityState.Affected
                            && committed
                            && afterState == StyleIntegrityState.Healthy;

                        Assert.Equal(expected,
                            ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                                before, committed, after));
                    }
                }
            }
        }

        [Fact]
        public void AT96_NotificationBookkeeping_IsOncePerDocumentAndClearable()
        {
            Assert.False(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Healthy) }));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Affected) }));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Indeterminate) }));

            OncePerDocumentNotificationBookkeeping book =
                new OncePerDocumentNotificationBookkeeping();

            Assert.True(book.TryMarkShown("document-a"));
            Assert.False(book.TryMarkShown("document-a"));
            Assert.True(book.HasBeenShown("document-a"));
            Assert.True(book.TryMarkShown("document-b"));

            book.Clear("document-a");
            Assert.False(book.HasBeenShown("document-a"));
            Assert.True(book.TryMarkShown("document-a"));
        }

        [Fact]
        public void AT97_ReloadFailureOwnershipAdvisory_RequiresTerminalObservation()
        {
            Assert.True(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterReloadFailure(
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.NotStarted));
            Assert.True(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterReloadFailure(
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.RolledBack));
            Assert.False(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterReloadFailure(
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.Pending));
            Assert.False(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterReloadFailure(
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.Committed));
            Assert.False(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterReloadFailure(
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.Unreadable));
        }

        [Fact]
        public void AT89_AutomaticNotification_RetriesWithoutConsumingShownToken()
        {
            Assert.True(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, false, 0));
            Assert.True(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, false, 2));
            Assert.False(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, false, 3));
            Assert.False(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, true, 0));
            Assert.False(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                false, false, 0));

            OncePerDocumentNotificationBookkeeping book =
                new OncePerDocumentNotificationBookkeeping();
            // A failed construction/show does not call TryMarkShown, so a
            // later Idling attempt remains eligible.
            Assert.False(book.HasBeenShown("document-a"));
            Assert.True(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, book.HasBeenShown("document-a"), 1));

            // Only the successful-show path consumes the once-per-document key.
            Assert.True(book.TryMarkShown("document-a"));
            Assert.False(ScheduleIntegrityFlowDecisions.CanAttemptAutomaticNotification(
                true, book.HasBeenShown("document-a"), 0));
        }

        [Fact]
        public void AT89_AutomaticNotification_CoversNullEmptyAndMixedSequences()
        {
            Assert.False(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(null));
            Assert.False(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new StyleIntegrityResult[0]));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new StyleIntegrityResult[] { null }));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Healthy), null }));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Healthy), Result(StyleIntegrityState.Affected) }));
            Assert.True(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Healthy), Result(StyleIntegrityState.Indeterminate) }));
            Assert.False(ScheduleIntegrityFlowDecisions.NeedsAutomaticNotification(
                new[] { Result(StyleIntegrityState.Healthy), Result(StyleIntegrityState.Healthy) }));
        }

        [Fact]
        public void AT104_DocumentOpenWarning_OffersReviewOnlyForAffectedEditableDocuments()
        {
            Assert.True(
                ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(true, false));
            Assert.False(
                ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(true, true));

            Assert.False(
                ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(false, false));
            Assert.False(
                ScheduleIntegrityFlowDecisions.ShouldOfferReviewRepairsNow(false, true));
        }

        [Fact]
        public void AT89_NotificationBookkeeping_RejectsNullAndEmptyKeys()
        {
            OncePerDocumentNotificationBookkeeping book =
                new OncePerDocumentNotificationBookkeeping();

            Assert.False(book.TryMarkShown(null));
            Assert.False(book.TryMarkShown(string.Empty));
            Assert.False(book.HasBeenShown(null));
            Assert.False(book.HasBeenShown(string.Empty));
            book.Clear(null);
            book.Clear(string.Empty);
            Assert.True(book.TryMarkShown("document-a"));
            book.Clear(null);
            Assert.True(book.HasBeenShown("document-a"));
        }

        [Fact]
        public void AT89_StyleIntegrityDisplay_MapsEveryState()
        {
            Assert.Equal(StyleIntegrityDisplay.Safe,
                StyleIntegrityDisplay.For(StyleIntegrityState.Healthy));
            Assert.Equal(StyleIntegrityDisplay.Unsafe,
                StyleIntegrityDisplay.For(StyleIntegrityState.Affected));
            Assert.Equal(StyleIntegrityDisplay.NotChecked,
                StyleIntegrityDisplay.For(StyleIntegrityState.Indeterminate));
        }

        [Fact]
        public void AT89_TargetedFallback_UsesAffectedHintAndUnreadableWorkbookOnly()
        {
            Assert.True(ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                StyleIntegrityState.Affected, false));
            Assert.False(ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                StyleIntegrityState.Affected, true));
            Assert.False(ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                StyleIntegrityState.Healthy, false));
            Assert.False(ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                StyleIntegrityState.Indeterminate, false));
            Assert.False(ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                null, false));
        }

        [Fact]
        public void AT89_BulkRepair_SeparatesDeclinedFailedAndRepairedCounters()
        {
            StyleIntegrityResult affected = Result(StyleIntegrityState.Affected);
            StyleIntegrityResult healthy = Result(StyleIntegrityState.Healthy);
            StyleIntegrityBulkSummary summary = new StyleIntegrityBulkSummary();

            ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(summary,
                ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                    ScheduleIntegrityFlowDecisions.BulkRepairChoice.Decline,
                    affected, false, null));
            ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(summary,
                ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                    ScheduleIntegrityFlowDecisions.BulkRepairChoice.ReloadFromWorkbook,
                    affected, false, healthy));
            ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(summary,
                ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                    ScheduleIntegrityFlowDecisions.BulkRepairChoice.ReloadFromWorkbook,
                    affected, true, healthy));
            ScheduleIntegrityFlowDecisions.ApplyBulkRepairOutcome(summary,
                ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                    ScheduleIntegrityFlowDecisions.BulkRepairChoice.RebuildFromCurrentTable,
                    affected, true, healthy));

            Assert.Equal(1, summary.RepairDeclined);
            Assert.Equal(1, summary.RepairFailed);
            Assert.Equal(1, summary.RepairedFromWorkbook);
            Assert.Equal(1, summary.RebuiltFromCurrentTable);
        }

        [Fact]
        public void AT89_BulkRepairChoice_CoversPerLinkOutcomeMatrix()
        {
            ScheduleIntegrityFlowDecisions.BulkRepairChoice[] choices =
            {
                ScheduleIntegrityFlowDecisions.BulkRepairChoice.Decline,
                ScheduleIntegrityFlowDecisions.BulkRepairChoice.ReloadFromWorkbook,
                ScheduleIntegrityFlowDecisions.BulkRepairChoice.RebuildFromCurrentTable
            };
            StyleIntegrityState?[] beforeStates =
            {
                null, StyleIntegrityState.Healthy, StyleIntegrityState.Affected
            };
            StyleIntegrityState?[] afterStates =
            {
                null, StyleIntegrityState.Healthy, StyleIntegrityState.Affected
            };

            foreach (ScheduleIntegrityFlowDecisions.BulkRepairChoice choice in choices)
            {
                foreach (StyleIntegrityState? beforeState in beforeStates)
                {
                    foreach (StyleIntegrityState? afterState in afterStates)
                    {
                        foreach (bool committed in new[] { false, true })
                        {
                            ScheduleIntegrityFlowDecisions.BulkRepairOutcome expected;
                            if (choice == ScheduleIntegrityFlowDecisions.BulkRepairChoice.Decline)
                            {
                                expected = ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Declined;
                            }
                            else if (!committed
                                || afterState != StyleIntegrityState.Healthy)
                            {
                                expected = ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed;
                            }
                            else if (choice == ScheduleIntegrityFlowDecisions.BulkRepairChoice.RebuildFromCurrentTable)
                            {
                                expected = ScheduleIntegrityFlowDecisions.BulkRepairOutcome.RebuiltFromCurrentTable;
                            }
                            else
                            {
                                expected = beforeState == StyleIntegrityState.Affected
                                    ? ScheduleIntegrityFlowDecisions.BulkRepairOutcome.RepairedFromWorkbook
                                    : ScheduleIntegrityFlowDecisions.BulkRepairOutcome.Failed;
                            }

                            StyleIntegrityResult before = beforeState.HasValue
                                ? Result(beforeState.Value) : null;
                            StyleIntegrityResult after = afterState.HasValue
                                ? Result(afterState.Value) : null;
                            Assert.Equal(expected,
                                ScheduleIntegrityFlowDecisions.ClassifyBulkRepair(
                                    choice, before, committed, after));
                        }
                    }
                }
            }
        }

        [Fact]
        public void AT96_ExplicitRepairDocumentPolicy_ProtectsDocumentBoundaries()
        {
            foreach (bool isReadOnly in new[] { false, true })
            {
                foreach (bool isReadOnlyFile in new[] { false, true })
                {
                    foreach (bool isDetached in new[] { false, true })
                    {
                        foreach (bool isWorkshared in new[] { false, true })
                        {
                            foreach (bool notEditable in new[] { false, true })
                            {
                                ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision expected;
                                if (isReadOnly)
                                {
                                    expected = ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnly;
                                }
                                else if (isDetached)
                                {
                                    expected = ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RequireDetachedConfirmation;
                                }
                                else if (isReadOnlyFile)
                                {
                                    expected = ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnlyFile;
                                }
                                else if (isWorkshared && notEditable)
                                {
                                    expected = ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseWorksharedNotEditable;
                                }
                                else
                                {
                                    expected = ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.Allow;
                                }

                                Assert.Equal(expected,
                                    ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                                        isReadOnly, isReadOnlyFile, isDetached,
                                        isWorkshared, notEditable));
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void AT97_ExplicitRepairDocumentPolicy_UsesFailClosedPrecedence()
        {
            Assert.Equal(
                ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnly,
                ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                    true, true, true, true, true));
            // Revit reports a detached document as opened from a read-only file
            // (journal.0240); detached therefore wins over that flag.
            Assert.Equal(
                ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RequireDetachedConfirmation,
                ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                    false, true, true, true, true));
            Assert.Equal(
                ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RefuseReadOnlyFile,
                ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                    false, true, false, false, false));
            Assert.Equal(
                ScheduleIntegrityFlowDecisions.ExplicitRepairDocumentDecision.RequireDetachedConfirmation,
                ScheduleIntegrityFlowDecisions.ForExplicitRepairDocument(
                    false, false, true, true, true));
        }

        [Fact]
        public void AT96_DetachedWorksharedDocument_NeverUsesCentralOwnershipAdvisory()
        {
            Assert.False(ScheduleIntegrityFlowDecisions.MayUseWorksharingOwnershipAdvisory(
                true, true));
            Assert.True(ScheduleIntegrityFlowDecisions.MayUseWorksharingOwnershipAdvisory(
                false, true));
            Assert.False(ScheduleIntegrityFlowDecisions.MayUseWorksharingOwnershipAdvisory(
                true, false));
        }

        [Fact]
        public void AT96_WorksharingAdvisoryDecision_CoversWorksharedDetachedAndShown()
        {
            foreach (bool isWorkshared in new[] { false, true })
            {
                foreach (bool isDetached in new[] { false, true })
                {
                    foreach (bool alreadyShown in new[] { false, true })
                    {
                        bool expected = isWorkshared && !isDetached && !alreadyShown;
                        Assert.Equal(expected,
                            ScheduleIntegrityFlowDecisions.ShouldShowWorksharingAdvisory(
                                isWorkshared, isDetached, alreadyShown));
                    }
                }
            }
        }

        [Fact]
        public void AT105_RegenerationFailure_ForbidsOwnershipFollowUp()
        {
            Assert.False(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterMutationFailure(
                true,
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.NotStarted));
            Assert.False(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterMutationFailure(
                true,
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.RolledBack));
            Assert.True(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterMutationFailure(
                false,
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.NotStarted));
            Assert.True(ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterMutationFailure(
                false,
                ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation.RolledBack));
        }

        [Fact]
        public void AT89_TargetedReloadRequiresHealthyFinalRescanAfterAnyMutation()
        {
            StyleIntegrityResult healthy = Result(StyleIntegrityState.Healthy);
            StyleIntegrityResult affected = Result(StyleIntegrityState.Affected);
            StyleIntegrityResult indeterminate = Result(StyleIntegrityState.Indeterminate);

            Assert.True(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                true, false, healthy));
            Assert.False(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                true, false, affected));
            Assert.False(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                true, false, indeterminate));
            Assert.True(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                false, true, healthy));
            Assert.False(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                false, true, null));
            Assert.False(ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                false, false, healthy));
        }

        [Fact]
        public void AT89_BulkSummary_UsesExactStateBasedLabels()
        {
            StyleIntegrityBulkSummary summary = new StyleIntegrityBulkSummary
            {
                Safe = 2,
                Unsafe = 1,
                NotChecked = 3,
                RepairedFromWorkbook = 1,
                RebuiltFromCurrentTable = 0,
                RepairFailed = 2,
                RepairDeclined = 1
            };

            Assert.Equal(
                "Schedule style check finished.\n\n"
                + "Safe: 2\n"
                + "Unsafe: 1\n"
                + "Not checked: 3\n"
                + "Repaired from workbook: 1\n"
                + "Rebuilt from current table: 0\n"
                + "Repair failed: 2\n"
                + "Repair declined: 1",
                summary.Format());
        }

        private static StyleIntegrityResult Result(StyleIntegrityState state)
        {
            return new StyleIntegrityResult { State = state };
        }
    }
}
