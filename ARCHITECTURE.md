# Excel Schedule Link architecture

## 1. Current baseline

The v0.10.0 source baseline is a Revit 2024 add-in. The add-in targets .NET
Framework 4.8; the reusable Contracts, Excel reader, and Layout libraries target
.NET Standard 2.0; the unit-test project targets .NET 8. The add-in reads an
`.xlsx` worksheet, renders the displayed cell text into a Revit `ViewSchedule`,
and stores link metadata on that schedule for later reloads.

The public source tree is organized around three boundaries: workbook input,
pure calculations and policy, and Revit/UI integration. Workbook file I/O and
parsing are completed before a workbook-based model-mutation transaction opens.
Each individual model mutation is placed in one transaction boundary; failures
attempt rollback and expose whether the final rollback status was observed.

## 2. Project boundaries

| Project | Target | Responsibility | Dependency boundary |
|---|---|---|---|
| `src/ExcelScheduleLink.Contracts` | netstandard2.0 | Reader contract plus Revit-free policy, organized into `Reader/` and `Policy/`; policy remains in this assembly so the net8 tests can exercise it | No external dependencies |
| `src/ExcelScheduleLink.Excel.OpenXml` | netstandard2.0 | XLSX sheet discovery, bounded parsing, fingerprinting, and display formatting | Owns `DocumentFormat.OpenXml` 3.5.1 |
| `src/ExcelScheduleLink.Layout` | netstandard2.0 | Pure column-width and row-height calculations | No Revit or Open XML types |
| `src/ExcelScheduleLink.Revit2024` | net48 | Ribbon, commands, dialogs, Revit rendering, persistence, reload, layout, and repair | Revit API plus Contracts, Excel.OpenXml, and Layout |
| `src/ExcelScheduleLink.Probe.OpenXmlPayload` | net48 | Isolated Open XML payload used by diagnostic commands | Not shipped to end users |
| `tests/ExcelScheduleLink.Excel.OpenXml.Tests` | net8.0 | Reader, policy, display, security, and layout tests | xUnit; references the three reusable libraries |
| `tools/UiHarness` | net48 | Renders the real WinForms dialogs outside Revit | Separate developer tool; not in the solution |

The Revit project references the Excel.OpenXml implementation project, but not
`DocumentFormat.OpenXml` directly. Local Revit 2024 API assemblies are used when
available; otherwise the project uses the pinned reference packages. Revit API
assemblies are not copied into the product output.

## 3. Runtime shape

The four principal flows are:

- **Link:** `LinkExcelCommand` validates the selected path, obtains the sheet
  selection and optional range, reads a `SheetModel` and `SourceFingerprint`,
  then uses `ModelMutation` for schedule creation, header rendering, row fitting
  under the selected policy, and v2 storage. The dialog requests no width scale;
  new links use the fixed 1.5 initial factor and start with current-width
  preservation on later reloads.
- **Reload:** `ReloadService` resolves and reads the workbook before its
  transaction. `TargetedReloadFlow` adds the consented current-table fallback
  and final integrity qualification. `ReloadAllFlow` processes links
  independently without offering that fallback; rows requiring source-identity
  or width-risk confirmation are skipped. UNC approval and the session
  worksharing advisory can still display dialogs.
- **Layout:** `LayoutService` resolves a linked schedule, reads the current
  widths, validates candidate widths, and applies one width set plus any
  requested column/row reload-policy change in a single mutation.
  `FitContentText` and `LayoutOps` allocate a fixed total for both dialog
  candidates and live Quick Layout widths; `HeaderRowFitter` sets fitted header
  rows during the mutation when the saved row policy requests it.
  `QuickLayoutRunner` and `LayoutDialogCommand` provide the command and dialog
  entry points.
- **Repair:** `ScheduleRepairCoordinator` owns explicit consent and per-schedule
  review. `ScheduleRepairService` captures an existing table in a read
  transaction and uses the snapshot only after its final status is verified as
  `RolledBack`, then restores it in one mutation transaction when the workbook
  is unavailable.

