using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Contracts;

namespace ExcelScheduleLink.Revit2024.Link
{
    /// <summary>
    /// All data needed to choose and present a single-link decision. It contains
    /// no model mutation; the reload service creates it after the source snapshot
    /// has been read and before opening its transaction.
    /// </summary>
    internal sealed class ReloadPromptContext
    {
        public SourceIdentityState Identity;
        public CreatorRelation CreatorRelation;
        public ReloadDecisionKind DecisionKind;
        public string ScheduleName = string.Empty;
        public string Creator = string.Empty;
        public string ResolvedPath = string.Empty;
        public bool IsLegacy;
        public bool WidthWarning;
        public bool IsLayoutSource;
    }

    /// <summary>
    /// Revit TaskDialog presentation for the pure §7.1 decision. Command links
    /// retain the established Revit DPI behavior and keep Cancel as the default
    /// whenever the source identity or shared layout is uncertain.
    /// </summary>
    internal static class ReloadDialogs
    {
        public static bool Authorize(Document doc, ReloadPromptContext context)
        {
            if (context == null)
            {
                return false;
            }

            if (context.DecisionKind == ReloadDecisionKind.Proceed)
            {
                WorksharingAdvisory.ShowIfNeeded(doc);
                return true;
            }

            if (context.IsLayoutSource)
            {
                return AuthorizeLayoutSource(doc, context);
            }

            switch (context.DecisionKind)
            {
                case ReloadDecisionKind.WarnContentChanged:
                    // The ordinary same-creator warning is separate from the
                    // stronger identity dialog; show the session advisory first.
                    WorksharingAdvisory.ShowIfNeeded(doc);
                    return ShowCommandDialog(
                        UserMessages.ReloadChangedInstruction,
                        UserMessages.ReloadChangedContent(context.ResolvedPath),
                        UserMessages.ReloadContinue,
                        UserMessages.ReloadContinueHint,
                        false);

                case ReloadDecisionKind.RequireConfirmation:
                    if (context.Identity == SourceIdentityState.Match && context.WidthWarning)
                    {
                        return ShowWidthDialog(doc, context);
                    }
                    return ShowUnknownDialog(doc, context, false);

                case ReloadDecisionKind.RequireStrongConfirmation:
                    if (context.Identity == SourceIdentityState.Differs)
                    {
                        return ShowStrongDialog(doc, context);
                    }
                    return ShowUnknownDialog(doc, context, true);

                default:
                    return false;
            }
        }

        /// <summary>Consent used by settings-only and non-source-reading layout actions.</summary>
        public static bool ConfirmLegacyMigration(Document doc, string scheduleName)
        {
            bool mergeAdvisory = WorksharingAdvisory.ShouldMerge(doc);
            string content = UserMessages.LegacyMigrationContent(scheduleName);
            if (mergeAdvisory)
            {
                content += "\n\n" + UserMessages.WorksharingAdvisorySentence;
                WorksharingAdvisory.MarkShown(doc);
            }
            return ShowCommandDialog(
                UserMessages.LegacyMigrationInstruction,
                content,
                UserMessages.LegacyMigrationContinue,
                UserMessages.LegacyMigrationContinueHint,
                true);
        }

        private static bool AuthorizeLayoutSource(Document doc, ReloadPromptContext context)
        {
            if (context.Identity == SourceIdentityState.Match)
            {
                WorksharingAdvisory.ShowIfNeeded(doc);
                return true;
            }

            bool mergeAdvisory = WorksharingAdvisory.ShouldMerge(doc);
            string content;
            string instruction;
            if (context.Identity == SourceIdentityState.Unknown)
            {
                instruction = UserMessages.LayoutSourceUnknownInstruction;
                content = UserMessages.LayoutSourceUnknownContent(
                    context.ResolvedPath, context.Creator, context.IsLegacy,
                    context.CreatorRelation == CreatorRelation.Different
                        || context.CreatorRelation == CreatorRelation.Unavailable);
            }
            else
            {
                instruction = UserMessages.LayoutSourceChangedInstruction;
                content = UserMessages.LayoutSourceChangedContent(
                    context.ResolvedPath, context.Creator,
                    context.CreatorRelation == CreatorRelation.Different
                        || context.CreatorRelation == CreatorRelation.Unavailable);
            }

            if (mergeAdvisory)
            {
                content += "\n\n" + UserMessages.WorksharingAdvisorySentence;
                WorksharingAdvisory.MarkShown(doc);
            }
            return ShowCommandDialog(
                instruction,
                content,
                UserMessages.LayoutSourceContinue,
                UserMessages.LayoutSourceContinueHint,
                true);
        }

        private static bool ShowUnknownDialog(
            Document doc, ReloadPromptContext context, bool strong)
        {
            bool mergeAdvisory = WorksharingAdvisory.ShouldMerge(doc);
            string content = UserMessages.ReloadUnknownContent(
                context.ResolvedPath, context.Creator, context.IsLegacy, strong);
            if (mergeAdvisory)
            {
                content += "\n\n" + UserMessages.WorksharingAdvisorySentence;
                WorksharingAdvisory.MarkShown(doc);
            }
            return ShowCommandDialog(
                UserMessages.ReloadUnknownInstruction,
                content,
                UserMessages.ReloadUnknownContinue,
                UserMessages.ReloadUnknownContinueHint,
                true);
        }

        private static bool ShowStrongDialog(Document doc, ReloadPromptContext context)
        {
            bool mergeAdvisory = WorksharingAdvisory.ShouldMerge(doc);
            string content = UserMessages.ReloadStrongContent(
                context.ResolvedPath, context.Creator);
            if (context.WidthWarning)
            {
                content += "\n\n" + UserMessages.ReloadWidthWarningContent(
                    context.Creator);
            }
            if (mergeAdvisory)
            {
                content += "\n\n" + UserMessages.WorksharingAdvisorySentence;
                WorksharingAdvisory.MarkShown(doc);
            }
            return ShowCommandDialog(
                UserMessages.ReloadStrongInstruction,
                content,
                UserMessages.ReloadStrongContinue,
                UserMessages.ReloadStrongContinueHint,
                true);
        }

        private static bool ShowWidthDialog(Document doc, ReloadPromptContext context)
        {
            bool mergeAdvisory = WorksharingAdvisory.ShouldMerge(doc);
            string content = UserMessages.ReloadWidthWarningContent(context.Creator);
            if (mergeAdvisory)
            {
                content += "\n\n" + UserMessages.WorksharingAdvisorySentence;
                WorksharingAdvisory.MarkShown(doc);
            }
            return ShowCommandDialog(
                UserMessages.ReloadWidthWarningInstruction,
                content,
                UserMessages.ReloadWidthWarningContinue,
                UserMessages.ReloadWidthWarningContinueHint,
                true);
        }

        private static bool ShowCommandDialog(
            string instruction, string content, string command, string hint,
            bool cancelDefault)
        {
            TaskDialog dialog = new TaskDialog(UserMessages.DialogTitle);
            dialog.MainInstruction = instruction;
            dialog.MainContent = content;
            dialog.AllowCancellation = true;
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, command, hint);
            // The link must be added before setting DefaultButton; Revit validates
            // the result immediately, as in LinkExcelCommand.AskAboutDuplicate.
            dialog.DefaultButton = cancelDefault
                ? TaskDialogResult.Cancel
                : TaskDialogResult.CommandLink1;
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }
    }
}
