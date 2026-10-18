using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    // The real types are supplied by the Revit command flow (ReloadService.cs).
    // The screenshot harness includes UserMessages.cs for its dialog labels, so it
    // needs only these data-shaped stand-ins and never executes reload behavior.
    // Keep their member names and types in step with the real ones whenever
    // UserMessages starts reading a new member.
    internal enum ReloadResultKind
    {
        Success,
        NotLinked,
        SourceMissing,
        NetworkAccessCancelled,
        ReadFailed,
        ConfirmationRequired,
        Cancelled,
        RenderFailed,
        RegenerationFailed
    }

    internal sealed class ReloadOutcome
    {
        public ReloadResultKind Kind;
        public string Detail = string.Empty;
        public SourceIdentityState Identity;
        public bool UserConfirmed;
        public string ScheduleName;
        public int Rows;
        public int Columns;
        public int? PreviousColumns;
        public int SourceRows;
        public int SourceColumns;
        public bool Truncated;
        public bool ViewWidthProblem;
        public int ColumnWidthsRejected;
        public int RowHeightsRejected;
        public int? RowsMayHideText = 0;
        public bool UnsafeStylesRepaired;

        public bool IsSuccess
        {
            get { return Kind == ReloadResultKind.Success; }
        }

        public bool ColumnCountChanged
        {
            get { return IsSuccess && PreviousColumns.HasValue
                && PreviousColumns.Value > 0 && Columns > 0
                && PreviousColumns.Value != Columns; }
        }
    }
}
