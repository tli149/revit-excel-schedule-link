using System;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Probe-manifest-only one-shot hooks for a regeneration-failure
    /// equivalent. Revit's
    /// RegenerationFailedException cannot be constructed by the probe, so the
    /// private marker exception is handled identically at every transaction
    /// boundary that handles the native exception. Two sites are offered: the
    /// post-mutation scan inside ModelMutation (the status path), and a
    /// rolled-back read outside ModelMutation (the exception path that reaches
    /// FatalRegenerationBoundary). Production code never arms either hook.
    /// </summary>
    internal static class DebugRegenerationFailureInjector
    {
        private static bool _failNext;

        // Negative while disarmed; otherwise the number of rolled-back reads
        // still allowed to pass before one fails.
        private static int _rolledBackReadsToPass = -1;

        public static void ArmNextRegenerationFailure()
        {
            _failNext = true;
        }

        public static bool ConsumeNextRegenerationFailure()
        {
            if (!_failNext)
            {
                return false;
            }
            _failNext = false;
            return true;
        }

        public static void ArmRolledBackReadFailure(int readsToPass)
        {
            if (readsToPass < 0)
            {
                throw new ArgumentOutOfRangeException("readsToPass");
            }
            _rolledBackReadsToPass = readsToPass;
        }

        public static bool ConsumeRolledBackReadFailure()
        {
            if (_rolledBackReadsToPass < 0)
            {
                return false;
            }
            if (_rolledBackReadsToPass > 0)
            {
                _rolledBackReadsToPass--;
                return false;
            }
            _rolledBackReadsToPass = -1;
            return true;
        }
    }

    /// <summary>Private equivalent of Revit's fatal regeneration exception.</summary>
    internal sealed class RegenerationFailureProbeException : Exception
    {
        public RegenerationFailureProbeException()
            : base("Simulated RegenerationFailedException.")
        {
        }
    }
}
