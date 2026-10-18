# Excel Schedule Link — Quick Start

Excel Schedule Link puts an Excel worksheet inside Revit as a schedule view.
Save the workbook, then reload the table with one click. That saves you from
copy-paste.

*Revit 2024 only. Installing takes about five minutes and needs no
administrator rights.*

> **Pilot release.** Try it on a copy of a project first, and keep backups.

---

## 1. Install (one time)

1. Download `ExcelScheduleLink_v0.10.0.zip` from the project's **Releases** page.
   Use the release ZIP, not the green **Code → Download ZIP** button. That
   button contains the source code.
2. **Before extracting:** right-click the ZIP → **Properties** → tick
   **Unblock** → **OK**. Skipping this is the most common reason for "nothing
   happens" later.
3. Extract the ZIP to any folder, for example `C:\Tools\ExcelScheduleLink`.
4. Close Revit. In the extracted folder, right-click
   `scripts\install-revit2024.ps1` → **Run with PowerShell**. Wait until the
   last lines turn green.
5. Start Revit 2024. Look for the new ribbon tab **Excel Schedule Link**.

   ![Ribbon tab](images/ribbon.png)

Trouble? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md), sections 1 and 2. More
detail on installing: [INSTALL.md](INSTALL.md).

## 2. Link your first worksheet

1. Open a practice model in Revit.
2. On the **Excel Schedule Link** tab, click **Link Excel**.
3. Choose your `.xlsx` file.
4. Choose the **worksheet** and leave **Cell range** empty (= the whole used
   range). The add-in calculates the initial widths from Excel and the displayed
   text with a fixed 1.5 factor; there is no scale field to set.

   ![Link dialog](images/link-dialog.png)

5. Click **Link Worksheet**. A new schedule view opens with your worksheet in it.

## 3. Get the column widths right

If the columns are too narrow or too wide, use the **Layout** panel on the same tab:

- **Quick Layout → Fit Content** redistributes the live table total when it is
  within the supported total-width range, using the displayed text. It keeps
  number/date/code values on one line when possible and reports text or merged
  cells that may wrap. If the live total is outside that range, it uses
  all-minimum or all-maximum column widths and warns that the total cannot be
  kept.
- **Column Widths…** shows each column as a letter plus a short heading, such as
  `B  Description`. It lets you type exact widths in the project units, or use
  Distribute Evenly, Restore Excel Widths or Fit to Total. Fit Content keeps the
  current candidate total within the supported total-width range; outside that
  range it uses all-minimum or all-maximum widths and warns that the total cannot
  be kept. **Table width** and **Fit to Total** are the explicit way to choose a
  different supported total. **Apply** keeps the dialog open so you can try
  another layout.
- Choose **Fit rows to text on reload** when the add-in should set each header
  row's height on Link, Reload, and every Apply. It may make rows taller or
  shorter. **Keep current row heights** disables text fitting; Apply leaves
  current heights alone, while Reload restores captured heights where possible
  and reports rows Revit could not keep. Wrapped text may still be hidden.

## 4. Put it on a sheet

Drag the new schedule from the **Project Browser** onto a sheet, just like any
Revit schedule. It prints and exports to PDF normally.

## 5. When the Excel file changes

1. Edit and **save** the workbook in Excel. It can stay open.
2. In Revit, open the linked schedule view.
3. Click **Reload Link**. The table updates in place; your sheets do not need
   any changes. New links keep their current column widths on later reloads;
   older links retain their saved reload policies and width factors.

If a column is dragged directly in Revit, the add-in leaves that width alone
until the next add-in Apply or Reload. Fit-to-text sets row heights during those
actions; Revit does not automatically grow them just because text wraps.

Fit Content, Distribute Evenly, and typed widths select **Keep these widths on
reload**; the Quick Layout Fit Content and Distribute commands save that policy
immediately. The row-height choice remains separate. Restore Excel Widths reads
the current worksheet and leaves both policies unchanged.

---

- **Want more?** [USER_GUIDE.md](USER_GUIDE.md) covers ranges, column widths,
  network workbooks, the Link Manager and styling.
- **Something wrong?** See [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
- **Tried it?** Feedback is welcome — see [PILOT_FEEDBACK.md](PILOT_FEEDBACK.md).
