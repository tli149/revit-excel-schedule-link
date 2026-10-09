# Excel Schedule Link architecture

## 1. Current baseline

Excel Schedule Link v0.9.3 is a pilot add-in for Autodesk Revit 2024. It reads
displayed worksheet content from an external XLSX workbook and renders it into
the header section of a Revit ViewSchedule. Link metadata is stored on that
schedule so the table can be reloaded later.

The runtime add-in targets .NET Framework 4.8. Reusable contracts, Excel
reading, and layout math target .NET Standard 2.0. Unit tests target .NET 8.

This document describes the implemented code boundaries in the public tree.
Earlier pilot decisions are historical context; the current source, tests, and
developer documentation are the authority for v0.9.3 behavior.

## 2. Runtime shape

The main link path is:

    Revit ribbon command
        -> WinForms selection dialog
        -> IExcelReadService
        -> OpenXmlExcelReadService
        -> SheetModel
        -> one Revit transaction
        -> ScheduleFactory + HeaderTableRenderer + ExcelLinkSchema
        -> linked ViewSchedule

Reload uses the stored ExcelLinkInfo to resolve the workbook, re-read it outside
the transaction, and then rebuild the existing header table inside one
transaction. Layout commands resolve a linked schedule, calculate candidate
widths with pure layout code, validate them, and apply an approved set in one
transaction.

This separation is deliberate: file I/O and parsing may fail without touching
the model, while model writes remain all-or-nothing.

## 3. Project boundaries

| Project | Target | Responsibility | Dependency boundary |
|---|---|---|---|
| src/ExcelScheduleLink.Contracts | netstandard2.0 | Sheet model, DTOs, read-service contract, read exception, stored-workbook path policy and approval interface | No external dependencies |
| src/ExcelScheduleLink.Excel.OpenXml | netstandard2.0 | XLSX worksheet discovery, parsing, and display formatting | Owns DocumentFormat.OpenXml 3.5.1 |
| src/ExcelScheduleLink.Layout | netstandard2.0 | Pure column-width and row-height calculations | No Revit or Open XML types |
| src/ExcelScheduleLink.Revit2024 | net48 | Ribbon, commands, dialogs, Revit schedule rendering, persistence | Revit API plus the three libraries above |
| src/ExcelScheduleLink.Probe.OpenXmlPayload | net48 | Isolated Open XML diagnostic payload | Developer/QA only; not shipped |
| tests/ExcelScheduleLink.Excel.OpenXml.Tests | net8.0 | Reader, display formatter, cell reference, and layout tests | xUnit |
| tools/UiHarness | net48 | Renders real dialog source outside Revit for clipping and scaling checks | Developer-only; excluded from the solution |

The Revit project resolves RevitAPI.dll and RevitAPIUI.dll from a local Revit
2024 installation when available. Otherwise it falls back to compile-time
Nice3point Revit API reference packages. Revit API assemblies are never copied
into the product output.

## 4. Link creation

LinkExcelCommand coordinates the production link operation:

1. The user chooses an XLSX file, worksheet, optional range, and width scale.
2. OpenXmlExcelReadService reads the workbook into a SheetModel before a Revit
   transaction exists.
3. ExcelLinkInfo records absolute and model-relative paths, range settings,
   source timestamp, reload modes, and width scale.
4. One transaction creates a method-B schedule, renders the worksheet into the
   schedule header, and stores the link entity.
5. Any exception during model mutation rolls back that entire transaction.

HeaderTableRenderer limits the rendering area to 500 rows by 50 columns,
applies displayed strings and merged cells, and maps Excel-derived column
widths into Revit feet with clamping.

### 4.1 Workbook input boundary

`WorkbookReadLimits` is the single owner of the Open XML resource policy. Every
reader entry point copies the source only after the path syntax gate and source
byte check, scans ZIP entry count, per-entry and cumulative expansion, and
compression ratio before `SpreadsheetDocument.Open`, and supplies a nonzero
`OpenSettings.MaxCharactersInPart`. It also bounds worksheet merges, styles,
shared strings, string lengths, Excel coordinates, and the 500-by-50 output
array before allocating it. The policy allows ordinary multi-sheet,
tens-of-megabytes workbooks and large worksheet row/cell counts while keeping
only cells in the selected top-left output window. The workbook `<dimension>`
value is validated only for syntax and legal coordinates because it may be
stale; actual cells define the used range. Unselected worksheet parts are not
loaded. Explicit ranges filter cells into the bounded output array and do not
retain cells outside that range.

