using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Probing;
using ExcelScheduleLink.Revit2024.UI;

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
    /// widths (with optional column/row reload-policy changes) as a single all-or-nothing
    /// transaction. UI decisions (dialogs, view switching, which mode to pass)
    /// belong to the commands, not here.
    ///
    /// Apply contract (design v0.8.0 9):
    ///   0. Preconditions are checked defensively - this is a public API and must
    ///      not assume a well-behaved caller (valid schedule, same Document, a
    ///      candidate count that matches the header columns, and, when a reload
    ///      policy is to be saved, an existing Excel link entity).
    ///   1. ValidateAll runs BEFORE any Revit change.
    ///   2. Invalid columns / over-limit total => no Transaction is started.
    ///   3. Width application and requested reload-policy saves happen in ONE Transaction.
    ///   4. If any SetColumnWidth fails, the whole Transaction is rolled back.
    ///   5. On rollback neither reload policy is changed (same transaction).
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

        /// <summary>
        /// Reads the current header-section column widths (feet). A documented
        /// native read failure or invalid dimension returns false with an empty
        /// result; no substitute width is fabricated.
        /// </summary>
        public static bool TryGetCurrentWidths(ViewSchedule schedule, out double[] feet)
        {
            feet = new double[0];
            try
            {
                TableSectionData tsd =
                    schedule.GetTableData().GetSectionData(SectionType.Header);
                int c0 = tsd.FirstColumnNumber;
                int n = tsd.NumberOfColumns;
                if (n <= 0)
                {
                    return true;
                }

                double[] read = new double[n];
                double total = 0.0;
                for (int c = 0; c < n; c++)
                {
                    double width = tsd.GetColumnWidth(c0 + c);
                    if (width <= 0.0 || double.IsNaN(width) || double.IsInfinity(width))
                    {
                        return false;
                    }
                    read[c] = width;
                    total += width;
                    if (double.IsNaN(total) || double.IsInfinity(total))
                    {
                        return false;
                    }
                }
                if (total <= 0.0 || double.IsNaN(total) || double.IsInfinity(total))
                {
                    return false;
                }
                feet = read;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                feet = new double[0];
                return false;
            }
        }

        public static FitContentResult ComputeFitContent(
            Document doc, ViewSchedule schedule,
            IReadOnlyList<double> currentFeet)
        {
            string targetFailure = GetTargetFailure(doc, schedule);
            if (targetFailure != null)
            {
                return FitContentResult.Failed(targetFailure);
            }
            if (currentFeet == null)
            {
                return FitContentResult.Failed(UserMessages.LayoutNoCandidates);
            }

            int suppliedCount = currentFeet.Count;
            if (suppliedCount == 0)
            {
                return FitContentResult.Failed(UserMessages.LayoutNoCandidates);
            }

            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                return FitContentResult.Failed(UserMessages.LayoutNotLinked);
            }

            int headerColumns;
            string headerFailure = GetHeaderColumnsFailure(schedule, out headerColumns);
            if (headerFailure != null)
            {
                return FitContentResult.Failed(headerFailure);
            }

            double[] suppliedFeet = new double[suppliedCount];
            double total = 0.0;
            for (int i = 0; i < suppliedCount; i++)
            {
                double width = currentFeet[i];
                if (width < 0.0 || double.IsNaN(width)
                    || double.IsInfinity(width))
                {
                    return FitContentResult.Failed(
                        UserMessages.LayoutFitContentInvalidTotal);
                }
                suppliedFeet[i] = width;
                total += width;
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    return FitContentResult.Failed(
                        UserMessages.LayoutFitContentInvalidTotal);
                }
            }

            string safetyFailure = GetHeaderSafetyFailure(doc, schedule);
            if (safetyFailure != null)
            {
                return FitContentResult.Failed(safetyFailure);
            }

            double widthScale = GetWidthScale(schedule);
            HeaderContentMeasurer.FitContentMeasurementOutcome read =
                HeaderContentMeasurer.ReadMeasurements(doc, schedule, widthScale);
            if (read == null || !read.Success)
            {
                return FitContentResult.Failed(
                    read == null || string.IsNullOrEmpty(read.UserMessage)
                        ? UserMessages.LayoutFitContentReadFailed
                        : read.UserMessage);
            }

            FitContentMeasurements measurements = read.Measurements;
            if (measurements.ColumnCount != suppliedFeet.Length)
            {
                return FitContentResult.Failed(
                    UserMessages.LayoutColumnCountMismatch(
                        suppliedFeet.Length, measurements.ColumnCount));
            }

            FixedTotalFitResult fixedTotal =
                LayoutOps.FitContentToTotal(measurements, total);
            if (!fixedTotal.CanApply)
            {
                string message = fixedTotal.LimitReason
                    == FitContentLimitReason.NoColumns
                    ? UserMessages.LayoutNoColumns
                    : UserMessages.LayoutFitContentInvalidTotal;
                return FitContentResult.Failed(message);
            }

            double[] feet = new double[fixedTotal.Feet.Count];
            for (int i = 0; i < fixedTotal.Feet.Count; i++)
            {
                feet[i] = fixedTotal.Feet[i];
            }
            return new FitContentResult
            {
                Success = fixedTotal.CanApply,
                Feet = feet,
                FixedTotal = fixedTotal,
                ColumnHeadings = read.ColumnHeadings
            };
        }

        public static string[] ReadColumnHeadings(
            Document doc, ViewSchedule schedule, int expectedColumns)
        {
            int count = expectedColumns < 0 ? 0 : expectedColumns;
            string[] empty = new string[count];
            string targetFailure = GetTargetFailure(doc, schedule);
            if (targetFailure != null)
            {
                return empty;
            }

            int headerColumns;
            string headerFailure = GetHeaderColumnsFailure(
                schedule, out headerColumns);
            if (headerFailure != null || headerColumns != expectedColumns)
            {
                return empty;
            }

            double widthScale = GetWidthScale(schedule);
            HeaderContentMeasurer.FitContentMeasurementOutcome read =
                HeaderContentMeasurer.ReadMeasurements(doc, schedule, widthScale);
            if (read == null || !read.Success
                || read.ColumnHeadings == null
                || read.ColumnHeadings.Length != expectedColumns)
            {
                return empty;
            }

            string[] headings = new string[expectedColumns];
            for (int i = 0; i < expectedColumns; i++)
            {
                headings[i] = read.ColumnHeadings[i] ?? string.Empty;
            }
            return headings;
        }

        public static LengthUnitChoice ResolveLengthUnit(Document doc)
        {
            try
            {
                return LengthUnitHelper.FromLengthUnit(
                    doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId());
            }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return LengthUnitHelper.FromLengthUnit(null);
            }
        }

        /// <summary>Current width scale of the link (1.0 when not available).</summary>
        public static double GetWidthScale(ViewSchedule schedule)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            return info != null && info.WidthScale > 0 ? info.WidthScale : 1.0;
        }

        /// <summary>Current column reload policy (ApplyExcelWidths when unavailable).</summary>
        public static ColumnWidthMode GetColumnWidthMode(ViewSchedule schedule)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            return info != null ? info.ColumnWidthMode : ColumnWidthMode.ApplyExcelWidths;
        }

        /// <summary>Current row reload policy (UseDefaultHeights when unavailable).</summary>
        public static RowHeightMode GetRowHeightMode(ViewSchedule schedule)
        {
            ExcelLinkStorage storage = schedule != null ? ExcelLinkSchema.LoadAny(schedule) : null;
            ExcelLinkInfo info = storage != null ? storage.Info : null;
            return info != null ? info.RowHeightMode : RowHeightMode.UseDefaultHeights;
        }

        /// <summary>
        /// Validates then applies candidate widths as a single all-or-nothing
        /// transaction, optionally saving new column and row reload policies in the SAME
        /// transaction. Returns without touching the model if any precondition or
        /// validation fails.
        /// </summary>
        /// <param name="modeToSave">New column reload policy to persist, or null to leave it unchanged.</param>
        /// <param name="rowModeToSave">New row reload policy to persist, or null to leave it unchanged.</param>
        /// <param name="maxTotalFeet">Optional safe total-width limit; null = no pre-check.</param>
        public static LayoutApplyResult Apply(
            Document doc, ViewSchedule schedule, IList<double> candidateFeet,
            ColumnWidthMode? modeToSave, RowHeightMode? rowModeToSave, double? maxTotalFeet,
            bool allowLegacyMigration = false,
            IReadOnlyList<string> columnHeadings = null)
        {
            // ---- (0) Preconditions: never trust the caller, never start a transaction ----
            string targetFailure = GetTargetFailure(doc, schedule);
            if (targetFailure != null)
            {
                return LayoutApplyResult.Failed(targetFailure, null);
            }
            if (candidateFeet == null)
            {
                return LayoutApplyResult.Failed(UserMessages.LayoutNoCandidates, null);
            }

            int headerColumns;
            string headerFailure = GetHeaderColumnsFailure(schedule, out headerColumns);
            if (headerFailure != null)
            {
                return LayoutApplyResult.Failed(headerFailure, null);
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
            double[] currentFeet;
            if (!TryGetCurrentWidths(schedule, out currentFeet))
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutDimensionsReadFailed, null);
            }
            if (currentFeet.Length != candidateFeet.Count)
            {
                return LayoutApplyResult.Failed(
                    UserMessages.LayoutColumnCountMismatch(
                        candidateFeet.Count, currentFeet.Length), null);
            }
            ValidationResult v =
                LayoutOps.ValidateAll(candidateFeet, maxTotalFeet, currentFeet);
            if (!v.IsValid)
            {
                LengthUnitChoice unit = ResolveLengthUnit(doc);
                IReadOnlyList<string> diagnosticHeadings =
                    columnHeadings != null
                    && columnHeadings.Count == currentFeet.Length
                        ? columnHeadings : null;
                return LayoutApplyResult.Failed(
                    LengthUnitHelper.ValidationMessage(v, unit, diagnosticHeadings),
                    v.InvalidColumns);
            }
            double[] feet = v.NormalizedFeet;

            // Layout is refused until a fresh read proves that the existing
            // header is safe. This is not a renderer-version cache or a reload
            // pre-scan; the read transaction is rolled back by the scanner.
            // It is a pre-layout gate, so its result is intentionally not stored.
            string safetyFailure = GetHeaderSafetyFailure(doc, schedule);
            if (safetyFailure != null)
            {
                return LayoutApplyResult.Failed(safetyFailure, null);
            }

            string scheduleName = schedule.Name;
            int at = 0;
            int? rowsMayHideText = 0;
            RowHeightMode effectiveRowMode = RowHeightMode.UseDefaultHeights;
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
                    TableSectionData currentHeader =
                        schedule.GetTableData().GetSectionData(SectionType.Header);
                    if (currentHeader.NumberOfColumns != headerColumns)
                    {
                        throw new InvalidOperationException(
                            UserMessages.LayoutColumnCountMismatch(
                                headerColumns, currentHeader.NumberOfColumns));
                    }
                    int c0 = currentHeader.FirstColumnNumber;

                    // (3)(4) All widths in one transaction; no per-column catch.
                    for (at = 0; at < headerColumns; at++)
                    {
                        currentHeader.SetColumnWidth(c0 + at, feet[at]);
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

                    effectiveRowMode = rowModeToSave ?? loaded.Info.RowHeightMode;
                    afterLayout = HeaderRowFitter.FitAndValidateInOpenTransaction(
                        schedule, effectiveRowMode, afterLayout, false,
                        out rowsMayHideText);
                    if (afterLayout == null
                        || afterLayout.State != StyleIntegrityState.Healthy)
                    {
                        throw new UnsafeScheduleStyleException(afterLayout);
                    }

                    // (3) Reload-policy saves in the SAME transaction (only if requested).
                    if (modeToSave.HasValue)
                    {
                        info.ColumnWidthMode = modeToSave.Value;
                    }
                    if (rowModeToSave.HasValue)
                    {
                        info.RowHeightMode = rowModeToSave.Value;
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
                string userMessage = UserMessages.LayoutApplied(
                    scheduleName, headerColumns);
                userMessage = UserMessages.AppendLayoutRowPolicyNote(
                    userMessage, effectiveRowMode);
                // LayoutApplied already names the schedule.
                userMessage = UserMessages.AppendRenderNotes(
                    userMessage, 0, 0, false, false, 0,
                    rowsMayHideText: rowsMayHideText);
                return new LayoutApplyResult
                {
                    Success = true,
                    UserMessage = userMessage
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
            // (4)(5) Whole transaction reverts: column widths AND any policy changes.
            // A rollback failure must not mask the original error.
            string where = at < headerColumns
                ? UserMessages.LayoutColumnLabel(at + 1)
                : UserMessages.LayoutSettingsLabel;
            return LayoutApplyResult.Failed(
                UserMessages.LayoutApplyFailed(
                    where, exception.Message, mutation.RollbackVerified), null);
        }

        /// <summary>
        /// Null when the schedule is a valid element of <paramref name="doc"/>;
        /// otherwise the shared invalid-target message.
        /// </summary>
        private static string GetTargetFailure(Document doc, ViewSchedule schedule)
        {
            if (doc == null || schedule == null || !schedule.IsValidObject)
            {
                return UserMessages.LayoutInvalidTarget;
            }
            Document owner;
            try { owner = schedule.Document; }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return UserMessages.LayoutInvalidTarget;
            }
            if (owner == null || !owner.Equals(doc))
            {
                return UserMessages.LayoutInvalidTarget;
            }
            return null;
        }

        /// <summary>
        /// Null when the HEADER section is readable and has columns; the column
        /// count is returned in <paramref name="headerColumns"/>.
        /// </summary>
        private static string GetHeaderColumnsFailure(ViewSchedule schedule,
            out int headerColumns)
        {
            headerColumns = 0;
            TableSectionData header;
            try { header = schedule.GetTableData().GetSectionData(SectionType.Header); }
            catch (Exception ex) when (!ModelMutation.IsFatalRegeneration(ex))
            {
                return UserMessages.LayoutInvalidTarget;
            }
            if (header == null)
            {
                return UserMessages.LayoutInvalidTarget;
            }
            headerColumns = header.NumberOfColumns;
            return headerColumns <= 0 ? UserMessages.LayoutNoColumns : null;
        }

        private static string GetHeaderSafetyFailure(
            Document doc, ViewSchedule schedule)
        {
            StyleIntegrityResult beforeLayout =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(
                    doc, schedule);
            if (beforeLayout == null
                || beforeLayout.State != StyleIntegrityState.Healthy)
            {
                if (beforeLayout != null
                    && beforeLayout.ReasonCode
                        == StyleIntegrityReasonCode.TransactionRollbackFailed)
                {
                    return UserMessages.LayoutStyleScanRollbackFailed(
                        beforeLayout.ReadTransactionRollbackError);
                }
                return UserMessages.LayoutUnsafeStyles;
            }
            return null;
        }
    }
}
