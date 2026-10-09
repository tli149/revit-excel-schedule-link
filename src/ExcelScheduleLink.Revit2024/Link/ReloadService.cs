using System;
using System.IO;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>Outcome category of a reload attempt.</summary>
    internal enum ReloadResultKind
    {
        Success,
        NotLinked,
        SourceMissing,
        NetworkAccessCancelled,
        ReadFailed,
        ConfirmationRequired,
        Cancelled,
        RenderFailed,
        RegenerationFailed
    }

    /// <summary>Result of reloading a single linked schedule.</summary>
    internal sealed class ReloadOutcome
    {
        public ReloadResultKind Kind;
        public string ScheduleName = string.Empty;
        public bool SourceChanged;
        public SourceIdentityState Identity;
        public ReloadDecisionKind DecisionKind;
        public CreatorRelation CreatorRelation;
        public string Creator = string.Empty;
        public string ResolvedPath = string.Empty;
        public bool IsLegacy;
        public bool WidthWarning;
        public bool UserConfirmed;
        public bool RollbackVerified;
        /// <summary>
        /// Fingerprint of the exact temporary snapshot parsed for this reload.
        /// </summary>
        public SourceFingerprint CurrentSourceFingerprint;
        public int Rows;
        public int Columns;
        public bool Truncated;
        public int SourceRows;
        public int SourceColumns;
        public string Detail = string.Empty; // failure detail / message

        /// <summary>Schedule view may not show the full table width (cosmetic; sheets are fine).</summary>
        public bool ViewWidthProblem;

        /// <summary>Header columns whose width Revit refused (0 = all applied).</summary>
        public int ColumnWidthsRejected;

        /// <summary>
        /// Session-only evidence supplied by a completed scan. It is never
        /// persisted and never used as a reload gate.
        /// </summary>
        public bool KnownAffectedBeforeReload;
        public bool UnsafeStylesRepaired;

        public bool IsSuccess { get { return Kind == ReloadResultKind.Success; } }
        public bool IsFatalRegenerationFailure
        {
            get { return Kind == ReloadResultKind.RegenerationFailed; }
        }
    }

    /// <summary>
    /// Shared reload flow used by both the Reload command and the Link Manager.
    /// Reads the workbook OUTSIDE the transaction, delegates the decision dialog
    /// to ReloadDialogs, and only then starts a transaction to re-render and
    /// update the stored link info. Any render failure rolls the whole
    /// transaction back, so an existing schedule is never left in a broken state.
    /// </summary>
    internal static class ReloadService
    {
        public static ReloadOutcome Reload(
            Document doc, ViewSchedule schedule,
            ReloadScope scope = ReloadScope.SingleLink,
            PendingLinkSettings pendingSettings = null,
            StyleIntegrityResult preReloadIntegrityHint = null)
        {
            ReloadOutcome outcome = new ReloadOutcome();
            outcome.ScheduleName = schedule != null ? schedule.Name : string.Empty;
            outcome.KnownAffectedBeforeReload = preReloadIntegrityHint != null
                && preReloadIntegrityHint.State == StyleIntegrityState.Affected;

            ExcelLinkStorage storage = schedule != null
                ? ExcelLinkSchema.LoadAny(schedule) : null;
            if (storage == null || storage.Info == null)
            {
                outcome.Kind = ReloadResultKind.NotLinked;
                return outcome;
            }
            ExcelLinkInfo info = storage.Info;
            outcome.IsLegacy = !storage.IsV2;

            PathResolutionResult pathResolution = PathResolver.ResolveForAction(doc, info);
            if (pathResolution == null || !pathResolution.IsResolved)
            {
                outcome.Kind = pathResolution != null
                    && pathResolution.Status == PathResolutionStatus.Cancelled
                    ? ReloadResultKind.NetworkAccessCancelled
                    : ReloadResultKind.SourceMissing;
                outcome.Detail = PathResolver.DescribeFailure(info, pathResolution);
                return outcome;
            }
            string xlsxPath = pathResolution.ResolvedPath;

            // ---- Read into a SheetModel (no transaction yet) ----
            SheetReadResult snapshot;
            try
            {
                string range = info.RangeMode == RangeMode.Explicit ? info.RangeAddress : null;
                snapshot = new OpenXmlExcelReadService().ReadSheetSnapshot(
                    xlsxPath, info.SheetName, range);
            }
            catch (ExcelReadException ex)
            {
                outcome.Kind = ReloadResultKind.ReadFailed;
                outcome.Detail = UserMessages.ReadFailed(ex.Message);
                return outcome;
            }

            SourceFingerprint fingerprint = snapshot.Fingerprint;
            SheetModel sheet = snapshot.Sheet;
            outcome.CurrentSourceFingerprint = fingerprint;
            outcome.SourceRows = sheet.SourceRowCount;
            outcome.SourceColumns = sheet.SourceColumnCount;

            SourceIdentityState identity = storage.IsV2
                ? SourceIdentity.Compare(info.LastAcceptedSourceSha256, fingerprint.Sha256)
                : SourceIdentityState.Unknown;
            string creator;
            CreatorRelation creatorRelation = WorksharingContext.GetCreatorRelation(
                doc, schedule.Id, out creator);
            bool widthWarning = creatorRelation == CreatorRelation.Different
                || creatorRelation == CreatorRelation.Unavailable;
            ColumnWidthMode effectiveColumnMode = pendingSettings != null
                ? pendingSettings.ColumnWidthMode : info.ColumnWidthMode;
            widthWarning = widthWarning
                && effectiveColumnMode == ColumnWidthMode.ApplyExcelWidths;
            ReloadDecision decision = ReloadDecisionTable.Decide(
                identity, creatorRelation, scope, widthWarning);

            outcome.Identity = identity;
            outcome.SourceChanged = identity == SourceIdentityState.Differs;
            outcome.DecisionKind = decision.Kind;
            outcome.CreatorRelation = creatorRelation;
            outcome.Creator = creator;
            outcome.ResolvedPath = xlsxPath;
            outcome.WidthWarning = widthWarning;

            if (decision.Kind == ReloadDecisionKind.FailRead)
            {
                outcome.Kind = ReloadResultKind.ReadFailed;
                outcome.Detail = UserMessages.ReadFailed(
                    UserMessages.ReloadSourceIdentityUnavailable);
                return outcome;
            }
            if (decision.Kind == ReloadDecisionKind.SkipConfirmationRequired)
            {
                outcome.Kind = ReloadResultKind.ConfirmationRequired;
                outcome.Detail = UserMessages.ReloadSkippedConfirmationRequired(
                    outcome.ScheduleName, identity, creatorRelation);
                return outcome;
            }

            if (!ReloadDialogs.Authorize(doc, new ReloadPromptContext
            {
                Identity = identity,
                CreatorRelation = creatorRelation,
                DecisionKind = decision.Kind,
                ScheduleName = outcome.ScheduleName,
                Creator = creator,
                ResolvedPath = xlsxPath,
                IsLegacy = !storage.IsV2,
                WidthWarning = widthWarning
            }))
            {
                outcome.Kind = ReloadResultKind.Cancelled;
                outcome.Detail = UserMessages.ReloadCancelled;
                return outcome;
            }
            outcome.UserConfirmed = decision.Kind != ReloadDecisionKind.Proceed;

            // ---- One transaction: re-render + update link info; rollback on failure ----
            using (Transaction transaction = new Transaction(doc, "Reload Excel Worksheet"))
            {
                bool transactionStarted = false;
                TransactionStatus? startStatus = null;
                try
                {
                    startStatus = transaction.Start();
                    if (startStatus != TransactionStatus.Started)
                    {
                        outcome.Kind = ReloadResultKind.RenderFailed;
                        ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation
                            observation = ObserveReloadFailure(false, startStatus);
                        outcome.Detail = ScheduleIntegrityFlowDecisions
                            .MayQueryOwnershipAfterReloadFailure(observation)
                            ? UserMessages.ReloadTransactionNotStarted
                            : UserMessages.ReloadFailedRolledBack(
                                UserMessages.ReloadTransactionNotStarted, false);
                        ApplyOwnershipFailureDetail(doc, schedule, outcome, observation);
                        return outcome;
                    }
                    transactionStarted = true;

                    if (!storage.IsV2)
                    {
                        if (!ExcelLinkMigration.MigrateInOpenTransaction(schedule))
                        {
                            throw new InvalidOperationException(
                                "The legacy link could not be upgraded to schema v2.");
                        }
                        info = ExcelLinkSchema.LoadV2(schedule);
                        if (info == null)
                        {
                            throw new InvalidOperationException(
                                "Schema v2 was not available after legacy-link migration.");
                        }
                    }

                    if (pendingSettings != null)
                    {
                        info.ColumnWidthMode = pendingSettings.ColumnWidthMode;
                        info.RowHeightMode = pendingSettings.RowHeightMode;
                        info.WidthScale = pendingSettings.WidthScale;
                    }

                    HeaderTableRenderer.RenderResult render = HeaderTableRenderer.Render(
                        schedule, sheet, info.WidthScale, info.ColumnWidthMode, info.RowHeightMode);

                    // RendererVersion is a statement about this successful
                    // render, not a health cache. It is saved only after the
                    // in-transaction validation above has returned Healthy.
                    info.RendererVersion = ExcelLinkInfo.CurrentRendererVersion;

                    // Stored paths are link configuration. Resolution through a
                    // fallback candidate is not consent to relocate the link.
                    info.SourceLastWriteUtcTicks = fingerprint.LastWriteUtcTicks;
                    info.LastLoadedUtcTicks = DateTime.UtcNow.Ticks;
                    info.LastAcceptedSourceSha256 = fingerprint.Sha256;
                    info.LastAcceptedSourceSizeBytes = fingerprint.SizeBytes;
                    ExcelLinkSchema.SaveV2(schedule, info);

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        TransactionStatus? rollbackStatus =
                            TransactionRollback.RollBackIfStarted(transaction);
                        outcome.RollbackVerified = rollbackStatus
                            == TransactionStatus.RolledBack;
                        outcome.Kind = ReloadResultKind.RenderFailed;
                        outcome.Detail = UserMessages.ReloadFailedRolledBack(
                            UserMessages.ReloadTransactionNotCommitted,
                            outcome.RollbackVerified);
                        ApplyOwnershipFailureDetail(
                            doc, schedule, outcome,
                            ObserveReloadFailure(transactionStarted, rollbackStatus));
                        return outcome;
                    }

                    outcome.Kind = ReloadResultKind.Success;
                    outcome.Rows = render.RowsRendered;
                    outcome.Columns = render.ColumnsRendered;
                    outcome.Truncated = render.Truncated;
                    outcome.ViewWidthProblem = render.HasViewWidthProblem;
                    outcome.ColumnWidthsRejected = render.ColumnWidthsRejected;
                    outcome.UnsafeStylesRepaired = outcome.KnownAffectedBeforeReload;
                    return outcome;
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
                {
                    outcome.RollbackVerified = TransactionRollback.RollBackIfStarted(transaction)
                        == TransactionStatus.RolledBack;
                    outcome.Kind = ReloadResultKind.RegenerationFailed;
                    outcome.Detail = UserMessages.RegenerationFailure;
                    return outcome;
                }
                catch (RegenerationFailureProbeException)
                {
                    outcome.RollbackVerified = TransactionRollback.RollBackIfStarted(transaction)
                        == TransactionStatus.RolledBack;
                    outcome.Kind = ReloadResultKind.RegenerationFailed;
                    outcome.Detail = UserMessages.RegenerationFailure;
                    return outcome;
                }
                catch (UnsafeScheduleStyleException)
                {
                    TransactionStatus? rollbackStatus =
                        TransactionRollback.RollBackIfStarted(transaction);
                    outcome.RollbackVerified = rollbackStatus
                        == TransactionStatus.RolledBack;
                    outcome.Kind = ReloadResultKind.RenderFailed;
                    outcome.Detail = UserMessages.ReloadUnsafeStyleValidationFailed(
                        outcome.ScheduleName, outcome.RollbackVerified);
                    ApplyOwnershipFailureDetail(
                        doc, schedule, outcome,
                        ObserveReloadFailure(transactionStarted, rollbackStatus));
                    return outcome;
                }
                catch (Exception ex)
                {
                    TransactionStatus? rollbackStatus =
                        TransactionRollback.RollBackIfStarted(transaction);
                    outcome.RollbackVerified = rollbackStatus
                        == TransactionStatus.RolledBack;
                    outcome.Kind = ReloadResultKind.RenderFailed;
                    outcome.Detail = UserMessages.ReloadFailedRolledBack(
                        ex.ToString(), outcome.RollbackVerified);
                    ApplyOwnershipFailureDetail(
                        doc, schedule, outcome,
                        ObserveReloadFailure(transactionStarted, rollbackStatus));
                    return outcome;
                }
            }
        }

        private static ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation
            ObserveReloadFailure(bool transactionStarted, TransactionStatus? status)
        {
            if (!status.HasValue)
            {
                return ScheduleIntegrityFlowDecisions
                    .ReloadFailureTransactionObservation.Unreadable;
            }
            switch (status.Value)
            {
                case TransactionStatus.Pending:
                    return ScheduleIntegrityFlowDecisions
                        .ReloadFailureTransactionObservation.Pending;
                case TransactionStatus.Committed:
                    return ScheduleIntegrityFlowDecisions
                        .ReloadFailureTransactionObservation.Committed;
                case TransactionStatus.RolledBack:
                    return ScheduleIntegrityFlowDecisions
                        .ReloadFailureTransactionObservation.RolledBack;
                case TransactionStatus.Uninitialized:
                    return transactionStarted
                        ? ScheduleIntegrityFlowDecisions
                            .ReloadFailureTransactionObservation.Unreadable
                        : ScheduleIntegrityFlowDecisions
                            .ReloadFailureTransactionObservation.NotStarted;
                default:
                    return ScheduleIntegrityFlowDecisions
                        .ReloadFailureTransactionObservation.Unreadable;
            }
        }

        /// <summary>
        /// Transaction start/mutation is authoritative. Only after Revit has
        /// refused to start or a started transaction's rollback was observed do
        /// we consult the advisory checkout cache. It can replace a generic
        /// failure detail, but never becomes a preflight gate for ordinary
        /// reload.
        /// </summary>
        private static void ApplyOwnershipFailureDetail(
            Document doc,
            ViewSchedule schedule,
            ReloadOutcome outcome,
            ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation observation)
        {
            if (doc == null || schedule == null || outcome == null)
            {
                return;
            }
            try
            {
                if (!ScheduleIntegrityFlowDecisions.MayQueryOwnershipAfterMutationFailure(
                    outcome.IsFatalRegenerationFailure, observation)
                    || !ScheduleIntegrityFlowDecisions.MayUseWorksharingOwnershipAdvisory(
                        doc.IsDetached, doc.IsWorkshared))
                {
                    return;
                }
                bool nothingChanged = observation
                    == ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation
                        .NotStarted
                    || observation
                    == ScheduleIntegrityFlowDecisions.ReloadFailureTransactionObservation
                        .RolledBack;
                string owner;
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(
                    doc, schedule.Id, out owner);
                if (status == CheckoutStatus.OwnedByOtherUser)
                {
                    outcome.Detail = UserMessages.RepairOwnershipMessage(
                        outcome.ScheduleName, owner, nothingChanged);
                }
            }
            catch (Exception)
            {
                // Checkout status is a local advisory. The authoritative
                // transaction result and its rollback qualification remain the
                // truthful fallback when the advisory cannot be read.
            }
        }
    }
}
