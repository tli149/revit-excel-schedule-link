using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ExcelScheduleLink.Layout
{
    public sealed class RowFitTextMetrics
    {
        public RowFitTextMetrics(double textSizeFeet)
        {
            TextSizeFeet = textSizeFeet;
        }

        public double TextSizeFeet { get; }
    }

    public sealed class RowFitResult
    {
        internal RowFitResult(IReadOnlyList<double> requiredFeet,
            IReadOnlyList<long> requiredLines,
            IReadOnlyList<FitContentCellPosition> tooNarrowCells,
            IReadOnlyList<int> tooNarrowRows, bool canFit)
        {
            if (requiredFeet == null)
            {
                throw new ArgumentNullException("requiredFeet");
            }
            if (requiredLines == null)
            {
                throw new ArgumentNullException("requiredLines");
            }
            if (tooNarrowCells == null)
            {
                throw new ArgumentNullException("tooNarrowCells");
            }
            if (tooNarrowRows == null)
            {
                throw new ArgumentNullException("tooNarrowRows");
            }

            RequiredFeet = Copy(requiredFeet);
            RequiredLines = Copy(requiredLines);
            TooNarrowCells = Copy(tooNarrowCells);
            TooNarrowRows = Copy(tooNarrowRows);
            CanFit = canFit;
        }

        public IReadOnlyList<double> RequiredFeet { get; }
        public IReadOnlyList<long> RequiredLines { get; }
        public IReadOnlyList<FitContentCellPosition> TooNarrowCells { get; }
        public IReadOnlyList<int> TooNarrowRows { get; }
        public bool CanFit { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class FitContentCellPosition
    {
        internal FitContentCellPosition(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }
    }
}
