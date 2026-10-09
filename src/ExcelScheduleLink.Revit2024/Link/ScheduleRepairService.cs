using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Link
{
    internal sealed class HeaderTableCellSnapshot
    {
        public string Text = string.Empty;
    }

    internal sealed class HeaderTableMergeSnapshot
    {
        public int Top;
        public int Left;
        public int Bottom;
        public int Right;
    }

    /// <summary>Complete, in-memory representation required by the fallback.</summary>
    internal sealed class HeaderTableSnapshot
    {
        public int Rows;
        public int Columns;
        public int FirstRowNumber;
        public int FirstColumnNumber;
        public readonly List<HeaderTableCellSnapshot> Cells =
            new List<HeaderTableCellSnapshot>();
        public readonly List<double> ColumnWidths = new List<double>();
        public readonly List<double> RowHeights = new List<double>();
        public readonly List<HeaderTableMergeSnapshot> Merges =
            new List<HeaderTableMergeSnapshot>();
    }

    internal sealed class RepairAttemptOutcome
    {
        public bool Success;
        public bool TransactionStarted;
        public bool RollbackVerified;
        public bool FromCurrentTable;
        public bool ConsentDeclined;
        public bool FatalRegenerationFailure;
        public string Detail = string.Empty;
    }

    internal sealed class SnapshotReadOutcome
    {
        public HeaderTableSnapshot Snapshot;
        public bool RollbackVerified;
        public string Detail = string.Empty;
        public bool Success { get { return Snapshot != null && RollbackVerified; } }
    }

    /// <summary>
    /// Implements the explicit no-workbook repair. Reading the existing table is
    /// itself transaction-scoped and rolled back; the subsequent mutation uses
    /// exactly one all-or-nothing transaction on the original ViewSchedule.
    /// </summary>
    internal static class ScheduleRepairService
    {
        internal const string RepairTransactionName =
            "Excel Schedule Link - Rebuild Unsafe Schedule";

        public static SnapshotReadOutcome CaptureSnapshotWithRolledBackTransaction(
            Document doc, ViewSchedule schedule)
        {
            SnapshotReadOutcome outcome = new SnapshotReadOutcome();
            if (doc == null || schedule == null || !schedule.IsValidObject)
            {
                outcome.Detail = "The document or schedule was invalid.";
                return outcome;
            }
            if (doc.IsReadOnly)
            {
                outcome.Detail = UserMessages.AutomaticReadOnlyMessage;
                return outcome;
            }
            if (doc.IsModifiable)
            {
                outcome.Detail = "A transaction is already active on the document.";
                return outcome;
            }

            using (Transaction transaction = new Transaction(doc,
                "Excel Schedule Link - Read Current Table"))
            {
                bool started = false;
                try
                {
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        outcome.Detail = "The current-table read transaction did not start.";
                        return outcome;
                    }
                    started = true;
                    if (!doc.IsModifiable)
                    {
                        outcome.Detail = "The document was not modifiable after the read transaction started.";
                        return outcome;
                    }
                    outcome.Snapshot = CaptureSnapshot(schedule);
                }
                catch (Exception ex)
                {
                    outcome.Snapshot = null;
                    outcome.Detail = ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    if (started)
                    {
                        TransactionStatus? final =
                            TransactionRollback.RollBackIfStarted(transaction);
                        outcome.RollbackVerified = final == TransactionStatus.RolledBack;
                        if (!outcome.RollbackVerified)
                        {
                            outcome.Snapshot = null;
                            outcome.Detail = UserMessages.RollbackCompletionNotVerified;
                        }
                    }
                }
            }
            return outcome;
        }

        public static RepairAttemptOutcome RebuildFromCurrentTable(
            Document doc, ViewSchedule schedule)
        {
            SnapshotReadOutcome read =
                CaptureSnapshotWithRolledBackTransaction(doc, schedule);
            if (!read.Success)
            {
                string snapshotDetail = string.IsNullOrEmpty(read.Detail)
                    || read.Detail == UserMessages.RollbackCompletionNotVerified
                    ? string.Empty
                    : "\n\n" + read.Detail;
                return Failed(
                    UserMessages.FallbackSnapshotFailedInstruction + "\n\n"
                    + UserMessages.FallbackSnapshotFailureContent(read.RollbackVerified)
                    + snapshotDetail, false, false);
            }

            // A detached document reports IsReadOnlyFile although it is the
            // editable copy; the explicit-repair policy already asked
            // for the detached-copy confirmation.
            if (doc.IsReadOnly || (doc.IsReadOnlyFile && !doc.IsDetached))
            {
                return Failed(
                    doc.IsReadOnly
                        ? UserMessages.AutomaticReadOnlyMessage
                        : UserMessages.ReadOnlyFileRepairMessage,
                    false, false);
            }

            using (Transaction transaction = new Transaction(doc, RepairTransactionName))
            {
                bool started = false;
                try
                {
                    TransactionStatus start = transaction.Start();
                    if (start != TransactionStatus.Started)
                    {
                        return Failed("Revit did not allow starting the repair transaction.",
                            false, false);
                    }
                    started = true;
                    if (!doc.IsModifiable)
                    {
                        return Failed("The document was not modifiable after the repair transaction started.",
                            false, true);
                    }

                    HeaderTableRenderer.ClearTo1x1(
                        schedule.GetTableData().GetSectionData(SectionType.Header));
                    TableSectionData section =
                        schedule.GetTableData().GetSectionData(SectionType.Header);
                    GrowToSnapshot(section, read.Snapshot);
                    RestoreCells(section, read.Snapshot);
                    RestoreWidthsAndHeights(section, read.Snapshot);
                    RestoreMerges(section, read.Snapshot);

                    double headerTotal;
                    if (!HeaderTableRenderer.TryGetHeaderWidthTotal(schedule, out headerTotal)
                        || !HeaderTableRenderer.SetBodyColumnWidth(schedule, headerTotal))
                    {
                        throw new BodyColumnWidthSyncException();
                    }

                    StyleIntegrityResult validation =
                        ScheduleStyleIntegrityScanner.ScanAfterMutationInOpenTransaction(
                            doc, schedule, true);
                    if (validation == null || validation.State != StyleIntegrityState.Healthy)
                    {
                        throw new UnsafeScheduleStyleException(validation);
                    }

                    ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
                    if (storage == null || storage.Info == null)
                    {
                        throw new InvalidOperationException("The link storage was unavailable.");
                    }
                    ExcelLinkInfo info = storage.Info;
                    if (!storage.IsV2)
                    {
                        if (!ExcelLinkMigration.MigrateInOpenTransaction(schedule))
                        {
                            throw new InvalidOperationException(
                                "The legacy link could not be upgraded.");
                        }
                        info = ExcelLinkSchema.LoadV2(schedule);
                        if (info == null)
                        {
                            throw new InvalidOperationException(
                                "Schema v2 was not available after legacy-link migration.");
                        }
                    }
                    info.RendererVersion = ExcelLinkInfo.CurrentRendererVersion;
                    ExcelLinkSchema.SaveV2(schedule, info);

                    TransactionStatus committed = transaction.Commit();
                    if (committed != TransactionStatus.Committed)
                    {
                        bool rollback = TransactionRollback.RollBackIfStarted(transaction)
                            == TransactionStatus.RolledBack;
                        return Failed(
                            UserMessages.FallbackRebuildFailed(
                                UserMessages.ReloadTransactionNotCommitted, rollback),
                            rollback, true);
                    }
                    return new RepairAttemptOutcome
                    {
                        Success = true,
                        TransactionStarted = true,
                        FromCurrentTable = true
                    };
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
                {
                    bool rollback = started
                        && TransactionRollback.RollBackIfStarted(transaction)
                            == TransactionStatus.RolledBack;
                    return Failed(
                        UserMessages.RegenerationFailure, rollback, started, true);
                }
                catch (RegenerationFailureProbeException)
                {
                    bool rollback = started
                        && TransactionRollback.RollBackIfStarted(transaction)
                            == TransactionStatus.RolledBack;
                    return Failed(
                        UserMessages.RegenerationFailure, rollback, started, true);
                }
                catch (Exception ex)
                {
                    bool rollback = started
                        && TransactionRollback.RollBackIfStarted(transaction)
                            == TransactionStatus.RolledBack;
                    return Failed(
                        UserMessages.FallbackRebuildFailed(ex.Message, rollback),
                        rollback, started);
                }
            }
        }

        private static HeaderTableSnapshot CaptureSnapshot(ViewSchedule schedule)
        {
            TableSectionData section =
                schedule.GetTableData().GetSectionData(SectionType.Header);
            int rows = section.NumberOfRows;
            int columns = section.NumberOfColumns;
            if (rows <= 0 || columns <= 0
                || StyleIntegrityLimits.ExceedsScheduleLimit(rows, columns))
            {
                throw new InvalidOperationException(
                    "The current header dimensions were outside the supported renderer limit.");
            }

            HeaderTableSnapshot snapshot = new HeaderTableSnapshot
            {
                Rows = rows,
                Columns = columns,
                FirstRowNumber = section.FirstRowNumber,
                FirstColumnNumber = section.FirstColumnNumber
            };

            HashSet<string> mergeKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int rowOffset = 0; rowOffset < rows; rowOffset++)
            {
                int row = snapshot.FirstRowNumber + rowOffset;
                for (int columnOffset = 0; columnOffset < columns; columnOffset++)
                {
                    int column = snapshot.FirstColumnNumber + columnOffset;
                    if (DebugCurrentTableCaptureFailureInjector.ConsumeNextCaptureFailure())
                    {
                        throw new InvalidOperationException(
                            "A current-table cell returned unavailable text.");
                    }
                    string text = schedule.GetCellText(SectionType.Header, row, column);
                    if (text == null)
                    {
                        throw new InvalidOperationException(
                            "A current-table cell returned unavailable text.");
                    }
                    snapshot.Cells.Add(new HeaderTableCellSnapshot { Text = text });

                    TableMergedCell merged = section.GetMergedCell(row, column);
                    if (merged != null && merged.IsValidObject
                        && (merged.Top != merged.Bottom || merged.Left != merged.Right))
                    {
                        string key = merged.Top + ":" + merged.Left + ":"
                            + merged.Bottom + ":" + merged.Right;
                        if (mergeKeys.Add(key))
                        {
                            snapshot.Merges.Add(new HeaderTableMergeSnapshot
                            {
                                Top = merged.Top - snapshot.FirstRowNumber,
                                Left = merged.Left - snapshot.FirstColumnNumber,
                                Bottom = merged.Bottom - snapshot.FirstRowNumber,
                                Right = merged.Right - snapshot.FirstColumnNumber
                            });
                        }
                    }
                }
            }

            for (int columnOffset = 0; columnOffset < columns; columnOffset++)
            {
                snapshot.ColumnWidths.Add(section.GetColumnWidth(
                    snapshot.FirstColumnNumber + columnOffset));
            }
            for (int rowOffset = 0; rowOffset < rows; rowOffset++)
            {
                snapshot.RowHeights.Add(section.GetRowHeight(
                    snapshot.FirstRowNumber + rowOffset));
            }
            return snapshot;
        }

        private static void GrowToSnapshot(
            TableSectionData section, HeaderTableSnapshot snapshot)
        {
            int guard = 0;
            while (section.NumberOfRows < snapshot.Rows && guard++ < 100000)
            {
                section.InsertRow(section.FirstRowNumber + 1);
            }
            guard = 0;
            while (section.NumberOfColumns < snapshot.Columns && guard++ < 100000)
            {
                section.InsertColumn(section.FirstColumnNumber + 1);
            }
            if (section.NumberOfRows != snapshot.Rows
                || section.NumberOfColumns != snapshot.Columns)
            {
                throw new InvalidOperationException(
                    "Revit did not recreate the captured table dimensions.");
            }
        }

        private static void RestoreCells(
            TableSectionData section, HeaderTableSnapshot snapshot)
        {
            int index = 0;
            for (int rowOffset = 0; rowOffset < snapshot.Rows; rowOffset++)
            {
                int row = section.FirstRowNumber + rowOffset;
                for (int columnOffset = 0; columnOffset < snapshot.Columns; columnOffset++)
                {
                    int column = section.FirstColumnNumber + columnOffset;
                    section.SetCellType(row, column, CellType.Text);
                    section.SetCellText(row, column, snapshot.Cells[index++].Text ?? string.Empty);
                }
            }
        }

        private static void RestoreWidthsAndHeights(
            TableSectionData section, HeaderTableSnapshot snapshot)
        {
            for (int columnOffset = 0; columnOffset < snapshot.Columns; columnOffset++)
            {
                section.SetColumnWidth(
                    section.FirstColumnNumber + columnOffset,
                    snapshot.ColumnWidths[columnOffset]);
            }
            for (int rowOffset = 0; rowOffset < snapshot.Rows; rowOffset++)
            {
                section.SetRowHeight(
                    section.FirstRowNumber + rowOffset,
                    snapshot.RowHeights[rowOffset]);
            }
        }

        private static void RestoreMerges(
            TableSectionData section, HeaderTableSnapshot snapshot)
        {
            foreach (HeaderTableMergeSnapshot merge in snapshot.Merges)
            {
                section.MergeCells(new TableMergedCell(
                    section.FirstRowNumber + merge.Top,
                    section.FirstColumnNumber + merge.Left,
                    section.FirstRowNumber + merge.Bottom,
                    section.FirstColumnNumber + merge.Right));
            }
        }

        private static RepairAttemptOutcome Failed(
            string detail, bool rollbackVerified, bool transactionStarted,
            bool fatalRegenerationFailure = false)
        {
            return new RepairAttemptOutcome
            {
                Success = false,
                RollbackVerified = rollbackVerified,
                TransactionStarted = transactionStarted,
                FromCurrentTable = false,
                Detail = detail ?? string.Empty,
                FatalRegenerationFailure = fatalRegenerationFailure
            };
        }
    }
}
