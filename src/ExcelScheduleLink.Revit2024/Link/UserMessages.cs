namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// Centralized user-facing strings for the production add-in UI
    /// (English-only by requirement). Data values such as file paths and
    /// worksheet names are interpolated verbatim and keep full Unicode.
    /// </summary>
    internal static class UserMessages
    {
        public const string DialogTitle = "Excel Schedule Link";

        public const string NoActiveDocument =
            "No document is open. Open a Revit project and run the command again.";

        public const string FileDialogTitle = "Select an Excel workbook";
        public const string FileDialogFilter = "Excel Workbook (*.xlsx)|*.xlsx";

        // ---- Stored workbook path policy ----
        public static string NetworkPathApprovalInstruction(string server)
        {
            return "Allow access to \\\\" + (server ?? string.Empty) + "?";
        }

        public static string NetworkPathApprovalContent(string fullPath)
        {
            return "Excel Schedule Link is about to access this network workbook:\n\n"
                + (fullPath ?? string.Empty) + "\n\n"
                + "Allow only if you trust this server. The approval is stored for "
                + "this Windows user on this computer and is not stored in the Revit model.";
        }

        public const string NetworkPathApprovalAllow = "Allow access";
        public const string NetworkPathApprovalAllowHint =
            "Approve this server and continue with the workbook action.";

        public static string NetworkAccessCancelled(string fullPath)
        {
            return "Network workbook access was canceled. No filesystem access was attempted for:\n"
                + (fullPath ?? string.Empty) + "\n\nNo model transaction was started.";
        }

        public static string NetworkAccessNotApproved(string server, string fullPath)
        {
            return "The network workbook was not accessed because server \\\\"
                + (server ?? string.Empty) + " has not been approved.\n\nPath: "
                + (fullPath ?? string.Empty) + "\n\nNo model transaction was started.";
        }

        public static string StoredWorkbookPathRejected(
            string path, ExcelScheduleLink.Contracts.StoredWorkbookPathIssue issue)
        {
            string reason;
            switch (issue)
            {
                case ExcelScheduleLink.Contracts.StoredWorkbookPathIssue.DeviceNamespace:
                    reason = "Windows device namespaces are not allowed.";
                    break;
                case ExcelScheduleLink.Contracts.StoredWorkbookPathIssue.WrongExtension:
                    reason = "Only .xlsx workbook paths are allowed.";
                    break;
                case ExcelScheduleLink.Contracts.StoredWorkbookPathIssue.TooLong:
                    reason = "The path is longer than the supported path/string limit.";
                    break;
                case ExcelScheduleLink.Contracts.StoredWorkbookPathIssue.NetworkNotApproved:
                    reason = "The network server has not been approved.";
                    break;
                case ExcelScheduleLink.Contracts.StoredWorkbookPathIssue.Empty:
                    reason = "The stored path is empty.";
                    break;
                default:
                    reason = "The path format is invalid.";
                    break;
            }
            return "The stored workbook path was rejected before filesystem access. "
                + reason + "\n\nPath: " + (path ?? string.Empty)
                + "\n\nNo model transaction was started.";
        }

        // ---- Link dialog ----
        public const string LinkDialogTitle = "Link Excel Worksheet";
        public const string LinkDialogSourceLabel = "Source:";
        public const string LinkDialogWorksheetLabel = "Worksheet:";
        public const string LinkDialogRangeLabel = "Cell range (optional):";
        public const string LinkDialogRangeHint =
            "Example: A1:F30. Leave empty to read the whole used range.";
        public const string LinkDialogWidthScaleLabel = "Column width scale:";
        public const string LinkDialogWidthScaleHint =
            "Multiplier applied to all column widths. Recommended: 1.5 - 2.0.";
        public const string LinkDialogOk = "Link Worksheet";
        public const string LinkDialogCancel = "Cancel";

        public static string ReadFailed(string detail)
        {
            return "Failed to read the Excel workbook.\n\n" + detail
                + "\n\nNo model transaction was started; the Revit model was not touched.";
        }

        public const string RollbackCompletionNotVerified =
            "Rollback completion could not be verified.";

        public const string RegenerationFailure =
            "Revit reported a regeneration failure. The change was abandoned.\n\n"
            + "Close this model without saving, then reopen it before continuing.";

        public static string LinkFailedRolledBack(string detail, bool rollbackVerified)
        {
            return "Failed to create the linked schedule. "
                + (rollbackVerified
                    ? "The whole transaction was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + "\n\n" + detail;
        }

        public static string LinkUnsafeStyleValidationFailed(bool rollbackVerified)
        {
            return "The linked schedule was not created because Revit did not initialize every cell style safely.\n\n"
                + (rollbackVerified
                    ? "The whole transaction was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + " Try again, and contact support if the check continues to fail.";
        }

        public static string LinkTransactionNotStarted(string status)
        {
            return "Revit did not allow starting the link transaction (status: "
                + status + "). No model changes were attempted.";
        }

        public static string LinkSucceeded(string scheduleName, int rows, int columns)
        {
            return "Created linked schedule \"" + scheduleName + "\" (" + rows + " rows x "
                + columns + " columns).";
        }

        public static string AppendRenderNotes(
            string summary,
            int totalRows,
            int totalColumns,
            bool truncated,
            bool viewWidthProblem,
            int rejectedColumns)
        {
            string result = summary ?? string.Empty;
            if (truncated)
            {
                result += "\n\n" + TruncationNote(totalRows, totalColumns);
            }
            if (viewWidthProblem)
            {
                result += "\n\n" + ViewWidthNote(rejectedColumns);
            }
            return result;
        }

        public static string TruncationNote(int totalRows, int totalColumns)
        {
            return "Note: the worksheet exceeds the limit of "
                + ExcelScheduleLink.Contracts.StyleIntegrityLimits.MaxRowsPerSchedule + " rows x "
                + ExcelScheduleLink.Contracts.StyleIntegrityLimits.MaxColumnsPerSchedule
                + " columns. Only the first part was imported ("
                + totalRows + " rows x " + totalColumns + " columns in the source).";
        }

        /// <summary>
        /// Shown when the schedule view could not be widened to the full table
        /// width. Sheet output is not affected, so the note says so explicitly:
        /// a squeezed view otherwise reads as a failed load.
        /// </summary>
        public static string ViewWidthNote(int rejectedColumns)
        {
            string note = "Note: the schedule view may show this table narrower than it really is, "
                + "which can squeeze one column. The schedule placed on a sheet is not affected. "
                + "Drag the \"A\" column strip below the table wider to see the full width.";
            if (rejectedColumns > 0)
            {
                note += "\n\nRevit also refused the requested width for " + rejectedColumns
                    + " column(s). Lower the column width scale in Link Manager and reload.";
            }
            return note;
        }

        // ---- Duplicate link guard ----
        public static string DuplicateLinkInstruction(string scheduleName)
        {
            return "This worksheet is already linked to \"" + scheduleName + "\".";
        }

        public const string DuplicateLinkContent =
            "The same workbook, worksheet and range are already linked in this model. "
            + "Linking again creates a second, independent schedule showing the same table.";

        public const string DuplicateLinkOpenExisting = "Open the existing schedule";
        public const string DuplicateLinkOpenExistingHint =
            "Nothing is created. The existing linked schedule becomes the active view.";
        public const string DuplicateLinkCreateAnother = "Create a second linked schedule";
        public const string DuplicateLinkCreateAnotherHint =
            "Links the same worksheet again. The two schedules reload independently.";

        public const string NoSchedulableField =
            "No schedulable field is available to configure the schedule (method B). "
            + "The schedule could not be created.";

        // ---- Reload ----
        public const string ReloadNoTarget =
            "No linked schedule to reload. Open a linked Excel schedule (or select exactly "
            + "one in the project browser) and run the command again.";

        public static string ReloadSourceNotFound(ExcelLinkInfo info)
        {
            string rel = string.IsNullOrEmpty(info.RelativePath) ? "(none)" : info.RelativePath;
            return "The linked workbook could not be found.\n\nAbsolute: " + info.AbsolutePath
                + "\nRelative: " + rel
                + "\n\nNo reload transaction was started; the Revit model was not touched.";
        }

        public static string ReloadFailedRolledBack(string detail, bool rollbackVerified)
        {
            return "Reload failed. "
                + (rollbackVerified
                    ? "The whole transaction was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + "\n\n" + detail;
        }

        public const string ReloadSourceIdentityUnavailable =
            "The workbook fingerprint was unavailable.";
        public const string ReloadTransactionNotStarted =
            "Revit did not allow starting the reload transaction; no model changes were attempted.";
        public const string ReloadTransactionNotCommitted =
            "Revit did not commit the reload transaction.";

        public const string ReloadChangedInstruction =
            "The workbook contents changed since the last accepted load.";
        public static string ReloadChangedContent(string resolvedPath)
        {
            return "ExcelScheduleLink found different workbook bytes at \""
                + (resolvedPath ?? string.Empty) + "\". If you intentionally edited or replaced "
                + "this workbook, continue. If this link should still use the previous source, cancel.";
        }
        public const string ReloadContinue = "Continue";
        public const string ReloadContinueHint =
            "Reload the changed workbook and accept its bytes as the new baseline.";

        public const string ReloadStrongInstruction =
            "This path may resolve to a different workbook on this computer.";
        public static string ReloadStrongContent(string resolvedPath, string creator)
        {
            return "This link was created by \"" + DisplayCreator(creator) + "\". At \""
                + (resolvedPath ?? string.Empty) + "\", this computer found workbook bytes that do not "
                + "match the last accepted source. This can mean the shared workbook was intentionally "
                + "edited, or the same path points to a different machine-local file. Reloading will "
                + "replace the shared Revit schedule with the resolved workbook.";
        }
        public const string ReloadStrongContinue = "Reload this resolved workbook";
        public const string ReloadStrongContinueHint =
            "Accept the resolved bytes and update the shared Revit schedule.";

        public const string ReloadUnknownInstruction =
            "This existing link has no workbook identity baseline.";
        public static string ReloadUnknownContent(
            string resolvedPath, string creator, bool isLegacy, bool strong)
        {
            string text = "It was stored by an older ExcelScheduleLink version. v0.9.1 cannot determine "
                + "whether \"" + (resolvedPath ?? string.Empty) + "\" is the workbook originally used by \""
                + DisplayCreator(creator) + "\". Continuing establishes these bytes as the first accepted "
                + "baseline";
            if (isLegacy)
            {
                text += " and upgrades the link";
            }
            text += ".";
            if (isLegacy)
            {
                text += " Older add-in versions will no longer manage this link.";
            }
            if (strong)
            {
                text += " Because creator evidence is different or unavailable in this workshared model, "
                    + "cancel if this may be a machine-local collision.";
            }
            return text;
        }
        public const string ReloadUnknownContinue = "Accept this source and establish the baseline";
        public const string ReloadUnknownContinueHint =
            "Use these bytes for this reload and protect future reloads with their fingerprint.";

        public const string ReloadWidthWarningInstruction =
            "This reload may replace another user's manual column widths.";
        public static string ReloadWidthWarningContent(string creator)
        {
            return "This link was created by \"" + DisplayCreator(creator) + "\" and is set to Apply Excel "
                + "widths. Reloading reapplies workbook widths to the shared schedule. Continue only if "
                + "that is intended, or cancel and choose Keep manual widths.";
        }
        public const string ReloadWidthWarningContinue = "Reload and apply this width policy";
        public const string ReloadWidthWarningContinueHint =
            "Reapply Excel widths to the shared schedule.";

        public static string ReloadSucceeded(ReloadOutcome outcome)
        {
            string decision;
            if (outcome.Identity == ExcelScheduleLink.Contracts.SourceIdentityState.Match)
            {
                decision = outcome.UserConfirmed
                    ? "Source contents matched the last accepted workbook; reload completed after confirmation."
                    : "Source contents matched the last accepted workbook.";
            }
            else if (outcome.Identity == ExcelScheduleLink.Contracts.SourceIdentityState.Differs)
            {
                decision = "Source contents changed and were reloaded after confirmation.";
            }
            else
            {
                decision = "Identity baseline established for a migrated link after confirmation.";
            }
            string text = "Reloaded \"" + outcome.ScheduleName + "\" (" + outcome.Rows + " rows x "
                + outcome.Columns + " columns).\n" + decision;
            if (outcome.UnsafeStylesRepaired)
            {
                text += "\n" + UnsafeCellStylesRepaired;
            }
            return text;
        }

        public const string UnsafeCellStylesRepaired = "Unsafe cell styles were repaired.";

        // ---- Schedule style integrity lifecycle / activation ----
        public const string AutomaticUnsafeStylesInstruction =
            "Unsafe Excel-linked schedule styles were found.";
        public static string AutomaticUnsafeStylesContent(
            System.Collections.Generic.IEnumerable<string> names)
        {
            return AutomaticUnsafeStylesContent(names, false);
        }
        public static string AutomaticUnsafeStylesContent(
            System.Collections.Generic.IEnumerable<string> names,
            bool offersReviewRepairs)
        {
            return "Do not open the following schedule view(s); Revit may close unexpectedly:\n"
                + string.Join("\n", names ?? new string[0])
                + "\n\n"
                + (offersReviewRepairs
                    ? "Choose Review repairs now to review and repair one schedule at a time. No repair was made."
                    : "Open Excel Schedule Link - Link Manager and choose Check and repair. No repair was made.");
        }
        public const string AutomaticReviewRepairsNow = "Review repairs now";
        public const string AutomaticReviewRepairsNowHint =
            "Review each affected schedule and authorize its repair separately.";
        public const string AutomaticNotNow = "Not now";
        public const string AutomaticRepairFlowFailedInstruction =
            "The document-open repair review did not finish.";
        public static string AutomaticRepairFlowFailed(System.Exception exception)
        {
            string detail = exception == null
                ? "The repair flow stopped without a diagnostic."
                : exception.GetType().Name + ": " + exception.Message;
            return AutomaticRepairFlowFailedInstruction + "\n\n"
                + "Some schedules may still have unsafe cell styles. No success was claimed. "
                + "Run Check and repair again.\n\nDiagnostic: " + detail;
        }
        public const string AutomaticUnverifiedInstruction =
            "Excel Schedule Link could not verify every linked schedule.";
        public const string AutomaticUnverifiedContent =
            "Do not open unchecked linked schedule views. Open Link Manager and run Check and repair. No repair was made.";
        public const string AutomaticReadOnlyMessage =
            "The document is currently read-only. Open an editable copy and run Check and repair before opening linked schedule views.";

        public static string ActivationUnsafe(string scheduleName)
        {
            return (scheduleName ?? string.Empty)
                + " was not opened because its cell styles are unsafe.\n\n"
                + "Opening it may close Revit unexpectedly. Repair the schedule first. Nothing was changed by this check.";
        }
        public static string ActivationUnverified(string scheduleName)
        {
            return (scheduleName ?? string.Empty)
                + " was not opened because Excel Schedule Link could not verify its cell styles.\n\n"
                + "Run Check and repair from an editable document before opening this schedule.";
        }
        public const string ActivationReloadFromWorkbook = "Reload from workbook";
        public const string ActivationReloadFromWorkbookHint =
            "Use the ordinary reload path and validate every rendered cell before opening.";

        public const string FallbackInstruction = "The linked workbook could not be read.";
        public const string FallbackContent =
            "You can rebuild this schedule from the text and layout currently stored in Revit. "
            + "This does not read Excel. Cell-specific formatting will be replaced by the schedule's current Appearance settings. "
            + "The schedule, name, and sheet placements will be kept.";
        public const string FallbackRebuild = "Rebuild from current table";
        public const string FallbackRebuildHint =
            "Explicitly consent to rebuild the existing schedule from its current Revit table.";
        public const string DetachedRepairSentence = "This repair changes the detached copy only.";
        public const string ReadOnlyFileRepairMessage =
            "This model was opened from a read-only file. Save an editable copy before repairing. Nothing was changed in the Revit model.";
        public static string RepairOwnershipMessage(
            string scheduleName, string owner, bool nothingChanged)
        {
            string text = (scheduleName ?? string.Empty)
                + " could not be repaired because it is not editable by the current user. ";
            if (HasReliableOwner(owner))
            {
                text += "It is currently owned by " + owner.Trim()
                    + ". Ask that user to synchronize and relinquish it, then reload latest. ";
            }
            else
            {
                text += "Ask the current owner to synchronize and relinquish it, then reload latest. ";
            }
            text += "Reload the schedule or run Check and repair again.";
            return nothingChanged
                ? text + " Nothing was changed in the Revit model."
                : text;
        }

        private static bool HasReliableOwner(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
            {
                return false;
            }
            string value = owner.Trim();
            return !string.Equals(value, "unknown", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "unavailable", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "n/a", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "<unknown>", System.StringComparison.OrdinalIgnoreCase);
        }
        public const string FallbackSnapshotFailedInstruction =
            "The schedule could not be rebuilt from its current table.";
        public static string FallbackSnapshotFailureContent(bool rollbackVerified)
        {
            return "Excel Schedule Link could not read all text and layout data needed to preserve the table. "
                + (rollbackVerified
                    ? "The whole transaction was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + " Do not open this schedule view.";
        }
        public const string FallbackPostRepairFailed =
            "The repair transaction completed, but the post-repair style check was not healthy. "
            + "The schedule was not opened. Run Check and repair again.";
        public static string FallbackRebuildFailed(string detail, bool rollbackVerified)
        {
            return FallbackSnapshotFailedInstruction + "\n\n"
                + (rollbackVerified
                    ? "The whole transaction was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + (string.IsNullOrEmpty(detail) ? string.Empty : "\n\n" + detail);
        }

        public const string ManagerIntegrityColumn = "Integrity";
        public const string ManagerCheckAndRepair = "Check and repair...";
        public const string ManagerCheckAndRepairTooltip =
            "Check all linked schedules and explicitly choose whether unsafe schedules may be reloaded.";
        public const string ManagerReloadSelectedRepairTooltip =
            "Reloading this link repairs its unsafe cell styles from the workbook.";

        public static string BulkRepairInstruction(int affectedCount)
        {
            return "Repair unsafe linked schedules?";
        }
        public static string BulkRepairContent(int affectedCount)
        {
            return "Excel Schedule Link found " + affectedCount
                + " linked schedule(s) with unsafe cell styles.\n"
                + "Reloading them from their linked workbooks changes the Revit model. "
                + "The existing schedules, names, and sheet placements will be kept. "
                + "Any existing source, width, or migration confirmation will still be shown.";
        }
        public const string BulkRepairClose = "Close without repairing";
        public const string ReviewRepairsOneAtATime = "Review repairs one at a time";
        public const string ReviewRepairsOneAtATimeHint =
            "Review each affected schedule before authorizing its repair.";
        public static string ReviewRepairInstruction(string scheduleName)
        {
            return "Review repair for \"" + (scheduleName ?? string.Empty) + "\".";
        }
        public static string ReviewRepairContent(string scheduleName, bool detached)
        {
            string content = "Reloading \"" + (scheduleName ?? string.Empty)
                + "\" from its workbook changes the Revit model. "
                + "The existing schedule, name, and sheet placements will be kept. "
                + "Any existing source, width, or migration confirmation will still be shown.";
            return detached
                ? content + "\n\n" + DetachedRepairSentence
                : content;
        }
        public const string ReviewRepairReload = "Reload from workbook";
        public const string ReviewRepairSkip = "Skip this schedule";

        public static string CheckAndRepairSummary(
            ExcelScheduleLink.Contracts.StyleIntegrityBulkSummary summary,
            System.Collections.Generic.IEnumerable<string> details)
        {
            string text = summary == null ? string.Empty : summary.Format();
            if (details != null)
            {
                foreach (string detail in details)
                {
                    text += "\n" + detail;
                }
            }
            return text;
        }

        public static string ReloadUnsafeStyleValidationFailed(
            string scheduleName, bool rollbackVerified)
        {
            return "Rendering " + (scheduleName ?? string.Empty)
                + " produced an unsafe or unverifiable cell style.\n\n"
                + (rollbackVerified
                    ? "The whole transaction was rolled back; the attempted change was not saved."
                    : RollbackCompletionNotVerified)
                + " This schedule may still be unsafe to open. Run Check and repair.";
        }

        public const string LegacyMigrationInstruction =
            "This link uses legacy ExcelScheduleLink storage.";
        public static string LegacyMigrationContent(string scheduleName)
        {
            return "The link on \"" + (scheduleName ?? string.Empty) + "\" was stored by an older "
                + "ExcelScheduleLink version. Continuing upgrades it to protected v0.9.1 storage. "
                + "Older add-in versions will no longer manage this link.";
        }
        public const string LegacyMigrationContinue = "Upgrade this link and continue";
        public const string LegacyMigrationContinueHint =
            "Replace the v1 entity with v2 in the same transaction as this action.";

        public const string LayoutSourceUnknownInstruction =
            "These Excel widths have no accepted workbook identity baseline.";
        public static string LayoutSourceUnknownContent(
            string resolvedPath, string creator, bool isLegacy, bool strong)
        {
            string text = "The widths were read from \"" + (resolvedPath ?? string.Empty)
                + "\". v0.9.1 cannot establish which workbook this existing link originally used. "
                + "Continuing applies these widths";
            if (isLegacy)
            {
                text += " and upgrades the link. Older add-in versions will no longer manage this link.";
            }
            else
            {
                text += ".";
            }
            if (strong)
            {
                text += " Because creator evidence is different or unavailable in this workshared "
                    + "model, cancel if this may be a machine-local collision (creator: \""
                    + DisplayCreator(creator) + "\").";
            }
            return text + " The first later reload will still require the identity confirmation.";
        }
        public const string LayoutSourceChangedInstruction =
            "The workbook bytes for these Excel widths changed.";
        public static string LayoutSourceChangedContent(
            string resolvedPath, string creator, bool crossUser)
        {
            string text = "Different workbook bytes were found at \"" + (resolvedPath ?? string.Empty)
                + "\". Applying these Excel widths uses that resolved source.";
            if (crossUser)
            {
                text += " The link creator is \"" + DisplayCreator(creator)
                    + "\" and this may be a machine-local path collision.";
            }
            return text;
        }
        public const string LayoutSourceContinue = "Use these Excel widths";
        public const string LayoutSourceContinueHint =
            "Apply the resolved workbook's widths to this schedule.";

        public static string DisplayCreator(string creator)
        {
            return string.IsNullOrWhiteSpace(creator) ? "unknown creator" : creator;
        }

        public const string WorksharingAdvisoryInstruction =
            "Changes to this Excel-linked schedule are shared with everyone.";
        public const string WorksharingAdvisoryContent =
            "Two-machine verification confirmed that the same source path can point to different workbook "
            + "bytes on different computers, and that reload can replace manual Revit column widths. "
            + "v0.9.1 checks workbook bytes before reload and may require confirmation. Use a genuinely "
            + "shared absolute workbook path. Reload all skips links that need an identity decision.";
        public const string WorksharingAdvisorySentence =
            "Worksharing note: the same source path may resolve to different bytes on another computer, "
            + "and Reload all skips links that need an identity decision.";

        public const string ReloadCancelled =
            "Reload was canceled. No reload transaction was started.";

        public static string ReloadSkippedConfirmationRequired(
            string scheduleName,
            ExcelScheduleLink.Contracts.SourceIdentityState identity,
            ExcelScheduleLink.Contracts.CreatorRelation creator)
        {
            string reason;
            if (identity == ExcelScheduleLink.Contracts.SourceIdentityState.Unknown)
            {
                reason = "identity baseline is unknown";
            }
            else if (identity == ExcelScheduleLink.Contracts.SourceIdentityState.Differs)
            {
                reason = creator == ExcelScheduleLink.Contracts.CreatorRelation.Different
                    || creator == ExcelScheduleLink.Contracts.CreatorRelation.Unavailable
                    ? "workbook bytes differ and creator confirmation is required"
                    : "workbook bytes differ and confirmation is required";
            }
            else
            {
                reason = "cross-user manual-width confirmation is required";
            }
            return "\"" + (scheduleName ?? string.Empty)
                + "\" — Confirmation required: " + reason + ".";
        }

        // ---- Link Manager ----
        public const string ManagerTitle = "Excel Schedule Link - Link Manager";
        public const string ManagerNoLinks =
            "This model has no Excel-linked schedules yet. Use \"Link Excel Worksheet...\" first.";
        public const string ManagerColSchedule = "Schedule";
        public const string ManagerColWorksheet = "Worksheet";
        public const string ManagerColRange = "Range";
        public const string ManagerColSource = "Source";
        public const string ManagerColStatus = "Status";
        public const string ManagerColScale = "Scale";
        public const string ManagerColColMode = "Col width";
        public const string ManagerColRowMode = "Row height";
        public const string ManagerStatusMissing = "Missing";
        public const string ManagerStatusNetworkNotApproved = "Network - not approved";
        public const string ManagerStatusInvalidPath = "Invalid workbook path";
        public const string ManagerStatusLegacy = "Legacy / identity unknown";
        public const string ManagerStatusProtected = "Protected";
        public const string ManagerStatusProtectedBaselineUnknown =
            "Protected / identity unknown";
        public const string ManagerSettingsGroup = "Settings for the selected link (applied on next reload)";
        public const string ManagerWidthScaleLabel = "Column width scale:";
        public const string ManagerWidthScaleHint = "Recommended: 1.5 - 2.0";
        public const string ManagerColModeLabel = "Column width mode:";
        public const string ManagerRowModeLabel = "Row height mode:";
        public const string ManagerApply = "Apply settings";
        public const string ManagerReloadSelected = "Reload selected";
        public const string ManagerReloadAll = "Reload all";
        public const string ManagerClose = "Close";
        public const string ManagerSelectFirst = "Select a link in the list first.";
        public const string ManagerSettingsSaved =
            "Settings saved. They will take effect the next time this link is reloaded.";
        public static string ManagerSettingsSaveFailed(
            bool rollbackVerified, bool transactionStarted)
        {
            return "The settings could not be saved. "
                + (!transactionStarted
                    ? "No settings transaction was started."
                    : rollbackVerified
                    ? "The settings transaction was rolled back; no settings change was saved."
                    : RollbackCompletionNotVerified);
        }
        public static string ManagerReloadAllSettingsSaveFailed(
            bool rollbackVerified, bool transactionStarted)
        {
            string transactionResult = !transactionStarted
                ? "No settings transaction was started."
                : rollbackVerified
                    ? "The settings transaction was rolled back; no settings change was saved."
                    : RollbackCompletionNotVerified;
            return "Reload all was canceled because the selected link's settings could not be saved.\n\n"
                + transactionResult + "\n"
                + "Zero links were reloaded. Your unsaved settings remain in the editor. To continue without them, "
                + "run Reload all again and choose Discard.";
        }
        public const string ManagerLinkNoLongerAvailable =
            "The selected linked schedule is no longer available. Refresh the manager and try again.";
        public const string ManagerSettingsChangedGroup =
            "Settings for the selected link (unsaved changes)";
        public const string ManagerSaveBeforeSelection =
            "Save changes to the selected link before selecting another link?\n\n"
            + "Yes = Save   No = Discard   Cancel = Keep editing";
        public const string ManagerSaveBeforeClose =
            "Save changes to the selected link before closing Link Manager?\n\n"
            + "Yes = Save   No = Discard   Cancel = Keep editing";
        public const string ManagerSaveBeforeReloadAll =
            "Save changes to the selected link before reloading all links?\n\n"
            + "Yes = Save   No = Discard   Cancel = Keep editing";
        public const string ManagerReloadSavesPending =
            "Unsaved settings are saved automatically before Reload selected.";

        public const string ColModeApplyExcel = "Apply Excel widths";
        public const string ColModeKeepManual = "Keep manual widths";
        public const string RowModeApplyExcel = "Apply Excel / auto heights";
        public const string RowModeKeepManual = "Keep manual heights";

        public static string ManagerReloadSummary(
            int success, int unchanged, int missing, int failed, int repaired,
            int confirmationRequired,
            System.Collections.Generic.IEnumerable<string> truncatedSchedules,
            System.Collections.Generic.IEnumerable<string> details)
        {
            string text = "Reload finished.\n\n"
                + "Succeeded: " + success + "\n"
                + "  (unchanged source: " + unchanged + ")\n"
                + "Repaired: " + repaired + "\n"
                + "Source missing: " + missing + "\n"
                + "Confirmation required: " + confirmationRequired + "\n"
                + "Failed: " + failed;
            if (truncatedSchedules != null)
            {
                System.Collections.Generic.List<string> names =
                    new System.Collections.Generic.List<string>();
                foreach (string name in truncatedSchedules)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
                if (names.Count > 0)
                {
                    text += "\nTruncated: " + string.Join(", ", names);
                }
            }
            if (details != null)
            {
                foreach (string detail in details)
                {
                    text += "\n  - " + detail;
                }
            }
            return text;
        }

        public static string ManagerReloadAllUnavailable(string scheduleName)
        {
            return (scheduleName ?? string.Empty) + ": linked schedule was unavailable.";
        }

        public static string ManagerReloadAllPostRepairNotHealthy(string scheduleName)
        {
            return (scheduleName ?? string.Empty)
                + ": post-repair integrity scan was not Healthy.";
        }

        public static string ManagerReloadAllFailure(string scheduleName, string detail)
        {
            return (scheduleName ?? string.Empty) + ": "
                + (string.IsNullOrEmpty(detail) ? "Reload failed." : detail);
        }

        // ---- Ribbon (provisional labels; finalized after on-device 32px review) ----
        public const string RibbonTab = "Excel Schedule Link";
        public const string RibbonPanel = "Excel Links";

        public const string RibbonLinkText = "Link\nExcel";
        public const string RibbonLinkTooltip = "Link an Excel worksheet into a new schedule.";
        public const string RibbonLinkLong =
            "Pick an .xlsx workbook, choose a worksheet and optional cell range, and create a "
            + "linked schedule that shows the worksheet content. The link is stored in the model "
            + "so it can be reloaded when the workbook changes.";

        public const string RibbonReloadText = "Reload\nLink";
        public const string RibbonReloadTooltip = "Reload the active linked schedule from Excel.";
        public const string RibbonReloadLong =
            "Rebuild the active (or selected) linked schedule from the current workbook, honoring "
            + "the stored width scale and column/row modes. Nothing changes if the workbook cannot "
            + "be read.";

        public const string RibbonManageText = "Link\nManager";
        public const string RibbonManageTooltip = "List and manage all Excel-linked schedules.";
        public const string RibbonManageLong =
            "Show every Excel-linked schedule with its source path and status, edit each link's "
            + "width scale and column/row modes, and reload the selected link or all links.";

        // ---- Layout ribbon panel ----
        public const string RibbonLayoutPanel = "Layout";

        public const string RibbonColumnWidthsText = "Column\nWidths...";
        public const string RibbonColumnWidthsTooltip = "Edit the column widths of the linked table.";
        public const string RibbonColumnWidthsLong =
            "Open the Column Widths dialog for the linked schedule: type each width in mm or "
            + "inches, or use Fit Content / Distribute Evenly / Restore Excel Widths / Fit to "
            + "Total, then apply. You can also choose whether the widths survive the next reload.";

        public const string RibbonQuickLayoutText = "Quick\nLayout";
        public const string RibbonQuickLayoutTooltip = "One-click column-width presets.";
        public const string RibbonQuickLayoutLong =
            "Apply a column-width preset to the linked schedule without opening a dialog.";

        public const string RibbonFitContentText = "Fit Content";
        public const string RibbonFitContentTooltip = "Resize each column to fit the text it shows.";
        public const string RibbonFitContentLong =
            "Measure the text currently shown in the linked table and size every column to fit it "
            + "(full-width characters counted correctly). The widths are then kept on reload.";

        public const string RibbonDistributeText = "Distribute Evenly";
        public const string RibbonDistributeTooltip =
            "Make all columns the same width (keeps the current total).";
        public const string RibbonDistributeLong =
            "Give every column the same width while keeping the table's overall width. The widths "
            + "are then kept on reload.";

        public const string RibbonRestoreWidthsText = "Restore Excel Widths";
        public const string RibbonRestoreWidthsTooltip =
            "Restore column widths from the current linked Excel worksheet.";
        public const string RibbonRestoreWidthsLong =
            "Re-read the linked worksheet and set the column widths it defines (scaled by this "
            + "link's width scale). The reload policy is left unchanged.";

        // ---- Layout (column widths) ----
        public const string LayoutNoTarget =
            "No Excel-linked schedule to lay out. Open a linked schedule, or select exactly one "
            + "(or its schedule placed on a sheet), and try again.";

        public static string LayoutAmbiguous(int count)
        {
            return count + " Excel-linked schedules are selected. Select exactly one and try again. "
                + "No layout transaction was started.";
        }

        public const string LayoutNotLinked = "The target schedule is not an Excel link.";
        public const string LayoutLegacyMigrationRequired =
            "This legacy link must be upgraded before its layout can be changed.";

        public static string LayoutLegacyMigrationFailed(bool rollbackVerified)
        {
            return LayoutLegacyMigrationRequired + " "
                + (rollbackVerified
                    ? "The whole change was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified);
        }

        public const string LayoutInvalidTarget =
            "The target schedule is not valid, or it does not belong to the active document. "
            + "No layout transaction was started.";

        public const string LayoutNoCandidates =
            "No column widths were supplied. No layout transaction was started.";

        public const string LayoutNoColumns =
            "The linked schedule has no header columns to resize. No layout transaction was started.";

        public const string LayoutUnsafeStyles =
            "The column widths were not changed because this schedule has unsafe or unverifiable cell styles.\n\n"
            + "No layout transaction was started. Run Check and repair first.";

        public static string LayoutStyleScanRollbackFailed(string detail)
        {
            return "The schedule-style safety check could not verify rollback of its read transaction.\n\n"
                + "No layout transaction was started.\n\n"
                + (string.IsNullOrEmpty(detail) ? RollbackCompletionNotVerified : detail);
        }

        public static string LayoutUnsafeStylesAfterRollback(bool rollbackVerified)
        {
            return "The column widths were not changed because this schedule has unsafe or unverifiable cell styles.\n\n"
                + (rollbackVerified
                    ? "The whole change was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + " Run Check and repair first.";
        }

        public static string LayoutBodyWidthSyncFailed(bool rollbackVerified)
        {
            return "The column widths could not be applied because Revit could not match the schedule's body column to the table width.\n\n"
                + (rollbackVerified
                    ? "The whole change was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified);
        }

        public static string LayoutColumnCountMismatch(int supplied, int actual)
        {
            return "The number of column widths (" + supplied + ") does not match the schedule's "
                + actual + " columns. The schedule may have been reloaded meanwhile. "
                + "No layout transaction was started.";
        }

        public static string LayoutTransactionNotStarted(string status)
        {
            return "Revit did not allow starting a transaction (status: " + status
                + "). No model changes were attempted.";
        }

        public static string LayoutCommitNotCompleted(string status, bool rollbackVerified)
        {
            return "The column widths could not be committed (transaction status: " + status
                + "). "
                + (rollbackVerified
                    ? "The change was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified);
        }

        // ---- Column Widths dialog ----
        public const string LayoutDialogTitle = "Excel Schedule Link - Column Widths";
        public const string LayoutDialogScheduleLabel = "Schedule:";
        public const string LayoutGridColumnHeader = "Column";
        public const string LayoutGridWidthHeader = "Width";
        public const string LayoutQuickGroup = "Quick layout";
        public const string LayoutQuickNote =
            "Updates the values above. Nothing is applied until you choose Apply & Close.";
        public const string LayoutFitContent = "Fit Content";
        public const string LayoutDistributeEvenly = "Distribute Evenly";
        public const string LayoutRestoreExcel = "Restore Excel Widths";
        public const string LayoutFitToTotal = "Fit to Total";
        public const string LayoutTargetTotalLabel = "Target total:";
        public const string LayoutFitContentTip = "Resize each column to fit the text it shows.";
        public const string LayoutDistributeTip = "Make all columns the same width (keeps the current total).";
        public const string LayoutRestoreTip = "Restore column widths from the current linked Excel worksheet.";
        public const string LayoutFitToTotalTip = "Scale the columns so their total matches the target.";
        public const string LayoutPolicyGroup = "Reload policy";
        public const string LayoutPolicyKeep = "Keep these widths on reload";
        public const string LayoutPolicyRecalculate = "Recalculate from Excel on reload";
        public const string LayoutApply = "Apply";
        public const string LayoutApplyTip =
            "Apply the widths now and keep this dialog open so you can keep adjusting.";
        public const string LayoutApplyAndClose = "Apply && Close";
        public const string LayoutOpenScheduleView = "Open Schedule View";
        public const string LayoutClose = "Close";
        public const string LayoutTotalPrefix = "Total: ";

        public static string LayoutInvalidCell(int oneBasedColumn, string unit)
        {
            return "Column " + oneBasedColumn + ": enter a positive number in " + unit + ".";
        }

        public static string LayoutInvalidCells(int count)
        {
            return count + " column width(s) are not valid numbers. Fix them before applying.";
        }

        public const string LayoutInvalidTarget2 = "Enter a positive number for the target total.";

        public static string LayoutFitContentPartial(double unresolvedInches)
        {
            return "Applied the best fit, but a merged cell still needs about "
                + unresolvedInches.ToString("F1")
                + " more inches than its columns can provide (Revit limits a column's width). "
                + "That merged text may still wrap.";
        }

        public static string LayoutFitToTotalNotReached(string reason, double reachableDisplay, string unit)
        {
            return reason + " The closest reachable total is about "
                + reachableDisplay.ToString("F1") + " " + unit + ", which was used instead.";
        }

        public const string LayoutUnsavedTitle = "Excel Schedule Link";
        public const string LayoutUnsavedMain = "Apply the column width changes?";

        // ---- Restore Excel widths (re-reads the current linked worksheet) ----
        public static string LayoutRestoreReadFailed(string detail)
        {
            return "The linked Excel worksheet could not be read, so the column widths were not "
                + "changed. No layout transaction was started.\n\n" + detail;
        }

        public static string LayoutRestoreColumnMismatch(int worksheetColumns, int scheduleColumns)
        {
            return "The linked worksheet now has " + worksheetColumns + " columns, but this schedule "
                + "has " + scheduleColumns + ". Run Reload Link first so the table matches the "
                + "worksheet, then restore the widths. No layout transaction was started.";
        }

        public static string LayoutInvalidInput(string reason)
        {
            return "The column widths could not be applied.\n\n" + reason
                + "\n\nNo layout transaction was started.";
        }

        public static string LayoutApplyFailed(
            string where, string detail, bool rollbackVerified)
        {
            return "Setting column widths failed at " + where + ". "
                + (rollbackVerified
                    ? "The whole change was rolled back; nothing was changed in the Revit model."
                    : RollbackCompletionNotVerified)
                + "\n\n" + detail;
        }

        public static string LayoutApplied(string scheduleName, int columns)
        {
            return "Updated the column widths of \"" + scheduleName + "\" (" + columns + " columns).";
        }

        public static string LayoutColumnLabel(int oneBased) { return "column " + oneBased; }
        public const string LayoutSettingsLabel = "the settings save";
    }
}