Column spans are validated as unsigned values against Excel's 1..16,384
column domain before conversion to signed indexes; only their intersection
with the accepted output columns is iterated. Fraction formats accept up to
three denominator placeholders and use a bounded continued-fraction
approximation with a fixed iteration budget; longer formats fall back to
General.

The schedule body is intentionally minimal. Revit exposes writable table
content in the schedule header, while the body is not suitable for this linked
table. The resulting editor-only A strip is a known Revit UI effect; it does not
appear on sheets or in printed/PDF output according to the recorded acceptance
checks.

## 5. Reload and link management

ReloadService is shared by the single-link command and Link Manager. Its
responsibilities are:

- resolve the source using stored absolute and model-relative paths;
- read the workbook before changing the document;
- detect the source timestamp change;
- preserve current dimensions when a keep-manual mode is selected;
- rebuild and re-save the link data transactionally;
- classify success, unchanged, missing-source, and failure outcomes.

All stored or resolved workbook candidates pass through
`StoredWorkbookPathPolicy` in `PathResolver` before `File.Exists`, timestamps,
copy, hashing, or Open XML access. Link Manager inspection returns
`Network - not approved` without probing an unapproved UNC server. An action
that needs the file shows a cancel-default confirmation naming the server and
full path; approvals are stored in the existing per-user settings file and
never in extensible storage. Duplicate checking compares validated strings and
does not probe source paths. User-facing approval and rejection text is owned
by `UserMessages`.

Link Manager discovers linked schedules through their extensible-storage entity,
shows source status, edits per-link width/height policy, and invokes the shared
reload service. Reload-all keeps each link independent so one missing or invalid
source does not damage other schedules.

### 5.1 Schedule style integrity

Revit gives cells created by `InsertRow`/`InsertColumn` their native style only
when the document regenerates. A header table persisted before that point
contains styleless cells, and opening such a view can crash Revit 2024.

- The pure decision table lives in ExcelScheduleLink.Contracts
  (`ScheduleStyleIntegrityModels`, `ScheduleIntegrityFlowModels`). A cell is
  classified as styleless only when all four conditions hold:
  - all four borders are unset;
  - it has no explicit override;
  - at least one usable baseline exists;
  - its font/size pair is an unambiguous mismatch against every available
    baseline.

  The baselines are the resolvable title/header text types and a complete,
  non-styleless, non-ambiguous top-left reference cell. Missing or ambiguous
  baseline evidence yields `Indeterminate`. Sizes are compared in 1/96-inch
  table units with exact, ambiguous, and mismatch outcomes.
- `ScheduleStyleIntegrityScanner` serves two cases, because `GetTableData`
  needs a modifiable document. Standalone checks read the table in dedicated
  transactions and request rollback in `finally`. If a final `RolledBack`
  status cannot be observed, the check becomes `Indeterminate`.
  `ScanAfterMutationInOpenTransaction` regenerates first and runs inside the
  caller's open mutation transaction. HeaderTableRenderer, layout apply, and
  repair use it as their post-mutation gate. That transaction may commit only
  when the result is `Healthy`. Otherwise the operation fails and a rollback is
  attempted, and messages claim a completed rollback only after observing
  `RolledBack` (`TransactionRollback`).
- Scans are bounded by `StyleIntegrityLimits`: an automatic scan covers 64
  schedules and 100,000 header cells, a manual scan 256 schedules, and a single
  schedule 25,000 header cells. Anything beyond a limit is reported as
  `Not checked`.
- `ScheduleIntegrityLifecycle` scans on DocumentOpened, notifies at Idling, and
  keeps per-session results in `ScheduleIntegritySessionCache`.
  `ScheduleActivationGuard` stops add-in-initiated view activation of an
  unsafe schedule. `ScheduleRepairCoordinator` and `ScheduleRepairService`
  run the explicit, per-schedule repair and the no-workbook rebuild.
- Repair is primarily an ordinary reload. The current-table rebuild is a
  separately consented fallback. It applies only to a targeted repair of a
  known-`Affected` schedule whose workbook is missing or unreadable, never to
  Reload all.
