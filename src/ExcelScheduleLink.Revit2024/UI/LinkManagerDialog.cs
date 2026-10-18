using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.Link;
// Alias the few Revit types so WinForms / Drawing types win for Form / Color /
// View (Autodesk.Revit.DB and Autodesk.Revit.UI define same-named types).
using Document = Autodesk.Revit.DB.Document;
using ElementId = Autodesk.Revit.DB.ElementId;
using ViewSchedule = Autodesk.Revit.DB.ViewSchedule;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace ExcelScheduleLink.Revit2024.UI
{
    /// <summary>
    /// Link Manager: lists all Excel-linked schedules, shows source path and
    /// missing-source status, and reloads the selected link or all links.
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
        private readonly Button _reloadSelected;
        private readonly Button _reloadAll;
        private readonly Button _checkAndRepair;
        private readonly Button _close;
        private readonly ToolTip _reloadSelectedToolTip = new ToolTip();
        private readonly Label _emptyHint;

        private List<LinkRow> _rows = new List<LinkRow>();
        private readonly UiSettings _settings;
        private bool _userAdjustedColumns;
        private bool _stretching;
        private bool _handlingSelection;
        private bool _fatalRegenerationFailure;

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
                RowCount = 3,
                Padding = new Padding(12)
            };
            // Constrain the single column to the form width so the list and the
            // button row stay inside the client area.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // empty hint
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

            // ---- Buttons: explicit 5-column grid (spacer + 4 buttons) so all
            // four are right-aligned and always visible (no Flow ambiguity). ----
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
            _checkAndRepair.Click += (s, e) => RunGuarded(OnCheckAndRepair);
            new ToolTip().SetToolTip(_checkAndRepair,
                UserMessages.ManagerCheckAndRepairTooltip);

            _reloadSelected = new Button
            {
                Text = UserMessages.ManagerReloadSelected,
                AutoSize = true,
                MinimumSize = new Size(120, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            _reloadSelected.Click += (s, e) => RunGuarded(OnReloadSelected);
            _reloadSelectedToolTip.SetToolTip(
                _reloadSelected, UserMessages.ManagerReloadSelectedTooltip);
            _reloadAll = new Button
            {
                Text = UserMessages.ManagerReloadAll,
                AutoSize = true,
                MinimumSize = new Size(110, 28),
                Margin = new Padding(6, 0, 0, 0)
            };
            _reloadAll.Click += (s, e) => RunGuarded(OnReloadAll);
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
            layout.Controls.Add(buttons, 0, 2);

            Controls.Add(layout);
            // Escape and the Close button use the dialog's normal close behavior.
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

        private void RefreshList(ElementId preferredId, bool rescanIntegrity = false)
        {
            if (_fatalRegenerationFailure)
            {
                return;
            }
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
            UpdateSelectionEnabled();
        }

        private void OnSelectionChanged()
        {
            if (_fatalRegenerationFailure)
            {
                return;
            }
            if (_handlingSelection)
            {
                return;
            }
            UpdateSelectionEnabled();
        }

        private ElementId SelectedScheduleId()
        {
            int idx = SelectedIndex();
            return idx >= 0 && idx < _rows.Count ? _rows[idx].ScheduleId : null;
        }

        private void UpdateSelectionEnabled()
        {
            ElementId selectedId = SelectedScheduleId();
            _reloadSelected.Enabled = selectedId != null;
            _reloadAll.Enabled = _rows.Count > 0;
            _checkAndRepair.Enabled = _rows.Count > 0;
            StyleIntegrityResult integrity = selectedId == null
                ? null : LinkManagerService.GetIntegrity(_rows, selectedId);
            _reloadSelectedToolTip.SetToolTip(
                _reloadSelected,
                integrity != null && integrity.State == StyleIntegrityState.Affected
                    ? UserMessages.ManagerReloadSelectedRepairTooltip
                    : UserMessages.ManagerReloadSelectedTooltip);
        }

        private void OnReloadSelected()
        {
            if (_fatalRegenerationFailure)
            {
                return;
            }
            ElementId id = SelectedScheduleId();
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

            StyleIntegrityResult prior = LinkManagerService.GetIntegrity(_rows, id);
            TargetedReloadFlowResult flow = TargetedReloadFlow.Run(
                _doc, schedule, ReloadScope.SingleLink, prior);
            ReloadOutcome outcome = flow.Reload;
            if (flow.FatalRegeneration)
            {
                StopAfterFatalRegenerationFailure(
                    outcome.IsFatalRegenerationFailure ? outcome.Detail : null);
                return;
            }
            bool fallbackSucceeded = flow.Fallback != null
                && flow.Fallback.Success;
            bool postRepairFailed = flow.MutationSucceeded && !flow.FinalHealthy;
            if (postRepairFailed)
            {
                TaskDialog.Show(UserMessages.DialogTitle,
                    UserMessages.FallbackPostRepairFailed);
            }
            else
            {
                if (!fallbackSucceeded)
                {
                    TargetedReloadFlow.ShowReloadOutcome(
                        flow, UserMessages.ManagerTitle);
                }
            }
            if (outcome.Kind != ReloadResultKind.Cancelled
                && outcome.Kind != ReloadResultKind.NetworkAccessCancelled)
            {
                RefreshList(id, false);
            }
        }

        private void OnCheckAndRepair()
        {
            if (_fatalRegenerationFailure)
            {
                return;
            }
            ElementId selected = SelectedScheduleId();
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
            if (_fatalRegenerationFailure)
            {
                return;
            }
            ElementId selectedId = SelectedScheduleId();

            ReloadAllFlowResult reloadAll = ReloadAllFlow.Run(
                _doc, new List<LinkRow>(_rows));
            if (reloadAll.FatalRegenerationFailure)
            {
                StopAfterFatalRegenerationFailure(reloadAll.FatalDetail);
                return;
            }
            RefreshList(selectedId, false);
            TaskDialog.Show(UserMessages.ManagerTitle,
                UserMessages.ManagerReloadSummary(
                    reloadAll.Success, reloadAll.Unchanged, reloadAll.Missing,
                    reloadAll.Failed, reloadAll.Repaired,
                    reloadAll.ConfirmationRequired,
                    reloadAll.TruncatedSchedules, reloadAll.Details));
        }

        private void OnManagerClosing(object sender, FormClosingEventArgs e)
        {
            if (_fatalRegenerationFailure)
            {
                e.Cancel = false;
                return;
            }
            SaveUiSettings();
        }

        private void RunGuarded(Action handler)
        {
            FatalRegenerationBoundary.Guard(handler, delegate
            {
                // A status-path stop may already have warned; never warn twice.
                StopAfterFatalRegenerationFailure(_fatalRegenerationFailure
                    ? null : UserMessages.RegenerationFailure);
            });
        }

        private void StopAfterFatalRegenerationFailure(
            string detail, bool close = true)
        {
            _fatalRegenerationFailure = true;
            try
            {
                if (!string.IsNullOrEmpty(detail))
                {
                    TaskDialog.Show(UserMessages.ManagerTitle, detail);
                }
            }
            catch (Exception)
            {
                // The dialog is already terminal: a failed notification must
                // neither keep it open nor start another warning.
            }
            finally
            {
                if (close)
                {
                    Close();
                }
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

    }
}
