using Autodesk.Revit.UI;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>Cancel-default TaskDialog for per-server UNC approval.</summary>
    internal static class StoredWorkbookPathDialogs
    {
        public static bool Authorize(string server, string fullPath)
        {
            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = UserMessages.NetworkPathApprovalInstruction(server);
            dialog.MainContent = UserMessages.NetworkPathApprovalContent(fullPath);
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UserMessages.NetworkPathApprovalAllow,
                UserMessages.NetworkPathApprovalAllowHint);
            dialog.DefaultButton = TaskDialogResult.Cancel;
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }
    }
}
