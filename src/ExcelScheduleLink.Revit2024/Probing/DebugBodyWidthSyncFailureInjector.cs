namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Probe-manifest-only one-shot hook for a body-width synchronization
    /// failure. It is armed by the probe
    /// command and consumed only by the layout body's synchronization seam.
    /// </summary>
    internal static class DebugBodyWidthSyncFailureInjector
    {
        private static bool _failNext;

        public static void ArmNextBodyWidthSyncFailure()
        {
            _failNext = true;
        }

        public static bool ConsumeNextBodyWidthSyncFailure()
        {
            if (!_failNext)
            {
                return false;
            }
            _failNext = false;
            return true;
        }
    }
}
