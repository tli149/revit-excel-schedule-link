# Changelog

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
