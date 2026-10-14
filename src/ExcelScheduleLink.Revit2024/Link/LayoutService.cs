using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal enum LayoutTargetKind { Resolved, None, Ambiguous }

    /// <summary>Outcome of resolving which linked schedule a Layout command acts on.</summary>
    internal sealed class LayoutTarget
    {
        public ViewSchedule Schedule;
        public LayoutTargetKind Kind;
        public string Message; // set for None / Ambiguous

        public static LayoutTarget Resolved(ViewSchedule schedule)
        {
            return new LayoutTarget { Schedule = schedule, Kind = LayoutTargetKind.Resolved };
        }
        public static LayoutTarget None(string message)
        {
            return new LayoutTarget { Kind = LayoutTargetKind.None, Message = message };
        }
        public static LayoutTarget Ambiguous(string message)
        {
            return new LayoutTarget { Kind = LayoutTargetKind.Ambiguous, Message = message };
        }
    }

    /// <summary>
    /// Failure preprocessor used ONLY by the Layout transaction (never registered
    /// globally): warnings are left to Revit's normal handling, but any unresolved
    /// error rolls the whole transaction back instead of stopping at a user prompt.
    /// This keeps the all-or-nothing contract synchronous - the commit can only end
    /// as Committed or RolledBack, never Pending waiting for user input.
    /// </summary>
    internal sealed class LayoutFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasError = false;
            foreach (FailureMessageAccessor message in failuresAccessor.GetFailureMessages())
            {
                FailureSeverity severity = message.GetSeverity();
                if (severity == FailureSeverity.Error || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                    break;
                }
            }
            // Warnings: continue normally (nothing is deleted or suppressed here).
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    internal sealed class BodyColumnWidthSyncException : Exception
    {
        public BodyColumnWidthSyncException()
            : base("The body column could not be synchronized.")
        {
        }
    }

    /// <summary>
    /// Service layer (no UI) for the Layout tools: resolve the target linked
    /// schedule, read its current header-column widths, and apply candidate
    /// widths (with an optional ColumnWidthMode change) as a single all-or-nothing
    /// transaction. UI decisions (dialogs, view switching, which mode to pass)
    /// belong to the commands, not here.
    ///
    /// Apply contract (design v0.8.0 9):
    ///   0. Preconditions are checked defensively - this is a public API and must
    ///      not assume a well-behaved caller (valid schedule, same Document, a
    ///      candidate count that matches the header columns, and, when a mode is
    ///      to be saved, an existing Excel link entity).
    ///   1. ValidateAll runs BEFORE any Revit change.
    ///   2. Invalid columns / over-limit total => no Transaction is started.
    ///   3. Width application and the ColumnWidthMode save happen in ONE Transaction.
    ///   4. If any SetColumnWidth fails, the whole Transaction is rolled back.
    ///   5. On rollback the ColumnWidthMode is not changed either (same transaction).
    ///
    /// Transaction status handling: Start() must return Started and Commit() must
    /// return Committed; any other status is reported as a failure and never as
    /// success. A <see cref="LayoutFailurePreprocessor"/> is installed on THIS
    /// transaction only (no global FailuresProcessor) so an unresolved error rolls
    /// back instead of waiting for user input, and SetClearAfterRollback(true) is
    /// set; the commit therefore resolves to Committed or RolledBack. If Pending is
    /// still returned it is reported with its own failure reason and the shared
    /// rollback helper is invoked. RollBack() is only called while the transaction
    /// is still Started, and an exception from RollBack never masks the original
    /// error.
    /// </summary>
    internal static class LayoutService
    {
        /// <summary>
        /// Resolves the linked schedule to act on, without guessing when ambiguous:
        ///   1. the active view if it is a linked schedule;
        ///   2. otherwise the single distinct linked schedule among the selection
        ///      (a selected ScheduleSheetInstance resolves to its ViewSchedule via
        ///      ScheduleId; the same schedule selected several ways counts once);
        ///   3. more than one distinct linked schedule => Ambiguous (no change);
        ///   4. none => None (no change).
        /// </summary>
        public static LayoutTarget ResolveTarget(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            ViewSchedule active = uidoc.ActiveView as ViewSchedule;
            if (active != null && LinkedScheduleFinder.IsLinked(active))
            {
                return LayoutTarget.Resolved(active);
            }

            HashSet<long> seen = new HashSet<long>();
            List<ViewSchedule> found = new List<ViewSchedule>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element el = doc.GetElement(id);
                ViewSchedule vs = el as ViewSchedule;
                if (vs == null)
                {
                    ScheduleSheetInstance ssi = el as ScheduleSheetInstance;
                    if (ssi != null)
                    {
                        vs = doc.GetElement(ssi.ScheduleId) as ViewSchedule; // original schedule
                    }
                }
                if (vs != null && !vs.IsTemplate && LinkedScheduleFinder.IsLinked(vs))
                {
                    if (seen.Add(vs.Id.Value)) found.Add(vs);
                }
            }

            if (found.Count == 1) return LayoutTarget.Resolved(found[0]);
            if (found.Count > 1) return LayoutTarget.Ambiguous(UserMessages.LayoutAmbiguous(found.Count));
            return LayoutTarget.None(UserMessages.LayoutNoTarget);
        }

        /// <summary>Reads the current header-section column widths (feet). Read-only.</summary>
        public static double[] GetCurrentWidths(ViewSchedule schedule)
        {
            TableSectionData tsd = schedule.GetTableData().GetSectionData(SectionType.Header);
            int c0 = tsd.FirstColumnNumber;
            int n = tsd.NumberOfColumns;
            double[] feet = new double[n];
            for (int c = 0; c < n; c++)
            {
                try { feet[c] = tsd.GetColumnWidth(c0 + c); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { feet[c] = LayoutMetrics.MinColumnFeet; }
            }
            return feet;
        }

        /// <summary>
        /// Computes Fit Content candidates from the text currently rendered in the
        /// header (no workbook access, no model change). The merged-cell guarantee
        /// is applied here - and only here (design v0.8.0 5.7).
        /// </summary>
        public static FitContentResult ComputeFitContent(ViewSchedule schedule, double widthScale)
        {
            HeaderContentMeasurer.Measurement m = HeaderContentMeasurer.Measure(schedule, widthScale);
            double[] feet = ColumnWidthMath.ComputeColumnFeet(m.MaxUnitsPerColumn, null, widthScale);
            MergeFitResult merged = LayoutOps.ApplyMergeConstraints(feet, m.Merges);
            return new FitContentResult
            {
                Feet = merged.Feet,
                UnresolvedDeficit = merged.UnresolvedDeficit
            };
        }

        /// <summary>Current width scale of the link (1.0 when not available).</summary>
        public static double GetWidthScale(ViewSchedule schedule)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            return info != null && info.WidthScale > 0 ? info.WidthScale : 1.0;
        }

        /// <summary>Current reload policy of the link (ApplyExcelWidths when unknown).</summary>
        public static ColumnWidthMode GetColumnWidthMode(ViewSchedule schedule)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            return info != null ? info.ColumnWidthMode : ColumnWidthMode.ApplyExcelWidths;
        }

        /// <summary>
        /// Validates then applies candidate widths as a single all-or-nothing
        /// transaction, optionally saving a new ColumnWidthMode in the SAME
        /// transaction. Returns without touching the model if any precondition or
        /// validation fails.
        /// </summary>
        /// <param name="modeToSave">New reload policy to persist, or null to leave it unchanged.</param>
        /// <param name="maxTotalFeet">Optional safe total-width limit; null = no pre-check.</param>
        public static LayoutApplyResult Apply(
            Document doc, ViewSchedule schedule, IList<double> candidateFeet,
            ColumnWidthMode? modeToSave, double? maxTotalFeet,
            bool allowLegacyMigration = false)
        {
            // ---- (0) Preconditions: never trust the caller, never start a transaction ----
            if (doc == null || schedule == null || !schedule.IsValidObject)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutInvalidTarget, null);
            }
            Document owner;
            try { owner = schedule.Document; }
            catch (Exception) { return LayoutApplyResult.Failed(UserMessages.LayoutInvalidTarget, null); }
            if (owner == null || !owner.Equals(doc))
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutInvalidTarget, null);
            }
            if (candidateFeet == null)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutNoCandidates, null);
            }

            TableSectionData tsd;
            try { tsd = schedule.GetTableData().GetSectionData(SectionType.Header); }
            catch (Exception) { return LayoutApplyResult.Failed(UserMessages.LayoutInvalidTarget, null); }
            if (tsd == null)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutInvalidTarget, null);
            }

            int headerColumns = tsd.NumberOfColumns;
            if (headerColumns <= 0)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutNoColumns, null);
            }
            if (candidateFeet.Count != headerColumns)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutColumnCountMismatch(candidateFeet.Count, headerColumns), null);
            }

            // Every layout action mutates a linked schedule. A v1 entity must be
            // explicitly authorized before this transaction can upgrade it.
            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutNotLinked, null);
            }
            if (!storage.IsV2 && !allowLegacyMigration)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutLegacyMigrationRequired, null);
            }

            // ---- (1)(2) Validate BEFORE any transaction; do not start one on failure ----
            ValidationResult v = LayoutOps.ValidateAll(candidateFeet, maxTotalFeet);
            if (!v.IsValid)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutInvalidInput(v.Reason), v.InvalidColumns);
            }
            double[] feet = v.NormalizedFeet; // clamped, safe to apply
            int c0 = tsd.FirstColumnNumber;

            // Layout is refused until a fresh read proves that the existing
            // header is safe. This is not a renderer-version cache or a reload
            // pre-scan; the read transaction is rolled back by the scanner.
            // It is a pre-layout gate, so its result is intentionally not stored.
            StyleIntegrityResult beforeLayout =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(doc, schedule);
            if (beforeLayout == null || beforeLayout.State != StyleIntegrityState.Healthy)
            {
                if (beforeLayout != null
                    && beforeLayout.ReasonCode == StyleIntegrityReasonCode.TransactionRollbackFailed)
                {
                    return LayoutApplyResult.Failed(
                        UserMessages.LayoutStyleScanRollbackFailed(
                            beforeLayout.ReadTransactionRollbackError), null);
                }
                return LayoutApplyResult.Failed(UserMessages.LayoutUnsafeStyles, null);
            }

            int at = 0;
            ModelMutationResult<bool> mutation = ModelMutation.Run<bool>(
                doc,
                "Excel Schedule Link - Set Column Widths",
                delegate (ModelMutationContext context)
                {
                    ModelMutationLinkLoadResult loaded =
                        context.LoadV2Link(schedule, allowLegacyMigration);
                    if (!loaded.IsLoaded)
                    {
                        if (loaded.Status == ModelMutationLinkLoadStatus.LegacyMigrationRefused
                            || loaded.Status == ModelMutationLinkLoadStatus.LegacyMigrationFailed)
                        {
                            context.Abort(UserMessages.LayoutLegacyMigrationRequired);
                            return false;
                        }
                        throw new InvalidOperationException(
                            loaded.Status == ModelMutationLinkLoadStatus.SchemaV2Unavailable
                                ? UserMessages.SchemaV2UnavailableAfterMigration
                                : UserMessages.LinkStorageUnavailable);
                    }

                    ExcelLinkInfo info = loaded.Info;
                    // (3)(4) All widths in one transaction; no per-column catch.
                    for (at = 0; at < headerColumns; at++)
                    {
                        tsd.SetColumnWidth(c0 + at, feet[at]);
                    }

                    double headerTotal;
                    if (DebugBodyWidthSyncFailureInjector.ConsumeNextBodyWidthSyncFailure())
                    {
                        throw new BodyColumnWidthSyncException();
                    }
                    if (!HeaderTableRenderer.TryGetHeaderWidthTotal(schedule, out headerTotal)
                        || !HeaderTableRenderer.SetBodyColumnWidth(schedule, headerTotal))
                    {
                        throw new BodyColumnWidthSyncException();
                    }

                    StyleIntegrityResult afterLayout =
                        ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                            doc, schedule, false);
                    if (afterLayout == null || afterLayout.State != StyleIntegrityState.Healthy)
                    {
                        throw new UnsafeScheduleStyleException(afterLayout);
                    }

                    // (3) Mode save in the SAME transaction (only if requested).
                    if (modeToSave.HasValue)
                    {
                        info.ColumnWidthMode = modeToSave.Value;
                    }
                    ExcelLinkSchema.SaveV2(schedule, info);
                    return true;
                },
                new ModelMutationOptions
                {
                    FailuresPreprocessor = new LayoutFailurePreprocessor(),
                    ClearAfterRollback = true
                });

            if (mutation.Status == ModelMutationStatus.Committed)
            {
                return new LayoutApplyResult
                {
                    Success = true,
                    UserMessage = UserMessages.LayoutApplied(schedule.Name, headerColumns)
                };
            }
            if (mutation.Status == ModelMutationStatus.FatalRegeneration)
            {
                return LayoutApplyResult.RegenerationFailed();
            }
            if (mutation.Status == ModelMutationStatus.NotStarted)
            {
                string status = mutation.StartStatus.HasValue
                    ? mutation.StartStatus.Value.ToString() : string.Empty;
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutTransactionNotStarted(status), null);
            }
            if (mutation.Status == ModelMutationStatus.StartThrew)
            {
                string detail = mutation.Exception == null
                    ? string.Empty : mutation.Exception.Message;
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutTransactionNotStarted(detail), null);
            }
            if (mutation.Status == ModelMutationStatus.Aborted)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutLegacyMigrationFailed(mutation.RollbackVerified), null);
            }
            if (mutation.Status == ModelMutationStatus.CommitNotCommitted)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutCommitNotCompleted(
                        mutation.CommitStatus.Value.ToString(), mutation.RollbackVerified), null);
            }
            if (mutation.Exception is BodyColumnWidthSyncException)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutBodyWidthSyncFailed(mutation.RollbackVerified), null);
            }
            if (mutation.Exception is UnsafeScheduleStyleException)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutUnsafeStylesAfterRollback(mutation.RollbackVerified), null);
            }

            Exception exception = mutation.Exception ?? new InvalidOperationException();
            // (4)(5) Whole transaction reverts: column widths AND the mode change.
            // A rollback failure must not mask the original error.
            string where = at < headerColumns
                ? UserMessages.LayoutColumnLabel(at + 1)
                : UserMessages.LayoutSettingsLabel;
            return LayoutApplyResult.Failed(
                UserMessages.LayoutApplyFailed(
                    where, exception.Message, mutation.RollbackVerified), null);
        }
    }
}