### Where each rule lives

| Rule | Owning code |
|---|---|
| Transaction lifecycle, fatal regeneration, and v1-to-v2 link loading | `ModelMutation` and `ModelMutationContext` |
| Which exceptions are a fatal regeneration; every catch-all around a Revit read filters them out with it | `ModelMutation.IsFatalRegeneration` |
| What a command, dialog callback or Idling repair does when a fatal regeneration reaches it as an exception, including that production command bodies never set Revit's `message` and `RunCommand` sets it only when the ESL fatal dialog could not be shown and the result is Failed; the lifecycle defers its one fatal message to the next Idling through `ContainInEventHandler` and `ShowDeferredMessage` | `FatalRegenerationBoundary` |
| Dropping all retained automatic integrity state after any fatal regeneration, without reading a document | `ScheduleIntegritySessionCache`, via `ModelMutation.FatalRegenerationCount` |
| Targeted reload, fallback, rescan, and final-health sequence, and that a caller shows no workbook reload outcome once the flow dispatched to the fallback (`ScheduleRepairCoordinator.TryFallback` reports it and puts the workbook reason in its offer or refusal details) | `TargetedReloadFlow` |
| Reload-all loop, counters, details, and per-row results | `ReloadAllFlow` |
| Workbook package, XML, string, coordinate, and fraction resource limits | `WorkbookReadLimits` |
| Activation, fallback eligibility, final-success qualification, explicit-repair document policy, and safe ownership-advisory timing | `ScheduleIntegrityFlowDecisions` |
| Fresh integrity qualification before activating an existing linked schedule | `ScheduleActivationGuard.CheckAndOfferRepair` |
| Layout source-identity authorization and legacy-migration consent | `LayoutMutationAuthorization.Authorize` |
| Explicit repair consent, per-schedule review, and final repair qualification | `ScheduleRepairCoordinator` |
| Scan-and-store operation | `ScheduleIntegritySessionCache.Rescan` |
| Automatic retry/notification state | `AutomaticIntegrityNotificationState` |
| Workbook unavailable classification | `ReloadOutcome.IsWorkbookUnavailable` |
| Render and scan limits | `StyleIntegrityLimits` |
| Schema-version contract for normalization and storage | `LinkFieldSnapshot` and `ExcelLinkInfo` |
| Extensible-storage GUIDs and field serialization | `ExcelLinkSchema` |
| User-facing wording | `UserMessages` |
| Reader exception boundary | `OpenXmlExcelReadService` and `ExcelReadException` |
| Relative-path safety | `RelativePathPolicy` |
| Stored-workbook path and UNC approval policy | `StoredWorkbookPathPolicy` and `INetworkServerApprovalStore` |
| Reload authorization decision | `ReloadDecisionTable` |
| v1-to-v2 field normalization | `LinkMigrationNormalizer` |
| Style facts and decision table | `StyleIntegrityPolicy.cs` and `StyleIntegrityDecisionTable` |
| Pure width, row-height, character, and layout operations | `LayoutMetrics`, `ColumnWidthMath`, `RowHeightMath`, `CharWidth`, `FitContentText`, and `LayoutOps` |
| Native header-row fitting and one unique affected-row diagnostic | `HeaderRowFitter` |
| Fixed-total Fit Content selection and estimate diagnostics | `FitContentText`, `LayoutOps`, `LayoutService`, and `UserMessages` |

## 4. Workbook input boundary

`IExcelReadService` exposes worksheet-name discovery and
`ReadSheetSnapshot`. `OpenXmlExcelReadService` implements that contract with a
temporary-copy guard: it copies the source with bounded retries, opens the copy
read-only, parses it, fingerprints the same copy, and then disposes the guard,
which attempts to delete the copy. Reader failures
surface as `ExcelReadException` and abort the workbook-based mutation before its
transaction starts. For a known affected schedule, a targeted flow may
subsequently offer a separately consented rebuild from the current table.

