namespace ExcelScheduleLink.Layout
{
    /// <summary>
    /// Classifies characters by display width so CJK (full-width) text is not
    /// under-measured as if it were ASCII -- the root cause of columns being too
    /// narrow for Japanese content and wrapping character-by-character.
    ///
    /// This is a pragmatic approximation of the Unicode East_Asian_Width
    /// property (Wide / Fullwidth => full-width) covering Japanese, Korean and
    /// full-width forms. It can be replaced by a complete East_Asian_Width table
    /// later without changing callers. Only the BMP is inspected; a supplementary
    /// CJK ideograph arrives as a surrogate pair (two chars) and is measured as
    /// two half-width units, which approximates one full-width glyph.
    /// </summary>
    public static class CharWidth
    {
        /// <summary>Width of a single character in half-width units.</summary>
        public static double UnitsFor(char c)
        {
            if (c == ' ')
            {
                return LayoutMetrics.SpaceUnits;
            }
            return IsFullWidth(c) ? LayoutMetrics.FullWidthUnits : LayoutMetrics.HalfWidthUnits;
        }

        /// <summary>
        /// Half-width units of the widest single line in the text: a cell with
        /// embedded newlines is as wide as its longest line, not the sum of all
        /// lines. Handles CR, LF and CRLF. Returns 0 for null / empty.
        /// </summary>
        public static double MeasureLongestLineUnits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0.0;
            }
            double best = 0.0;
            double line = 0.0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    if (line > best) best = line;
                    line = 0.0;
                    // CRLF counts as one break
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    continue;
                }
                line += UnitsFor(c);
            }
            return line > best ? line : best;
        }

        /// <summary>Total half-width units of a string (0 for null / empty).</summary>
        public static double MeasureUnits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0.0;
            }
            double units = 0.0;
            for (int i = 0; i < text.Length; i++)
            {
                units += UnitsFor(text[i]);
            }
            return units;
        }

        /// <summary>
        /// True for characters that occupy a full (roughly double) cell width:
        /// CJK ideographs, kana, Hangul syllables/jamo, and full-width forms.
        /// Half-width katakana (U+FF61..U+FF9F) is intentionally excluded.
        /// </summary>
        private static bool IsFullWidth(char c)
        {
            int u = c;
            if (u == 0x3000) return true;                 // ideographic space
            if (u >= 0x1100 && u <= 0x115F) return true;  // Hangul Jamo
            if (u >= 0x2E80 && u <= 0x303E) return true;  // CJK radicals .. CJK symbols & punctuation
            if (u >= 0x3041 && u <= 0x33FF) return true;  // Hiragana, Katakana, Bopomofo, CJK symbols
            if (u >= 0x3400 && u <= 0x4DBF) return true;  // CJK Unified Ideographs Extension A
            if (u >= 0x4E00 && u <= 0x9FFF) return true;  // CJK Unified Ideographs
            if (u >= 0xA000 && u <= 0xA4CF) return true;  // Yi Syllables / Radicals
            if (u >= 0xAC00 && u <= 0xD7A3) return true;  // Hangul Syllables
            if (u >= 0xF900 && u <= 0xFAFF) return true;  // CJK Compatibility Ideographs
            if (u >= 0xFE30 && u <= 0xFE4F) return true;  // CJK Compatibility Forms
            if (u >= 0xFF00 && u <= 0xFF60) return true;  // full-width ASCII forms
            if (u >= 0xFFE0 && u <= 0xFFE6) return true;  // full-width signs
            return false;
        }
    }
}
