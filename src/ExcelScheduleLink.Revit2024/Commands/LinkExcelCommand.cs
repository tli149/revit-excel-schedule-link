using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;
using ExcelScheduleLink.Excel.OpenXml;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.Probing;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Step 2 production command: links an Excel worksheet into a new
    /// ViewSchedule.
    ///
    /// Flow (the reading phase is strictly outside the Transaction):
    ///   1. Pick the workbook (.xlsx) and the worksheet / optional range.
    ///   2. Read the worksheet into a SheetModel. Any failure here aborts the
    ///      command before a Transaction is even started.
    ///   3. Inside one Transaction: create the method-B schedule, render the
    ///      header table, store the link info (extensible storage). Any
    ///      failure rolls back the WHOLE transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LinkExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;
            UiSettings uiSettings = UiSettings.Load();

            // ---- 1. Workbook selection ----
            string xlsxPath;
            using (System.Windows.Forms.OpenFileDialog fileDialog = new System.Windows.Forms.OpenFileDialog())
            {
                fileDialog.Title = UserMessages.FileDialogTitle;
                fileDialog.Filter = UserMessages.FileDialogFilter;
                // The selected file is an explicit user choice. The centralized
                // policy still validates the path before the reader uses it.
                fileDialog.CheckFileExists = true;
                string lastDirectory = uiSettings.GetString("LinkLastDirectory", string.Empty);
                if (!string.IsNullOrEmpty(lastDirectory) && Directory.Exists(lastDirectory))
                {
                    fileDialog.InitialDirectory = lastDirectory;
                }
                if (fileDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                xlsxPath = fileDialog.FileName;
            }

            PathResolutionResult selectedPath =
                PathResolver.AuthorizeUserSelectedPath(xlsxPath);
            if (selectedPath == null || !selectedPath.IsResolved)
            {
                TaskDialog.Show(
                    UserMessages.DialogTitle,
                    PathResolver.DescribeFailure(null, selectedPath));
                return Result.Cancelled;
            }

            IExcelReadService excelService = new OpenXmlExcelReadService();

            // ---- 2. Worksheet / range selection, then read (no Transaction yet) ----
            string sheetName;
            string rangeAddress;
            double widthScale;
            SheetModel sheet;
            SourceFingerprint sourceFingerprint;
            try
            {
                System.Collections.Generic.IReadOnlyList<string> sheetNames =
                    excelService.GetSheetNames(xlsxPath);
                using (LinkExcelDialog dialog =
                    new LinkExcelDialog(
                        xlsxPath,
                        sheetNames,
                        uiSettings.GetDouble("LinkWidthScale", ExcelLinkInfo.DefaultWidthScale),
                        uiSettings.GetString("LinkWorksheet", string.Empty),
                        uiSettings.GetString("LinkRange", string.Empty)))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK
                        || string.IsNullOrEmpty(dialog.SelectedSheetName))
                    {
                        return Result.Cancelled;
                    }
                    sheetName = dialog.SelectedSheetName;
                    rangeAddress = dialog.RangeAddressOrNull;
                    widthScale = dialog.WidthScale;
                }

                // Re-linking an identical workbook/worksheet/range is nearly
                // always a mis-click, so ask before creating a second schedule.
                // Checked before the read so a cancelled prompt costs nothing.
                ViewSchedule existing =
                    LinkedScheduleFinder.FindDuplicate(doc, xlsxPath, sheetName, rangeAddress);
                if (existing != null)
                {
                    DuplicateChoice choice = AskAboutDuplicate(existing.Name);
                    if (choice == DuplicateChoice.Cancel)
                    {
                        return Result.Cancelled;
                    }
                    if (choice == DuplicateChoice.OpenExisting)
                    {
                        if (ScheduleActivationGuard.CheckAndOfferRepair(uidoc, existing))
                        {
                            try
                            {
                                uidoc.ActiveView = existing;
                            }
                            catch (Exception)
                            {
                            }
                        }
                        return Result.Cancelled; // nothing was changed in the model
                    }
                }

                SheetReadResult snapshot = excelService.ReadSheetSnapshot(
                    xlsxPath, sheetName, rangeAddress);
                sheet = snapshot.Sheet;
                sourceFingerprint = snapshot.Fingerprint;

                // Remember only inputs that successfully read. An invalid range
                // should not become the next run's default.
                uiSettings.SetString("LinkLastDirectory", Path.GetDirectoryName(xlsxPath) ?? string.Empty);
                uiSettings.SetString("LinkWorksheet", sheetName);
                uiSettings.SetString("LinkRange", rangeAddress ?? string.Empty);
                uiSettings.SetDouble("LinkWidthScale", widthScale);
                uiSettings.Save();
            }
            catch (ExcelReadException ex)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.ReadFailed(ex.Message));
                return Result.Cancelled; // nothing was changed in the model
            }

            ExcelLinkInfo info = BuildLinkInfo(
                doc, xlsxPath, sheetName, rangeAddress, widthScale, sourceFingerprint);

            // Linking is the first model mutation in this command. The notice is
            // session-local and is intentionally shown after the read/duplicate
            // checks, immediately before the one creation transaction.
            WorksharingAdvisory.ShowIfNeeded(doc);

            // ---- 3. One Transaction: create + render + store; rollback on any failure ----
            ViewSchedule schedule = null;
            HeaderTableRenderer.RenderResult renderResult;
            string baseName = Path.GetFileNameWithoutExtension(xlsxPath) + " - " + sheetName;
            using (Transaction transaction = new Transaction(doc, "Link Excel Worksheet"))
            {
                TransactionStatus started;
                try
                {
                    started = transaction.Start();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.LinkTransactionNotStarted(ex.Message));
                    message = ex.Message;
                    return Result.Failed;
                }
                if (started != TransactionStatus.Started)
                {
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.LinkTransactionNotStarted(started.ToString()));
                    message = started.ToString();
                    return Result.Failed;
                }
                try
                {
                    schedule = ScheduleFactory.Create(doc, baseName);
                    renderResult = HeaderTableRenderer.Render(schedule, sheet, info.WidthScale);
                    info.RendererVersion = ExcelLinkInfo.CurrentRendererVersion;
                    ExcelLinkSchema.SaveV2(schedule, info);
                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException(
                            "The link transaction did not commit.");
                    }
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
                {
                    TransactionRollback.RollBackIfStarted(transaction);
                    message = UserMessages.RegenerationFailure;
                    TaskDialog.Show(UserMessages.DialogTitle, message);
                    return Result.Failed;
                }
                catch (RegenerationFailureProbeException)
                {
                    TransactionRollback.RollBackIfStarted(transaction);
                    message = UserMessages.RegenerationFailure;
                    TaskDialog.Show(UserMessages.DialogTitle, message);
                    return Result.Failed;
                }
                catch (UnsafeScheduleStyleException)
                {
                    bool rollbackVerified = TransactionRollback.RollBackIfStarted(transaction)
                        == TransactionStatus.RolledBack;
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.LinkUnsafeStyleValidationFailed(rollbackVerified));
                    message = UserMessages.LinkUnsafeStyleValidationFailed(rollbackVerified);
                    return Result.Failed;
                }
                catch (Exception ex)
                {
                    bool rollbackVerified = TransactionRollback.RollBackIfStarted(transaction)
                        == TransactionStatus.RolledBack;
                    TaskDialog.Show(UserMessages.DialogTitle,
                        UserMessages.LinkFailedRolledBack(ex.ToString(), rollbackVerified));
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            // Open the new schedule view (best effort)
            try
            {
                uidoc.ActiveView = schedule;
            }
            catch (Exception)
            {
            }

            string summary = UserMessages.LinkSucceeded(
                schedule.Name, renderResult.RowsRendered, renderResult.ColumnsRendered);
            summary = UserMessages.AppendRenderNotes(
                summary, sheet.SourceRowCount, sheet.SourceColumnCount,
                renderResult.Truncated, renderResult.HasViewWidthProblem,
                renderResult.ColumnWidthsRejected);
            TaskDialog.Show(UserMessages.DialogTitle, summary);
            return Result.Succeeded;
        }

        private enum DuplicateChoice { OpenExisting, CreateAnother, Cancel }

        /// <summary>
        /// Offers the three sensible answers to "this is already linked": open
        /// what exists, link it a second time anyway, or back out. Creating a
        /// duplicate stays available because the same table on two schedules is
        /// occasionally wanted; it is just never the silent default. Closing the
        /// dialog counts as Cancel.
        /// </summary>
        private static DuplicateChoice AskAboutDuplicate(string existingScheduleName)
        {
            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = UserMessages.DuplicateLinkInstruction(existingScheduleName);
            dialog.MainContent = UserMessages.DuplicateLinkContent;
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UserMessages.DuplicateLinkOpenExisting,
                UserMessages.DuplicateLinkOpenExistingHint);
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                UserMessages.DuplicateLinkCreateAnother,
                UserMessages.DuplicateLinkCreateAnotherHint);
            // Must come after the links exist: the setter validates immediately
            // and rejects a default button that has not been added yet.
            dialog.DefaultButton = TaskDialogResult.CommandLink1;

            switch (dialog.Show())
            {
                case TaskDialogResult.CommandLink1:
                    return DuplicateChoice.OpenExisting;
                case TaskDialogResult.CommandLink2:
                    return DuplicateChoice.CreateAnother;
                default:
                    return DuplicateChoice.Cancel;
            }
        }

        private static ExcelLinkInfo BuildLinkInfo(
            Document doc, string xlsxPath, string sheetName, string rangeAddress,
            double widthScale, SourceFingerprint sourceFingerprint)
        {
            ExcelLinkInfo info = new ExcelLinkInfo();
            info.SchemaVersion = ExcelLinkInfo.CurrentSchemaVersion;
            info.AbsolutePath = Path.GetFullPath(xlsxPath);
            info.RelativePath = PathResolver.GetModelRelativePath(doc, xlsxPath);
            info.PathMode = PathMode.AbsolutePreferred;
            info.SheetName = sheetName;
            info.RangeMode = string.IsNullOrEmpty(rangeAddress) ? RangeMode.UsedRange : RangeMode.Explicit;
            info.RangeAddress = rangeAddress ?? string.Empty;
            info.SourceLastWriteUtcTicks = sourceFingerprint.LastWriteUtcTicks;
            info.LastLoadedUtcTicks = DateTime.UtcNow.Ticks;
            info.CreatedSourceSha256 = sourceFingerprint.Sha256;
            info.LastAcceptedSourceSha256 = sourceFingerprint.Sha256;
            info.LastAcceptedSourceSizeBytes = sourceFingerprint.SizeBytes;
            info.WidthScale = widthScale;
            return info;
        }
    }
}
