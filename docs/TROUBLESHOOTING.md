# Excel Schedule Link — Troubleshooting

Find the symptom that matches what you see. Each entry gives you a fix to try.
If it still fails, see [Reporting a problem](#reporting-a-problem).

---

## 1. The Excel Schedule Link tab does not appear

**Symptom:** no **Excel Schedule Link** tab on the Revit ribbon.

**Fix:**
1. Close Revit.
2. Run `scripts\install-revit2024.ps1` again (right-click → Run with PowerShell).
3. Check that this file exists:
   `%AppData%\Autodesk\Revit\Addins\2024\ExcelScheduleLink.addin`
4. Start Revit again.

**Still missing?** Check that you are running Revit **2024**. This build does
not load in other versions.

## 2. A command does nothing (no dialog, no error)

**Symptom:** you click a button and see no response, or Revit shows an error
that mentions `0x80131515`.

**Cause:** Windows is blocking the DLLs ("Mark of the Web") because the ZIP was
extracted before you unblocked it.

**Fix (no admin rights needed):**
1. Close Revit.
2. Run `scripts\install-revit2024.ps1` again — it unblocks the files it
   installs.
3. Start Revit again.

**Next time:** unblock the ZIP (right-click → Properties → Unblock) *before*
you extract it.

## 3. PowerShell or Windows refuses to run the scripts

**Symptom:** the script window closes right away, PowerShell says running
scripts is disabled, or SmartScreen warns about an unknown publisher.

**Cause:** the 0.9.3 pilot files are not digitally signed, and your computer's
policy does not allow unsigned scripts from the internet.

**Fix:** unblock the ZIP before extracting (see INSTALL.md, Step 1), then use
the command-line version in INSTALL.md, Step 4. On a company computer, if it
is still refused, ask your IT team — do not try to switch the policy off.

## 4. "Failed to read the Excel workbook."

**Symptom:** linking or reloading shows this error. The model is unchanged.

**Check, in this order:**
- Confirm that the file is really `.xlsx`. (`.xls` and `.csv` are not
  supported.)
- Open it in Excel. If Excel shows a repair prompt, save a fresh copy and link
  that one.
- Confirm that the worksheet is still in the workbook with the same name.
- Does the message say *"exceeds the Excel Schedule Link safety limit"*? The
  file is larger or stranger than the add-in accepts (for example over 128 MB).
  Save a copy with just the sheet you need, or split the workbook, and link
  that. Big worksheets on their own are fine — they are shown top-left, up to
  500 × 50.

## 5. Reload says "The linked workbook could not be found."

**Symptom:** Reload reports the missing file, but the table stays as it was.

**Cause:** the workbook was moved or renamed, or its drive is not connected.

**Fix:** put the file back where it was. The message shows the paths it tried.
You can also create a new link to the new location. In Link Manager, the
**Status** column shows which links are `Missing`.

## 6. A network workbook is "not approved"

**Symptom:** Link Manager shows `Network - not approved`, or you are asked
**"Allow access to \\server?"**.

**Cause:** the add-in asks before it contacts a network server for the first
time. See [USER_GUIDE.md](USER_GUIDE.md#workbooks-on-a-network-server).

**Fix:** select the link and click **Reload selected**. If you know and trust
the server, choose **Allow access**. If you do not recognize it, choose
**Cancel** and check with whoever sent you the model.

`Invalid workbook path` means the saved path cannot be a valid `.xlsx`
location. Create a new link to the right file.

## 7. Columns are too narrow or too wide

- In **Link Manager**, select the link, set **Column width scale**
  (1.5–2.0 is recommended), then click **Reload selected**.
- Or use the **Layout** panel: **Quick Layout → Fit Content**, or
  **Column Widths…** for exact values.
- To keep widths you dragged by hand, set **Column width mode** to
  **Keep manual widths** in Link Manager.

## 8. Revit says "Column Width Too Large" while dragging

This is Revit's own limit for schedule columns. The add-in keeps every width it
sets within that limit. You are more likely to hit it when dragging by hand at
large width scales. Use a scale of 1.5–2.0 and make the table wider on the
sheet instead.

## 9. A lone "A" appears under the table in the schedule view

That is Revit's column letter for a hidden helper column. It never shows on
sheets, prints or PDFs.

Its width still matters because the schedule view fits the table inside it.
The add-in sets it to the table width on every link and reload. If a table
looks squeezed into one narrow column, **reload the link once**.

## 10. The whole table disappeared from the schedule view

The table probably disappeared because *Show Title* was turned off in the
schedule's Appearance settings. Turn **Show Title** back on — the table lives
in the title area.

## 11. My worksheet is not in the dropdown

The dropdown lists every sheet in the workbook you picked. If one is missing,
you may not have saved the workbook after adding the sheet, or you may have
picked a different file. Save in Excel and try again.

## 12. Revit closed when I opened a linked schedule, or you see "Unsafe Excel-linked schedule styles were found."

**Cause:** a version before 0.9.2 saved this table with cells that have no cell
style. Opening that schedule view can close Revit 2024. The Revit journal may
contain `NULL pStyle for cell in initCellStyle()`.

**Fix:**

1. Reopen the model. Do **not** open the schedule views named in the warning.
2. Choose **Review repairs now**, or open **Link Manager** and click
   **Check and repair...**.
3. For each schedule, choose **Reload from workbook**. If the workbook cannot be
   found or read, choose **Rebuild from current table** to rebuild it from the
   table already stored in Revit.
4. Check that the schedule now shows `Safe` in Link Manager's **Integrity**
   column, then open it. Do not open a schedule marked
   `Not checked - do not open`; run **Check and repair...** in an editable model
   first.

If the repair says the model is read-only, save an editable copy first. If
another user has borrowed the schedule, ask them to synchronize and relinquish
it, reload latest, and try again. If Revit reports a regeneration failure,
close the model **without saving** and reopen it.

## 13. The message says "Rollback completion could not be verified."

Revit did not confirm that the failed change was fully undone. Close the model
**without saving**, open it again, and check the schedule before you continue.

---

## Reporting a problem

Open an issue on the project's GitHub page. Include:

1. What you clicked and what you expected.
2. The exact message text (a screenshot is fine).
3. Your Revit 2024 build (Help → About) and the add-in version (0.9.3).
4. For "nothing happens" problems: the relevant lines of the newest journal
   file from `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2024\Journals\`.

**Please do not attach client or project files.** GitHub issues are public.
Do not upload real `.rvt` or `.xlsx` files, and remove project names, client
names, user names and server paths from logs and screenshots. A small made-up
workbook that shows the same problem is ideal.

Security problems (for example, a file that makes Revit hang or crash on
purpose) should not go in a public issue — see [SECURITY.md](../SECURITY.md).
