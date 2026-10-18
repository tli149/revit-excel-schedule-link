using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Layout;
using ExcelScheduleLink.Revit2024.Link;
using ExcelScheduleLink.Revit2024.UI;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>Candidate widths produced by a quick-layout preset.</summary>
    internal sealed class QuickLayoutCandidates
    {
        public double[] Feet;      // null when the preset could not produce widths
        public string Error;       // shown instead of applying
        public string Note;        // shown after a successful apply (e.g. merge deficit)
        public IReadOnlyList<string> ColumnHeadings;
        public LayoutSourceSnapshot SourceSnapshot; // set when Restore Excel read a source
    }

    /// <summary>
    /// Shared flow for the one-click Layout presets: resolve the linked schedule,
    /// compute candidate widths, then hand them to LayoutService, which validates
    /// and applies them as a single all-or-nothing transaction. No dialog.
    /// </summary>
    internal static class QuickLayoutRunner
    {
        public static Result Run(
            ExternalCommandData commandData, ref string message,
            Func<Document, ViewSchedule, double[], QuickLayoutCandidates> compute,
            ColumnWidthMode? modeToSave)
        {
            return FatalRegenerationBoundary.RunCommand(ref message,
                delegate
                {
                    return RunCore(commandData, compute, modeToSave);
                });
        }

        private static Result RunCore(
            ExternalCommandData commandData,
            Func<Document, ViewSchedule, double[], QuickLayoutCandidates> compute,
            ColumnWidthMode? modeToSave)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.NoActiveDocument);
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            LayoutTarget target = LayoutService.ResolveTarget(uidoc);
            if (target.Kind != LayoutTargetKind.Resolved)
            {
                TaskDialog.Show(UserMessages.DialogTitle, target.Message);
                return Result.Cancelled;
            }
            ViewSchedule schedule = target.Schedule;

            double[] currentFeet;
            if (!LayoutService.TryGetCurrentWidths(schedule, out currentFeet))
            {
                TaskDialog.Show(
                    UserMessages.DialogTitle, UserMessages.LayoutDimensionsReadFailed);
                return Result.Cancelled;
            }
            if (currentFeet.Length == 0)
            {
                TaskDialog.Show(UserMessages.DialogTitle, UserMessages.LayoutNoColumns);
                return Result.Cancelled;
            }

            QuickLayoutCandidates candidates = compute(doc, schedule, currentFeet);
            if (candidates == null || candidates.Feet == null)
            {
                string detail = candidates != null && !string.IsNullOrEmpty(candidates.Error)
                    ? candidates.Error
                    : UserMessages.LayoutNoCandidates;
                TaskDialog.Show(UserMessages.DialogTitle, detail);
                return Result.Cancelled; // nothing changed
            }

            if (!LayoutMutationAuthorization.Authorize(
                doc, schedule, candidates.SourceSnapshot))
            {
                return Result.Cancelled;
            }

            LayoutApplyResult applied = LayoutService.Apply(
                doc, schedule, candidates.Feet, modeToSave, null, null, true,
                columnHeadings: candidates.ColumnHeadings);

            string summary = applied.UserMessage;
            if (applied.Success && !string.IsNullOrEmpty(candidates.Note))
            {
                summary += "\n\n" + candidates.Note;
            }
            TaskDialog.Show(UserMessages.DialogTitle, summary);

            if (!applied.Success)
            {
                return Result.Failed;
            }
            return Result.Succeeded;
        }
    }

    /// <summary>Sizes every column to the text currently shown (merged spans honored).</summary>
    [Transaction(TransactionMode.Manual)]
    public class FitContentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return QuickLayoutRunner.Run(commandData, ref message,
                delegate (Document doc, ViewSchedule schedule, double[] currentFeet)
                {
                    FitContentResult fit = LayoutService.ComputeFitContent(
                        doc, schedule, currentFeet);
                    if (fit == null || !fit.Success || fit.Feet == null)
                    {
                        return new QuickLayoutCandidates
                        {
                            Error = fit != null && !string.IsNullOrEmpty(fit.UserMessage)
                                ? fit.UserMessage
                                : UserMessages.LayoutFitContentReadFailed
                        };
                    }
                    LengthUnitChoice unit = LayoutService.ResolveLengthUnit(doc);
                    return new QuickLayoutCandidates
                    {
                        Feet = fit.Feet,
                        ColumnHeadings = fit.ColumnHeadings,
                        Note = LengthUnitHelper.FitContentNotes(
                            fit.FixedTotal, unit, fit.ColumnHeadings)
                    };
                },
                ColumnWidthMode.KeepManualWidths); // an intentional layout: keep it on reload
        }
    }

    /// <summary>Gives every column the same width, keeping the current total.</summary>
    [Transaction(TransactionMode.Manual)]
    public class DistributeEvenlyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return QuickLayoutRunner.Run(commandData, ref message,
                delegate (Document doc, ViewSchedule schedule, double[] currentFeet)
                {
                    double total = 0.0;
                    foreach (double f in currentFeet) total += f;
                    return new QuickLayoutCandidates
                    {
                        Feet = LayoutOps.DistributeEvenlyFeet(currentFeet.Length, total)
                    };
                },
                ColumnWidthMode.KeepManualWidths);
        }
    }

    /// <summary>
    /// Re-reads the linked worksheet and applies the widths it defines. The reload
    /// policies are deliberately left untouched (design v0.8.0 6).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RestoreExcelWidthsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return QuickLayoutRunner.Run(commandData, ref message,
                delegate (Document doc, ViewSchedule schedule, double[] currentFeet)
                {
                    ExcelWidthResult read = ExcelWidthReader.ReadColumnWidths(
                        doc, schedule, currentFeet.Length);
                    if (read == null || !read.Success)
                    {
                        return new QuickLayoutCandidates
                        {
                            Error = read != null ? read.UserMessage : UserMessages.LayoutNotLinked
                        };
                    }
                    return new QuickLayoutCandidates
                    {
                        Feet = read.Feet,
                        SourceSnapshot = read.SourceSnapshot
                    };
                },
                null); // reload policy unchanged
        }
    }
}
