using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    // The real outcome type is supplied by the Revit command flow. The
    // screenshot harness includes UserMessages.cs for its dialog labels, so it
    // needs only this data-shaped stand-in and never executes reload behavior.
    internal sealed class ReloadOutcome
    {
        public SourceIdentityState Identity;
        public bool UserConfirmed;
        public string ScheduleName;
        public int Rows;
        public int Columns;
        public bool UnsafeStylesRepaired;
    }
}
