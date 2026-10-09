using System;
using System.Collections.Generic;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Stores only per-user UNC server approvals in the existing user-scope
    /// settings file. No server approval is persisted in the Revit model.
    /// </summary>
    internal sealed class UserNetworkServerApprovalStore : INetworkServerApprovalStore
    {
        private const string SettingsKey = "ApprovedNetworkServers";
        private readonly UiSettings _settings;
        private readonly HashSet<string> _approved =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public UserNetworkServerApprovalStore()
        {
            _settings = UiSettings.Load();
            string saved = _settings.GetString(SettingsKey, string.Empty);
            foreach (string item in saved.Split(
                new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string server = StoredWorkbookPathPolicy.NormalizeServer(item);
                if (!string.IsNullOrEmpty(server))
                {
                    _approved.Add(server);
                }
            }
        }

        public bool IsApproved(string server)
        {
            string normalized = StoredWorkbookPathPolicy.NormalizeServer(server);
            return !string.IsNullOrEmpty(normalized) && _approved.Contains(normalized);
        }

        public void Approve(string server)
        {
            string normalized = StoredWorkbookPathPolicy.NormalizeServer(server);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }
            _approved.Add(normalized);
            _settings.SetString(SettingsKey, string.Join("|", _approved));
            _settings.Save();
        }
    }
}
