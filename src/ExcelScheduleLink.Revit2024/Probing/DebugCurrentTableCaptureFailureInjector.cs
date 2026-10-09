namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Probe-manifest-only one-shot hook for a current-table capture failure.
    /// It is armed by the probe
    /// command and consumed immediately before the next current-table cell
    /// text read. Production code never arms this hook.
    /// </summary>
    internal static class DebugCurrentTableCaptureFailureInjector
    {
        private static bool _failNext;

        public static void ArmNextCaptureFailure()
        {
            _failNext = true;
        }

        public static bool ConsumeNextCaptureFailure()
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
