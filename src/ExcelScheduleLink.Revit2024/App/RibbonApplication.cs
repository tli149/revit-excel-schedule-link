using System;
using System.Reflection;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.App
{
    /// <summary>
    /// Builds the "Excel Schedule Link" ribbon tab on startup. This is the single
    /// production manifest entry (Type="Application"); the three commands are
    /// launched by these ribbon buttons (PushButtonData references them by
    /// assembly + class name), so they are no longer registered as External
    /// Tools commands (no duplicate menu entries).
    ///
    /// OnStartup is fully guarded: a ribbon build failure must never stop Revit
    /// from loading.
    /// </summary>
    public class RibbonApplication : IExternalApplication
    {
        private const string CommandsNamespace = "ExcelScheduleLink.Revit2024.Commands.";
        private const string AvailabilityNamespace = "ExcelScheduleLink.Revit2024.App.";
        private ScheduleIntegrityLifecycle _scheduleIntegrityLifecycle;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                _scheduleIntegrityLifecycle = new ScheduleIntegrityLifecycle(app);
                _scheduleIntegrityLifecycle.Subscribe();
                string tab = UserMessages.RibbonTab;
                try { app.CreateRibbonTab(tab); }
                catch (Exception) { /* tab already exists - reuse it */ }

                RibbonPanel panel = app.CreateRibbonPanel(tab, UserMessages.RibbonPanel);
                string asm = Assembly.GetExecutingAssembly().Location;

                AddButton(panel, asm, "ESL_Link", UserMessages.RibbonLinkText,
                    CommandsNamespace + "LinkExcelCommand",
                    UserMessages.RibbonLinkTooltip, UserMessages.RibbonLinkLong,
                    "link_32.png", "link_16.png",
                    AvailabilityNamespace + "DocumentOpenAvailability");

                AddButton(panel, asm, "ESL_Reload", UserMessages.RibbonReloadText,
                    CommandsNamespace + "ReloadExcelCommand",
                    UserMessages.RibbonReloadTooltip, UserMessages.RibbonReloadLong,
                    "reload_32.png", "reload_16.png",
                    AvailabilityNamespace + "LinkedScheduleAvailability");

                AddButton(panel, asm, "ESL_Manage", UserMessages.RibbonManageText,
                    CommandsNamespace + "LinkManagerCommand",
                    UserMessages.RibbonManageTooltip, UserMessages.RibbonManageLong,
                    "manage_32.png", "manage_16.png",
                    AvailabilityNamespace + "DocumentOpenAvailability");

                BuildLayoutPanel(app, tab, asm);
            }
            catch (Exception)
            {
                // Never block Revit startup because of a ribbon build issue.
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            if (_scheduleIntegrityLifecycle != null)
            {
                _scheduleIntegrityLifecycle.Unsubscribe();
                _scheduleIntegrityLifecycle = null;
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Layout panel: the Column Widths dialog (the main entry) plus a pulldown
        /// with the one-click presets, so four commands do not crowd the tab.
        /// </summary>
        private static void BuildLayoutPanel(UIControlledApplication app, string tab, string asm)
        {
            const string availability = AvailabilityNamespace + "ScheduleOrInstanceAvailability";
            RibbonPanel panel = app.CreateRibbonPanel(tab, UserMessages.RibbonLayoutPanel);

            AddButton(panel, asm, "ESL_ColumnWidths", UserMessages.RibbonColumnWidthsText,
                CommandsNamespace + "LayoutDialogCommand",
                UserMessages.RibbonColumnWidthsTooltip, UserMessages.RibbonColumnWidthsLong,
                "columnwidths_32.png", "columnwidths_16.png", availability);

            PulldownButtonData quickData = new PulldownButtonData(
                "ESL_QuickLayout", UserMessages.RibbonQuickLayoutText);
            quickData.ToolTip = UserMessages.RibbonQuickLayoutTooltip;
            quickData.LongDescription = UserMessages.RibbonQuickLayoutLong;
            PulldownButton quick = panel.AddItem(quickData) as PulldownButton;
            if (quick == null)
            {
                return;
            }
            quick.LargeImage = RibbonImageLoader.Load("quicklayout_32.png");
            quick.Image = RibbonImageLoader.Load("quicklayout_16.png");

            AddPulldownItem(quick, asm, "ESL_FitContent", UserMessages.RibbonFitContentText,
                CommandsNamespace + "FitContentCommand",
                UserMessages.RibbonFitContentTooltip, UserMessages.RibbonFitContentLong,
                "fitcontent_32.png", "fitcontent_16.png", availability);

            AddPulldownItem(quick, asm, "ESL_Distribute", UserMessages.RibbonDistributeText,
                CommandsNamespace + "DistributeEvenlyCommand",
                UserMessages.RibbonDistributeTooltip, UserMessages.RibbonDistributeLong,
                "distribute_32.png", "distribute_16.png", availability);

            AddPulldownItem(quick, asm, "ESL_RestoreWidths", UserMessages.RibbonRestoreWidthsText,
                CommandsNamespace + "RestoreExcelWidthsCommand",
                UserMessages.RibbonRestoreWidthsTooltip, UserMessages.RibbonRestoreWidthsLong,
                "restorewidths_32.png", "restorewidths_16.png", availability);
        }

        private static void AddPulldownItem(
            PulldownButton parent, string assemblyPath, string name, string text,
            string className, string tooltip, string longDescription,
            string largeIcon, string smallIcon, string availabilityClassName)
        {
            PushButtonData data = new PushButtonData(name, text, assemblyPath, className);
            data.ToolTip = tooltip;
            data.LongDescription = longDescription;
            if (!string.IsNullOrEmpty(availabilityClassName))
            {
                data.AvailabilityClassName = availabilityClassName;
            }
            PushButton button = parent.AddPushButton(data) as PushButton;
            if (button != null)
            {
                button.LargeImage = RibbonImageLoader.Load(largeIcon);
                button.Image = RibbonImageLoader.Load(smallIcon);
            }
        }

        private static void AddButton(
            RibbonPanel panel, string assemblyPath, string name, string text,
            string className, string tooltip, string longDescription,
            string largeIcon, string smallIcon, string availabilityClassName)
        {
            PushButtonData data = new PushButtonData(name, text, assemblyPath, className);
            data.ToolTip = tooltip;
            data.LongDescription = longDescription;
            if (!string.IsNullOrEmpty(availabilityClassName))
            {
                data.AvailabilityClassName = availabilityClassName;
            }
            PushButton button = panel.AddItem(data) as PushButton;
            if (button != null)
            {
                button.LargeImage = RibbonImageLoader.Load(largeIcon);
                button.Image = RibbonImageLoader.Load(smallIcon);
            }
        }
    }
}
