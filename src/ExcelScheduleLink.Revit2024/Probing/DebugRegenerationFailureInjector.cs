using System;

namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Probe-manifest-only one-shot hook for a regeneration-failure
    /// equivalent. Revit's
    /// RegenerationFailedException cannot be constructed by the probe, so the
    /// private marker exception is handled identically at every transaction
    /// boundary that handles the native exception.
    /// </summary>
    internal static class DebugRegenerationFailureInjector
    {
        private static bool _failNext;

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
