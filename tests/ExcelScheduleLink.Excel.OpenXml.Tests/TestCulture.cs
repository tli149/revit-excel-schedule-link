using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ExcelScheduleLink.Excel.OpenXml.Tests
{
    /// <summary>Temporarily changes both cultures and restores them on dispose.</summary>
    internal sealed class TestCultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        public TestCultureScope(string cultureName)
            : this(CultureInfo.GetCultureInfo(cultureName))
        {
        }

        public TestCultureScope(CultureInfo culture)
        {
            if (culture == null)
            {
                throw new ArgumentNullException("culture");
            }

            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }

    /// <summary>
    /// Allows the CI verification command to choose the initial culture without
    /// changing the machine's user or system locale.
    /// </summary>
    internal static class TestProcessCulture
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            string cultureName = Environment.GetEnvironmentVariable(
                "ESL_TEST_PROCESS_CULTURE");
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return;
            }

            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
