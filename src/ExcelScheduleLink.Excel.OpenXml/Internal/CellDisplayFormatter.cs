using System;
using System.Globalization;
using System.Text;

namespace ExcelScheduleLink.Excel.OpenXml.Internal
{
    /// <summary>
    /// Interprets number formats (numFmt) and produces strings close to what
    /// Excel displays. Coverage: integer / decimal / thousands separator /
    /// percent / date / time / leading zeros / formula cached values / General /
    /// text (@); custom numeric formats with quoted unit literals (e.g.
    /// 0.0 "A", 0.00 "kVA"); and fraction formats (# ?/?, # ??/??).
    /// Unsupported formats (scientific, conditional sections, date formats that
    /// mix in quoted literals, ...) fall back to the invariant string of the
    /// raw value. Explicit custom formats remain invariant. Only the built-in
    /// short-date (14) and short-date-plus-time (22) formats use the current
    /// process culture; the other supported built-in date/time ids use fixed
    /// Excel patterns.
    /// </summary>
    internal static class CellDisplayFormatter
    {
        public static string Format(RawCell cell, bool date1904)
        {
            try
            {
                switch (cell.Kind)
                {
                    case RawCellKind.Empty:
                        return string.Empty;
                    case RawCellKind.Text:
                        return cell.Raw ?? string.Empty;
                    case RawCellKind.Boolean:
                        return IsTrue(cell.Raw) ? "TRUE" : "FALSE";
                    case RawCellKind.Error:
                        return cell.Raw ?? string.Empty;
                    case RawCellKind.IsoDate:
                        return FormatIsoDate(cell);
                    case RawCellKind.Number:
                        return FormatNumber(cell, date1904);
                    default:
                        return cell.Raw ?? string.Empty;
                }
            }
            catch (Exception)
            {
                return GeneralFallback(cell);
            }
        }

        private static string GeneralFallback(RawCell cell)
        {
            if (string.IsNullOrEmpty(cell.Raw))
            {
                return cell.Raw ?? string.Empty;
            }

            double value;
            return double.TryParse(cell.Raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)
                ? GeneralString(value)
                : cell.Raw;
        }

        // ------------------------------------------------------------------