`WorkbookReadLimits` is the single Open XML resource policy. It checks the source
and ZIP package before DOM loading, bounds XML expansion, worksheet/style/shared
string counts, strings, coordinates, merges, fraction parsing, and the output
array. The rendered output is bounded to 500 rows by 50 columns. Only the
selected worksheet and selected output window are retained.

`RawCellReader` owns OOXML cell, merge, width, style, and cached-value reading.
`CellDisplayFormatter` converts those values into strings close to Excel's
display, including supported numeric, date/time, percentage, unit-literal,
leading-zero, and fraction formats. Unsupported formats use the documented
fallback. CJK-aware measurement is handled later by the Layout library.

Revit entry points validate selected or stored candidates through `PathResolver`
and `StoredWorkbookPathPolicy` before `File.Exists`, timestamps, copying, or
reading. Local paths and UNC paths are distinguished without contacting an
unapproved server. Resolving an unapproved stored UNC candidate for an action
requires a cancel-default approval dialog. Explicitly selecting a UNC workbook
in the file dialog approves its server without a second prompt. Approvals are
stored per Windows user, never in the model.

## 5. Link creation, reload, and repair

`LinkExcelCommand` collects the workbook, worksheet, and optional range. It reads
the workbook before the first model transaction. The mutation creates a method-B
schedule, renders its header with the fixed 1.5 initial width factor, fits header
rows when the row policy requests it, and writes the v2 link entity.
`HeaderTableRenderer` applies displayed text, merged cells, width/height policy,
and the post-regeneration style-integrity check before the mutation can commit.

`ReloadService` resolves the stored absolute and model-relative candidates,
reads the workbook outside the transaction, compares v2 source fingerprints,
and asks `ReloadDecisionTable` plus `ReloadDialogs` to authorize the action. The
render and metadata update then run through `ModelMutation`. `ReloadOutcome`
preserves the identity, transaction, rollback, render, truncation, view-width,
column-count, and fitted-row diagnostic facts needed by the command and Link
Manager. A changed rendered column count is only a hint: fixed ranges, same-count
substitutions, and capped dimensions can evade it.

`TargetedReloadFlow` runs the common sequence: reload, offer the separately
consented current-table rebuild only for a known affected schedule with an
unavailable workbook, rescan and store the final result when the caller requests
that step, and set the targeted flow's final-success flag only when the final
result is `Healthy`. The activation guard deliberately preserves its early
return after an unsuccessful fallback. Callers show reload outcomes only
through `TargetedReloadFlow.ShowReloadOutcome`, which stays silent after a
dispatched fallback; final-qualification warnings and fatal reporting keep
their existing handling. A fatal regeneration stops the flow before any
rescan or later document access. `ReloadAllFlow` processes each resolved
schedule independently, rescans cancelled and failed rows, never offers the
current-table fallback, and stops on fatal regeneration.

`ScheduleRepairCoordinator` applies the explicit repair document policy,
including read-only, detached-copy, and worksharing editability decisions. Its
fallback uses `ScheduleRepairService`: the existing header is captured in a
read transaction and the snapshot is used only when its final status is verified
as `RolledBack`, then restored, validated, and saved in one mutation transaction.
The repair flow qualifies a repair as successful only after a healthy final scan;
a committed mutation can still be downgraded when that scan is not healthy.

## 6. Schedule-integrity subsystem

`Policy/StyleIntegrityPolicy.cs` contains the dependency-free style facts,
results, reason codes, limits, and decision table. A result is `Healthy`,
`Affected`, or `Indeterminate`. The styleless-cell rule requires a complete cell,
all four borders unset, no explicit override, a usable baseline, and an
unambiguous font/size mismatch. Structural incompleteness yields `Indeterminate`.
Once the structural checks pass, a known styleless cell yields `Affected` even
when other observations are unreadable or ambiguous. Without a known styleless
cell, unresolved evidence prevents a `Healthy` result and yields `Indeterminate`.

