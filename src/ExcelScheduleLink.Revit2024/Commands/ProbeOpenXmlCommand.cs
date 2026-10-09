using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ExcelScheduleLink.Revit2024.Probing;

namespace ExcelScheduleLink.Revit2024.Commands
{
    /// <summary>
    /// Diagnostic probe for Open XML assembly loading.
    ///
    /// This command and the add-in do not statically reference
    /// DocumentFormat.OpenXml. It records the loaded assemblies, copies a test
    /// workbook with retries, loads the payload with Assembly.LoadFrom, invokes
    /// its string-only API, records the final state, and removes the temporary
    /// copy. It never changes the Revit model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ProbeOpenXmlCommand : IExternalCommand
    {
        private const string PayloadFileName = "ExcelScheduleLink.Probe.OpenXmlPayload.dll";
        private const string TestBookRelativePath = "TestData\\Step0_TestBook.xlsx";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Step0Reporter r = new Step0Reporter();
            string reportPath = null;
            string tempCopy = null;

            // Record the condition supplied by the user. Automatic detection is
            // intentionally avoided because Revit may load Dynamo Player
            // assemblies even when Dynamo has not been started.
            r.SetUserCondition(AskUserCondition());

            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                r.Add("Add-in directory: " + asmDir);

                // 1. State before loading the payload.
                r.AddLoadedOpenXmlAssemblies("Before payload load");

                // 2. Locate and copy the test workbook.
                string xlsxPath = Path.Combine(asmDir, TestBookRelativePath);
                if (!File.Exists(xlsxPath))
                {
                    r.AddFail("Test workbook was not found: " + xlsxPath
                        + "\n       Place the TestData folder beside the diagnostic files.", null);
                    return Result.Succeeded; // The report records the failure.
                }
                r.Add("Test workbook: " + xlsxPath);
                tempCopy = LockedFileCopier.CopyToTemp(xlsxPath, 3, 500, r.Add);

                // 3. Explicitly load the payload.
                string payloadPath = Path.Combine(asmDir, "Payload", PayloadFileName);
                if (!File.Exists(payloadPath))
                {
                    r.AddFail("Payload DLL was not found: " + payloadPath, null);
                    return Result.Succeeded;
                }
                Assembly payloadAsm = Assembly.LoadFrom(payloadPath);
                r.AddOk("Assembly.LoadFrom(Payload): " + payloadAsm.FullName);
                r.Add("  Location: " + payloadAsm.Location);

                // 4. Invoke Run(string) through reflection without shared types.
                Type payloadType = payloadAsm.GetType(
                    "ExcelScheduleLink.Probe.OpenXmlPayload.ProbePayload", true);
                MethodInfo runMethod = payloadType.GetMethod(
                    "Run", BindingFlags.Public | BindingFlags.Static);
                if (runMethod == null)
                {
                    r.AddFail("ProbePayload.Run method was not found", null);
                    return Result.Succeeded;
                }

                string payloadReport;
                try
                {
                    payloadReport = (string)runMethod.Invoke(null, new object[] { tempCopy });
                }
                catch (TargetInvocationException tie)
                {
                    // Payload exceptions, including load failures and version
                    // conflicts, appear here.
                    r.AddFail("Exception while running the payload (possible Open XML conflict)",
                        tie.InnerException ?? tie);
                    return Result.Succeeded;
                }

                foreach (string line in payloadReport.Split(
                    new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    r.Add("  " + line);
                }
                if (payloadReport.StartsWith("PayloadResult=OK", StringComparison.Ordinal))
                {
                    r.AddOk("Payload workbook read");
                }
                else
                {
                    r.AddFail("Payload workbook read (payload reported failure)", null);
                }

                // 5. State after running the payload.
                r.AddLoadedOpenXmlAssemblies("After payload run");
            }
            catch (Exception ex)
            {
                r.AddFail("Open XML probe failed", ex);
            }
            finally
            {
                LockedFileCopier.TryDelete(tempCopy, r.Add);
                reportPath = r.Flush(uiapp, "Probe_OpenXml");
            }

            TaskDialog.Show("Excel Schedule Link diagnostics",
                "Open XML probe completed." + (r.HasFailure ? "\nFailures were recorded; review the report." : "\nAll operations succeeded.")
                + "\n\nDetailed report:\n" + reportPath);
            return Result.Succeeded;
        }

        /// <summary>Ask the user to record the condition for this session.</summary>
        private static string AskUserCondition()
        {
            TaskDialog dialog = new TaskDialog("Excel Schedule Link diagnostics - session condition");
            dialog.MainInstruction = "Select the condition to record for this session";
            dialog.MainContent =
                "This is recorded in the report. Dynamo assembly detection is not automatic because it can be misleading.";
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Condition A: Dynamo was not started in this session");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Condition B: Dynamo was started in this session");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Do not specify");
            dialog.AllowCancellation = true;

            TaskDialogResult result = dialog.Show();
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    return "Condition A (Dynamo not started)";
                case TaskDialogResult.CommandLink2:
                    return "Condition B (Dynamo started)";
                default:
                    return "Not specified";
            }
        }
    }
}
