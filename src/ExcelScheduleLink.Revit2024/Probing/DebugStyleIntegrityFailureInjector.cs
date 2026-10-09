namespace ExcelScheduleLink.Revit2024.Probing
{
    /// <summary>
    /// Probe-manifest-only one-shot hook for a post-render style-validation
    /// failure. It is armed by the probe
    /// command and consumed only by the renderer's validation guard. The type
    /// is present in Release because probe commands share the product DLL, but
    /// its default is false and production code never arms it; the production
    /// manifest and ribbon expose no command that can arm it.
    /// </summary>
    internal static class DebugStyleIntegrityFailureInjector
    {
        private static bool _failNext;

        public static void ArmNextValidationFailure()
        {
            _failNext = true;
        }

        public static bool ConsumeNextValidationFailure()
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
