# Changelog

## 0.10.0

Changed:

- New links request only the workbook, worksheet and optional range. Their initial
  widths are calculated from Excel and the displayed text with a fixed 1.5 factor,
  without a scale question. New links keep their current column widths on later
  reloads. Existing links retain their saved reload policies and width factors;
  factor editing is no longer offered.
- Column Widths now owns separate column-width and row-height reload policies.
  With **Fit rows to text on reload**, the add-in sets each header-row height on
  Link, every Reload, and every Column Widths Apply, including Quick Layout; it
  can make rows taller or shorter. With **Keep current row heights**, text
  fitting is disabled; Apply leaves current heights alone, while Reload restores
  captured heights where possible and reports rows Revit could not keep. A
  successful Apply notes that wrapped text may be hidden.
- Link Manager is a six-column register for linked sources, status and integrity,
  with reload and repair actions. Its settings editor and settings-save prompts
  have been removed.
- Fit Content redistributes the current candidate total in Column Widths and the
  current live total in Quick Layout when the total is within the supported
  total-width range. Outside that range it falls back to all-minimum or
  all-maximum column widths and warns that the total cannot be kept. It
  prioritizes number/date/code-like values, reports values or merged cells that
  may wrap, and shows an estimated comfortable width. Long-text caps can produce
  a **(limited estimate)** note. Fit Content does not shrink, truncate or split
  text; widening remains an explicit Table width / Fit to Total action.
- Restore Excel Widths reads the current worksheet and applies workbook-derived
  widths, so it can change the table total.
- Column Widths shows each column as a letter plus a short heading, such as
  `B  Description`. Changed typed widths outside **30.5 to 609.6 mm** or
  **1.20 to 24.00 in** are refused with the column name and range. A pre-existing
  narrower width made by dragging in Revit is preserved when it is not changed in
  the dialog. Non-numeric input marks the cell, explains the problem in the
  status line, and blocks Apply until corrected.
- Successful reloads report changed rendered column counts as a limited hint to
  inspect widths. Fixed ranges, same-count substitutions and capped dimensions
  can evade the hint; equal-count changes are not detected.
- Reload all can process a matching-source link created by another user when its
  saved column policy is Keep. For a link created by another user, or whose
  creator is unknown, a policy that recalculates widths from Excel still
  requires the independent width-risk confirmation for a targeted reload and
  is skipped by Reload all. Changed or unknown source identity still
  requires confirmation and is skipped by Reload all. Source-identity and
  width-risk confirmations remain in place.
- A regeneration failure produces one add-in message. The add-in then stops
  further automatic work for that document session; a change that had already
  completed before the failure remains.

Upgrading from 0.9.x:

- Links created with 0.9.x keep their saved settings. Most use the row setting
  that 0.9.x showed as **Apply Excel / auto heights**, which is now **Fit rows
  to text on reload**. On the next Reload, or the next Column Widths Apply
  including Quick Layout, the add-in fits each header-row height to its text,
  so row heights adjusted by hand are replaced. To keep them, open Column
  Widths, choose **Keep current row heights on reload** and Apply before
  reloading.

Limitations:

- Row and width estimates are approximate. Row fitting was calibrated on screen at
  150% display scaling with Arial; very long Latin prose can wrap one line more
  than estimated. Sheet and PDF output was checked for the tested tables, not for
  every font or table size, and performance for a 500-row table has not yet been
  measured. Inspect wrapped text on sheets and exported output, especially in
  merged cells and manually resized rows.

## 0.9.4

Fixes:

- The built-in Excel time format `h:mm` (format ID 20) now shows 24-hour time,
  for example `13:45`, on every Windows locale. It previously followed the
  machine's short-time pattern, which on English systems showed `1:45 PM`.
  The built-in short-date formats still follow the regional settings, as in
  Excel.
- **Check and repair** treats a cancelled network-server approval the same way
  **Reload Link** and **Link Manager** do. The approval is the workbook-access
  prompt. For a schedule with unsafe cell styles, it now offers the separately
  confirmed **Rebuild from current table** instead of counting the schedule as
  declined.
- If Revit reports a fatal regeneration failure while Link Manager saves link
  settings, Link Manager now stops and closes without reading the model again.
  This also applies when the save is triggered by closing the dialog. Reload
  and the other model changes already behaved this way.

Internal:

- The code that changes the Revit model has been reorganized so that each rule
  lives in one place. This covers:
  - the transaction handling;
  - the reload and repair flow;
  - the open-time warning state.

  Behavior is otherwise unchanged.
- `ARCHITECTURE.md` describes the current code. It includes a table of which
  type owns each rule.

## 0.9.3 - First public release

Excel Schedule Link is a Revit 2024 add-in that displays a read-only Excel
worksheet in a Revit schedule view. It can reload the linked worksheet, manage
multiple links, and adjust column widths while keeping the link data in the
Revit model.

This release also hardens the boundaries around untrusted workbook and install
inputs:

- Workbook reads enforce limits on source size, package expansion, XML
  characters, worksheet structures, strings, coordinates, and the rendered
  500-row by 50-column window.
- Network workbook servers are not contacted until the user approves the
  server for the current Windows account. Approvals are kept in the per-user
  settings file, outside the Revit model.
- Installation writes an exact receipt of the files it copied. Uninstall uses
  that receipt or a fixed package allowlist and leaves unrelated files alone.

The add-in remains a pilot for Autodesk Revit 2024. See the user and developer
documentation for current limitations and verification boundaries.
