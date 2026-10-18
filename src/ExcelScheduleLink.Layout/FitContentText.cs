using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ExcelScheduleLink.Layout
{
    public static class FitContentText
    {
        private const string NumberCore =
            @"(?:[0-9]+(?:[.,][0-9]+)?|[0-9]{1,3}(?:,[0-9]{3})+(?:\.[0-9]+)?|[0-9]{1,3}(?:\.[0-9]{3})+(?:,[0-9]+)?|[0-9]{1,3}(?: [0-9]{3})+(?:[.,][0-9]+)?)";
        private const string UnitSuffix =
            @"(?:%|mm|cm|m|km|mm2|mm3|cm2|cm3|m2|m3|mm²|mm³|cm²|cm³|m²|m³|㎡|㎥|in|ft|kg|g|t|N|kN|Pa|kPa|MPa|V|W|kW|A|Hz|°C|°F|℃|℉|pcs|EA)";
        // Complete numeric pattern, assembled in one place:
        // \A(?:[+-]?(?:[$€£¥] ?)?NumberCore(?: ?UnitSuffix)?|\((?:[$€£¥] ?)?NumberCore(?: ?UnitSuffix)?\))\z
        private const string NumericPattern =
            @"\A(?:[+-]?(?:[$€£¥] ?)?" + NumberCore
            + @"(?: ?" + UnitSuffix + @")?|\((?:[$€£¥] ?)?"
            + NumberCore + @"(?: ?" + UnitSuffix + @")?\))\z";
        private const string YearFirstDate =
            @"\A[0-9]{4}([./-])[0-9]{1,2}\1[0-9]{1,2}\z";
        private const string YearLastDate =
            @"\A[0-9]{1,2}([./-])[0-9]{1,2}\1[0-9]{4}\z";
        private const string JapaneseDate =
            @"\A[0-9]{4}年[0-9]{1,2}月[0-9]{1,2}日\z";
        // Code: require at least one ASCII letter AND one digit, no spaces,
        // length <= FitContentCodeMaxCharacters, and this whole shape:
        private const string CodeShape = @"\A[A-Za-z0-9][A-Za-z0-9._/#:+-]*\z";

        private static readonly Regex NumberRegex = new Regex(
            NumericPattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex YearFirstDateRegex = new Regex(
            YearFirstDate, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex YearLastDateRegex = new Regex(
            YearLastDate, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex JapaneseDateRegex = new Regex(
            JapaneseDate, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex CodeRegex = new Regex(
            CodeShape, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsProtectedValue(string text)
        {
            return IsProtectedNormalized(NormalizeForClassification(text));
        }

        public static string[] GetColumnHeadings(FitContentTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            string[] headings = new string[table.ColumnCount < 0
                ? 0 : table.ColumnCount];
            int[] headingRows = new int[headings.Length];
            for (int column = 0; column < headingRows.Length; column++)
            {
                headings[column] = string.Empty;
                headingRows[column] = int.MaxValue;
            }

            for (int i = 0; i < table.Cells.Count; i++)
            {
                FitContentCell cell = table.Cells[i];
                if (cell == null || cell.ColumnSpan != 1
                    || cell.Column < 0 || cell.Column >= headings.Length)
                {
                    continue;
                }

                string heading = NormalizeHeading(cell.Text);
                if (heading.Length == 0 || cell.Row >= headingRows[cell.Column])
                {
                    continue;
                }

                headings[cell.Column] = TruncateHeading(heading);
                headingRows[cell.Column] = cell.Row;
            }
            return headings;
        }

        public static FitContentMeasurements Prepare(FitContentTable table,
            double widthScale, int headingRowCount,
            double minColumnFeet, double maxColumnFeet)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            try
            {
                ValidateInputs(table, widthScale, headingRowCount,
                    minColumnFeet, maxColumnFeet);

                List<FitContentCell> anchors = CopyAndSortCells(table.Cells);
                ValidateShape(table.RowCount, table.ColumnCount, anchors);

                int effectiveHeadingRows = Math.Min(headingRowCount, table.RowCount);
                if (table.RowCount == 0 && table.ColumnCount == 0)
                {
                    return new FitContentMeasurements(
                        0, 0, widthScale, minColumnFeet, maxColumnFeet,
                        effectiveHeadingRows,
                        new List<FitContentMeasuredCell>(),
                        new bool[0], new double[0], new double[0],
                        new double[0], false);
                }

                bool[] protectedColumns = new bool[table.ColumnCount];
                long[] sampleCounts = new long[table.ColumnCount];
                long[] matchingCounts = new long[table.ColumnCount];
                double[] matchingProtectionFeet = new double[table.ColumnCount];
                double[] preferredColumnFeet = new double[table.ColumnCount];
                double[] textWeights = new double[table.ColumnCount];
                for (int column = 0; column < table.ColumnCount; column++)
                {
                    preferredColumnFeet[column] = minColumnFeet;
                }

                List<FitContentMeasuredCell> measuredCells =
                    new List<FitContentMeasuredCell>(anchors.Count);
                bool measurementCapped = false;

                for (int i = 0; i < anchors.Count; i++)
                {
                    FitContentCell cell = anchors[i];
                    TextMeasurement measurement = MeasureText(cell.Text);
                    double preferredFeet = ComputeScaledWidthFeet(
                        widthScale,
                        Math.Min(measurement.LongestLineUnits,
                            LayoutMetrics.MaxContentUnits));
                    FitContentMeasuredCell measuredCell =
                        new FitContentMeasuredCell(
                            cell.Row, cell.Column, cell.RowSpan,
                            cell.ColumnSpan, measurement.Lines,
                            measurement.FullLines, measurement.MaxGlyphUnits,
                            measurement.LongestLineUnits, preferredFeet,
                            measurement.IsProtectedValue,
                            measurement.MeasurementCapped);
                    measuredCells.Add(measuredCell);

                    if (cell.ColumnSpan == 1
                        && preferredFeet > preferredColumnFeet[cell.Column])
                    {
                        preferredColumnFeet[cell.Column] = preferredFeet;
                    }

                    double perColumnWeight =
                        (measurement.CappedLineHalfUnits / 2.0) / cell.ColumnSpan;
                    EnsureFinite(perColumnWeight,
                        "The text weight is too large.");
                    int columnEnd = checked(cell.Column + cell.ColumnSpan);
                    for (int column = cell.Column; column < columnEnd; column++)
                    {
                        textWeights[column] += perColumnWeight;
                        EnsureFinite(textWeights[column],
                            "The text weight is too large.");
                    }

                    if (cell.Row >= effectiveHeadingRows
                        && cell.RowSpan == 1 && cell.ColumnSpan == 1
                        && !IsClassificationBlank(measurement.ClassificationText))
                    {
                        int column = cell.Column;
                        sampleCounts[column] = checked(sampleCounts[column] + 1L);
                        if (measurement.IsProtectedValue)
                        {
                            matchingCounts[column] =
                                checked(matchingCounts[column] + 1L);
                            double demand = ComputeScaledWidthFeet(
                                widthScale, measurement.LongestLineUnits);
                            EnsureFinite(demand,
                                "The protection width is too large.");
                            if (demand > matchingProtectionFeet[column])
                            {
                                matchingProtectionFeet[column] = demand;
                            }
                        }
                    }

                    measurementCapped = measurementCapped
                        || measurement.MeasurementCapped;
                }

                double[] protectionRequiredFeet =
                    new double[table.ColumnCount];
                for (int column = 0; column < table.ColumnCount; column++)
                {
                    long samples = sampleCounts[column];
                    long matches = matchingCounts[column];
                    bool protects = samples >= LayoutMetrics.FitContentProtectionMinSamples
                        && checked(matches * 100L)
                            >= checked(samples * LayoutMetrics.FitContentProtectionPercent);
                    protectedColumns[column] = protects;
                    protectionRequiredFeet[column] = protects
                        ? matchingProtectionFeet[column]
                        : minColumnFeet;
                    preferredColumnFeet[column] = ClampToBounds(
                        preferredColumnFeet[column], minColumnFeet,
                        maxColumnFeet);
                    EnsureFinite(protectionRequiredFeet[column],
                        "The protection width is too large.");
                    EnsureFinite(preferredColumnFeet[column],
                        "The preferred width is too large.");
                }

                return new FitContentMeasurements(
                    table.RowCount, table.ColumnCount, widthScale,
                    minColumnFeet, maxColumnFeet, effectiveHeadingRows,
                    measuredCells, protectedColumns, protectionRequiredFeet,
                    preferredColumnFeet, textWeights, measurementCapped);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    "The fit-content input exceeds supported numeric bounds.",
                    "table", exception);
            }
        }

        public static long EstimateLineCount(FitContentMeasuredCell cell,
            double spanFeet, double widthScale)
        {
            if (cell == null)
            {
                throw new ArgumentNullException("cell");
            }
            ValidatePositiveFinite(spanFeet, "spanFeet");
            ValidatePositiveFinite(widthScale, "widthScale");

            double halfUnitFeet = widthScale
                * LayoutMetrics.HalfWidthInchesPerUnit
                / LayoutMetrics.InchesPerFoot;
            double horizontalPaddingFeet = widthScale
                * LayoutMetrics.CellPaddingInches
                / LayoutMetrics.InchesPerFoot;
            EnsurePositiveFinite(halfUnitFeet, "The width unit is too small.");
            EnsureFiniteNonnegative(horizontalPaddingFeet,
                "The horizontal padding is too large.");

            double capacity = CapacityForSpan(spanFeet, halfUnitFeet,
                horizontalPaddingFeet);
            if (capacity >= LayoutMetrics.MaxContentUnits)
            {
                return CountExplicitLines(cell.Lines);
            }

            return CountLineBins(cell.Lines, spanFeet, halfUnitFeet,
                horizontalPaddingFeet);
        }

        public static long EstimateRequiredLineCount(
            FitContentMeasuredCell cell, double spanFeet,
            double halfUnitFeet, double horizontalPaddingFeet)
        {
            if (cell == null)
            {
                throw new ArgumentNullException("cell");
            }
            ValidatePositiveFinite(spanFeet, "spanFeet");
            EnsurePositiveFinite(halfUnitFeet, "halfUnitFeet");
            EnsureFiniteNonnegative(horizontalPaddingFeet,
                "horizontalPaddingFeet");

            return CountLineBins(cell.FullLines, spanFeet, halfUnitFeet,
                horizontalPaddingFeet);
        }

        private static long CountLineBins(
            IReadOnlyList<FitContentLineBin> lineBins,
            double spanFeet, double halfUnitFeet,
            double horizontalPaddingFeet)
        {
            if (lineBins == null)
            {
                throw new ArgumentNullException("lineBins");
            }
            EnsurePositiveFinite(spanFeet, "spanFeet");
            EnsurePositiveFinite(halfUnitFeet, "halfUnitFeet");
            EnsureFiniteNonnegative(horizontalPaddingFeet,
                "horizontalPaddingFeet");

            double capacity = CapacityForSpan(spanFeet, halfUnitFeet,
                horizontalPaddingFeet);
            long total = 0L;
            for (int i = 0; i < lineBins.Count; i++)
            {
                FitContentLineBin bin = lineBins[i];
                if (bin == null || bin.HalfUnits < 0 || bin.Count <= 0)
                {
                    throw new ArgumentException(
                        "Line bins must contain nonnegative lengths and positive counts.",
                        "lineBins");
                }

                double units = bin.HalfUnits / 2.0;
                double numerator = units
                    - LayoutMetrics.FitContentLineToleranceUnits;
                if (numerator < 0.0)
                {
                    numerator = 0.0;
                }

                double lineCount = Math.Ceiling(numerator / capacity);
                if (double.IsNaN(lineCount)
                    || double.IsInfinity(lineCount)
                    || lineCount > long.MaxValue)
                {
                    throw new ArgumentException(
                        "The estimated line count is too large.",
                        "lineBins");
                }

                long lines = lineCount < 1.0 ? 1L : checked((long)lineCount);
                total = checked(total + checked((long)bin.Count * lines));
            }

            return total;
        }

        internal static long CountExplicitLines(
            IReadOnlyList<FitContentLineBin> lineBins)
        {
            if (lineBins == null)
            {
                throw new ArgumentNullException("lineBins");
            }

            long total = 0L;
            for (int i = 0; i < lineBins.Count; i++)
            {
                FitContentLineBin bin = lineBins[i];
                if (bin == null || bin.Count <= 0)
                {
                    throw new ArgumentException(
                        "Line bins must contain positive counts.",
                        "lineBins");
                }
                total = checked(total + bin.Count);
            }
            return total;
        }

        private static double CapacityForSpan(double spanFeet,
            double halfUnitFeet, double horizontalPaddingFeet)
        {
            double availableFeet = spanFeet - horizontalPaddingFeet;
            double rawCapacity = availableFeet / halfUnitFeet;
            if (double.IsNaN(rawCapacity))
            {
                throw new ArgumentException(
                    "The span capacity is not a number.", "spanFeet");
            }
            return Math.Max(LayoutMetrics.FitContentMinCapacityUnits,
                rawCapacity);
        }

        private static void ValidateInputs(FitContentTable table,
            double widthScale, int headingRowCount,
            double minColumnFeet, double maxColumnFeet)
        {
            if (table.RowCount < 0)
            {
                throw new ArgumentException(
                    "The row count cannot be negative.", "table");
            }
            if (table.ColumnCount < 0)
            {
                throw new ArgumentException(
                    "The column count cannot be negative.", "table");
            }
            if (headingRowCount < 0)
            {
                throw new ArgumentException(
                    "The heading row count cannot be negative.",
                    "headingRowCount");
            }
            ValidatePositiveFinite(widthScale, "widthScale");
            ValidatePositiveFinite(minColumnFeet, "minColumnFeet");
            ValidatePositiveFinite(maxColumnFeet, "maxColumnFeet");
            if (minColumnFeet > maxColumnFeet)
            {
                throw new ArgumentException(
                    "The minimum column width cannot exceed the maximum.",
                    "minColumnFeet");
            }

            if ((table.RowCount == 0 || table.ColumnCount == 0)
                && (table.RowCount != 0 || table.ColumnCount != 0
                    || table.Cells.Count != 0))
            {
                throw new ArgumentException(
                    "A zero dimension requires an empty zero-by-zero table.",
                    "table");
            }

            double columnMaximum = table.ColumnCount * maxColumnFeet;
            EnsureFinite(columnMaximum, "The column maximum is too large.");
        }

        private static List<FitContentCell> CopyAndSortCells(
            IReadOnlyList<FitContentCell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException("cells");
            }

            List<FitContentCell> result = new List<FitContentCell>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == null)
                {
                    throw new ArgumentException(
                        "The table cannot contain a null cell.", "cells");
                }
                result.Add(cells[i]);
            }
            result.Sort(CompareCells);
            return result;
        }

        private static int CompareCells(FitContentCell left,
            FitContentCell right)
        {
            int row = left.Row.CompareTo(right.Row);
            return row != 0 ? row : left.Column.CompareTo(right.Column);
        }

        private static void ValidateShape(int rowCount, int columnCount,
            List<FitContentCell> anchors)
        {
            int gridSize = checked(rowCount * columnCount);
            if (rowCount == 0 && columnCount == 0)
            {
                if (anchors.Count != 0)
                {
                    throw new ArgumentException(
                        "A zero-by-zero table cannot contain anchors.",
                        "table");
                }
                return;
            }

            if (anchors.Count == 0)
            {
                throw new ArgumentException(
                    "A nonempty table must contain anchors.", "table");
            }

            bool[,] occupied = new bool[rowCount, columnCount];
            int covered = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                FitContentCell cell = anchors[i];
                if (cell.Row < 0 || cell.Column < 0
                    || cell.RowSpan <= 0 || cell.ColumnSpan <= 0)
                {
                    throw new ArgumentException(
                        "Anchor coordinates and spans must be positive and in bounds.",
                        "table");
                }

                int rowEnd = checked(cell.Row + cell.RowSpan);
                int columnEnd = checked(cell.Column + cell.ColumnSpan);
                if (rowEnd > rowCount || columnEnd > columnCount)
                {
                    throw new ArgumentException(
                        "An anchor extends outside the table.", "table");
                }

                for (int row = cell.Row; row < rowEnd; row++)
                {
                    for (int column = cell.Column;
                        column < columnEnd; column++)
                    {
                        if (occupied[row, column])
                        {
                            throw new ArgumentException(
                                "Anchors cannot overlap.", "table");
                        }
                        occupied[row, column] = true;
                        covered = checked(covered + 1);
                    }
                }
            }

            if (covered != gridSize)
            {
                throw new ArgumentException(
                    "Anchors must cover every table position exactly once.",
                    "table");
            }
        }

        private static TextMeasurement MeasureText(string text)
        {
            SortedDictionary<int, int> cappedBins =
                new SortedDictionary<int, int>();
            SortedDictionary<int, int> fullBins =
                new SortedDictionary<int, int>();
            long lineHalfUnits = 0L;
            long cappedLineHalfUnits = 0L;
            double maxGlyphUnits = 0.0;
            double longestLineUnits = 0.0;
            bool measurementCapped = false;
            int maxContentHalfUnits = checked((int)(
                LayoutMetrics.MaxContentUnits * 2.0));

            for (int i = 0; i < text.Length; i++)
            {
                char original = text[i];
                if (original == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    AddLine(cappedBins, fullBins, lineHalfUnits,
                        ref cappedLineHalfUnits, ref longestLineUnits,
                        ref measurementCapped, maxContentHalfUnits);
                    lineHalfUnits = 0L;
                    continue;
                }
                if (original == '\n')
                {
                    AddLine(cappedBins, fullBins, lineHalfUnits,
                        ref cappedLineHalfUnits, ref longestLineUnits,
                        ref measurementCapped, maxContentHalfUnits);
                    lineHalfUnits = 0L;
                    continue;
                }

                double units = CharWidth.UnitsFor(original);
                EnsureFiniteNonnegative(units,
                    "A character width is not finite.");
                if (units > maxGlyphUnits)
                {
                    maxGlyphUnits = units;
                }
                long halfUnits = ToHalfUnits(units);
                lineHalfUnits = checked(lineHalfUnits + halfUnits);
            }

            AddLine(cappedBins, fullBins, lineHalfUnits,
                ref cappedLineHalfUnits, ref longestLineUnits,
                ref measurementCapped, maxContentHalfUnits);

            List<FitContentLineBin> lines = ToBins(cappedBins);
            List<FitContentLineBin> fullLines = ToBins(fullBins);
            string normalized = NormalizeForClassification(text);
            return new TextMeasurement(
                lines, fullLines, maxGlyphUnits, longestLineUnits,
                cappedLineHalfUnits,
                IsProtectedNormalized(normalized), normalized,
                measurementCapped);
        }

        private static void AddLine(
            SortedDictionary<int, int> cappedBins,
            SortedDictionary<int, int> fullBins,
            long lineHalfUnits, ref long cappedLineHalfUnits,
            ref double longestLineUnits, ref bool measurementCapped,
            int maxContentHalfUnits)
        {
            if (lineHalfUnits < 0 || lineHalfUnits > int.MaxValue)
            {
                throw new OverflowException();
            }

            int fullHalfUnits = checked((int)lineHalfUnits);
            int cappedHalfUnits = checked((int)Math.Min(
                lineHalfUnits, (long)maxContentHalfUnits));
            AddBin(fullBins, fullHalfUnits);
            AddBin(cappedBins, cappedHalfUnits);
            cappedLineHalfUnits = checked(
                cappedLineHalfUnits + cappedHalfUnits);
            double lineUnits = lineHalfUnits / 2.0;
            if (lineUnits > longestLineUnits)
            {
                longestLineUnits = lineUnits;
            }
            if (lineHalfUnits > maxContentHalfUnits)
            {
                measurementCapped = true;
            }
        }

        private static void AddBin(SortedDictionary<int, int> bins,
            int halfUnits)
        {
            int count;
            if (bins.TryGetValue(halfUnits, out count))
            {
                bins[halfUnits] = checked(count + 1);
            }
            else
            {
                bins.Add(halfUnits, 1);
            }
        }

        private static List<FitContentLineBin> ToBins(
            SortedDictionary<int, int> bins)
        {
            List<FitContentLineBin> result =
                new List<FitContentLineBin>(bins.Count);
            foreach (KeyValuePair<int, int> pair in bins)
            {
                result.Add(new FitContentLineBin(pair.Key, pair.Value));
            }
            return result;
        }

        private static long ToHalfUnits(double units)
        {
            double halfUnits = units * 2.0;
            if (double.IsNaN(halfUnits)
                || double.IsInfinity(halfUnits)
                || halfUnits < 0.0
                || halfUnits != Math.Truncate(halfUnits)
                || halfUnits > long.MaxValue)
            {
                throw new OverflowException();
            }
            return checked((long)halfUnits);
        }

        private static double ComputeScaledWidthFeet(double widthScale,
            double units)
        {
            EnsureFiniteNonnegative(units, "The measured width is invalid.");
            double baseFeet = (units * LayoutMetrics.HalfWidthInchesPerUnit
                + LayoutMetrics.CellPaddingInches)
                / LayoutMetrics.InchesPerFoot;
            EnsureFiniteNonnegative(baseFeet,
                "The base width is too large.");
            double result = widthScale * baseFeet;
            EnsureFiniteNonnegative(result,
                "The scaled width is too large.");
            return result;
        }

        private static double ClampToBounds(double value, double min,
            double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static string NormalizeForClassification(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                builder.Append(NormalizeCharacter(text[i]));
            }
            return TrimAsciiSpaceAndTab(builder.ToString());
        }

        private static string NormalizeHeading(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n")
                .Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                StringBuilder builder = new StringBuilder(line.Length);
                bool pendingSpace = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char character = line[i];
                    if (char.IsWhiteSpace(character))
                    {
                        pendingSpace = true;
                        continue;
                    }
                    if (pendingSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }
                    builder.Append(character);
                    pendingSpace = false;
                }
                return builder.ToString().Trim();
            }
            return string.Empty;
        }

        private static string TruncateHeading(string heading)
        {
            if (heading.Length <= LayoutMetrics.ColumnHeadingMaxLength)
            {
                return heading;
            }

            int length = LayoutMetrics.ColumnHeadingMaxLength - 1;
            if (length > 0 && length < heading.Length
                && char.IsHighSurrogate(heading[length - 1])
                && char.IsLowSurrogate(heading[length]))
            {
                length--;
            }
            return heading.Substring(0, length) + '\u2026';
        }

        private static char NormalizeCharacter(char character)
        {
            int value = character;
            if (value >= 0xFF01 && value <= 0xFF5E)
            {
                return (char)(value - 0xFEE0);
            }
            if (value == 0x3000)
            {
                return ' ';
            }
            if (value == 0x2212)
            {
                return '-';
            }
            if (value == 0xFFE5)
            {
                return '\u00A5';
            }
            return character;
        }

        private static string TrimAsciiSpaceAndTab(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && IsAsciiSpaceOrTab(value[start]))
            {
                start++;
            }
            while (end > start && IsAsciiSpaceOrTab(value[end - 1]))
            {
                end--;
            }
            return start == 0 && end == value.Length
                ? value
                : value.Substring(start, end - start);
        }

        private static bool IsClassificationBlank(string value)
        {
            if (value.Length == 0)
            {
                return true;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character != ' ' && character != '\t'
                    && character != '\r' && character != '\n')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiSpaceOrTab(char character)
        {
            return character == ' ' || character == '\t';
        }

        private static bool IsProtectedNormalized(string normalized)
        {
            if (normalized.Length == 0
                || normalized.Length > LayoutMetrics.FitContentProtectionMaxCharacters)
            {
                return false;
            }
            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if (character == '\t' || character == '\r'
                    || character == '\n')
                {
                    return false;
                }
            }

            if (NumberRegex.IsMatch(normalized)
                || YearFirstDateRegex.IsMatch(normalized)
                || YearLastDateRegex.IsMatch(normalized)
                || JapaneseDateRegex.IsMatch(normalized))
            {
                return true;
            }

            if (normalized.Length > LayoutMetrics.FitContentCodeMaxCharacters
                || !CodeRegex.IsMatch(normalized))
            {
                return false;
            }

            bool hasLetter = false;
            bool hasDigit = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if ((character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z'))
                {
                    hasLetter = true;
                }
                else if (character >= '0' && character <= '9')
                {
                    hasDigit = true;
                }
            }
            return hasLetter && hasDigit;
        }

        private static void ValidatePositiveFinite(double value,
            string parameterName)
        {
            if (value <= 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(
                    "The value must be finite and positive.", parameterName);
            }
        }

        private static void EnsurePositiveFinite(double value,
            string message)
        {
            if (value <= 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }

        private static void EnsureFiniteNonnegative(double value,
            string message)
        {
            if (value < 0.0 || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }

        private static void EnsureFinite(double value, string message)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException(message);
            }
        }

        private sealed class TextMeasurement
        {
            internal TextMeasurement(List<FitContentLineBin> lines,
                List<FitContentLineBin> fullLines, double maxGlyphUnits,
                double longestLineUnits, long cappedLineHalfUnits,
                bool isProtectedValue, string classificationText,
                bool measurementCapped)
            {
                Lines = lines;
                FullLines = fullLines;
                MaxGlyphUnits = maxGlyphUnits;
                LongestLineUnits = longestLineUnits;
                CappedLineHalfUnits = cappedLineHalfUnits;
                IsProtectedValue = isProtectedValue;
                ClassificationText = classificationText;
                MeasurementCapped = measurementCapped;
            }

            internal List<FitContentLineBin> Lines { get; }
            internal List<FitContentLineBin> FullLines { get; }
            internal double MaxGlyphUnits { get; }
            internal double LongestLineUnits { get; }
            internal long CappedLineHalfUnits { get; }
            internal bool IsProtectedValue { get; }
            internal string ClassificationText { get; }
            internal bool MeasurementCapped { get; }
        }
    }
}
