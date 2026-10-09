using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace UiHarness
{
    internal static class Program
    {
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2024";
        private static string _outDir;

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRevit;
            _outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            Directory.CreateDirectory(_outDir);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm", 1.0f);
            Render(1, LengthUnitChoice.Inches, "layout_1col_in", 1.0f);
            Render(50, LengthUnitChoice.Millimeters, "layout_50col_mm", 1.0f);
            // Font-scaled runs stand in for high-DPI: controls grow while the form
            // keeps its designed size - the same stress that clipped the real dialog.
            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm_150", 1.5f);
            Render(3, LengthUnitChoice.Millimeters, "layout_3col_mm_200", 2.0f);
            RenderLinkExcel("link-dialog", 1.0f);
            RenderLinkExcel("link-dialog_200", 2.0f);
            RenderLinkManagerPreview("link-manager");
            CheckLinkExcelFallback();
            Console.WriteLine("done");
        }

        private static Assembly ResolveRevit(object sender, ResolveEventArgs e)
        {
            try
            {
                string name = new AssemblyName(e.Name).Name;
                string path = Path.Combine(RevitDir, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch (Exception) { return null; }
        }

        private static void Render(int columns, LengthUnitChoice unit, string baseName, float fontScale)
        {
            double[] feet = new double[columns];
            for (int i = 0; i < columns; i++)
            {
                feet[i] = 0.4 + (i % 5) * 0.15; // varied, inside [0.10, 2.0]
            }

            LayoutDialog dialog = new LayoutDialog(
                "Sample Workbook - Data",
                feet,
                ColumnWidthMode.ApplyExcelWidths,
                unit,
                delegate { return new FitContentResult { Feet = feet, UnresolvedDeficit = 0.0 }; },
                delegate { return ExcelWidthResult.Failed("harness: no workbook"); },
                 delegate (double[] f, ColumnWidthMode m, LayoutSourceSnapshot s)
                {
                    return new LayoutApplyResult { Success = true, UserMessage = "harness: applied" };
                });

            if (Math.Abs(fontScale - 1.0f) > 0.01f)
            {
                dialog.Font = new Font(dialog.Font.FontFamily, dialog.Font.Size * fontScale);
            }
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(0, 0);
            dialog.Show();
            Application.DoEvents();
            dialog.Refresh();
            Application.DoEvents();

            using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                bmp.Save(Path.Combine(_outDir, baseName + ".png"));
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("form: " + dialog.Width + "x" + dialog.Height
                + "  client=" + dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            DumpControls(dialog, dialog, sb, 0);
            File.WriteAllText(Path.Combine(_outDir, baseName + ".txt"), sb.ToString());

            dialog.Close();
            dialog.Dispose();
        }

        private static void RenderLinkExcel(string baseName, float fontScale)
        {
            LinkExcelDialog dialog = new LinkExcelDialog(
                @"C:\Projects\Sample\Schedule Data.xlsx",
                new List<string> { "Data" },
                1.5,
                "Data",
                string.Empty);
            if (dialog.SelectedSheetName != "Data"
                || dialog.RangeAddressOrNull != null
                || Math.Abs(dialog.WidthScale - 1.5) > 0.0001)
            {
                throw new InvalidOperationException("Link Excel did not restore the successful defaults.");
            }

            if (Math.Abs(fontScale - 1.0f) > 0.01f)
            {
                dialog.Font = new Font(dialog.Font.FontFamily, dialog.Font.Size * fontScale);
            }
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(0, 0);
            dialog.Show();
            Application.DoEvents();
            dialog.Refresh();
            Application.DoEvents();

            using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                bmp.Save(Path.Combine(_outDir, baseName + ".png"));
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("form: " + dialog.Width + "x" + dialog.Height
                + "  client=" + dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            DumpControls(dialog, dialog, sb, 0);
            File.WriteAllText(Path.Combine(_outDir, baseName + ".txt"), sb.ToString());

            dialog.Close();
            dialog.Dispose();
        }

        /// <summary>
        /// The production LinkManagerDialog needs a live Revit Document to load
        /// linked schedules. This preview keeps the screenshot harness runnable
        /// outside Revit while exercising the same public-facing sample rows.
        /// It is deliberately harness-only and is never compiled into the add-in.
        /// </summary>
        private static void RenderLinkManagerPreview(string baseName)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Excel Schedule Link - Link Manager";
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(0, 0);
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(1200, 640);

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Padding = new Padding(12)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                ListView list = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    HideSelection = false,
                    GridLines = true,
                    MultiSelect = false
                };
                list.Columns.Add("Schedule", 170);
                list.Columns.Add("Worksheet", 95);
                list.Columns.Add("Range", 85);
                list.Columns.Add("Source", 400);
                list.Columns.Add("Status", 150);
                list.Columns.Add("Integrity", 125);
                list.Columns.Add("Scale", 65);
                list.Columns.Add("Col width", 100);
                list.Columns.Add("Row height", 120);

                ListViewItem present = new ListViewItem(new[]
                {
                    "Sample Workbook - Data",
                    "Data",
                    "(used range)",
                    @"C:\Projects\Sample\Schedule Data.xlsx",
                    "Protected",
                    "Healthy",
                    "1.50",
                    "Apply Excel widths",
                    "Apply Excel / auto heights"
                });
                ListViewItem missing = new ListViewItem(new[]
                {
                    "Schedule - Data",
                    "Data",
                    "B2:C3",
                    @"C:\Proj\Missing Book.xlsx",
                    "Missing",
                    "Not checked",
                    "1.20",
                    "Keep manual widths",
                    "Keep manual heights"
                });
                missing.ForeColor = Color.Firebrick;
                list.Items.Add(present);
                list.Items.Add(missing);
                layout.Controls.Add(list, 0, 0);

                GroupBox settings = new GroupBox
                {
                    Text = "Settings for the selected link (applied on next reload)",
                    Dock = DockStyle.Fill,
                    Height = 90,
                    Padding = new Padding(10),
                    Margin = new Padding(0, 12, 0, 0)
                };
                TableLayoutPanel settingsGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 4,
                    RowCount = 2,
                    AutoSize = true
                };
                settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                settingsGrid.Controls.Add(new Label
                {
                    Text = "Column width scale:",
                    AutoSize = true,
                    Margin = new Padding(3, 7, 4, 0)
                }, 0, 0);
                NumericUpDown scale = new NumericUpDown
                {
                    DecimalPlaces = 2,
                    Increment = 0.1M,
                    Minimum = 0.2M,
                    Maximum = 10.0M,
                    Value = 1.5M,
                    Width = 80,
                    Margin = new Padding(3, 3, 6, 6)
                };
                settingsGrid.Controls.Add(scale, 1, 0);
                settingsGrid.Controls.Add(new Label
                {
                    Text = "Recommended: 1.5 - 2.0",
                    AutoSize = true,
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(0, 8, 18, 0)
                }, 2, 1);
                settingsGrid.Controls.Add(new Label
                {
                    Text = "Column width mode:",
                    AutoSize = true,
                    Margin = new Padding(3, 7, 4, 0)
                }, 2, 0);
                ComboBox columnMode = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 170,
                    Margin = new Padding(3, 3, 16, 3)
                };
                columnMode.Items.AddRange(new object[] { "Apply Excel widths", "Keep manual widths" });
                columnMode.SelectedIndex = 0;
                settingsGrid.Controls.Add(columnMode, 3, 0);
                settingsGrid.Controls.Add(new Label
                {
                    Text = "Row height mode:",
                    AutoSize = true,
                    Margin = new Padding(3, 7, 4, 0)
                }, 0, 1);
                ComboBox rowMode = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 170,
                    Margin = new Padding(3, 3, 16, 3)
                };
                rowMode.Items.AddRange(new object[] { "Apply Excel / auto heights", "Keep manual heights" });
                rowMode.SelectedIndex = 0;
                settingsGrid.Controls.Add(rowMode, 1, 1);
                Button apply = new Button
                {
                    Text = "Apply settings",
                    AutoSize = true,
                    MinimumSize = new Size(110, 28),
                    Anchor = AnchorStyles.Right,
                    Margin = new Padding(18, 3, 3, 3)
                };
                settingsGrid.Controls.Add(apply, 3, 1);
                settings.Controls.Add(settingsGrid);
                layout.Controls.Add(settings, 0, 1);

                FlowLayoutPanel buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    Height = 40,
                    Margin = new Padding(0, 8, 0, 0)
                };
                buttons.Controls.Add(new Button
                {
                    Text = "Close",
                    DialogResult = DialogResult.Cancel,
                    AutoSize = true,
                    MinimumSize = new Size(90, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                buttons.Controls.Add(new Button
                {
                    Text = "Reload all",
                    AutoSize = true,
                    MinimumSize = new Size(110, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                buttons.Controls.Add(new Button
                {
                    Text = "Reload selected",
                    AutoSize = true,
                    MinimumSize = new Size(120, 28),
                    Margin = new Padding(6, 0, 0, 0)
                });
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);

                dialog.Show();
                Application.DoEvents();
                present.Selected = true;
                list.Focus();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bmp, new Rectangle(0, 0, dialog.Width, dialog.Height));
                    bmp.Save(Path.Combine(_outDir, baseName + ".png"));
                }
                dialog.Close();
            }
        }

        private static void CheckLinkExcelFallback()
        {
            using (LinkExcelDialog dialog = new LinkExcelDialog(
                @"C:\Projects\Another.xlsx",
                new List<string> { "First", "Second" },
                1.7,
                "Worksheet no longer present",
                string.Empty))
            {
                if (dialog.SelectedSheetName != "First")
                {
                    throw new InvalidOperationException(
                        "Link Excel must select the first worksheet when the remembered name is absent.");
                }
            }
        }

        /// <summary>Reports any control whose bounds fall outside its parent's client area.</summary>
        private static void DumpControls(Control parent, Form form, StringBuilder sb, int depth)
        {
            foreach (Control c in parent.Controls)
            {
                Rectangle b = c.Bounds;
                Size client = parent.ClientSize;
                bool clipped = b.Right > client.Width + 1 || b.Bottom > client.Height + 1
                    || b.X < -1 || b.Y < -1;
                sb.AppendLine(new string(' ', depth * 2)
                    + c.GetType().Name + " '" + Shorten(c.Text) + "' "
                    + b.X.ToString(CultureInfo.InvariantCulture) + ","
                    + b.Y.ToString(CultureInfo.InvariantCulture) + " "
                    + b.Width + "x" + b.Height
                    + " (parent client " + client.Width + "x" + client.Height + ")"
                    + (clipped ? "   <<< CLIPPED" : "")
                    + (c.Visible ? "" : "   [hidden]"));
                DumpControls(c, form, sb, depth + 1);
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", " ").Replace("\r", " ");
            return text.Length > 34 ? text.Substring(0, 34) + "..." : text;
        }
    }
}