`ScheduleStyleIntegrityScanner` is the Revit adapter. A standalone scan runs in
a dedicated transaction and only qualifies its result after observing a final
`RolledBack` status. A post-mutation scan regenerates inside the caller's open
transaction and returns `Healthy` only when the resulting header is verifiable.
The manual and automatic scan budgets are owned by `StyleIntegrityLimits`: 256
and 64 schedules respectively, 25,000 cells for one schedule, and 100,000
automatic header cells.

`ScheduleIntegritySessionCache` owns session-only results for each open document.
Its `Rescan` method performs the scanner call and cache write together.
`ScheduleIntegrityDocumentState` owns its result snapshot and delegates retry and
notification transitions to `AutomaticIntegrityNotificationState`. The lifecycle
subscribes to document-open/close and Idling events, scans at open, defers UI to
Idling, retries once after a foreign transaction, and consumes the notification
token only after the dialog returns. Explicit repairs are started only after
user consent.

## 7. Layout subsystem

`ExcelScheduleLink.Layout` contains no Revit or Open XML types. `LayoutMetrics`
holds tuning constants; `CharWidth` measures text including CJK widths;
`ColumnWidthMath` converts and clamps column widths; `FitContentText` selects
headings and measures displayed text; `RowHeightMath` supplies estimated row
heights; and `LayoutOps` implements reset-to-Excel, fixed-total Fit Content,
merge constraints, even distribution, fit-to-total redistribution, clamping,
and validation. Calculated or changed widths are finite and constrained to the
supported range. Validation deliberately preserves a live-equal native width
even when that existing width is outside the supported range.

`LayoutService` reads current header widths, computes candidates through the pure
library, validates before starting a mutation transaction, and uses `ModelMutation`
for widths, body-column synchronization, row fitting, style validation, and
optional link-policy storage. Fit Content measures the rendered header and uses
the current candidate total in the dialog or the current live total in Quick
Layout when that total is supported. Distribute Evenly uses the dialog's current
candidates or, from Quick Layout, the live widths, clamped to the column bounds;
Restore Excel Widths reads the linked worksheet and can change the total. The
dialog displays headings as a letter plus a short heading, reports
protected-value and merged-cell shortages, and shows an estimated comfortable
width; Fit to Total is the explicit widening action. Changed typed widths are
shown in project units and use the 30.5–609.6 mm / 1.20–24.00 in display range.
An unchanged native width can remain outside that range.
The Column Widths dialog edits candidate widths and can invoke a command-owned
Apply callback while remaining open. `LayoutDialogCommand` re-verifies and
authorizes each Apply. Fit-to-text sets header-row heights during Apply and can
shorten or enlarge them; Keep disables fitting and leaves heights alone during
Apply. Reload captures and restores saved heights where possible and reports
rows Revit did not keep. A fitting limitation is a successful action with a
warning, not an automatic rollback. After a successful Apply, Cancel or Discard
drops only unapplied edits and preserves the committed changes. Quick-layout
commands use the same service.

## 8. Persistence contract

`ExcelLinkSchema` stores link metadata on the linked `ViewSchedule`. The shipped
v1 schema GUID is `6fd491d4-0092-45ae-82ad-2d51ce4a0615`. Its frozen fields are
`SchemaVersion`, `RendererVersion`, `AbsolutePath`, `RelativePath`, `PathMode`,
`SheetName`, `RangeMode`, `RangeAddress`, `SourceLastWriteUtcTicks`,
`LastLoadedUtcTicks`, `ColumnWidthMode`, `RowHeightMode`, and `WidthScale`.

Schema v2 is current and uses GUID `4d40b55b-c928-4e46-8c15-2c85dbf510ad`.
It preserves every v1 field and adds `CreatedSourceSha256`,
`LastAcceptedSourceSha256`, and `LastAcceptedSourceSizeBytes`. The v1 GUID and
field set are immutable; a future schema change requires another GUID and an
explicit migration. `ExcelLinkInfo` and `LinkFieldSnapshot` both express the
legacy version 1 and current version 2 contract. `ExcelLinkMigration` invokes
`LinkMigrationNormalizer`, writes v2, and removes v1 inside the caller's open
mutation transaction.

