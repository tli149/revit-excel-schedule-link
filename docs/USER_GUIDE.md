# Excel Schedule Link — User Guide

> **Pilot release (0.9.3), Revit 2024 only.** Try it on a copy of a project
> first, keep backups, and check the result before you save or sync. See
> [Before you start](#before-you-start).

## How it works

- **Link** lets you bring one Excel worksheet into Revit as a schedule view. The
  link (the file, worksheet and settings) is saved inside the Revit model.
- **Reload** reads the workbook again and rebuilds the table in place. Sheets
  that show the schedule keep their layout.
- The add-in only **reads** Excel. It never writes to your workbook. It reads a
  temporary copy, so the file can stay open in Excel.
- Every command reads the whole workbook first, then changes the model in a
  single Revit transaction. If something fails, the add-in rolls that
  transaction back and tells you whether Revit confirmed the rollback. See
  [When something fails](#when-something-fails).

## Before you start

- **Revit 2024 only.** Other Revit versions will not load this build.
- **Pilot software.** It has been tested, but not on every kind of project.
  Use a copy or a throwaway model the first time.
- **Back up** the model before your first real use. Check the result before you
  save or synchronize.
- **Workshared models:** agree with your team before you link or reload
  schedules in a central model. Other people see the changes after you sync.
- **Unsigned files.** The DLLs and install scripts are not digitally signed.
  Windows may warn about them. See [INSTALL.md](INSTALL.md).

## Linking a worksheet

On the **Excel Schedule Link** ribbon tab, click **Link Excel**, then choose an
`.xlsx` file.

![Link dialog](images/link-dialog.png)

| Field | What it does |
|---|---|
| **Worksheet** | Choose which sheet of the workbook to show. |
| **Cell range** (optional) | For example `A1:F30` to show only that block. Leave it empty to use the whole used range. |
| **Column width scale** | Multiplies all column widths. **1.5–2.0 is recommended.** Higher values (for example 3.0) read well but make the table very wide. |

Click **Link Worksheet**. A new schedule view named `<workbook> - <worksheet>`
opens. You can rename it in the Project Browser like any other view. The link
stays with the schedule, not with its name.

The next time you use Link Excel, the folder, worksheet, range and width scale
from your last successful link are offered again. If the new workbook does not
have that worksheet, its first worksheet is selected instead.

**Already linked?** If the same workbook, worksheet **and** range are already
linked in this model, the add-in tells you and offers to open the existing
schedule. You can still create a second one. A **different range** of the same
worksheet counts as a new link, so you can split one worksheet across several
schedules.

### What comes across from Excel

| Excel feature | Shown in Revit? |
|---|---|
| Values, including formula results | Yes — the value Excel last saved (formulas are not recalculated) |
| Number formats: whole numbers, decimals, thousands separators, %, dates, times, leading zeros | Yes |
| Units inside custom formats (`12.3 A`, `12.35 kVA`, `1234 CFM`) and `$` prefixes | Yes |
| Fractions (`1 1/2`, `2 7/10`) | Yes, up to three-digit denominators |
| Merged cells | Yes |
| Column widths | Approximately (adjust with the width scale) |
| Scientific notation, elapsed time like `[h]:mm`, dates mixed with quoted text | Shown as plain values for now |
| Fonts, colors, borders, row heights, images, charts | No — see [Styling the table](#styling-the-table) |

**Size limits.**

- A linked table shows at most **500 rows × 50 columns**. A bigger worksheet or
  range is not refused: the top-left 500 × 50 cells are shown, and a note tells
  you how big the source was.
- Only the worksheet you pick is read. Other sheets in the same workbook can be
  as large as you like.
- To protect Revit from broken or deliberately crafted files, a workbook is
  refused **before it is read** if it is bigger than 128 MB, or if its contents
  are far outside what Excel itself produces (for example a cell with more than
  32,767 characters, or coordinates beyond Excel's own grid). The message
  starts with *"The workbook exceeds the Excel Schedule Link safety limit
  for…"* and names the limit. Real project workbooks are well within these
  limits.

## Workbooks on a network server

Workbooks on a mapped drive (for example `Z:\Projects\...`) work like local
files.

For a network path that starts with `\\` (for example
`\\fileserver\projects\...`):

- **When you pick the file yourself** in Link Excel, that counts as your
  approval. You will not get an extra question.
- **When the path comes from the model** (Reload, Reload all, repairs,
  Restore Excel Widths) and you have not used that server before, the add-in
  first asks **"Allow access to \\fileserver?"** and shows the full path.
  **Cancel** is the default. Choose **Allow access** only if you trust the
  server. Reload all asks at most once per server.
- Your approval is remembered **for your Windows account on this computer**,
  not in the Revit model. A model you receive from someone else can never
  approve a server for you.
- Until you approve a server, Link Manager shows its links as
  `Network - not approved` and does not contact the server at all.
- Approved servers are listed in `%AppData%\ExcelScheduleLink\ui-settings.txt`
  on the line that starts with `ApprovedNetworkServers=`. To withdraw an
  approval, close Revit and remove the server name from that line.

This protects you from models that point at an unknown server. Simply
contacting a server can send it information about your Windows sign-in.

## Placing the schedule on a sheet

Drag the schedule from the Project Browser onto a sheet, just as you would for
any other Revit schedule. Printing and PDF export work normally.

- In the **schedule view** you can drag individual column widths.
- On a **sheet** you can only drag the table's overall width. That is normal
  Revit behavior.
- To set exact widths from anywhere, including a sheet, use the
  [Layout tools](#adjusting-column-widths).

## Adjusting column widths

The **Layout** panel works with the linked schedule that is open or selected,
including a schedule placed on a sheet.

### Column Widths…

This opens a dialog that lists every column:

| Part | What it does |
|---|---|
| **Width** | Type an exact width in mm (or inches in an imperial project). |
| **Fit Content** | Sizes each column to the text it shows. CJK (Chinese, Japanese, Korean) characters count as full width, and merged cells widen their columns together so the text fits. |
| **Distribute Evenly** | Gives every column the same width, keeping the total. |
| **Restore Excel Widths** | Reads the worksheet again and applies the widths it has **now**. |
| **Fit to Total** | Scales all columns to the total width you type, keeping their proportions. |
| **Reload policy** | *Keep these widths on reload* or *Recalculate from Excel on reload* — see below. |

Nothing changes in the model until you press one of these buttons:

- **Apply** — applies the widths and keeps the dialog open, so you can try
  another layout.
- **Apply & Close** — applies and closes.
- **Open Schedule View** — jumps to the schedule view (asks first what to do
  with changes you have not applied).
- **Close** — closes; asks first if you have changes you have not applied.

Widths are applied all together or not at all. If one column cannot be set,
nothing changes and the column is named in the message.

### Quick Layout

The **Quick Layout** dropdown applies *Fit Content*, *Distribute Evenly* or
*Restore Excel Widths* in one click, without opening the dialog.

### How layout and reload work together

- *Fit Content*, *Distribute Evenly* and typing your own widths count as a
  deliberate layout, so the link switches to **Keep manual widths on reload**.
- *Restore Excel Widths* changes only the current widths. It never changes the
  reload policy.
- You can set the policy yourself in the dialog or in the Link Manager.

Revit only allows roughly 1.2–24 inches per column, so a very long text may
still wrap.

## Reloading after Excel changes

1. Save the workbook in Excel. It can stay open.
2. Open the linked schedule view, or select it in the Project Browser.
3. On the **Excel Schedule Link** tab, click **Reload Link**.

The result message tells you what happened: the workbook had not changed, or
the changed workbook was reloaded after you confirmed it. Sheet placements are
kept, and nothing is duplicated.

If the workbook cannot be found at its saved path or next to the model where it
was found before, the reload says so and changes nothing. Put the file back, or
link the new location.

### When the workbook looks different

Each link remembers a fingerprint (SHA-256) of the workbook it last read.

- **Same fingerprint:** the reload goes ahead without asking you.
- **Different, and you created the link:** you get a normal "the workbook has
  changed — reload?" question.
- **Different, and someone else created the link (or Revit cannot tell who):**
  the question is stricter. **Cancel and locate the intended workbook** is the
  default. Choose **Reload this resolved workbook** only if you are sure it is
  the right file. This catches the case where the same path on your computer
  points to a different file than on a colleague's computer.

**Reload all** never answers these questions for you. Links that need a
decision are skipped and listed as **Confirmation required**, separately from
missing or failed links.

In a workshared model, the first command that changes an Excel-linked schedule
shows a short reminder once per Revit session. It does not block anything.

### Links made with older versions

Links made with Excel Schedule Link versions before 0.9.1 appear in Link
Manager as **Legacy / identity unknown**. The first time you reload, save
settings, or apply a layout to such a link, the add-in asks before upgrading it.
After the upgrade, older versions of the add-in can no longer reload or manage
that link (the table itself stays visible). Cancel leaves everything as it was.

## Unsafe schedule styles

Versions before 0.9.2 could save a linked table whose cells had no cell style.
Opening such a schedule view can make Revit 2024 close unexpectedly. The add-in
now checks every new, reloaded or rebuilt table before saving it. It also looks
for schedules that an older version already saved this way.

### When a model opens

The add-in checks the linked schedules. The check runs inside a transaction
that is rolled back, so it is not meant to leave any change in the model. It
covers up to 64 schedules and 100,000 header cells in total, and up to 25,000
header cells per schedule. Anything beyond these limits is shown as
`Not checked - do not open`.

If unsafe schedules are found in an editable model, you see
**Unsafe Excel-linked schedule styles were found.** with a list of schedules:

- **Do not open the listed views** from the Project Browser until they are
  repaired. The add-in cannot stop Revit's Project Browser from opening them.
- **Review repairs now** takes you through the schedules one by one. Choose
  **Reload from workbook** to repair, or **Skip this schedule**.
- **Not now** (the default), **Cancel**, or closing the dialog changes nothing.
  You can repair later from Link Manager.

A read-only model, or one with schedules marked `Not checked`, gets a message
without repair choices. Open an editable copy, or use **Check and repair...**
in Link Manager.

### Repairing

- **Reload from workbook** is the normal repair. It keeps the schedule, its
  name and its sheet placements, and shows the usual confirmations. If the
  repaired table passes the check, the result says **Unsafe cell styles were
  repaired.**
- **Reload all** also reloads unsafe links under the same rules, never asks
  per-link questions, and reports **Repaired: n**.
- **Rebuild from current table** is offered when one unsafe schedule's workbook
  cannot be found or read. It rebuilds the schedule from the text and layout
  already stored in Revit, without Excel. Cell-specific formatting is replaced
  by the schedule's Appearance settings. You confirm it every time, and
  Reload all never uses it.

### What the add-in will not do

- It will not open an unsafe or unchecked schedule for you, from any dialog.
  It offers **Reload from workbook** instead.
- Repairs (the repair review and **Rebuild from current table**) refuse a model
  that is read-only or opened from a read-only file, unless it is a detached
  copy. Save an editable copy first. A normal reload is not affected.
- In a detached copy, repairs say **This repair changes the detached copy
  only.**
- In a workshared model, a schedule that another user has borrowed cannot be
  changed. Ask them to synchronize and relinquish it, reload latest, and try
  again.

## When something fails

- If the workbook cannot be read, nothing in the model is touched.
- If a later step fails, the add-in rolls back its transaction. It says the
  change was rolled back **only when Revit confirms it**. Otherwise it says
  **Rollback completion could not be verified.** In that case, or if Revit
  reports a regeneration failure, **close the model without saving** and open
  it again before you continue.

## The Link Manager

On the **Excel Schedule Link** tab, click **Link Manager** to see and manage
your links.

![Link Manager](images/link-manager.png)

- You see every linked schedule with its worksheet, range, source path and
  **Status**:
  - `Protected` — the link has a workbook fingerprint.
  - `Protected / identity unknown` — the first fingerprint is still pending.
  - `Legacy / identity unknown` — made with an older version and not upgraded.
  - `Missing` — the workbook cannot be found.
  - `Network - not approved` — the workbook is on a network server you have not
    approved yet. See [Workbooks on a network server](#workbooks-on-a-network-server).
  - `Invalid workbook path` — the saved path cannot be a usable `.xlsx` location.
    The add-in does not try to open it. Create a new link.
- **Integrity** shows `Safe`, `Unsafe - reload required` or
  `Not checked - do not open`. **Check and repair...** checks up to 256
  schedules and lets you approve each repair. See
  [Unsafe schedule styles](#unsafe-schedule-styles).
- **Reload selected** reloads the selected link with the settings shown in the
  editor. **Reload all** reloads every link that does not need a decision. One
  failing link never affects the others.
- **Settings** for the selected link apply on its next reload:

| Setting | Meaning |
|---|---|
| **Column width scale** | Same as in the Link dialog. 1.5–2.0 is recommended. |
| **Column width mode** | *Apply Excel widths*: every reload re-applies the widths from Excel. *Keep manual widths*: widths you set by hand survive reloads. |
| **Row height mode** | The same idea for row heights. |

These modes only control what happens **during a reload**. They do not change
how dragging works in Revit, and they do not lift Revit's own column-width
limit.

When you change a setting, the heading says **unsaved changes** and
**Apply settings** becomes available. If you select another link, close the
window, or click Reload all with unsaved edits, you are asked to
**Save / Discard / Cancel**. Reload selected uses the visible settings
automatically.

The window size and column layout are remembered between sessions.

## Styling the table

Excel fonts and colors are not copied. Use Revit's own tools to style the
schedule so it matches your office standards:

1. Select the linked schedule view.
2. In **Properties**, open **Appearance** (Edit...).
3. Set the text font, text size and grid lines like for any Revit schedule.
   These settings survive reloads.

Tip: style one linked schedule the way you like, then use it as the reference
for the others.

## Good to know

- **A small "A" strip under the table in the schedule view.** That is Revit's
  column letter for a hidden helper column. It never shows on sheets, prints or
  PDFs.
- **Keep "Show Title" on.** The table lives in the schedule's title area.
  Turning off *Show Title* in Appearance hides the whole table.
- **Moving files.** A link remembers the full path and a path relative to the
  model. Moving the project and the workbook together usually keeps the link
  working.

## FAQ

**Does the workbook need to be closed?**
No. Linking and reloading work while the file is open in Excel.

**Are formulas recalculated?**
No. Revit shows the values Excel last saved, so save the workbook after
editing.

**Can it write back to Excel?**
No. The link is one-way, Excel → Revit, on purpose.

**Old `.xls` files or `.csv`?**
No, only `.xlsx`.

**Do colleagues without the add-in see the table?**
Yes. The table is ordinary Revit content. They just cannot reload it.

**Non-English text?**
Sheet names, cell values and file paths in any language are supported,
including CJK text.

**Where do I report a problem?**
Open an issue on the project's GitHub page. See
[TROUBLESHOOTING.md](TROUBLESHOOTING.md#reporting-a-problem) for what to
include — and what not to attach.
