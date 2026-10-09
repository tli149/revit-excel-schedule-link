# Development guide

This document is for maintainers and contributors working on the Revit 2024
add-in. End-user installation and feature instructions are in the bundled user
documents.

## Requirements and source build

The solution targets:

- .NET Framework 4.8 for the Revit add-in;
- .NET Standard 2.0 for contracts, Excel reading, and layout math;
- .NET 8 for unit tests.

The repository pins the .NET 8 SDK feature band in `global.json`. `scripts/build.ps1`
looks for a usable SDK on `PATH`, then the per-user SDK under
`%LOCALAPPDATA%\Microsoft\dotnet`, and then Visual Studio MSBuild via
`vswhere`. A local Revit 2024 installation is used when both API DLLs are
present. Otherwise the Revit project uses the pinned Nice3point reference
packages; Revit API assemblies are never copied into the product output.

Build and test the solution, then create the public package with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Zip
```

Unit tests run by default. `-SkipTests` is available for an intentional
build-only run. `-RevitApiDir` can point to a Revit 2024 installation or to a
nonexistent directory to exercise the NuGet reference fallback. The script
creates a runtime-only `dist\ExcelScheduleLink` tree and, with `-Zip`, a
source-free ZIP at the repository root plus `SHA256SUMS.txt`.

The ZIP contains the production DLLs, the production manifest template, the
installer scripts, the root package README, the user guides, pilot feedback,
documentation images, the MIT license, and third-party notices. The build
script excludes source, test, build-intermediate, and diagnostic deployment
files. It also checks the staged files and ZIP for personal or builder-specific
paths. The ZIP is not described as reproducible unless two clean builds have
been compared explicitly.

## Project layout

| Path | Purpose |
|---|---|
| `src/ExcelScheduleLink.Contracts` | Shared models, policies, and service contracts with no external dependencies |
| `src/ExcelScheduleLink.Excel.OpenXml` | Open XML workbook reading and display formatting |
| `src/ExcelScheduleLink.Layout` | Revit-free width and height calculations |
| `src/ExcelScheduleLink.Revit2024` | Ribbon, dialogs, Revit transactions, rendering, reload, persistence, and QA probes |
| `src/ExcelScheduleLink.Probe.OpenXmlPayload` | Developer-only payload for an isolated Open XML loading probe |
| `tests/ExcelScheduleLink.Excel.OpenXml.Tests` | Unit and workbook-reader tests |
| `tools/UiHarness` | Renders real WinForms dialogs outside Revit for automated layout checks |
| `scripts` | Build, package, install, uninstall, and end-user preflight scripts |
| `testdata` | Small workbook fixtures and their generators |

The UI harness is deliberately outside `ExcelScheduleLink.sln`. It compiles
the real dialog source and reports controls that fall outside their parent
client areas.

## What is rendered

Workbook values, cached formula results, common number formats, text, Boolean
and error values, merged cells, and basic Excel-derived column widths are
rendered into the schedule header. The reader uses the displayed value cached
in the workbook; it does not recalculate formulas. The render window is limited
to 500 rows by 50 columns, and larger input is accompanied by a truncation
notice.

Fonts, colors, borders, row-height tools, images, charts, conditional
formatting, automatic file watching, and write-back to Excel are not currently
implemented. Revit remains responsible for schedule appearance settings such
as font size and grid display.

## Revit constraints

The add-in uses a multi-category schedule with a hidden body field and renders
the linked table in the header because the Revit API does not provide suitable
writable body content for this table. Revit may show one `A` column strip below
the table while the schedule view is being edited. That editor-only strip does
not appear on sheets or in printed/PDF output. Individual column widths are
edited in the schedule view; a placed schedule exposes only its overall sheet
width.

Dialogs inherit Revit's DPI awareness and are content-sized. Do not add a
forced DPI context, form-level font, `AutoScaleMode`, or owner behavior without
both UI-harness rendering and real Revit/high-DPI verification.

Each workbook read completes before a Revit transaction starts. Each model
mutation is one transaction, and a failed link, reload, render, or layout
operation must not leave a partial model change. Style integrity is checked
after regeneration before a mutation can commit; an unsafe or indeterminate
result is not reported as a successful commit.

## Stored link data

Link metadata is stored as an extensible-storage entity on the linked
`ViewSchedule`. The schema GUID and v1 field set are frozen:

```text
6fd491d4-0092-45ae-82ad-2d51ce4a0615
```

The entity records the schema and renderer versions, absolute and
model-relative workbook paths, worksheet and range, source timestamps, reload
modes, and width scale. Later identity fields protect reload decisions when
the workbook bytes change. A schema change requires a new GUID and an explicit
migration.

`StoredWorkbookPathPolicy` is the single gate for stored and resolved workbook
paths. It rejects unsupported or unsafe path forms before existence checks,
timestamps, copying, hashing, or Open XML access. Approved network servers are
kept per Windows user in `%APPDATA%\ExcelScheduleLink\ui-settings.txt`, not in
the model.

`WorkbookReadLimits` is the single owner of workbook resource limits. It
checks source size, ZIP entry count and expansion, XML character limits,
worksheet structures, strings, coordinates, merges, styles, and the bounded
render array before the Open XML document is opened.

## Installation and security boundaries

The installer writes only to the current user's Revit 2024 Addins directory.
It records every copied file in `.ExcelScheduleLink.install-receipt.txt`,
removes Mark-of-the-Web only from files owned by that install, and refuses
reparse-point roots. Uninstall uses the receipt or a fixed package allowlist,
leaves unknown files in place, and removes the deployment directory only when
it is empty. Per-user UI preferences and approved network servers are kept.

QA and probe command classes are compiled into the Revit DLL so the same code
can be exercised by maintainers. They are not registered by the production
manifest, and the probe manifest and payload are not shipped to end users.

## Developer troubleshooting

If the source build cannot find an SDK, confirm the pinned feature band with:

```powershell
dotnet --list-sdks
```

If the machine has no Revit installation, pass a nonexistent `-RevitApiDir` to
exercise the pinned reference-package fallback. If an installed package does
not load, check for Mark-of-the-Web on the ZIP and use the installer or the
preflight script to inspect the deployed files. Do not diagnose a source build
from stale DLLs in `bin` or `dist`; rebuild the relevant project.

For dialog layout changes, build `tools\UiHarness\UiHarness.csproj` and run
the harness against a temporary output directory. Inspect the generated text
reports and confirm that no visible control is marked `CLIPPED`. Real Revit
acceptance still requires a disposable model and cannot be replaced by the
harness.