        private static bool IsTrue(string raw)
        {
            return raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatIsoDate(RawCell cell)
        {
            DateTime dt;
            if (!DateTime.TryParse(cell.Raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
            {
                return cell.Raw ?? string.Empty;
            }
            if (IsBuiltinDateTime(cell))
            {
                return FormatBuiltinDateTime(dt, cell.NumFmtId);
            }

            string code = ResolveFormatCode(cell.NumFmtId, cell.FormatCode);
            FormatSpec spec;
            if (code != null && FormatSpec.TryParse(code, out spec) && spec.Kind == FormatKind.DateTime)
            {
                return dt.ToString(spec.NetDateFormat, CultureInfo.InvariantCulture);
            }
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(RawCell cell, bool date1904)
        {
            double value;
            if (!double.TryParse(cell.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return cell.Raw ?? string.Empty;
            }

            if (IsBuiltinDateTime(cell))
            {
                return FormatBuiltinDateTime(FromSerial(value, date1904), cell.NumFmtId);
            }

            string code = ResolveFormatCode(cell.NumFmtId, cell.FormatCode);
            if (code == null)
            {
                return GeneralString(value);
            }

            FormatSpec spec;
            if (!FormatSpec.TryParse(code, out spec))
            {
                return GeneralString(value); // unsupported format → fallback
            }

            switch (spec.Kind)
            {
                case FormatKind.DateTime:
                    return FromSerial(value, date1904).ToString(spec.NetDateFormat, CultureInfo.InvariantCulture);
                case FormatKind.Text:
                case FormatKind.General:
                    return GeneralString(value);
                case FormatKind.Numeric:
                    return FormatNumeric(value, spec);
                case FormatKind.Fraction:
                    return FormatFraction(value, spec);
                default:
                    return GeneralString(value);
            }
        }

        private static string FormatNumeric(double value, FormatSpec spec)
        {
            bool negative = value < 0;
            double mag = Math.Abs(spec.IsPercent ? value * 100.0 : value);

            string s;
            if (spec.Thousands)
            {
                s = mag.ToString("N" + spec.Decimals, CultureInfo.InvariantCulture);
            }
            else
            {
                s = mag.ToString("F" + spec.Decimals, CultureInfo.InvariantCulture);
                if (spec.MinIntegerDigits > 1)
                {
                    s = PadIntegerPart(s, spec.MinIntegerDigits);
                }
            }
            if (spec.IsPercent)
            {
                s = s + "%";
            }
            // Assemble: sign + prefix + number + suffix (prefix/suffix are the
            // literal text such as a "kVA" unit suffix).
            return (negative ? "-" : string.Empty) + spec.Prefix + s + spec.Suffix;
        }

        /// <summary>Renders a value as a (mixed or improper) fraction.</summary>
        private static string FormatFraction(double value, FormatSpec spec)
        {
            bool negative = value < 0;
            double abs = Math.Abs(value);
            long integer = (long)Math.Floor(abs);
            double frac = abs - integer;

            int maxDenom = 1;
            for (int i = 0; i < spec.FractionDenomDigits; i++)
            {
                maxDenom *= 10;
            }
            maxDenom -= 1; // 1 digit → 9, 2 → 99, 3 → 999
            if (maxDenom < 1)
            {
                maxDenom = 1;
            }

            int num;
            int den;
            BestFraction(frac, maxDenom, out num, out den);
            if (num == den) // rounded up to a whole
            {
                integer += 1;
                num = 0;
            }

            string sign = negative ? "-" : string.Empty;
            if (!spec.FractionHasInteger)
            {
                long improper = integer * den + num;
                return sign + improper.ToString(CultureInfo.InvariantCulture)
                    + "/" + den.ToString(CultureInfo.InvariantCulture);
            }

            // Mixed: integer and/or fraction part, blank when zero.
            StringBuilder sb = new StringBuilder();
            if (integer != 0)
            {
                sb.Append(integer.ToString(CultureInfo.InvariantCulture));
            }
            if (num != 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(num.ToString(CultureInfo.InvariantCulture));
                sb.Append('/');
                sb.Append(den.ToString(CultureInfo.InvariantCulture));
            }
            if (sb.Length == 0)
            {
                sb.Append('0'); // value is exactly zero
            }
            return sign + sb;
        }

        /// <summary>
        /// Best n/d approximation of frac in [0,1) with d <= maxDenom.
        /// Continued fractions reach the bounded denominator without trying
        /// every denominator. The iteration cap is defense in depth for
        /// pathological floating-point input; ties prefer the smaller
        /// denominator to preserve the existing formatter's behavior.
        /// </summary>
        private static void BestFraction(double frac, int maxDenom, out int num, out int den)
        {
            long previousNumerator = 0L;
            long previousDenominator = 1L;
            long numerator = 1L;
            long denominator = 0L;
            double remaining = frac;
            int iterations = 0;
            while (iterations < WorkbookReadLimits.MaxFractionApproximationIterations)
            {
                long whole = (long)Math.Floor(remaining);
                long nextDenominator = checked(
                    previousDenominator + whole * denominator);
                if (nextDenominator > maxDenom)
                {
                    break;
                }
                long nextNumerator = checked(previousNumerator + whole * numerator);
                previousNumerator = numerator;
                previousDenominator = denominator;
                numerator = nextNumerator;
                denominator = nextDenominator;
                iterations++;

                double fractionalPart = remaining - whole;
                if (fractionalPart <= double.Epsilon)
                {
                    break;
                }
                remaining = 1.0 / fractionalPart;
            }

            long semiconvergentDenominator = denominator == 0L
                ? 1L
                : (maxDenom - previousDenominator) / denominator;
            long semiconvergentNumerator = previousNumerator
                + semiconvergentDenominator * numerator;

            long bestNumerator = 0L;
            long bestDenominator = 1L;
            double bestError = Math.Abs(frac);
            ConsiderFraction(
                frac, previousNumerator, previousDenominator,
                ref bestNumerator, ref bestDenominator, ref bestError);
            ConsiderFraction(
                frac, numerator, denominator,
                ref bestNumerator, ref bestDenominator, ref bestError);
            ConsiderFraction(
                frac, semiconvergentNumerator, semiconvergentDenominator,
                ref bestNumerator, ref bestDenominator, ref bestError);

            // A final rounded candidate prevents an early floating-point
            // termination from selecting a poor approximation while remaining
            // O(1) and bounded by the denominator cap.
            long roundedNumerator = (long)Math.Round(
                frac * maxDenom, MidpointRounding.AwayFromZero);
            ConsiderFraction(
                frac, roundedNumerator, maxDenom,
                ref bestNumerator, ref bestDenominator, ref bestError);

            num = checked((int)bestNumerator);
            den = checked((int)bestDenominator);
        }

        private static void ConsiderFraction(
            double target, long candidateNumerator, long candidateDenominator,
            ref long bestNumerator, ref long bestDenominator, ref double bestError)
        {
            if (candidateDenominator <= 0L || candidateNumerator < 0L
                || candidateNumerator > candidateDenominator)
            {
                return;
            }
            double error = Math.Abs(
                target - (double)candidateNumerator / candidateDenominator);
            if (error < bestError - 1e-12
                || (Math.Abs(error - bestError) <= 1e-12
                    && candidateDenominator < bestDenominator))
            {
                bestError = error;
                bestNumerator = candidateNumerator;
                bestDenominator = candidateDenominator;
            }
        }

        /// <summary>Zero-pads the integer part of "123.45" to the minimum digit count.</summary>
        private static string PadIntegerPart(string s, int minIntegerDigits)
        {
            int dot = s.IndexOf('.');
            string intPart = dot < 0 ? s : s.Substring(0, dot);
            string rest = dot < 0 ? string.Empty : s.Substring(dot);
            if (intPart.Length < minIntegerDigits)
            {
                intPart = intPart.PadLeft(minIntegerDigits, '0');
            }
            return intPart + rest;
        }

        /// <summary>Excel "General" equivalent (significant digits, invariant,
        /// no trailing zeros, no scientific notation).</summary>
        internal static string GeneralString(double value)
        {
            return value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        /// <summary>Excel serial value → DateTime (1900 and 1904 date systems).</summary>
        internal static DateTime FromSerial(double serial, bool date1904)
        {
            return DateTime.FromOADate(date1904 ? serial + 1462.0 : serial);
        }

        private static bool IsBuiltinDateTime(RawCell cell)
        {
            if (!string.IsNullOrEmpty(cell.FormatCode))
            {
                return false;
            }
            return (cell.NumFmtId >= 14 && cell.NumFmtId <= 22)
                || cell.NumFmtId == 45 || cell.NumFmtId == 47;
        }

        private static bool IsCultureLocalizedBuiltin(int id)
        {
            return id == 14 || id == 22;
        }

        private static string FormatBuiltinDateTime(DateTime value, int id)
        {
            if (IsCultureLocalizedBuiltin(id))
            {
                CultureInfo culture = CultureInfo.CurrentCulture;
                string localizedPattern = id == 14
                    ? culture.DateTimeFormat.ShortDatePattern
                    : culture.DateTimeFormat.ShortDatePattern + " "
                        + culture.DateTimeFormat.ShortTimePattern;
                return value.ToString(localizedPattern, culture);
            }

            // Built-in 20 is Excel's 24-hour h:mm format, not the host's
            // short-time pattern. All other supported built-in patterns are
            // likewise evaluated invariantly so the host culture cannot alter
            // an explicit Excel format.
            return value.ToString(
                BuiltinNetDateTimeFormatCode(id), CultureInfo.InvariantCulture);
        }

        /// <summary>numFmtId / custom format code → the format code to use. Null = General.</summary>
        internal static string ResolveFormatCode(int numFmtId, string customFormatCode)
        {
            if (!string.IsNullOrEmpty(customFormatCode))
            {
                return customFormatCode;
            }
            return BuiltinFormatCode(numFmtId);
        }

        /// <summary>Main subset of built-in numFmtIds (ECMA-376). Null = treat as General.</summary>
        internal static string BuiltinFormatCode(int id)
        {
            CultureInfo culture = IsCultureLocalizedBuiltin(id)
                ? CultureInfo.CurrentCulture
                : CultureInfo.InvariantCulture;
            return BuiltinFormatCode(id, culture);
        }

        internal static string BuiltinFormatCode(int id, CultureInfo culture)
        {
            culture = culture ?? CultureInfo.InvariantCulture;
            switch (id)
            {
                case 1: return "0";
                case 2: return "0.00";
                case 3: return "#,##0";
                case 4: return "#,##0.00";
                case 9: return "0%";
                case 10: return "0.00%";
                case 12: return "# ?/?";
                case 13: return "# ??/??";
                case 14: return culture.DateTimeFormat.ShortDatePattern;
                case 22: return culture.DateTimeFormat.ShortDatePattern + " "
                    + culture.DateTimeFormat.ShortTimePattern;
                case 15:
                case 16:
                case 17:
                case 18:
                case 19:
                case 20:
                case 21: return BuiltinStructuralFormatCode(id);
                case 37:
                case 38: return "#,##0";
                case 39:
                case 40: return "#,##0.00";
                case 45: return "mm:ss";
                case 47: return "mm:ss";
                case 49: return "@";
                default: return BuiltinStructuralFormatCode(id);
            }
        }

        private static string BuiltinStructuralFormatCode(int id)
        {
            switch (id)
            {
                case 15: return "d-mmm-yy";
                case 16: return "d-mmm";
                case 17: return "mmm-yy";
                case 18: return "h:mm AM/PM";
                case 19: return "h:mm:ss AM/PM";
                case 20: return "h:mm";
                case 21: return "h:mm:ss";
                case 45:
                case 47: return "mm:ss";
                default: return null; // 0=General; 11, 46, 48 etc. unsupported → General
            }
        }

        private static string BuiltinNetDateTimeFormatCode(int id)
        {
            switch (id)
            {
                case 15: return "d-MMM-yy";
                case 16: return "d-MMM";
                case 17: return "MMM-yy";
                case 18: return "h:mm tt";
                case 19: return "h:mm:ss tt";
                case 20: return "H:mm";
                case 21: return "H:mm:ss";
                case 45:
                case 47: return "mm:ss";
                default: return null;
            }
        }
    }

    internal enum FormatKind
    {
        General,
        Numeric,
        DateTime,
        Text,
        Fraction
    }

    /// <summary>
    /// Result of the simplified Excel format-code parse. Of multi-section codes
    /// (separated by ';') only the first (positive) section is used.
    /// </summary>
    internal struct FormatSpec
    {
        public FormatKind Kind;
        public int Decimals;
        public bool Thousands;
        public bool IsPercent;
        public int MinIntegerDigits;
        public string NetDateFormat;
        /// <summary>Literal text rendered before the number (e.g. a currency mark).</summary>
        public string Prefix;
        /// <summary>Literal text rendered after the number (e.g. a unit such as "kVA").</summary>
        public string Suffix;
        public bool FractionHasInteger;
        public int FractionDenomDigits;

        /// <summary>Returns false for formats we cannot interpret (the caller
        /// falls back to General).</summary>
        public static bool TryParse(string formatCode, out FormatSpec spec)
        {
            spec = new FormatSpec();
            spec.Prefix = string.Empty;
            spec.Suffix = string.Empty;
            if (string.IsNullOrEmpty(formatCode))
            {
                spec.Kind = FormatKind.General;
                return true;
            }

            string section = formatCode.Split(';')[0].Trim();
            if (section.Length == 0)
            {
                return false;
            }
            if (string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
            {
                spec.Kind = FormatKind.General;
                return true;
            }
            if (section == "@")
            {
                spec.Kind = FormatKind.Text;
                return true;
            }

            section = StripBrackets(section);

            // Fractions are detected on the raw section (the space between the
            // integer group and the numerator is significant).
            if (TryParseFraction(section, ref spec))
            {
                return true;
            }

            // Date / time detection. Date letters are looked for OUTSIDE quoted
            // literals (so a unit like 0.0 "h" is not mistaken for a date), and
            // the format is then built from the full section so separators such
            // as spaces are preserved.
            string scanText = StripLiteralsForScan(section);
            bool scanHas12 = false;
            string scanForAmPm = ReplaceAmPm(scanText, ref scanHas12);
            bool hasDateLetter = false;
            foreach (char ch in scanForAmPm)
            {
                char c = char.ToLowerInvariant(ch);
                if (c == 'y' || c == 'd' || c == 'h' || c == 's' || c == 'm')
                {
                    hasDateLetter = true;
                    break;
                }
            }
            if (hasDateLetter)
            {
                // Date formats that also carry quoted literals are not supported
                // (the literal separators would be lost) → General fallback.
                if (section.IndexOf('"') >= 0 || section.IndexOf('\\') >= 0)
                {
                    return false;
                }
                bool has12Hour = false;
                string dateSection = ReplaceAmPm(section, ref has12Hour);
                string net = BuildNetDateFormat(dateSection, has12Hour);
                if (net == null)
                {
                    return false;
                }
                spec.Kind = FormatKind.DateTime;
                spec.NetDateFormat = net;
                return true;
            }

            // Numeric / unit path: separate active format chars from literal text
            // (quoted strings, escapes, spaces, other literal characters).
            string active;
            string prefix;
            string suffix;
            bool hasLiteral;
            Tokenize(section, out active, out prefix, out suffix, out hasLiteral);
            if (active.Length == 0)
            {
                return false;
            }
            if (active.IndexOfAny(new[] { 'E', 'e' }) >= 0 && active.IndexOfAny(new[] { '0', '#' }) >= 0)
            {
                return false; // scientific notation is unsupported
            }

            spec.Prefix = prefix;
            spec.Suffix = suffix;
            return TryParseNumeric(active, ref spec);
        }

        // ------------------------------------------------------------------

        private static string StripBrackets(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            bool inBracket = false;
            foreach (char c in s)
            {
                if (c == '[') { inBracket = true; continue; }
                if (c == ']') { inBracket = false; continue; }
                if (!inBracket) { sb.Append(c); }
            }
            return sb.ToString();
        }

        /// <summary>Removes quoted strings and escaped characters so a date-letter
        /// scan only sees the "active" characters (e.g. the h in 0.0 "h" is a
        /// unit literal, not a time token).</summary>
        private static string StripLiteralsForScan(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    int end = s.IndexOf('"', i + 1);
                    i = end < 0 ? s.Length : end + 1;
                }
                else if ((c == '\\' || c == '_' || c == '*') && i + 1 < s.Length)
                {
                    i += 2; // escaped / width / fill char → skip the literal
                }
                else
                {
                    sb.Append(c);
                    i += 1;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Splits a format section into the "active" format characters and the
        /// literal text before the first / after the last numeric placeholder.
        /// Quotes, backslash escapes, spaces, and other non-format characters
        /// are treated as literals.
        /// </summary>
        private static void Tokenize(
            string section, out string active, out string prefix, out string suffix, out bool hasLiteral)
        {
            StringBuilder activeBuilder = new StringBuilder();
            // Parallel list: for each active char, is it a numeric placeholder?
            StringBuilder pre = new StringBuilder();
            StringBuilder suf = new StringBuilder();
            bool seenPlaceholder = false;
            hasLiteral = false;

            int i = 0;
            while (i < section.Length)
            {
                char c = section[i];
                string literal = null;
                if (c == '"')
                {
                    int end = section.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        literal = section.Substring(i + 1);
                        i = section.Length;
                    }
                    else
                    {
                        literal = section.Substring(i + 1, end - i - 1);
                        i = end + 1;
                    }
                }
                else if (c == '\\' && i + 1 < section.Length)
                {
                    literal = section[i + 1].ToString();
                    i += 2;
                }
                else if (c == '_' && i + 1 < section.Length)
                {
                    literal = " "; // _x reserves the width of x → render a space
                    i += 2;
                }
                else if (c == '*' && i + 1 < section.Length)
                {
                    i += 2; // *x fill → ignore
                    continue;
                }
                else if (c == ' ')
                {
                    literal = " ";
                    i += 1;
                }
                else if (IsActiveFormatChar(c))
                {
                    activeBuilder.Append(c);
                    if (IsPlaceholder(c))
                    {
                        seenPlaceholder = true;
                    }
                    // Suffix only keeps literals AFTER the last active char, so
                    // reset it on every active char once a placeholder was seen.
                    // The prefix (literals before the first placeholder) is never
                    // touched here.
                    if (seenPlaceholder)
                    {
                        suf.Clear();
                    }
                    i += 1;
                    continue;
                }
                else
                {
                    literal = c.ToString();
                    i += 1;
                }

                if (literal != null)
                {
                    hasLiteral = true;
                    if (seenPlaceholder)
                    {
                        suf.Append(literal);
                    }
                    else
                    {
                        pre.Append(literal);
                    }
                }
            }

            active = activeBuilder.ToString();
            prefix = pre.ToString();
            suffix = suf.ToString();
        }

        private static bool IsActiveFormatChar(char c)
        {
            // Numeric placeholders, date letters, and date separators.
            if (IsPlaceholder(c)) return true;
            char l = char.ToLowerInvariant(c);
            if (l == 'y' || l == 'm' || l == 'd' || l == 'h' || l == 's') return true;
            if (l == 'a' || l == 'p') return true; // AM/PM letters
            if (c == ':' || c == '/' || c == '-' || c == '+' || c == '(' || c == ')') return true;
            return false;
        }

        private static bool IsPlaceholder(char c)
        {
            return c == '0' || c == '#' || c == '?' || c == ',' || c == '.' || c == '%';
        }

        private static bool TryParseFraction(string section, ref FormatSpec spec)
        {
            int slash = section.IndexOf('/');
            if (slash <= 0 || slash >= section.Length - 1)
            {
                return false;
            }
            if (section.IndexOf('/', slash + 1) >= 0)
            {
                return false; // more than one '/'
            }

            string left = section.Substring(0, slash);
            string right = section.Substring(slash + 1);

            // Denominator must be all fraction placeholders (0 # ?)
            string denTrim = right.Trim();
            if (denTrim.Length == 0 || !IsAllFractionPlaceholders(denTrim))
            {
                return false;
            }

            // Left of '/': optional "integerGroup space numeratorGroup" (mixed)
            // or just a numerator group (improper).
            string numerator;
            bool hasInteger;
            int sp = left.TrimEnd().LastIndexOf(' ');
            string leftTrim = left.Trim();
            if (sp > 0)
            {
                string intGroup = left.Substring(0, sp).Trim();
                numerator = left.Substring(sp + 1).Trim();
                if (!IsAllFractionPlaceholders(intGroup) || !IsAllFractionPlaceholders(numerator))
                {
                    return false;
                }
                hasInteger = true;
            }
            else
            {
                numerator = leftTrim;
                if (!IsAllFractionPlaceholders(numerator))
                {
                    return false;
                }
                hasInteger = false;
            }

            if (denTrim.Length > WorkbookReadLimits.MaxFractionDenominatorDigits)
            {
                // Excel's supported ?/???-style denominator has three digits.
                // Longer attacker-controlled formats fall back to General so
                // formatting cost cannot grow exponentially.
                return false;
            }
            spec.Kind = FormatKind.Fraction;
            spec.FractionHasInteger = hasInteger;
            spec.FractionDenomDigits = denTrim.Length;
            return true;
        }

        private static bool IsAllFractionPlaceholders(string s)
        {
            foreach (char c in s)
            {
                if (c != '0' && c != '#' && c != '?')
                {
                    return false;
                }
            }
            return s.Length > 0;
        }

        private const char AmPmMarker = '';

        private static string ReplaceAmPm(string s, ref bool has12Hour)
        {
            string upper = s.ToUpperInvariant();
            int idx = upper.IndexOf("AM/PM", StringComparison.Ordinal);
            if (idx >= 0)
            {
                has12Hour = true;
                return s.Substring(0, idx) + AmPmMarker + s.Substring(idx + 5);
            }
            idx = upper.IndexOf("A/P", StringComparison.Ordinal);
            if (idx >= 0)
            {
                has12Hour = true;
                return s.Substring(0, idx) + AmPmMarker + s.Substring(idx + 3);
            }
            return s;
        }

        private static string BuildNetDateFormat(string section, bool has12Hour)
        {
            StringBuilder result = new StringBuilder(section.Length + 4);
            int i = 0;
            char prevLetter = '\0';
            while (i < section.Length)
            {
                char c = section[i];
                char lower = char.ToLowerInvariant(c);
                if (lower == 'y' || lower == 'm' || lower == 'd' || lower == 'h' || lower == 's')
                {
                    int start = i;
                    while (i < section.Length && char.ToLowerInvariant(section[i]) == lower)
                    {
                        i++;
                    }
                    int len = i - start;
                    string mapped = MapDateToken(lower, len, prevLetter, NextDateLetter(section, i), has12Hour);
                    if (mapped == null)
                    {
                        return null;
                    }
                    result.Append(mapped);
                    prevLetter = lower;
                }
                else if (c == AmPmMarker)
                {
                    result.Append("tt");
                    i++;
                }
                else if (c == '/' || c == ':' || c == ' ' || c == '-' || c == '.' || c == ',' || c == '(' || c == ')')
                {
                    result.Append(c);
                    i++;
                }
                else if (char.IsLetter(c))
                {
                    return null;
                }
                else
                {
                    result.Append(c);
                    i++;
                }
            }
            return result.ToString();
        }

        private static char NextDateLetter(string section, int from)
        {
            for (int j = from; j < section.Length; j++)
            {
                char lower = char.ToLowerInvariant(section[j]);
                if (lower == 'y' || lower == 'm' || lower == 'd' || lower == 'h' || lower == 's')
                {
                    return lower;
                }
                if (char.IsLetter(section[j]))
                {
                    return '\0';
                }
            }
            return '\0';
        }

        private static string MapDateToken(char letter, int length, char prevLetter, char nextLetter, bool has12Hour)
        {
            switch (letter)
            {
                case 'y':
                    return length >= 4 ? "yyyy" : "yy";
                case 'd':
                    if (length >= 4) return "dddd";
                    if (length == 3) return "ddd";
                    return length == 2 ? "dd" : "d";
                case 'h':
                    if (has12Hour) return length >= 2 ? "hh" : "h";
                    return length >= 2 ? "HH" : "H";
                case 's':
                    return length >= 2 ? "ss" : "s";
                case 'm':
                    bool isMinute = prevLetter == 'h' || nextLetter == 's';
                    if (isMinute)
                    {
                        return length >= 2 ? "mm" : "m";
                    }
                    if (length >= 4) return "MMMM";
                    if (length == 3) return "MMM";
                    return length == 2 ? "MM" : "M";
                default:
                    return null;
            }
        }

        private static bool TryParseNumeric(string section, ref FormatSpec spec)
        {
            bool percent = section.IndexOf('%') >= 0;
            string body = percent ? section.Replace("%", string.Empty) : section;
            body = body.Trim();
            if (body.Length == 0)
            {
                return false;
            }

            int dot = body.IndexOf('.');
            string intPart = dot < 0 ? body : body.Substring(0, dot);
            string decPart = dot < 0 ? string.Empty : body.Substring(dot + 1);

            int minIntegerDigits = 0;
            bool thousands = false;
            foreach (char c in intPart)
            {
                if (c == '0') { minIntegerDigits++; }
                else if (c == ',') { thousands = true; }
                else if (c == '#' || c == '?') { /* digit placeholder */ }
                else { return false; }
            }

            int decimals = 0;
            foreach (char c in decPart)
            {
                if (c == '0' || c == '#' || c == '?') { decimals++; }
                else { return false; }
            }

            spec.Kind = FormatKind.Numeric;
            spec.IsPercent = percent;
            spec.Thousands = thousands;
            spec.Decimals = decimals;
            spec.MinIntegerDigits = minIntegerDigits;
            return true;
        }
    }
}