- Explicit repair (the per-schedule review and the fallback) applies
  `ForExplicitRepairDocument`. It refuses `IsReadOnly` documents. A
  detached document takes precedence over `IsReadOnlyFile`, which is refused
  only when the document is not detached. Explicit repair also checks
  `GetCheckoutStatus` before mutating and refuses `OwnedByOtherUser`. Ordinary
  reload lets the transaction result decide. It consults checkout status only
  after a start refusal or an observed rollback. The owner is named only when
  Revit returns a reliable name.
- Diagnostic probes for these paths are registered only in the probe
  manifest template.

## 6. Layout subsystem

ExcelScheduleLink.Layout holds algorithms and tuning constants that can be
tested without Revit. The Revit layer supplies current table state and content
measurements, while the UI edits a candidate set rather than mutating the model
continuously.

Implemented operations include:

- fit content, including CJK-aware measurement and merged-span constraints;
- distribute evenly;
- restore Excel-derived widths;
- fit to a requested total using constrained redistribution;
- direct per-column values in project-appropriate units.

Validation occurs before writing. The final width set is applied as a single
Revit operation so an invalid column cannot intentionally leave a partial
layout. Reload policy and the immediate layout operation are separate choices:
manual edits can switch a link to keep-manual widths, while restore-Excel does
not silently rewrite that policy.

## 7. Persistence contract

ExcelLinkSchema stores schema version, renderer version, source paths, path
preference, worksheet and range, timestamps, width/height reload modes, and
width scale directly on the linked ViewSchedule.

The shipped schema GUID is:

    6fd491d4-0092-45ae-82ad-2d51ce4a0615

That GUID and its v1 field set are immutable. Adding, removing, or changing a
field requires a new schema GUID and a migration keyed by SchemaVersion.

## 8. UI and Revit integration

RibbonApplication creates the Excel Schedule Link tab, product commands, and
layout commands. Availability checks are intentionally inexpensive; deeper
link validation happens only when a command runs.

Dialogs are WinForms and intentionally inherit Revit's DPI awareness. The
current behavior avoids forced DPI context, explicit form-level font,
AutoScaleMode, AutoScaleDimensions, and modal owner injection. Layouts are
content-sized to survive font scaling. Any change to these rules needs both
UiHarness rendering and real Revit/high-DPI confirmation.

Embedded 16 px and 32 px PNG resources provide ribbon icons. Add-in manifest
templates are copied from the Revit project and have their deployment path
placeholder resolved during installation.

## 9. Safety properties

The most important behavioral guarantees are:

- a workbook read failure changes nothing in Revit;
- a render or persistence failure rolls back the model operation;
- one failed reload does not change that schedule or other linked schedules;
- parsing uses a temporary copy so an open Excel workbook is not held or
  locked by the add-in;
- workbook package expansion and render allocation are bounded before DOM load;
- unapproved UNC paths are displayed without probing the network, and the
  approval decision is per Windows user rather than model data;
- formulas are not recalculated; only the value cached in the workbook is
  rendered;
- no render, reload, repair, or layout apply is allowed to commit a header
  cell that the post-regeneration style check cannot verify;
- source path and Unicode worksheet content are preserved;
- QA and probe command classes are compiled into the Revit DLL, but the
  production manifest does not register them and the probe payload is not
  shipped to end users.

## 10. Build, tests, and distribution

ExcelScheduleLink.sln contains the five source projects and the unit-test
project. UiHarness is deliberately built separately. `testdata` contains the
small XLSX fixtures used by the public tests. Build intermediates, distribution
trees, generated PDFs, and harness renderings are outputs rather than sources
of truth.

`global.json` pins the .NET 8 SDK feature band used by tests. The Revit API
fallback is pinned to the exact Nice3point package version used by both API
reference packages. `scripts/build.ps1` runs the solution build and tests,
creates a runtime-only distribution, and optionally creates a ZIP with a
SHA-256 checksum. The ZIP is not called reproducible unless two clean builds
have been compared.

## 11. Repository boundary

Track implementation, tests, scripts, public documentation, fixture generators,
and small reproducible fixtures. The public snapshot excludes `design/`,
`results/`, and `docs/validation/`; no source or test must depend on them.
Ignore build intermediates, distribution copies, generated PDFs, harness
renderings, bulk runtime evidence, RVT models, archives, and local agent state.

Ignoring a file only keeps it out of new commits. It does not delete or relocate
the existing workspace asset. Routine implementation history belongs in focused
commits; architecture contracts and acceptance criteria remain in versioned
documentation.
