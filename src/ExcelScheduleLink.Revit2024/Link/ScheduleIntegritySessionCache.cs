using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>Session-only state for one open Revit document.</summary>
    internal sealed class ScheduleIntegrityDocumentState
    {
        public Document Document;
        public readonly Dictionary<long, StyleIntegrityResult> Results =
            new Dictionary<long, StyleIntegrityResult>();
        public readonly OncePerDocumentNotificationBookkeeping Notifications =
            new OncePerDocumentNotificationBookkeeping();
        public bool AutomaticNotificationPending;
        public int AutomaticNotificationAttempts;
        public bool AutomaticNotificationInProgress;
        public bool RetryPending;
        public bool RetryAttempted;
        public bool ReadOnlyAtOpen;
    }

    /// <summary>
    /// Owns the last scan results for the current Revit session only. Nothing
    /// here is written to extensible storage, and renderer version is never
    /// used as a health cache.
    /// </summary>
    internal static class ScheduleIntegritySessionCache
    {
        // Do not use Document as a Dictionary key. Revit can hand the same
        // underlying document to different commands through different managed
        // wrappers, and a wrapper's hash code is not a safe underlying identity.
        private static readonly List<ScheduleIntegrityDocumentState> States =
            new List<ScheduleIntegrityDocumentState>();
        private static readonly List<PendingClose> PendingCloses =
            new List<PendingClose>();

        public static ScheduleIntegrityDocumentState BeginAutomatic(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            ScheduleIntegrityDocumentState state =
                new ScheduleIntegrityDocumentState { Document = doc };
            int existing = FindIndex(doc);
            if (existing >= 0)
            {
                RemovePendingClose(States[existing]);
                States[existing] = state;
            }
            else
            {
                States.Add(state);
            }
            return state;
        }

        public static ScheduleIntegrityDocumentState GetOrCreate(Document doc)
        {
            if (doc == null)
            {
                return null;
            }
            int index = FindIndex(doc);
            if (index >= 0)
            {
                // Keep the newest wrapper so later API access uses a live
                // wrapper, while preserving the coalesced state object.
                States[index].Document = doc;
                return States[index];
            }
            return BeginAutomatic(doc);
        }

        public static IList<ScheduleIntegrityDocumentState> GetStates()
        {
            return new List<ScheduleIntegrityDocumentState>(States);
        }

        /// <summary>
        /// Fast Idling preflight. It deliberately does not allocate when no
        /// retry or automatic notification is pending.
        /// </summary>
        public static bool HasPendingWork()
        {
            foreach (ScheduleIntegrityDocumentState state in States)
            {
                if (state != null && (state.RetryPending || state.AutomaticNotificationPending))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes stale wrappers before Idling performs either a retry scan or
        /// notification construction. IsValidObject can itself throw while a
        /// document is being torn down, so that check is contained here.
        /// </summary>
        public static void PruneInvalidDocuments()
        {
            for (int index = States.Count - 1; index >= 0; index--)
            {
                ScheduleIntegrityDocumentState state = States[index];
                bool valid = false;
                try
                {
                    valid = state != null && state.Document != null
                        && state.Document.IsValidObject;
                }
                catch (Exception)
                {
                    valid = false;
                }

                if (!valid)
                {
                    RemovePendingClose(state);
                    States.RemoveAt(index);
                }
            }
        }

        public static IList<ScheduleIntegrityDocumentState> GetPendingStates()
        {
            List<ScheduleIntegrityDocumentState> pending =
                new List<ScheduleIntegrityDocumentState>();
            foreach (ScheduleIntegrityDocumentState state in States)
            {
                if (state != null && (state.RetryPending || state.AutomaticNotificationPending))
                {
                    pending.Add(state);
                }
            }
            return pending;
        }

        public static IDictionary<long, StyleIntegrityResult> GetResults(Document doc)
        {
            ScheduleIntegrityDocumentState state = GetOrCreate(doc);
            return state == null
                ? new Dictionary<long, StyleIntegrityResult>()
                : new Dictionary<long, StyleIntegrityResult>(state.Results);
        }

        public static StyleIntegrityResult GetResult(Document doc, ElementId scheduleId)
        {
            if (scheduleId == null)
            {
                return null;
            }
            ScheduleIntegrityDocumentState state = GetOrCreate(doc);
            StyleIntegrityResult result;
            return state != null && state.Results.TryGetValue(scheduleId.Value, out result)
                ? result : null;
        }

        public static void SetResult(
            Document doc, ElementId scheduleId, StyleIntegrityResult result)
        {
            if (doc == null || scheduleId == null || result == null)
            {
                return;
            }
            GetOrCreate(doc).Results[scheduleId.Value] = result;
        }

        public static IDictionary<long, StyleIntegrityResult> ScanManual(Document doc)
        {
            return Scan(doc, StyleIntegrityScanScope.Manual);
        }

        public static IDictionary<long, StyleIntegrityResult> ScanAutomatic(Document doc)
        {
            return Scan(doc, StyleIntegrityScanScope.Automatic);
        }

        /// <summary>
        /// Records a cancellable close attempt. Removal is intentionally
        /// deferred until DocumentClosed reports success.
        /// </summary>
        public static void MarkClosing(int documentId, Document doc)
        {
            if (doc == null)
            {
                return;
            }
            int index = FindIndex(doc);
            if (index < 0)
            {
                return;
            }
            ScheduleIntegrityDocumentState state = States[index];
            RemovePendingClose(state);
            PendingCloses.Add(new PendingClose(documentId, state));
        }

        public static void RemoveAfterSuccessfulClose(int documentId)
        {
            for (int index = PendingCloses.Count - 1; index >= 0; index--)
            {
                PendingClose pending = PendingCloses[index];
                if (!SameDocumentId(pending.DocumentId, documentId))
                {
                    continue;
                }
                States.Remove(pending.State);
                PendingCloses.RemoveAt(index);
            }
        }

        public static void PreserveAfterCancelledClose(int documentId)
        {
            for (int index = PendingCloses.Count - 1; index >= 0; index--)
            {
                if (SameDocumentId(PendingCloses[index].DocumentId, documentId))
                {
                    PendingCloses.RemoveAt(index);
                }
            }
        }

        public static void Remove(Document doc)
        {
            int index = FindIndex(doc);
            if (index >= 0)
            {
                RemovePendingClose(States[index]);
                States.RemoveAt(index);
            }
        }

        public static void ClearAll()
        {
            States.Clear();
            PendingCloses.Clear();
        }

        private static IDictionary<long, StyleIntegrityResult> Scan(
            Document doc, StyleIntegrityScanScope scope)
        {
            ScheduleIntegrityDocumentState state = GetOrCreate(doc);
            if (state == null)
            {
                return new Dictionary<long, StyleIntegrityResult>();
            }

            IList<ViewSchedule> schedules = LinkedScheduleFinder.FindAll(doc);
            if (doc.IsReadOnly)
            {
                state.ReadOnlyAtOpen = true;
                SetUnavailable(state, schedules,
                    StyleIntegrityReasonCode.DocumentReadOnly,
                    "The document is read-only.");
                return GetResults(doc);
            }
            if (doc.IsModifiable)
            {
                SetUnavailable(state, schedules,
                    StyleIntegrityReasonCode.ForeignTransactionActive,
                    "A transaction is already active on the document.");
                return GetResults(doc);
            }

            IList<StyleIntegrityResult> results =
                ScheduleStyleIntegrityScanner.ScanSchedules(doc, schedules, scope);
            ApplyResults(state, schedules, results);
            return GetResults(doc);
        }

        private static void ApplyResults(
            ScheduleIntegrityDocumentState state,
            IList<ViewSchedule> schedules,
            IList<StyleIntegrityResult> results)
        {
            int resultIndex = 0;
            for (int index = 0; index < schedules.Count; index++)
            {
                StyleIntegrityResult result = null;
                if (results != null && resultIndex < results.Count)
                {
                    result = results[resultIndex++];
                }

                // ScanSchedules emits one result with no Facts when its bounded
                // batch limit is reached. It describes the current and every
                // remaining schedule as not checked; never turn that into a
                // partial Healthy result.
                if (result == null || (result.ReasonCode == StyleIntegrityReasonCode.ScanLimit
                    && result.Facts == null))
                {
                    StyleIntegrityResult limit = result ??
                        StyleIntegrityDecisionTable.LimitResult(schedules.Count - index);
                    for (int skipped = index; skipped < schedules.Count; skipped++)
                    {
                        state.Results[schedules[skipped].Id.Value] =
                            CloneLimitResult(limit);
                    }
                    return;
                }
                state.Results[schedules[index].Id.Value] = result;
            }
        }

        private static StyleIntegrityResult CloneLimitResult(StyleIntegrityResult source)
        {
            StyleIntegrityResult result = StyleIntegrityDecisionTable.LimitResult(
                source == null ? 1 : Math.Max(1, source.SkippedScheduleCount));
            result.FirstReadError = source == null ? string.Empty : source.FirstReadError;
            return result;
        }

        private static void SetUnavailable(
            ScheduleIntegrityDocumentState state,
            IList<ViewSchedule> schedules,
            StyleIntegrityReasonCode reason,
            string detail)
        {
            foreach (ViewSchedule schedule in schedules)
            {
                state.Results[schedule.Id.Value] = new StyleIntegrityResult
                {
                    State = StyleIntegrityState.Indeterminate,
                    ReasonCode = reason,
                    FirstReadError = detail ?? string.Empty
                };
            }
        }

        private static int FindIndex(Document doc)
        {
            for (int index = 0; index < States.Count; index++)
            {
                if (States[index] != null && SameDocument(States[index].Document, doc))
                {
                    return index;
                }
            }
            return -1;
        }

        // Keep this pattern in sync with WorksharingAdvisory. Reference
        // equality is the fast path; underlying equality is attempted only
        // while both wrappers are valid and is never allowed to escape.
        private static bool SameDocument(Document left, Document right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            try
            {
                return left.IsValidObject && right.IsValidObject && left.Equals(right);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameDocumentId(int left, int right)
        {
            return left == right;
        }

        private static void RemovePendingClose(ScheduleIntegrityDocumentState state)
        {
            if (state == null)
            {
                return;
            }
            for (int index = PendingCloses.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(PendingCloses[index].State, state))
                {
                    PendingCloses.RemoveAt(index);
                }
            }
        }

        private sealed class PendingClose
        {
            public readonly int DocumentId;
            public readonly ScheduleIntegrityDocumentState State;

            public PendingClose(int documentId, ScheduleIntegrityDocumentState state)
            {
                DocumentId = documentId;
                State = state;
            }
        }
    }
}
