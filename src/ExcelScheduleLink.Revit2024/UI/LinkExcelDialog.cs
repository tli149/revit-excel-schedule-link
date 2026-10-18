using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>
    /// Worksheet / range picker shown after the workbook is
    /// selected. Everything (including the button row) lives in a single
    /// TableLayoutPanel so there is no Dock ordering ambiguity, and the form
    /// auto-sizes to its content so no row can be clipped on any DPI. All
    /// labels come from UserMessages (English-only); worksheet names are data
    /// and keep full Unicode.
    /// </summary>
    internal sealed class LinkExcelDialog : Form
    {
        private readonly ComboBox _sheetCombo;
        private readonly TextBox _rangeText;
        private readonly TableLayoutPanel _layout;
        private readonly int _sheetRow;

        public LinkExcelDialog(
            string workbookPath,
            IReadOnlyList<string> sheetNames,
            string preferredSheetName,
            string preferredRangeAddress)
        {
            // DPI: no AutoScaleMode and no explicit Font on purpose. Revit's own
            // DPI awareness plus WinForms' default font scaling render crisply on
            // high-DPI monitors; forcing AutoScaleMode.Dpi (with a Per-Monitor
            // thread context) made the text blurry instead.

            Text = UserMessages.LinkDialogTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            // Grow to fit the content so a row can never be clipped, regardless
            // of DPI scaling or font size.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(480, 0);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(14),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = 0;

            // Source
            layout.Controls.Add(MakeLabel(UserMessages.LinkDialogSourceLabel), 0, row);
            Label sourceValue = new Label
            {
                Text = workbookPath,
                AutoEllipsis = true,
                AutoSize = false,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 8)
            };
            new ToolTip().SetToolTip(sourceValue, workbookPath);
            layout.Controls.Add(sourceValue, 1, row++);

            // Worksheet
            layout.Controls.Add(MakeLabel(UserMessages.LinkDialogWorksheetLabel), 0, row);
            _sheetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FormattingEnabled = true,
                Margin = new Padding(3, 3, 3, 8)
            };
            foreach (string name in sheetNames)
            {
                _sheetCombo.Items.Add(name ?? string.Empty);
            }
            if (_sheetCombo.Items.Count > 0)
            {
                _sheetCombo.SelectedIndex = 0;
                if (!string.IsNullOrEmpty(preferredSheetName))
                {
                    int preferredIndex = _sheetCombo.FindStringExact(preferredSheetName);
                    if (preferredIndex >= 0)
                    {
                        _sheetCombo.SelectedIndex = preferredIndex;
                    }
                }
            }
            _sheetRow = row;
            layout.Controls.Add(_sheetCombo, 1, row++);
            _sheetCombo.SizeChanged += delegate { FitSheetRow(); };

            // Cell range
            layout.Controls.Add(MakeLabel(UserMessages.LinkDialogRangeLabel), 0, row);
            _rangeText = new TextBox
            {
                Text = preferredRangeAddress ?? string.Empty,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(3, 3, 3, 0)
            };
            layout.Controls.Add(_rangeText, 1, row++);

            // Range hint (under the range field, second column)
            Label rangeHint = new Label
            {
                Text = UserMessages.LinkDialogRangeHint,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8)
            };
            layout.Controls.Add(rangeHint, 1, row++);

            // Buttons (own row, second column, right-aligned)
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(3, 6, 3, 0)
            };
            Button cancelButton = new Button
            {
                Text = UserMessages.LinkDialogCancel,
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            Button okButton = new Button
            {
                Text = UserMessages.LinkDialogOk,
                DialogResult = DialogResult.OK,
                AutoSize = true,
                MinimumSize = new Size(90, 28)
            };
            buttons.Controls.Add(cancelButton); // RightToLeft → Cancel on the right
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons, 1, row);

            Controls.Add(layout);
            _layout = layout;
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        protected override void OnLoad(EventArgs e)
        {
            FitSheetRow();
            base.OnLoad(e);
        }

        /// <summary>
        /// A drop-down list's real height is set by Windows when its window is
        /// created, and at high DPI it is taller than the height the layout
        /// reserves for it, so the range box below would overlap it. The
        /// worksheet row is therefore sized from the real height, now and
        /// whenever that height changes.
        /// </summary>
        private void FitSheetRow()
        {
            if (_layout == null)
            {
                return;
            }
            while (_layout.RowStyles.Count <= _sheetRow)
            {
                _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            int needed = _sheetCombo.Height + _sheetCombo.Margin.Vertical;
            Control label = _layout.GetControlFromPosition(0, _sheetRow);
            if (label != null)
            {
                needed = Math.Max(needed, label.PreferredSize.Height + label.Margin.Vertical);
            }
            _layout.RowStyles[_sheetRow] = new RowStyle(SizeType.Absolute, needed);
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 6, 8, 0)
            };
        }

        public string SelectedSheetName
        {
            get { return _sheetCombo.SelectedItem != null ? _sheetCombo.SelectedItem.ToString() : null; }
        }

        /// <summary>Trimmed range address, or null when left empty (used range).</summary>
        public string RangeAddressOrNull
        {
            get
            {
                string text = _rangeText.Text != null ? _rangeText.Text.Trim() : string.Empty;
                return text.Length == 0 ? null : text;
            }
        }
    }
}
