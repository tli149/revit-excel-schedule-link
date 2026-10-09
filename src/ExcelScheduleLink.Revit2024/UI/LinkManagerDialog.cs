using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;
// Alias the few Revit types so WinForms / Drawing types win for Form / Color /
// View / ComboBox (Autodesk.Revit.DB and Autodesk.Revit.UI define same-named types).
using Document = Autodesk.Revit.DB.Document;
using ElementId = Autodesk.Revit.DB.ElementId;
using ViewSchedule = Autodesk.Revit.DB.ViewSchedule;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>
    /// Link Manager: lists all Excel-linked schedules, shows source path and
    /// missing-source status, lets the user edit per-link settings
    /// (column-width mode, row-height mode, width scale) which persist for the
    /// next reload, and reloads the selected link or all links.
    ///
    /// Modal dialog: it runs inside the command's API context, so it calls the
    /// services (which open transactions) directly on the main thread. Built
    /// from a single TableLayoutPanel (no Dock ordering ambiguity), verified by
    /// rendering. English-only; data values keep Unicode.
    /// </summary>
    internal sealed class LinkManagerDialog : Form
    {
        private readonly Document _doc;
        private readonly ListView _list;
        private readonly NumericUpDown _widthScale;
        private readonly ComboBox _colMode;
        private readonly ComboBox _rowMode;
        private readonly Button _apply;
        private readonly Button _reloadSelected;
        private readonly Button _reloadAll;
        private readonly Button _checkAndRepair;
        private readonly Button _close;
        private readonly ToolTip _reloadSelectedToolTip = new ToolTip();
        private readonly GroupBox _settingsGroup;
        private readonly Label _emptyHint;

        private List<LinkRow> _rows = new List<LinkRow>();
        private readonly UiSettings _settings;
        private bool _userAdjustedColumns;
        private bool _stretching;
        private bool _loadingEditor;
        private bool _handlingSelection;
        private bool _settingsDirty;
        private bool _fatalRegenerationFailure;
        private ElementId _editorScheduleId;

        public LinkManagerDialog(Document doc)
        {
            _doc = doc;

            // DPI: no AutoScaleMode and no explicit Font on purpose. Revit's own
            // DPI awareness plus WinForms' default font scaling render crisply on
            // high-DPI monitors; forcing AutoScaleMode.Dpi (with a Per-Monitor
            // thread context) made the text blurry instead.
            Text = UserMessages.ManagerTitle;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(860, 480);
            // Larger default (pilot feedback), and the last window size is
            // remembered per user (clamped to the working area on restore).
            _settings = UiSettings.Load();
            Rectangle work = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1600, 900);
            int width = Math.Min(_settings.GetInt("ManagerWidth", 1200), work.Width - 40);
            int height = Math.Min(_settings.GetInt("ManagerHeight", 640), work.Height - 40);
            ClientSize = new Size(Math.Max(width, 860), Math.Max(height, 480));
            FormClosing += OnManagerClosing;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            // Constrain the single column to the form width; without this the
            // column auto-sizes to its widest child (the settings grid) and the
            // panel overflows past the right edge, clipping the rightmost button.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // empty hint
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // settings
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons

            // ---- List ----
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = true
            };
            _list.Columns.Add(UserMessages.ManagerColSchedule, 170);
            _list.Columns.Add(UserMessages.ManagerColWorksheet, 95);
            _list.Columns.Add(UserMessages.ManagerColRange, 85);
            _list.Columns.Add(UserMessages.ManagerColSource, 280); // stretches to fill (see below)
            _list.Columns.Add(UserMessages.ManagerColStatus, 180);
            _list.Columns.Add(UserMessages.ManagerIntegrityColumn, 180);
            _list.Columns.Add(UserMessages.ManagerColScale, 52);
            _list.Columns.Add(UserMessages.ManagerColColMode, 115);
            _list.Columns.Add(UserMessages.ManagerColRowMode, 115);
            _list.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            // The Source column absorbs any extra width so long paths stay
            // visible — until the user adjusts columns manually, after which
            // their widths win and are remembered.
            int[] savedColumns = _settings.GetIntList("ManagerColumns", _list.Columns.Count);
            if (savedColumns != null)
            {
                _userAdjustedColumns = true;
                for (int i = 0; i < savedColumns.Length; i++)
                {
                    if (savedColumns[i] >= 24)
                    {
                        _list.Columns[i].Width = savedColumns[i];
                    }
                }
            }
            _list.Resize += (s, e) => StretchSourceColumn();
            _list.ColumnWidthChanged += (s, e) =>
            {
                if (!_stretching)
                {
                    _userAdjustedColumns = true;
                }
            };
            layout.Controls.Add(_list, 0, 0);

            _emptyHint = new Label
            {
                Text = UserMessages.ManagerNoLinks,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 4, 3, 4),
                Visible = false
            };
            layout.Controls.Add(_emptyHint, 0, 1);

            // ---- Settings group ----
            _settingsGroup = new GroupBox
            {
                Text = UserMessages.ManagerSettingsGroup,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10)
            };
            // Clean 2-row, 4-column grid (no wrapping flow):
            //   row0: [scale label][spinner]   [col-mode label][col-mode combo]
            //   row1: [row-mode label][combo]                  [Apply, right]
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            grid.Controls.Add(MakeLabel(UserMessages.ManagerWidthScaleLabel), 0, 0);
            FlowLayoutPanel scaleRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0)
            };
            _widthScale = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.1M,
                Minimum = 0.2M,
                Maximum = 10.0M,
                Width = 80,
                Value = 1.5M,
                Margin = new Padding(3, 3, 6, 6)
            };
            _widthScale.ValueChanged += (s, e) => MarkSettingsDirty();
            scaleRow.Controls.Add(_widthScale);
            scaleRow.Controls.Add(new Label
            {
                Text = UserMessages.ManagerWidthScaleHint,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 18, 0)
            });
            grid.Controls.Add(scaleRow, 1, 0);

            grid.Controls.Add(MakeLabel(UserMessages.ManagerColModeLabel), 2, 0);
            _colMode = MakeCombo(UserMessages.ColModeApplyExcel, UserMessages.ColModeKeepManual);
            _colMode.SelectedIndexChanged += (s, e) => MarkSettingsDirty();
            _colMode.Anchor = AnchorStyles.Left;
            grid.Controls.Add(_colMode, 3, 0);

            grid.Controls.Add(MakeLabel(UserMessages.ManagerRowModeLabel), 0, 1);
            _rowMode = MakeCombo(UserMessages.RowModeApplyExcel, UserMessages.RowModeKeepManual);
            _rowMode.SelectedIndexChanged += (s, e) => MarkSettingsDirty();
            _rowMode.Anchor = AnchorStyles.Left;
            grid.Controls.Add(_rowMode, 1, 1);
            grid.SetColumnSpan(_rowMode, 2);

            _apply = new Button
            {
                Text = UserMessages.ManagerApply,
                AutoSize = true,
                MinimumSize = new Size(110, 28),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(18, 3, 3, 3)
            };
            _apply.Click += (s, e) => OnApply();
            grid.Controls.Add(_apply, 3, 1);
            _settingsGroup.Controls.Add(grid);
            layout.Controls.Add(_settingsGroup, 0, 2);

            // ---- Buttons: explicit 5-column grid (spacer + 4 buttons) so all
            // three are right-aligned and always visible (no Flow ambiguity). ----
            // AutoSize is OFF on purpose: it would collapse the Percent-100
            // spacer column and break the right-alignment.
            TableLayoutPanel buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Height = 40,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // spacer
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _checkAndRepair = new Button
            {
                Text = UserMessages.ManagerCheckAndRepair,
                AutoSize = true,
                MinimumSize = new Size(150, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            _checkAndRepair.Click += (s, e) => OnCheckAndRepair();
            new ToolTip().SetToolTip(_checkAndRepair,
                UserMessages.ManagerCheckAndRepairTooltip);

            _reloadSelected = new Button
            {
                Text = UserMessages.ManagerReloadSelected,
                AutoSize = true,
                MinimumSize = new Size(120, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            _reloadSelected.Click += (s, e) => OnReloadSelected();
            _reloadSelectedToolTip.SetToolTip(
                _reloadSelected, UserMessages.ManagerReloadSavesPending);
            _reloadAll = new Button
            {
                Text = UserMessages.ManagerReloadAll,
                AutoSize = true,
                MinimumSize = new Size(110, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            _reloadAll.Click += (s, e) => OnReloadAll();
            _close = new Button
            {
                Text = UserMessages.ManagerClose,
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            buttons.Controls.Add(_checkAndRepair, 1, 0);
            buttons.Controls.Add(_reloadSelected, 2, 0);
            buttons.Controls.Add(_reloadAll, 3, 0);
            buttons.Controls.Add(_close, 4, 0);
            layout.Controls.Add(buttons, 0, 3);

            Controls.Add(layout);
            // Enter must not close the manager while the user is editing a
            // spinner or combo. Escape/Close follows the unsaved-change prompt.
            CancelButton = _close;

            RefreshList(null);
        }

        // ------------------------------------------------------------------

        private const int SourceColumnIndex = 3;

        /// <summary>Resizes the Source column to fill the list's remaining width
        /// (skipped once the user has adjusted column widths manually).</summary>
        private void StretchSourceColumn()
        {
            if (_userAdjustedColumns)
            {
                return;
            }
            int other = 0;
            for (int i = 0; i < _list.Columns.Count; i++)
            {
                if (i != SourceColumnIndex)
                {
                    other += _list.Columns[i].Width;
                }
            }
            int available = _list.ClientSize.Width - other - 4;
            if (available > 180)
            {
                _stretching = true;
                try
                {
                    _list.Columns[SourceColumnIndex].Width = available;
                }
                finally
                {
                    _stretching = false;
                }
            }
        }

        /// <summary>Persists window size and (when manually adjusted) column widths.</summary>
        private void SaveUiSettings()
        {
            _settings.SetInt("ManagerWidth", ClientSize.Width);
            _settings.SetInt("ManagerHeight", ClientSize.Height);
            if (_userAdjustedColumns)
            {
                List<int> widths = new List<int>(_list.Columns.Count);
                foreach (ColumnHeader column in _list.Columns)
                {
                    widths.Add(column.Width);
                }
                _settings.SetIntList("ManagerColumns", widths);
            }
            _settings.Save();
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 4, 0)
            };
        }

        private static ComboBox MakeCombo(params string[] items)
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170,
                Margin = new Padding(3, 3, 16, 3)
            };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            return combo;
        }

        private void RefreshList(ElementId preferredId, bool rescanIntegrity = false)
        {
            if (rescanIntegrity)
            {
                ScheduleIntegritySessionCache.ScanManual(_doc);
            }
            _rows = LinkManagerService.GetLinks(
                _doc, ScheduleIntegritySessionCache.GetResults(_doc));
            _handlingSelection = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (LinkRow row in _rows)
                {
                    ListViewItem item = new ListViewItem(row.ScheduleName);
                    item.SubItems.Add(row.SheetName);
                    item.SubItems.Add(row.RangeText);
                    item.SubItems.Add(row.SourceDisplayPath);
                    item.SubItems.Add(row.StatusDisplay);
                    item.SubItems.Add(StyleIntegrityDisplay.For(
                        row.IntegrityResult == null
                            ? StyleIntegrityState.Indeterminate
                            : row.IntegrityResult.State));
                    item.SubItems.Add(row.WidthScale.ToString("0.00", CultureInfo.InvariantCulture));
                    item.SubItems.Add(ColModeText(row.ColumnWidthMode));
                    item.SubItems.Add(RowModeText(row.RowHeightMode));
                    if (row.IntegrityResult != null
                        && row.IntegrityResult.State == StyleIntegrityState.Affected)
                    {
                        item.ForeColor = Color.Firebrick;
                    }
                    else if (row.IntegrityResult == null
                        || row.IntegrityResult.State == StyleIntegrityState.Indeterminate)
                    {
                        item.ForeColor = Color.DarkOrange;
                    }
                    else if (!row.SourceFound)
                    {
                        item.ForeColor = Color.Firebrick;
                    }
                    _list.Items.Add(item);
                }

                int select = FindRowIndex(preferredId);
                if (select < 0 && _rows.Count > 0)
                {
                    select = 0;
                }
                if (select >= 0)
                {
                    _list.Items[select].Selected = true;
                    _list.Items[select].Focused = true;
                    _list.Items[select].EnsureVisible();
                }
            }
            finally
            {
                _list.EndUpdate();
                _handlingSelection = false;
            }

            _emptyHint.Visible = _rows.Count == 0;
            LoadSelectedEditor();
        }

        private void OnSelectionChanged()
        {
            if (_handlingSelection)
            {
                return;
            }

            int idx = SelectedIndex();
            // ListView raises a transient no-selection event while moving from
            // one row to another. Wait for the following selected-row event.
            if (idx < 0)
            {
                return;
            }

            ElementId nextId = _rows[idx].ScheduleId;
            if (_settingsDirty && _editorScheduleId != null && nextId != _editorScheduleId)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    UserMessages.ManagerSaveBeforeSelection,
                    UserMessages.ManagerTitle,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    RestoreEditorSelection();
                    return;
                }
                if (answer == DialogResult.Yes && !SaveEditorSettings())
                {
                    RestoreEditorSelection();
                    return;
                }
                if (answer == DialogResult.No)
                {
                    _settingsDirty = false;
                }
            }

            LoadSelectedEditor();
        }

        private void LoadSelectedEditor()
        {
            int idx = SelectedIndex();
            _loadingEditor = true;
            try
            {
                if (idx < 0)
                {
                    _editorScheduleId = null;
                    _settingsDirty = false;
                    _reloadSelectedToolTip.SetToolTip(
                        _reloadSelected, UserMessages.ManagerReloadSavesPending);
                    return;
                }

                LinkRow row = _rows[idx];
                _editorScheduleId = row.ScheduleId;
                _reloadSelectedToolTip.SetToolTip(
                    _reloadSelected,
                    row.IntegrityResult != null
                        && row.IntegrityResult.State == StyleIntegrityState.Affected
                        ? UserMessages.ManagerReloadSelectedRepairTooltip
                        : UserMessages.ManagerReloadSavesPending);
                _widthScale.Value = ClampScale(row.WidthScale);
                _colMode.SelectedIndex =
                    row.ColumnWidthMode == ColumnWidthMode.KeepManualWidths ? 1 : 0;
                _rowMode.SelectedIndex =
                    row.RowHeightMode == RowHeightMode.KeepManualHeights ? 1 : 0;
                _settingsDirty = false;
            }
            finally
            {
                _loadingEditor = false;
                UpdateEditorEnabled();
            }
        }

        private void MarkSettingsDirty()
        {
            if (_loadingEditor || _editorScheduleId == null)
            {
                return;
            }
            _settingsDirty = true;
            UpdateEditorEnabled();
        }

        private void UpdateEditorEnabled()
        {
            bool enabled = _editorScheduleId != null;
            _widthScale.Enabled = enabled;
            _colMode.Enabled = enabled;
            _rowMode.Enabled = enabled;
            _apply.Enabled = enabled && _settingsDirty;
            _reloadSelected.Enabled = enabled;
            _reloadAll.Enabled = _rows.Count > 0;
            _checkAndRepair.Enabled = _rows.Count > 0;
            _settingsGroup.Text = _settingsDirty
                ? UserMessages.ManagerSettingsChangedGroup
                : UserMessages.ManagerSettingsGroup;
        }

        private bool SaveEditorSettings(bool forReloadAll = false)
        {
            if (_editorScheduleId == null)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerSelectFirst);
                return false;
            }

            ColumnWidthMode colMode = _colMode.SelectedIndex == 1
                ? ColumnWidthMode.KeepManualWidths : ColumnWidthMode.ApplyExcelWidths;
            RowHeightMode rowMode = _rowMode.SelectedIndex == 1
                ? RowHeightMode.KeepManualHeights : RowHeightMode.ApplyExcelHeights;
            ViewSchedule schedule = _doc.GetElement(_editorScheduleId) as ViewSchedule;
            if (schedule == null)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerLinkNoLongerAvailable);
                return false;
            }
            ExcelLinkStorage storage = ExcelLinkSchema.LoadAny(schedule);
            if (storage == null || storage.Info == null)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerLinkNoLongerAvailable);
                return false;
            }
            if (!storage.IsV2)
            {
                if (!ReloadDialogs.ConfirmLegacyMigration(_doc, schedule.Name))
                {
                    return false;
                }
            }
            else
            {
                WorksharingAdvisory.ShowIfNeeded(_doc);
            }
            SettingsSaveOutcome saveOutcome = LinkManagerService.SaveSettings(
                _doc, _editorScheduleId, colMode, rowMode, (double)_widthScale.Value, true);
            if (saveOutcome == null || !saveOutcome.Success)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, forReloadAll
                    ? UserMessages.ManagerReloadAllSettingsSaveFailed(
                        saveOutcome != null && saveOutcome.RollbackVerified,
                        saveOutcome != null && saveOutcome.TransactionStarted)
                    : UserMessages.ManagerSettingsSaveFailed(
                        saveOutcome != null && saveOutcome.RollbackVerified,
                        saveOutcome != null && saveOutcome.TransactionStarted));
                return false;
            }

            int idx = FindRowIndex(_editorScheduleId);
            if (idx >= 0)
            {
                LinkRow row = _rows[idx];
                row.WidthScale = (double)_widthScale.Value;
                row.ColumnWidthMode = colMode;
                row.RowHeightMode = rowMode;
                _list.Items[idx].SubItems[5].Text =
                    row.IntegrityResult == null
                        ? string.Empty
                        : StyleIntegrityDisplay.For(row.IntegrityResult.State);
                _list.Items[idx].SubItems[6].Text =
                    row.WidthScale.ToString("0.00", CultureInfo.InvariantCulture);
                _list.Items[idx].SubItems[7].Text = ColModeText(colMode);
                _list.Items[idx].SubItems[8].Text = RowModeText(rowMode);
            }
            _settingsDirty = false;
            UpdateEditorEnabled();
            return true;
        }

        private void OnApply()
        {
            ElementId id = _editorScheduleId;
            if (!SaveEditorSettings())
            {
                return;
            }
            RefreshList(id);
            TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerSettingsSaved);
        }

        private void OnReloadSelected()
        {
            ElementId id = _editorScheduleId;
            if (id == null)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerSelectFirst);
                return;
            }
            ViewSchedule schedule = _doc.GetElement(id) as ViewSchedule;
            if (schedule == null)
            {
                TaskDialog.Show(UserMessages.ManagerTitle, UserMessages.ManagerLinkNoLongerAvailable);
                RefreshList(null);
                return;
            }

            PendingLinkSettings pending = _settingsDirty
                ? new PendingLinkSettings
                {
                    ColumnWidthMode = _colMode.SelectedIndex == 1
                        ? ColumnWidthMode.KeepManualWidths : ColumnWidthMode.ApplyExcelWidths,
                    RowHeightMode = _rowMode.SelectedIndex == 1
                        ? RowHeightMode.KeepManualHeights : RowHeightMode.ApplyExcelHeights,
                    WidthScale = (double)_widthScale.Value
                }
                : null;
            StyleIntegrityResult prior = LinkManagerService.GetIntegrity(_rows, id);
            ReloadOutcome outcome = ReloadService.Reload(
                _doc, schedule, ReloadScope.SingleLink, pending, prior);
            if (outcome.IsFatalRegenerationFailure)
            {
                StopAfterFatalRegenerationFailure(outcome.Detail);
                return;
            }
            bool fallbackSucceeded = false;
            bool offersFallback =
                    ScheduleIntegrityFlowDecisions.ShouldOfferCurrentTableFallback(
                        prior == null ? (StyleIntegrityState?)null : prior.State,
                        outcome.Kind != ReloadResultKind.SourceMissing
                            && outcome.Kind != ReloadResultKind.NetworkAccessCancelled
                            && outcome.Kind != ReloadResultKind.ReadFailed);
            if (offersFallback)
            {
                RepairAttemptOutcome fallback =
                    ScheduleRepairCoordinator.TryFallback(_doc, schedule);
                if (fallback != null && fallback.FatalRegenerationFailure)
                {
                    StopAfterFatalRegenerationFailure(null);
                    return;
                }
                fallbackSucceeded = fallback != null && fallback.Success;
            }
            StyleIntegrityResult after = RescanOne(id);
            bool mutationSucceeded = outcome.IsSuccess || fallbackSucceeded;
            bool finalRescanHealthy =
                ScheduleIntegrityFlowDecisions.IsSuccessfulTargetedReload(
                    outcome.IsSuccess, fallbackSucceeded, after);
            bool postRepairFailed = mutationSucceeded && !finalRescanHealthy;
            if (postRepairFailed)
            {
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.FallbackPostRepairFailed);
            }
            else
            {
                if (!fallbackSucceeded)
                {
                    ShowReloadOutcome(outcome);
                }
            }
            if (outcome.IsSuccess && !postRepairFailed)
            {
                _settingsDirty = false;
                RefreshList(id, false);
            }
            else if (pending == null && outcome.Kind != ReloadResultKind.Cancelled
                && outcome.Kind != ReloadResultKind.NetworkAccessCancelled)
            {
                RefreshList(id, false);
            }
            else if (pending != null && outcome.Kind != ReloadResultKind.Cancelled
                && outcome.Kind != ReloadResultKind.NetworkAccessCancelled)
            {
                UpdateIntegrityRow(id, after);
            }
        }

        private void OnCheckAndRepair()
        {
            ElementId selected = _editorScheduleId;
            CheckAndRepairOutcome outcome =
                ScheduleRepairCoordinator.CheckAndRepair(_doc);
            if (outcome != null && outcome.FatalRegenerationFailure)
            {
                StopAfterFatalRegenerationFailure(null);
                return;
            }
            RefreshList(selected, false);
            TaskDialog.Show(UserMessages.ManagerTitle,
                UserMessages.CheckAndRepairSummary(
                    outcome.Summary, outcome.Details));
        }

        private void OnReloadAll()
        {
            ElementId selectedId = _editorScheduleId;
            if (_settingsDirty)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    UserMessages.ManagerSaveBeforeReloadAll,
                    UserMessages.ManagerTitle,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    return;
                }
                if (answer == DialogResult.Yes && !SaveEditorSettings(true))
                {
                    return;
                }
                if (answer == DialogResult.No)
                {
                    _settingsDirty = false;
                    UpdateEditorEnabled();
                }
            }

            int success = 0, unchanged = 0, missing = 0, failed = 0, repaired = 0,
                confirmationRequired = 0;
            List<string> truncatedSchedules = new List<string>();
            List<string> confirmationDetails = new List<string>();
            foreach (LinkRow row in new List<LinkRow>(_rows))
            {
                ViewSchedule schedule = _doc.GetElement(row.ScheduleId) as ViewSchedule;
                if (schedule == null)
                {
                    failed++;
                    confirmationDetails.Add(
                        UserMessages.ManagerReloadAllUnavailable(row.ScheduleName));
                    continue;
                }
                ReloadOutcome outcome = ReloadService.Reload(
                    _doc, schedule, ReloadScope.AllLinks, null,
                    row.IntegrityResult);
                if (outcome.IsFatalRegenerationFailure)
                {
                    StopAfterFatalRegenerationFailure(outcome.Detail);
                    return;
                }
                switch (outcome.Kind)
                {
                    case ReloadResultKind.Success:
                        if (outcome.Truncated)
                        {
                            truncatedSchedules.Add(row.ScheduleName);
                        }
                        StyleIntegrityResult after = RescanOne(row.ScheduleId);
                        if (ScheduleIntegrityFlowDecisions.CountsAsWorkbookRepair(
                                row.IntegrityResult, true, after))
                        {
                            repaired++;
                        }
                        if (after == null || after.State != StyleIntegrityState.Healthy)
                        {
                            failed++;
                            confirmationDetails.Add(
                                UserMessages.ManagerReloadAllPostRepairNotHealthy(
                                    row.ScheduleName));
                            break;
                        }
                        success++;
                        if (outcome.Identity == ExcelScheduleLink.Contracts.SourceIdentityState.Match)
                        {
                            unchanged++;
                        }
                        break;
                    case ReloadResultKind.SourceMissing:
                        missing++;
                        RescanOne(row.ScheduleId);
                        break;
                    case ReloadResultKind.NetworkAccessCancelled:
                        confirmationRequired++;
                        confirmationDetails.Add(outcome.Detail);
                        RescanOne(row.ScheduleId);
                        break;
                    case ReloadResultKind.ConfirmationRequired:
                        confirmationRequired++;
                        confirmationDetails.Add(outcome.Detail);
                        RescanOne(row.ScheduleId);
                        break;
                    case ReloadResultKind.Cancelled:
                        RescanOne(row.ScheduleId);
                        break;
                    default:
                        RescanOne(row.ScheduleId);
                        failed++;
                        confirmationDetails.Add(
                            UserMessages.ManagerReloadAllFailure(
                                row.ScheduleName, outcome.Detail));
                        break;
                }
            }
            RefreshList(selectedId, false);
            TaskDialog.Show(UserMessages.ManagerTitle,
                UserMessages.ManagerReloadSummary(
                    success, unchanged, missing, failed, repaired,
                    confirmationRequired, truncatedSchedules, confirmationDetails));
        }

        private static void ShowReloadOutcome(ReloadOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }
            if (outcome.Kind == ReloadResultKind.Cancelled)
            {
                return;
            }
            if (!outcome.IsSuccess && !string.IsNullOrEmpty(outcome.Detail))
            {
                TaskDialog.Show(UserMessages.ManagerTitle, outcome.Detail);
                return;
            }
            if (outcome.IsSuccess)
            {
                string summary = UserMessages.ReloadSucceeded(outcome);
                summary = UserMessages.AppendRenderNotes(
                    summary, outcome.SourceRows, outcome.SourceColumns,
                    outcome.Truncated, outcome.ViewWidthProblem,
                    outcome.ColumnWidthsRejected);
                TaskDialog.Show(UserMessages.ManagerTitle, summary);
            }
        }

        private void OnManagerClosing(object sender, FormClosingEventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                e.Cancel = false;
                return;
            }
            if (_settingsDirty)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    UserMessages.ManagerSaveBeforeClose,
                    UserMessages.ManagerTitle,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes && !SaveEditorSettings())
                {
                    e.Cancel = true;
                    return;
                }
            }
            SaveUiSettings();
        }

        private void StopAfterFatalRegenerationFailure(string detail)
        {
            _fatalRegenerationFailure = true;
            if (!string.IsNullOrEmpty(detail))
            {
                TaskDialog.Show(UserMessages.ManagerTitle, detail);
            }
            Close();
        }

        private StyleIntegrityResult RescanOne(ElementId id)
        {
            ViewSchedule schedule = _doc.GetElement(id) as ViewSchedule;
            if (schedule == null)
            {
                return null;
            }
            StyleIntegrityResult result =
                ScheduleStyleIntegrityScanner.ScanWithRolledBackTransaction(
                    _doc, schedule);
            ScheduleIntegritySessionCache.SetResult(_doc, id, result);
            return result;
        }

        private void UpdateIntegrityRow(ElementId id, StyleIntegrityResult result)
        {
            int index = FindRowIndex(id);
            if (index < 0)
            {
                return;
            }
            LinkRow row = _rows[index];
            row.IntegrityResult = result;
            ListViewItem item = _list.Items[index];
            item.SubItems[5].Text = StyleIntegrityDisplay.For(
                result == null ? StyleIntegrityState.Indeterminate : result.State);
            if (result != null && result.State == StyleIntegrityState.Affected)
            {
                item.ForeColor = Color.Firebrick;
            }
            else if (result == null || result.State == StyleIntegrityState.Indeterminate)
            {
                item.ForeColor = Color.DarkOrange;
            }
            else if (!row.SourceFound)
            {
                item.ForeColor = Color.Firebrick;
            }
            else
            {
                item.ForeColor = _list.ForeColor;
            }
        }

        private int SelectedIndex()
        {
            return _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        }

        private int FindRowIndex(ElementId id)
        {
            if (id == null)
            {
                return -1;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].ScheduleId == id)
                {
                    return i;
                }
            }
            return -1;
        }

        private void RestoreEditorSelection()
        {
            int idx = FindRowIndex(_editorScheduleId);
            if (idx < 0)
            {
                return;
            }
            _handlingSelection = true;
            try
            {
                foreach (ListViewItem item in _list.Items)
                {
                    item.Selected = false;
                }
                _list.Items[idx].Selected = true;
                _list.Items[idx].Focused = true;
                _list.Items[idx].EnsureVisible();
            }
            finally
            {
                _handlingSelection = false;
            }
        }

        private static string ColModeText(ColumnWidthMode mode)
        {
            return mode == ColumnWidthMode.KeepManualWidths
                ? UserMessages.ColModeKeepManual : UserMessages.ColModeApplyExcel;
        }

        private static string RowModeText(RowHeightMode mode)
        {
            return mode == RowHeightMode.KeepManualHeights
                ? UserMessages.RowModeKeepManual : UserMessages.RowModeApplyExcel;
        }

        private static decimal ClampScale(double value)
        {
            if (double.IsNaN(value) || value < 0.2) return 1.0M;
            if (value > 10.0) return 10.0M;
            return (decimal)value;
        }
    }
}
