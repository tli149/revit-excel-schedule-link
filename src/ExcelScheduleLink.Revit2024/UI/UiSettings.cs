using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>
    /// User-scope UI preferences (window size, list column widths). Stored as
    /// a tiny key=value text file under %APPDATA%\ExcelScheduleLink — no model
    /// data, no admin rights, and intentionally no JSON library (to avoid
    /// pulling extra DLLs into the Revit process). All operations are
    /// best-effort: a missing or corrupt file simply yields defaults.
    /// </summary>
    internal sealed class UiSettings
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ExcelScheduleLink", "ui-settings.txt");
            }
        }

        public static UiSettings Load()
        {
            UiSettings settings = new UiSettings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            settings._values[line.Substring(0, eq).Trim()] =
                                line.Substring(eq + 1).Trim();
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return settings;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in _values)
                {
                    sb.Append(kv.Key).Append('=').AppendLine(kv.Value);
                }
                File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public int GetInt(string key, int fallback)
        {
            string text;
            int value;
            if (_values.TryGetValue(key, out text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }

        public void SetInt(string key, int value)
        {
            _values[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        public string GetString(string key, string fallback)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : fallback;
        }

        public void SetString(string key, string value)
        {
            // One preference per line. Paths and worksheet names may contain
            // '=' (the reader splits only on the first one), but line breaks
            // would corrupt the tiny key=value format.
            _values[key] = (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        /// <summary>Comma-separated int list, or null when absent/invalid.</summary>
        public int[] GetIntList(string key, int expectedCount)
        {
            string text;
            if (!_values.TryGetValue(key, out text))
            {
                return null;
            }
            string[] parts = text.Split(',');
            if (parts.Length != expectedCount)
            {
                return null;
            }
            int[] result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                {
                    return null;
                }
            }
            return result;
        }

        public void SetIntList(string key, IList<int> values)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            _values[key] = sb.ToString();
        }
    }
}
