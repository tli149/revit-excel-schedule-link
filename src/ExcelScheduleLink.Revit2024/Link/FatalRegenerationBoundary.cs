using System;
using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Owns what an entry point does when a fatal regeneration reaches it as an
    /// exception: show UserMessages.RegenerationFailure once and stop without
    /// further document reads. Exception classification remains in
    /// ModelMutation.IsFatalRegeneration. Results from
    /// ModelMutationStatus.FatalRegeneration keep their existing handling.
    /// </summary>
    internal static class FatalRegenerationBoundary
    {
        private const int DeferredMessageMaxAttempts = 3;
        private static bool _messageDeferred;
        private static int _deferredAttempts;

        // This is the outermost frame of every production command. Revit shows
        // `message` in its own box when a command returns Failed, and every ESL
        // failure path already shows an ESL dialog. The body gets no `message`;
        // this frame sets it only if the fatal ESL dialog could not be shown.
        // Reporting is best effort when Revit's UI is unavailable.
        internal static Result RunCommand(ref string message, Func<Result> body)
        {
            long committedBefore = ModelMutation.CommittedCount;
            try
            {
                return body();
            }
            catch (Exception ex) when (ModelMutation.IsFatalRegeneration(ex))
            {
                ModelMutation.RecordFatalRegeneration();
                // Revit undoes what a command committed when it returns Failed or
                // Cancelled. Keep what this command already committed instead of asking
                // Revit to undo it on a document that failed to regenerate. The result
                // is decided before the notification so a failed notification cannot
                // change it.
                Result result = ModelMutation.CommittedCount != committedBefore
                    ? Result.Succeeded : Result.Failed;
                if (!ShowMessage() && result == Result.Failed)
                {
                    message = UserMessages.RegenerationFailure;
                }
                return result;
            }
        }

        // For a dialog callback: a fatal becomes the callback's own fatal result.
        internal static T Guard<T>(Func<T> body, Func<T> fatalResult)
        {
            try
            {
                return body();
            }
            catch (Exception ex) when (ModelMutation.IsFatalRegeneration(ex))
            {
                ModelMutation.RecordFatalRegeneration();
                return fatalResult();
            }
        }

        // For a dialog event handler.
        internal static void Guard(Action body, Action onFatal)
        {
            try
            {
                body();
            }
            catch (Exception ex) when (ModelMutation.IsFatalRegeneration(ex))
            {
                ModelMutation.RecordFatalRegeneration();
                onFatal();
            }
        }

        // An event that must not show UI now (DocumentOpened) or that must finish its own
        // cleanup first: the one fatal message is shown at the next Idling. Fatals before
        // then join the same pending notification and keep its attempt budget.
        internal static void DeferMessage()
        {
            if (_messageDeferred) return;
            _messageDeferred = true;
            _deferredAttempts = 0;
        }

        // Called first in every Idling tick. Best effort and document-free: a failed
        // presentation is retried on later ticks up to the budget.
        internal static void ShowDeferredMessage()
        {
            if (!_messageDeferred) return;
            _messageDeferred = false; // clear first: no reentrant delivery
            int attempts = _deferredAttempts + 1;
            if (ShowMessage()) return;
            if (_messageDeferred) return; // a new batch started meanwhile and owns its budget
            _deferredAttempts = attempts;
            _messageDeferred = attempts < DeferredMessageMaxAttempts;
        }

        // The one lifecycle response to a fatal: record it, so the integrity cache drops all
        // retained automatic state before its next lookup, and defer the message. The caller
        // then returns at once without touching the document.
        internal static void ContainInEventHandler()
        {
            ModelMutation.RecordFatalRegeneration();
            DeferMessage();
        }

        // The one fatal notification. Best effort: it never throws, so a command
        // still returns its result and an event handler (Idling) never throws.
        // Returns false when the dialog could not be shown.
        internal static bool ShowMessage()
        {
            try
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.RegenerationFailure);
                return true;
            }
            catch (Exception)
            {
                // Revit may be tearing down the UI.
                return false;
            }
        }
    }
}
