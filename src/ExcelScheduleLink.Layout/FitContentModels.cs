using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ExcelScheduleLink.Layout
{
    public sealed class FitContentCell
    {
        public FitContentCell(int row, int column, int rowSpan, int columnSpan,
            string text)
        {
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            Text = text ?? string.Empty;
        }

        public int Row { get; }
        public int Column { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public string Text { get; }
    }

    public sealed class FitContentTable
    {
        public FitContentTable(int rowCount, int columnCount,
            IReadOnlyList<FitContentCell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException("cells");
            }

            RowCount = rowCount;
            ColumnCount = columnCount;
            Cells = Copy(cells);
        }

        public int RowCount { get; }
        public int ColumnCount { get; }
        public IReadOnlyList<FitContentCell> Cells { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class FitContentLineBin
    {
        internal FitContentLineBin(int halfUnits, int count)
        {
            HalfUnits = halfUnits;
            Count = count;
        }

        public int HalfUnits { get; }
        public int Count { get; }
    }

    public sealed class FitContentMeasuredCell
    {
        internal FitContentMeasuredCell(int row, int column, int rowSpan,
            int columnSpan, IReadOnlyList<FitContentLineBin> lines,
            IReadOnlyList<FitContentLineBin> fullLines, double maxGlyphUnits,
            double longestLineUnits, double preferredFeet,
            bool isProtectedValue, bool measurementCapped)
        {
            if (lines == null)
            {
                throw new ArgumentNullException("lines");
            }
            if (fullLines == null)
            {
                throw new ArgumentNullException("fullLines");
            }

            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            Lines = Copy(lines);
            FullLines = Copy(fullLines);
            MaxGlyphUnits = maxGlyphUnits;
            LongestLineUnits = longestLineUnits;
            PreferredFeet = preferredFeet;
            IsProtectedValue = isProtectedValue;
            MeasurementCapped = measurementCapped;
        }

        public int Row { get; }
        public int Column { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public IReadOnlyList<FitContentLineBin> Lines { get; }
        public IReadOnlyList<FitContentLineBin> FullLines { get; }
        public double MaxGlyphUnits { get; }
        public double LongestLineUnits { get; }
        public double PreferredFeet { get; }
        public bool IsProtectedValue { get; }
        public bool MeasurementCapped { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class FitContentMeasurements
    {
        internal FitContentMeasurements(int rowCount, int columnCount,
            double widthScale, double minColumnFeet, double maxColumnFeet,
            int headingRowCount, IReadOnlyList<FitContentMeasuredCell> cells,
            IReadOnlyList<bool> protectedColumns,
            IReadOnlyList<double> protectionRequiredFeet,
            IReadOnlyList<double> preferredColumnFeet,
            IReadOnlyList<double> textWeights, bool measurementCapped)
        {
            if (cells == null)
            {
                throw new ArgumentNullException("cells");
            }
            if (protectedColumns == null)
            {
                throw new ArgumentNullException("protectedColumns");
            }
            if (protectionRequiredFeet == null)
            {
                throw new ArgumentNullException("protectionRequiredFeet");
            }
            if (preferredColumnFeet == null)
            {
                throw new ArgumentNullException("preferredColumnFeet");
            }
            if (textWeights == null)
            {
                throw new ArgumentNullException("textWeights");
            }

            RowCount = rowCount;
            ColumnCount = columnCount;
            WidthScale = widthScale;
            MinColumnFeet = minColumnFeet;
            MaxColumnFeet = maxColumnFeet;
            HeadingRowCount = headingRowCount;
            Cells = Copy(cells);
            ProtectedColumns = Copy(protectedColumns);
            ProtectionRequiredFeet = Copy(protectionRequiredFeet);
            PreferredColumnFeet = Copy(preferredColumnFeet);
            TextWeights = Copy(textWeights);
            MeasurementCapped = measurementCapped;
        }

        public int RowCount { get; }
        public int ColumnCount { get; }
        public double WidthScale { get; }
        public double MinColumnFeet { get; }
        public double MaxColumnFeet { get; }
        public int HeadingRowCount { get; }
        public IReadOnlyList<FitContentMeasuredCell> Cells { get; }
        public IReadOnlyList<bool> ProtectedColumns { get; }
        public IReadOnlyList<double> ProtectionRequiredFeet { get; }
        public IReadOnlyList<double> PreferredColumnFeet { get; }
        public IReadOnlyList<double> TextWeights { get; }
        public bool MeasurementCapped { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public enum FitContentStage
    {
        InvalidInput = -1,
        OutOfRange = 0,
        Comfortable = 1,
        Constrained = 2
    }

    public enum FitContentLimitReason
    {
        None,
        BelowMinimum,
        AboveMaximum,
        NoColumns
    }

    public sealed class FixedTotalFitResult
    {
        internal FixedTotalFitResult(
            IReadOnlyList<double> feet,
            FitContentStage stage,
            FitContentLimitReason limitReason,
            bool canApply,
            double requestedTotalFeet,
            double actualTotalFeet,
            bool targetPreserved,
            IReadOnlyList<bool> protectedColumns,
            IReadOnlyList<int> protectionFailedColumns,
            IReadOnlyList<FitContentMergeDeficit> unmetMerges,
            IReadOnlyList<double> comfortableFeet,
            double comfortableTotalFeet,
            bool comfortableEstimateLimited,
            long estimatedRowLineTotal,
            long baselineRowLineTotal,
            long estimatedTallestRowLines,
            int optimizationTrials,
            bool optimizationBudgetExhausted)
        {
            Feet = Copy(feet, "feet");
            Stage = stage;
            LimitReason = limitReason;
            CanApply = canApply;
            RequestedTotalFeet = requestedTotalFeet;
            ActualTotalFeet = actualTotalFeet;
            TargetPreserved = targetPreserved;
            ProtectedColumns = Copy(protectedColumns, "protectedColumns");
            ProtectionFailedColumns = Copy(protectionFailedColumns,
                "protectionFailedColumns");
            UnmetMerges = Copy(unmetMerges, "unmetMerges");
            ComfortableFeet = Copy(comfortableFeet, "comfortableFeet");
            ComfortableTotalFeet = comfortableTotalFeet;
            ComfortableEstimateLimited = comfortableEstimateLimited;
            EstimatedRowLineTotal = estimatedRowLineTotal;
            BaselineRowLineTotal = baselineRowLineTotal;
            EstimatedTallestRowLines = estimatedTallestRowLines;
            OptimizationTrials = optimizationTrials;
            OptimizationBudgetExhausted = optimizationBudgetExhausted;
        }

        public IReadOnlyList<double> Feet { get; }
        public FitContentStage Stage { get; }
        public FitContentLimitReason LimitReason { get; }
        public bool CanApply { get; }
        public double RequestedTotalFeet { get; }
        public double ActualTotalFeet { get; }
        public bool TargetPreserved { get; }
        public IReadOnlyList<bool> ProtectedColumns { get; }
        public IReadOnlyList<int> ProtectionFailedColumns { get; }
        public IReadOnlyList<FitContentMergeDeficit> UnmetMerges { get; }
        public IReadOnlyList<double> ComfortableFeet { get; }
        public double ComfortableTotalFeet { get; }
        public bool ComfortableEstimateLimited { get; }
        public long EstimatedRowLineTotal { get; }
        public long BaselineRowLineTotal { get; }
        public long EstimatedTallestRowLines { get; }
        public int OptimizationTrials { get; }
        public bool OptimizationBudgetExhausted { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values,
            string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class FitContentMergeDeficit
    {
        internal FitContentMergeDeficit(int row, int column, int rowSpan,
            int columnSpan, double requiredFeet, double actualFeet)
        {
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            RequiredFeet = requiredFeet;
            ActualFeet = actualFeet;
            DeficitFeet = Math.Max(0.0, requiredFeet - actualFeet);
        }

        public int Row { get; }
        public int Column { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public double RequiredFeet { get; }
        public double ActualFeet { get; }
        public double DeficitFeet { get; }
    }
}