## 9. UI and Revit integration

`RibbonApplication` creates the production ribbon and connects Link, Reload,
Link Manager, Column Widths, and Quick Layout commands. `LinkManagerDialog`
lists linked schedules with six register columns, displays source and integrity
status, and delegates selected reload, reload-all, and repair to the services
above. It has no per-link settings editor or settings-save prompt; Column Widths
owns the column and row reload policies.

WinForms dialogs inherit Revit's DPI awareness and default font behavior. They do
not force a DPI context, form-level font, `AutoScaleMode`, or modal owner. Layouts
are content-sized and controls grow with their text. Shared wording belongs to
`UserMessages`; file paths, worksheet names, and other data values retain their
Unicode content.

## 10. Safety properties

- For workbook-based actions, workbook parsing and path validation happen before
  a model mutation begins.
- Each schedule mutation uses a single transaction. Mutation failures attempt
  rollback; restoration is confirmed only when the final transaction status is
  `TransactionStatus.RolledBack`. Pending or unreadable status leaves rollback
  completion unverified.
- `ModelMutation` classifies native and probe regeneration failures as fatal and
  performs only internal transaction cleanup before returning; its callers stop
  the action without follow-up document reads.
- A fatal regeneration shows one user message, stops further automatic work for
  that document session, and preserves a mutation that had already committed
  before the failure. This is separate from ordinary rollback and from the
  visual question of whether fitted text is fully visible.
- A committed reload or repair can subsequently fail final integrity
  qualification without undoing its committed changes. Bulk reload processes
  schedules independently.
- Bulk reload isolates ordinary per-link failures, but a fatal regeneration
  failure ends the batch before later links are processed.
- The temporary-copy reader does not hold the source workbook open, and formulas
  are not recalculated; cached workbook values are displayed.
- Package expansion, parsed strings, coordinates, schedule scans, and rendered
  dimensions are bounded before untrusted input can create an unbounded model
  operation.
- Unapproved UNC candidates are inspected or presented without a network probe;
  approval is per Windows user rather than extensible-storage data.
- A render, repair, or layout apply can commit only after the post-regeneration
  style check is `Healthy`. A cached session result is never used as the final
  health gate.
- The production manifest registers the ribbon application. Diagnostic command
  classes remain compiled into the Revit DLL but are not registered by the
  production manifest or exposed by the production ribbon. The separate probe
  manifest and Open XML diagnostic payload are not included in the normal
  end-user package.

## 11. Build and repository boundary

`ExcelScheduleLink.sln` contains the five source projects and the unit-test
project. `tools/UiHarness` is built separately. `global.json` requests .NET
SDK 8.0.400 with `latestFeature` roll-forward within 8.0; prerelease SDKs are
disabled. `scripts/build.ps1` builds the solution in Release, runs tests
by default, creates the runtime distribution, and optionally creates a source-free
ZIP with a SHA-256 checksum. Build output, package output, generated PDFs,
screenshots, Revit models, archives, runtime evidence, credentials, and
environment files are outputs or local data, not implementation sources.

`tools/UiHarness` renders the real Link and Column Widths dialogs for layout
checks. Its Link Manager image is a separate hand-maintained preview and is not
acceptance evidence for the production Manager. Native Revit checks remain
necessary for row-height sufficiency, wrapping, rollback, and display scaling.
The row estimator was calibrated on screen at 150% with Arial; very long Latin
prose can wrap one line more than estimated, sheet/PDF checks cover the tested
tables rather than every font or table size, and 500-row performance has not
yet been measured.

The repository history should contain source, tests, scripts, public
documentation, and small reproducible fixtures. Ignoring an output does not
delete it. Public code must remain independent of local evidence and machine-
specific paths.
