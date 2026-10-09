using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>
    /// Loads embedded PNG icons as WPF <see cref="ImageSource"/> for ribbon
    /// buttons. Icons are embedded resources (nothing extra to deploy), read via
    /// BitmapFrame.Create from the manifest resource stream. Any failure returns
    /// null so a missing/renamed icon degrades to a text-only button rather than
    /// breaking the ribbon.
    /// </summary>
    internal static class RibbonImageLoader
    {
        // Default SDK resource name is "<RootNamespace>.<folder>.<file>".
        private const string Prefix = "ExcelScheduleLink.Revit2024.Resources.";

        public static ImageSource Load(string fileName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                Stream stream = asm.GetManifestResourceStream(Prefix + fileName);
                if (stream == null)
                {
                    // Fallback: match by suffix in case the resource name differs.
                    foreach (string name in asm.GetManifestResourceNames())
                    {
                        if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            stream = asm.GetManifestResourceStream(name);
                            break;
                        }
                    }
                }
                if (stream == null)
                {
                    return null;
                }
                using (stream)
                {
                    return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
