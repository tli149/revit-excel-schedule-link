# Changelog

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
