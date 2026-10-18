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

**Cause:** the 0.10.0 pilot files are not digitally signed, and your computer's
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

- Use the **Layout** panel: **Quick Layout → Fit Content**, or **Column
  Widths…** for exact values. Fit Content keeps the current total only within
  the supported total-width range and reports values or merged cells that may
  wrap. If the total is below or above that range, it uses all-minimum or
  all-maximum widths and warns that the total cannot be kept. To choose a wider
  supported total, enter a new **Table width** and press **Fit to Total**.
- **Restore Excel Widths** reads the current worksheet and applies its widths,
  so it can change the table total.
- In Column Widths, changed values must be from **30.5 to 609.6 mm** or
  **1.20 to 24.00 in**. A width made narrower by dragging in Revit can remain
  below that range as long as you do not change that cell in the dialog.
- If a width cell contains text that is not a number, the cell is marked and the
  status line at the bottom explains the problem and the range. Apply is blocked
  until the value is corrected.
- To keep widths on reload, choose **Keep these widths on reload** in Column
  Widths. New links keep their current widths by default; existing links retain
  their saved policies and factors.

## 8. Text is wrapped or hidden after a width change

- **Fit rows to text on reload** makes the add-in set every header-row height on
  Link, Reload, and every Column Widths Apply, including Quick Layout. It may
  make rows taller or shorter. Revit does not grow the rows automatically just
  because text wraps.
- **Keep current row heights on reload** disables text fitting. Apply leaves the
  current heights alone; Reload restores captured heights where possible and
  reports **Revit did not keep the saved height for N row(s).** Wrapped text may
  be hidden. Choose Fit rows to text and apply, or adjust the row heights
  yourself.
- If a known set of rows cannot be fitted, the action still succeeds and reports
  **Text may be hidden in N row(s): the rows could not be fitted to their text.**
  If fitting measurements are unavailable, it says that rows could not be fitted
  because measurements were unavailable and asks you to check for hidden text.
  These are warnings about visibility, not proof that all text is visible. Check
  the view, sheet and exported PDF.
- The fitted values are estimates calibrated on screen at 150% with Arial. Very
  long Latin prose can wrap one line more than estimated. Sheet/PDF output was
  checked for the tested tables, not every font or table size; 500-row performance
  has not yet been measured.

## 9. My hand-sized row heights changed after upgrading

**Symptom:** after upgrading from 0.9.x, a Reload or a Column Widths Apply
(including Quick Layout) replaced row heights you had adjusted by hand.

**Cause:** links created with 0.9.x keep their saved settings. Most use the row
setting that 0.9.x showed as **Apply Excel / auto heights**, which is now **Fit
rows to text on reload**, so the add-in fits each header row to its text.

**Fix:** for each link whose heights you want to keep, open **Column Widths**
before the next Reload, choose **Keep current row heights on reload**, and press
**Apply**. If the heights were already replaced, adjust them again by hand; with
Keep selected, later Reloads restore them where Revit allows.

## 10. Revit says "Column Width Too Large" while dragging

This is Revit's own limit for schedule columns. The add-in keeps changed values
within its supported range, but a native drag can leave an already narrow width
below the dialog's displayed range. Use Column Widths and **Fit to Total** to
make the table wider when needed.

## 11. A lone "A" appears under the table in the schedule view

That is Revit's column letter for a hidden helper column. It never shows on
sheets, prints or PDFs.

Its width still matters because the schedule view fits the table inside it.
The add-in sets it to the table width on every link and reload. If a table
looks squeezed into one narrow column, **reload the link once**.

## 12. The whole table disappeared from the schedule view

The table probably disappeared because *Show Title* was turned off in the
schedule's Appearance settings. Turn **Show Title** back on — the table lives
in the title area.

## 13. My worksheet is not in the dropdown

The dropdown lists every sheet in the workbook you picked. If one is missing,
you may not have saved the workbook after adding the sheet, or you may have
picked a different file. Save in Excel and try again.

## 14. Revit closed when I opened a linked schedule, or you see "Unsafe Excel-linked schedule styles were found."

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
close the model **without saving** and reopen it. The add-in stops all further
automatic work for that document session after this failure. Any change that
had already completed before the failure remains; the failed action is not
treated as successful. You see one add-in message, not a second Revit error box.

## 15. The message says "Rollback completion could not be verified."

Revit did not confirm that the failed change was fully undone. Close the model
**without saving**, open it again, and check the schedule before you continue.

---

## Reporting a problem

Open an issue on the project's GitHub page. Include:

1. What you clicked and what you expected.
2. The exact message text (a screenshot is fine).
3. Your Revit 2024 build (Help → About) and the add-in version (0.10.0).
4. For "nothing happens" problems: the relevant lines of the newest journal
   file from `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2024\Journals\`.

**Please do not attach client or project files.** GitHub issues are public.
Do not upload real `.rvt` or `.xlsx` files, and remove project names, client
names, user names and server paths from logs and screenshots. A small made-up
workbook that shows the same problem is ideal.

Security problems (for example, a file that makes Revit hang or crash on
purpose) should not go in a public issue — see [SECURITY.md](../SECURITY.md).
