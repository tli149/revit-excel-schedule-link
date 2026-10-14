using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Link;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>What the user asked for when the Column Widths dialog closed.</summary>
    internal enum LayoutDialogAction
    {
        Cancelled,
        ApplyAndClose,
        DiscardAndClose,
        ApplyAndOpenSchedule,
        DiscardAndOpenSchedule
    }

    /// <summary>
    /// Plain data the dialog hands back to the command: candidate widths in
    /// column order, the chosen reload policy and the requested action. The
    /// dialog itself never touches Revit (no transaction, no ActiveView change).
    /// </summary>
    internal sealed class LayoutDialogOutcome
    {
        public LayoutDialogAction Action = LayoutDialogAction.Cancelled;
        public double[] CandidateFeet;
        public ColumnWidthMode ReloadPolicy;
        public LayoutSourceSnapshot SourceSnapshot;
    }

    /// <summary>
    /// Column Widths dialog (design v0.8.0 10): a one-way modal that edits
    /// CANDIDATE values only. Fit Content / Distribute Evenly / Restore Excel
    /// Widths / Fit to Total update the grid; nothing reaches the model until the
    /// command applies the returned outcome.
    ///
    /// Each grid row keeps its immutable schedule column index in its Tag, and
    /// sorting/reordering is disabled, so candidates are rebuilt by column index
    /// and never by display order. The "A/B/C" labels are cosmetic.
    /// </summary>
    internal sealed class LayoutDialog : Form
    {
        private sealed class RowState
        {
            public int ColumnIndex;   // immutable identity of the schedule column
            public double Feet;       // current candidate (internal units)
            public bool Invalid;      // last edit could not be parsed
        }

        private const string WidthColumnName = "width";

        private readonly LengthUnitChoice _unit;
        private readonly Func<FitContentResult> _fitContent;
        private readonly Func<ExcelWidthResult> _restoreExcel;
        private readonly Func<double[], ColumnWidthMode, LayoutSourceSnapshot, LayoutApplyResult> _applyNow;
        private readonly int _columnCount;

        private readonly DataGridView _grid;
        private readonly TextBox _targetTotal;
        private readonly Label _status;
        private readonly RadioButton _policyKeep;
        private readonly RadioButton _policyRecalculate;
        private readonly Button _applyClose;

        private bool _dirty;
        private bool _suppressEvents;
        private bool _fatalRegenerationFailure;
        private LayoutSourceSnapshot _sourceSnapshot;

        public LayoutDialogOutcome Outcome { get; private set; }

        public LayoutDialog(
            string scheduleName,
            double[] currentFeet,
            ColumnWidthMode currentMode,
            LengthUnitChoice unit,
            Func<FitContentResult> fitContentProvider,
            Func<ExcelWidthResult> restoreExcelProvider,
            Func<double[], ColumnWidthMode, LayoutSourceSnapshot, LayoutApplyResult> applyNowProvider)
        {
            _unit = unit;
            _fitContent = fitContentProvider;
            _restoreExcel = restoreExcelProvider;
            _applyNow = applyNowProvider;
            _columnCount = currentFeet != null ? currentFeet.Length : 0;
            Outcome = new LayoutDialogOutcome { ReloadPolicy = currentMode };

            // DPI: deliberately no AutoScaleMode and no explicit Font here. Doing
            // nothing about DPI - inheriting Revit's own awareness and WinForms'
            // default font scaling - is what renders crisply on a 4K monitor;
            // forcing AutoScaleMode.Dpi made this dialog soft. Same rule for every
            // dialog in this add-in.
            Text = UserMessages.LayoutDialogTitle;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(620, 580);
            ClientSize = new Size(640, 620);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // never auto-expand
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // schedule label
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // grid
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // quick layout
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // reload policy
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

            root.Controls.Add(new Label
            {
                Text = UserMessages.LayoutDialogScheduleLabel + " " + scheduleName,
                AutoSize = true,
                AutoEllipsis = true,
                Margin = new Padding(3, 0, 3, 6)
            }, 0, 0);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(BuildQuickGroup(), 0, 2);
            root.Controls.Add(BuildPolicyGroup(currentMode, out _policyKeep, out _policyRecalculate), 0, 3);

            _status = new Label
            {
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 6),
                MaximumSize = new Size(560, 0)
            };
            root.Controls.Add(_status, 0, 4);

            Control buttons = BuildButtons(out _applyClose);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            AcceptButton = _applyClose;

            Control[] found = Controls.Find("targetTotal", true);
            _targetTotal = found.Length > 0 ? (TextBox)found[0] : new TextBox();

            LoadRows(currentFeet);
            _targetTotal.Text = FormatDisplay(SumFeet()); // default target = current total
            UpdateStatus();
        }

        // ------------------------------------------------------------ building

        private DataGridView BuildGrid()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = false,  // display order must stay put
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = SystemColors.Window,
                Margin = new Padding(3, 0, 3, 8),
                // Header and rows must grow with the font, otherwise the first row
                // is clipped at high DPI.
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                MinimumSize = new Size(0, 120) // never let the list collapse to nothing
            };

            DataGridViewTextBoxColumn label = new DataGridViewTextBoxColumn
            {
                Name = "label",
                HeaderText = UserMessages.LayoutGridColumnHeader,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable, // no sorting at all
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells // header must not clip at high DPI
            };
            DataGridViewTextBoxColumn width = new DataGridViewTextBoxColumn
            {
                Name = WidthColumnName,
                HeaderText = UserMessages.LayoutGridWidthHeader + " (" + LengthUnitHelper.Label(_unit) + ")",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            width.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns.Add(label);
            grid.Columns.Add(width);

            grid.CellEndEdit += OnCellEndEdit;
            return grid;
        }

        private Control BuildQuickGroup()
        {
            // A TableLayoutPanel, not a GroupBox: GroupBox.AutoSize ignores docked
            // children, so its caption band clipped the last button row at high DPI.
            // A bold caption label plus spacing gives the same grouping visually.
            // Two equal columns keep the widest button inside half the width.
            TableLayoutPanel grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 2, 3, 8)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            // Explicit row styles: without them the panel spreads rows evenly and
            // the buttons stretch to fill the leftover height.
            for (int r = 0; r < 5; r++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label caption = new Label
            {
                Text = UserMessages.LayoutQuickGroup,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 0, 3, 2)
            };
            grid.Controls.Add(caption, 0, 0);
            grid.SetColumnSpan(caption, 2);

            Label note = new Label
            {
                Text = UserMessages.LayoutQuickNote,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                UseMnemonic = false, // the text contains "&" (Apply & Close), not an access key
                Margin = new Padding(3, 0, 3, 6)
            };
            grid.Controls.Add(note, 0, 1);
            grid.SetColumnSpan(note, 2);

            ToolTip tips = new ToolTip();

            // Target total row (spans both columns).
            TableLayoutPanel targetRow = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4)
            };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            targetRow.Controls.Add(new Label
            {
                Text = UserMessages.LayoutTargetTotalLabel,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 6, 3)
            }, 0, 0);
            TextBox target = new TextBox
            {
                Name = "targetTotal",
                Width = 90,
                TextAlign = HorizontalAlignment.Right,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 6, 3)
            };
            targetRow.Controls.Add(target, 1, 0);
            targetRow.Controls.Add(new Label
            {
                Text = LengthUnitHelper.Label(_unit),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 3, 3)
            }, 2, 0);
            grid.Controls.Add(targetRow, 0, 2);
            grid.SetColumnSpan(targetRow, 2);

            Button fit = MakeButton(UserMessages.LayoutFitContent, OnFitContent);
            tips.SetToolTip(fit, UserMessages.LayoutFitContentTip);
            Button distribute = MakeButton(UserMessages.LayoutDistributeEvenly, OnDistributeEvenly);
            tips.SetToolTip(distribute, UserMessages.LayoutDistributeTip);
            Button restore = MakeButton(UserMessages.LayoutRestoreExcel, OnRestoreExcel);
            tips.SetToolTip(restore, UserMessages.LayoutRestoreTip);
            Button fitTotal = MakeButton(UserMessages.LayoutFitToTotal, OnFitToTotal);
            tips.SetToolTip(fitTotal, UserMessages.LayoutFitToTotalTip);

            grid.Controls.Add(fit, 0, 3);
            grid.Controls.Add(distribute, 1, 3);
            grid.Controls.Add(restore, 0, 4);
            grid.Controls.Add(fitTotal, 1, 4);

            return grid;
        }

        private Control BuildPolicyGroup(ColumnWidthMode current, out RadioButton keep, out RadioButton recalculate)
        {
            TableLayoutPanel inner = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 2, 3, 6)
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int r = 0; r < 3; r++) inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inner.Controls.Add(new Label
            {
                Text = UserMessages.LayoutPolicyGroup,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 0, 3, 2)
            }, 0, 0);

            keep = new RadioButton
            {
                Text = UserMessages.LayoutPolicyKeep,
                AutoSize = true,
                Checked = current == ColumnWidthMode.KeepManualWidths
            };
            recalculate = new RadioButton
            {
                Text = UserMessages.LayoutPolicyRecalculate,
                AutoSize = true,
                Checked = current != ColumnWidthMode.KeepManualWidths
            };
            keep.CheckedChanged += OnPolicyChanged;
            recalculate.CheckedChanged += OnPolicyChanged;

            inner.Controls.Add(keep, 0, 1);
            inner.Controls.Add(recalculate, 0, 2);
            return inner;
        }

        private Control BuildButtons(out Button applyClose)
        {
            // Grid instead of a fixed-height flow panel: a percent spacer column
            // right-aligns the buttons and the row height follows the font, so
            // nothing is cut off at large scaling.
            TableLayoutPanel host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(3, 4, 3, 0)
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // spacer
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // "Apply" keeps the dialog open so several width variants can be tried
            // against the real schedule; the command owns the transaction (this
            // dialog runs inside the command's Revit API context, like the Link
            // Manager does).
            Button applyNow = new Button
            {
                Text = UserMessages.LayoutApply,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 3, 0, 3)
            };
            applyNow.Click += OnApplyNow;
            new ToolTip().SetToolTip(applyNow, UserMessages.LayoutApplyTip);

            applyClose = new Button
            {
                Text = UserMessages.LayoutApplyAndClose,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 3, 0, 3)
            };
            applyClose.Click += OnApplyAndClose;

            Button openView = new Button
            {
                Text = UserMessages.LayoutOpenScheduleView,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 3, 0, 3)
            };
            openView.Click += OnOpenScheduleView;

            Button close = new Button
            {
                Text = UserMessages.LayoutClose,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 3, 0, 3)
            };
            close.Click += OnCloseClicked;

            host.Controls.Add(applyNow, 1, 0);
            host.Controls.Add(applyClose, 2, 0);
            host.Controls.Add(openView, 3, 0);
            host.Controls.Add(close, 4, 0);

            FormClosing += OnFormClosing;
            return host;
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            // Dock=Fill: the button takes its cell's width instead of demanding a
            // fixed one, so long labels never overflow the group at large fonts.
            Button b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 3, 3, 3)
            };
            b.Click += handler;
            return b;
        }

        // ------------------------------------------------------------- row data

        private void LoadRows(double[] feet)
        {
            _suppressEvents = true;
            _grid.Rows.Clear();
            for (int i = 0; i < _columnCount; i++)
            {
                int index = _grid.Rows.Add(ColumnLabel(i), string.Empty);
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = new RowState { ColumnIndex = i, Feet = feet[i] };
                WriteCell(row);
            }
            _suppressEvents = false;
        }

        /// <summary>Cosmetic label only (A, B, ... Z, AA, ...); identity is the Tag.</summary>
        private static string ColumnLabel(int zeroBased)
        {
            string s = string.Empty;
            int n = zeroBased;
            do
            {
                s = (char)('A' + (n % 26)) + s;
                n = n / 26 - 1;
            } while (n >= 0);
            return s;
        }

        private void WriteCell(DataGridViewRow row)
        {
            RowState state = (RowState)row.Tag;
            double display = LengthUnitHelper.FromFeet(state.Feet, _unit);
            row.Cells[WidthColumnName].Value =
                display.ToString("F" + LengthUnitHelper.Decimals(_unit), CultureInfo.CurrentCulture);
            row.Cells[WidthColumnName].Style.BackColor = state.Invalid
                ? Color.MistyRose
                : SystemColors.Window;
        }

        private void SetAllFeet(double[] feet)
        {
            if (feet == null) return;
            _suppressEvents = true;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                RowState state = (RowState)row.Tag;
                if (state.ColumnIndex >= 0 && state.ColumnIndex < feet.Length)
                {
                    state.Feet = feet[state.ColumnIndex]; // rebuilt by column index, not row order
                    state.Invalid = false;
                    WriteCell(row);
                }
            }
            _suppressEvents = false;
        }

        private double[] CurrentFeet()
        {
            double[] feet = new double[_columnCount];
            for (int i = 0; i < feet.Length; i++) feet[i] = LayoutMetrics.MinColumnFeet;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                RowState state = (RowState)row.Tag;
                if (state.ColumnIndex >= 0 && state.ColumnIndex < feet.Length)
                {
                    feet[state.ColumnIndex] = state.Feet;
                }
            }
            return feet;
        }

        private int InvalidCount()
        {
            int n = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (((RowState)row.Tag).Invalid) n++;
            }
            return n;
        }

        // ------------------------------------------------------------- handlers

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressEvents || e.RowIndex < 0) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            RowState state = (RowState)row.Tag;
            _sourceSnapshot = null;
            object value = row.Cells[WidthColumnName].Value;
            string text = value != null ? value.ToString().Trim() : string.Empty;

            double parsed;
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
            if (!ok)
            {
                // Also accept the invariant separator, so "12.5" works on a comma locale.
                ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
            }
            if (!ok || double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed <= 0.0)
            {
                state.Invalid = true;
                WriteCell(row);
                SetStatus(UserMessages.LayoutInvalidCell(state.ColumnIndex + 1, LengthUnitHelper.Label(_unit)), true);
                return;
            }

            state.Feet = LengthUnitHelper.ToFeet(parsed, _unit);
            state.Invalid = false;
            _suppressEvents = true;
            WriteCell(row);
            _suppressEvents = false;
            MarkDirty(true);
            UpdateStatus();
        }

        private void OnFitContent(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure) return;
            if (_fitContent == null) return;
            FitContentResult result = _fitContent();
            if (result == null || result.Feet == null) return;
            _sourceSnapshot = null;
            SetAllFeet(result.Feet);
            MarkDirty(true);
            if (result.UnresolvedDeficit > LayoutOps.Tolerance)
            {
                double inches = result.UnresolvedDeficit * LayoutMetrics.InchesPerFoot;
                SetStatus(UserMessages.LayoutFitContentPartial(inches), false);
            }
            else
            {
                UpdateStatus();
            }
        }

        private void OnDistributeEvenly(object sender, EventArgs e)
        {
            _sourceSnapshot = null;
            double total = 0.0;
            foreach (double f in CurrentFeet()) total += f;
            SetAllFeet(LayoutOps.DistributeEvenlyFeet(_columnCount, total));
            MarkDirty(true);
            UpdateStatus();
        }

        private void OnRestoreExcel(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure) return;
            if (_restoreExcel == null) return;
            ExcelWidthResult result = _restoreExcel();
            if (result == null || !result.Success)
            {
                // Candidates and dirty state stay exactly as they were.
                MessageBox.Show(this,
                    result != null ? result.UserMessage : UserMessages.LayoutNotLinked,
                    UserMessages.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetAllFeet(result.Feet);
            _sourceSnapshot = result.SourceSnapshot;
            MarkDirty(false); // widths changed, but the reload policy must not be touched
            UpdateStatus();
        }

        private void OnFitToTotal(object sender, EventArgs e)
        {
            string text = _targetTotal.Text != null ? _targetTotal.Text.Trim() : string.Empty;
            double target;
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out target)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out target);
            if (!ok || double.IsNaN(target) || double.IsInfinity(target) || target <= 0.0)
            {
                SetStatus(UserMessages.LayoutInvalidTarget2, true);
                return;
            }

            double targetFeet = LengthUnitHelper.ToFeet(target, _unit);
            FitToTotalResult result = LayoutOps.FitToTotalFeet(CurrentFeet(), targetFeet);
            _sourceSnapshot = null;
            SetAllFeet(result.Feet);
            MarkDirty(true);

            if (!result.Achievable)
            {
                double reachable = 0.0;
                foreach (double f in result.Feet) reachable += f;
                SetStatus(UserMessages.LayoutFitToTotalNotReached(
                    result.Reason ?? string.Empty,
                    LengthUnitHelper.FromFeet(reachable, _unit),
                    LengthUnitHelper.Label(_unit)), true);
            }
            else
            {
                UpdateStatus();
            }
        }

        private void OnPolicyChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;
            RadioButton rb = sender as RadioButton;
            if (rb != null && !rb.Checked) return; // only react to the newly checked one
            _dirty = true;
            UpdateStatus();
        }

        /// <summary>
        /// Applies the current candidates immediately and stays open, so several
        /// layouts can be compared against the real schedule. The command supplies
        /// the callback and owns the re-verification plus the all-or-nothing
        /// transaction; the dialog only passes values.
        /// </summary>
        private void OnApplyNow(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure) return;
            if (_applyNow == null) return;
            if (!ValidateBeforeApply()) return;

            LayoutApplyResult result = _applyNow(
                CurrentFeet(), CurrentPolicy(), _sourceSnapshot);
            if (result == null) return;

            if (result.Success)
            {
                _dirty = false; // already in the model; closing needs no prompt
                _sourceSnapshot = null;
                SetStatus(result.UserMessage, false);
            }
            else
            {
                SetStatus(result.UserMessage, true);
                MessageBox.Show(this, result.UserMessage, UserMessages.DialogTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (result.FatalRegenerationFailure)
                {
                    // Revit's document is no longer legal to read after a
                    // regeneration failure. Close this dialog without asking
                    // about unsaved candidates; the command must return too.
                    _fatalRegenerationFailure = true;
                    PrepareOutcome(LayoutDialogAction.Cancelled);
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            }
        }

        private ColumnWidthMode CurrentPolicy()
        {
            return _policyKeep.Checked
                ? ColumnWidthMode.KeepManualWidths
                : ColumnWidthMode.ApplyExcelWidths;
        }

        private void OnApplyAndClose(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                Finish(LayoutDialogAction.Cancelled);
                return;
            }
            if (!ValidateBeforeApply()) return;
            Finish(LayoutDialogAction.ApplyAndClose);
        }

        private void OnOpenScheduleView(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                Finish(LayoutDialogAction.Cancelled);
                return;
            }
            if (!_dirty)
            {
                Finish(LayoutDialogAction.DiscardAndOpenSchedule);
                return;
            }
            DialogResult answer = AskUnsaved();
            if (answer == DialogResult.Cancel) return;                 // stay in the dialog
            if (answer == DialogResult.No)
            {
                Finish(LayoutDialogAction.DiscardAndOpenSchedule);
                return;
            }
            if (!ValidateBeforeApply()) return;
            Finish(LayoutDialogAction.ApplyAndOpenSchedule);
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                Finish(LayoutDialogAction.Cancelled);
                return;
            }
            if (!_dirty)
            {
                Finish(LayoutDialogAction.Cancelled);
                return;
            }
            DialogResult answer = AskUnsaved();
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.No)
            {
                Finish(LayoutDialogAction.DiscardAndClose);
                return;
            }
            if (!ValidateBeforeApply()) return;
            Finish(LayoutDialogAction.ApplyAndClose);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                e.Cancel = false;
                return;
            }
            // The X button follows the same rules as Close.
            if (Outcome.Action != LayoutDialogAction.Cancelled || !_dirty) return;
            if (e.CloseReason != CloseReason.UserClosing) return;

            DialogResult answer = AskUnsaved();
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.No)
            {
                PrepareOutcome(LayoutDialogAction.DiscardAndClose);
                return;
            }
            if (!ValidateBeforeApply()) { e.Cancel = true; return; }
            PrepareOutcome(LayoutDialogAction.ApplyAndClose);
        }

        private DialogResult AskUnsaved()
        {
            return MessageBox.Show(this, UserMessages.LayoutUnsavedMain, UserMessages.LayoutUnsavedTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        private bool ValidateBeforeApply()
        {
            int invalid = InvalidCount();
            if (invalid > 0)
            {
                SetStatus(UserMessages.LayoutInvalidCells(invalid), true);
                return false;
            }
            return true;
        }

        private void PrepareOutcome(LayoutDialogAction action)
        {
            Outcome = new LayoutDialogOutcome
            {
                Action = action,
                CandidateFeet = CurrentFeet(),
                ReloadPolicy = _policyKeep.Checked
                    ? ColumnWidthMode.KeepManualWidths
                    : ColumnWidthMode.ApplyExcelWidths,
                SourceSnapshot = _sourceSnapshot
            };
        }

        private void Finish(LayoutDialogAction action)
        {
            PrepareOutcome(action);
            DialogResult = action == LayoutDialogAction.Cancelled ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Marks the candidates as changed. <paramref name="presetKeep"/> switches
        /// the reload policy to "keep" (Fit Content / Distribute / manual edits);
        /// Restore Excel Widths passes false so the policy is left alone. The user
        /// can still switch it back before applying.
        /// </summary>
        private void MarkDirty(bool presetKeep)
        {
            _dirty = true;
            if (presetKeep && !_policyKeep.Checked)
            {
                _suppressEvents = true;
                _policyKeep.Checked = true;
                _suppressEvents = false;
            }
        }

        private double SumFeet()
        {
            double total = 0.0;
            foreach (double f in CurrentFeet()) total += f;
            return total;
        }

        private string FormatDisplay(double feet)
        {
            return LengthUnitHelper.FromFeet(feet, _unit)
                .ToString("F" + LengthUnitHelper.Decimals(_unit), CultureInfo.CurrentCulture);
        }

        private void UpdateStatus()
        {
            SetStatus(UserMessages.LayoutTotalPrefix + FormatDisplay(SumFeet())
                + " " + LengthUnitHelper.Label(_unit), false);
        }

        private void SetStatus(string text, bool warning)
        {
            _status.Text = text;
            _status.ForeColor = warning ? Color.Firebrick : SystemColors.ControlText;
        }
    }
}
