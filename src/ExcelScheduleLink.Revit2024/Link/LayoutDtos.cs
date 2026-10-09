using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Data-only results passed between the Layout services and the Column Widths
    /// dialog. Deliberately free of Revit types so the dialog (and a rendering
    /// harness) can use them without touching the Revit API.
    /// </summary>
    internal sealed class FitContentResult
    {
        public double[] Feet;
        /// <summary>Feet a merged span still needs but could not get (columns at Max).</summary>
        public double UnresolvedDeficit;
    }

    /// <summary>Outcome of applying candidate column widths to a linked schedule.</summary>
    internal sealed class LayoutApplyResult
    {
        public bool Success;
        public bool FatalRegenerationFailure;
        public string UserMessage;
        public int[] InvalidColumns; // set when validation failed before any transaction

        public static LayoutApplyResult Failed(string message, int[] invalidColumns)
        {
            return new LayoutApplyResult { Success = false, UserMessage = message, InvalidColumns = invalidColumns };
        }

        public static LayoutApplyResult RegenerationFailed()
        {
            return new LayoutApplyResult
            {
                Success = false,
                FatalRegenerationFailure = true,
                UserMessage = UserMessages.RegenerationFailure
            };
        }
    }

    /// <summary>Fingerprint captured by Restore Excel Widths before applying it.</summary>
    internal sealed class LayoutSourceSnapshot
    {
        public SourceFingerprint Fingerprint;
        public string ResolvedPath = string.Empty;
    }

    /// <summary>Result of re-reading the column widths from the linked worksheet.</summary>
    internal sealed class ExcelWidthResult
    {
        public bool Success;
        public double[] Feet;        // set on success (scaled + clamped)
        public string UserMessage;   // set on failure
        public LayoutSourceSnapshot SourceSnapshot;

        public static ExcelWidthResult Failed(string message)
        {
            return new ExcelWidthResult { Success = false, UserMessage = message };
        }
    }
}
