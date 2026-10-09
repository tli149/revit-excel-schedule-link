using System;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// Parser for "B13"-style cell references and "A1:F30"-style range references.
    /// Rows and columns are returned 0-based.
    /// </summary>
    internal static class CellReference
    {
        /// <summary>"BC23" → (row=22, col=54). Throws ExcelReadException on failure.</summary>
        public static void Parse(string reference, out int row, out int column)
        {
            if (string.IsNullOrEmpty(reference))
            {
                throw new ExcelReadException(Messages.CellReferenceEmpty);
            }

            int i = 0;
            long col = 0L;
            while (i < reference.Length && IsAsciiLetter(reference[i]))
            {
                col = checked(col * 26L + (char.ToUpperInvariant(reference[i]) - 'A' + 1L));
                if (col > WorkbookReadLimits.ExcelMaximumColumns)
                {
                    throw new ExcelReadException(Messages.CellReferenceOutOfBounds(
                        reference, "column", WorkbookReadLimits.ExcelMaximumColumns));
                }
                i++;
            }
            if (i == 0 || i >= reference.Length)
            {
                throw new ExcelReadException(Messages.CellReferenceInvalid(reference));
            }

            long rowOneBased;
            if (!long.TryParse(reference.Substring(i), out rowOneBased) || rowOneBased < 1)
            {
                throw new ExcelReadException(Messages.CellReferenceInvalidRow(reference));
            }
            if (rowOneBased > WorkbookReadLimits.ExcelMaximumRows)
            {
                throw new ExcelReadException(Messages.CellReferenceOutOfBounds(
                    reference, "row", WorkbookReadLimits.ExcelMaximumRows));
            }

            row = checked((int)rowOneBased - 1);
            column = checked((int)col) - 1;
        }

        /// <summary>
        /// "A1:F30" → 0-based, inclusive range. A single cell like "B2" is
        /// also accepted as a one-cell range.
        /// </summary>
        public static void ParseRange(
            string rangeAddress,
            out int firstRow, out int firstColumn, out int lastRow, out int lastColumn)
        {
            if (string.IsNullOrEmpty(rangeAddress))
            {
                throw new ExcelReadException(Messages.RangeEmpty);
            }

            string[] parts = rangeAddress.Split(':');
            if (parts.Length == 1)
            {
                Parse(parts[0], out firstRow, out firstColumn);
                lastRow = firstRow;
                lastColumn = firstColumn;
                return;
            }
            if (parts.Length != 2)
            {
                throw new ExcelReadException(Messages.RangeInvalid(rangeAddress));
            }

            Parse(parts[0], out firstRow, out firstColumn);
            Parse(parts[1], out lastRow, out lastColumn);
            if (lastRow < firstRow || lastColumn < firstColumn)
            {
                throw new ExcelReadException(Messages.RangeReversed(rangeAddress));
            }
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }
}
