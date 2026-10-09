# Guide for coding agents

This file is the public repository guide for Excel Schedule Link. A narrower
`AGENTS.md` next to a changed file takes precedence if one is added later.

## Start from evidence

Before editing:

1. Check Git status and preserve unrelated user changes.
2. Read `README.md`, `ARCHITECTURE.md`, and the relevant developer or user
   documentation.
3. Inspect the actual implementation path; do not treat a proposal or an old
   distribution as proof of current behavior.
4. State any assumption that would materially change scope.

The public snapshot does not include `design/`, `results/`, or
`docs/validation/`. Source, tests, tools, remaining scripts, project files,
and public documentation must not require those excluded paths at build or
test time.

## Sources of truth

Normal Git history should contain:

- source and project files under `src`;
- unit tests under `tests`;
- developer tools under `tools`, excluding their generated output;
- build, install, uninstall, and preflight scripts;
- reproducible test-workbook generators and small fixtures;
- architecture, developer, user, security, contribution, and release
  documentation.

Normal Git history should not contain:

- `bin`, `obj`, `dist`, generated PDFs, UI-harness output, DLLs, or PDBs;
- Revit models, ZIP archives, screenshots, temporary review bundles, or bulk
  runtime evidence;
- local agent state, credentials, secrets, or environment files.

Ignored files are not to be deleted merely because Git ignores them. A
distribution is a deliverable, not the canonical source implementation.

## Architectural invariants

- `ExcelScheduleLink.Contracts` stays dependency-free and owns shared DTOs
  plus `IExcelReadService`.
- `DocumentFormat.OpenXml` remains behind the `Excel.OpenXml` implementation
  boundary. The Revit add-in must not reference that package directly.
- `ExcelScheduleLink.Layout` remains pure layout math without Revit or Open XML
  types.
- Workbook parsing happens before opening a Revit transaction.
- Revit model mutations for one user action are transactional. A failed link,
  reload, render, or layout apply must not leave a partial model change.
- The shipped extensible-storage schema GUID and field set are frozen. A
  schema change requires a new GUID and an explicit migration.
- Preserve the established high-DPI behavior: dialogs inherit Revit DPI
  awareness and are content-sized. Do not add forced DPI context, form font,
  `AutoScaleMode`, or owner behavior without real visual verification.
- `UserMessages.cs` is the single owner for shared user-facing wording. Do not
  duplicate policy text in callers.

## Design principle

Each rule should live in one place (A Philosophy of Software Design). Before
copying logic or adding a catch, flag, or branch, search for another place that
already encodes the same knowledge and consolidate or propose consolidation.
When reporting a change, answer briefly:

1. What do callers no longer need to know?
2. Did the number of places to edit for this rule go down, and were other
   entry points covered?
3. Why is a new abstraction preferable to extending an existing module?

"Improved maintainability" is not an answer by itself.

## Build and verification

- Prefer the narrowest relevant unit tests, then the solution-level test suite
  when the required SDK is available.
- Use `tools/UiHarness` for dialog-rendering checks; it is a developer tool and
  is intentionally not part of `ExcelScheduleLink.sln`.
- Never claim a clean source build merely because existing DLLs or `dist`
  files are present.
- `scripts/build.ps1` builds `ExcelScheduleLink.sln` in Release, runs unit tests
  by default, and regenerates the runtime package. `-Zip` adds the public
  source-free ZIP and its checksum file.
- Revit acceptance checks must use a disposable model, never a production RVT.
- Finish code changes with relevant test results, `git diff --check`, and a
  clear statement of anything that could not be verified locally.

## Git workflow

- Check the repository root, branch, `HEAD`, status, and outer/nested
  repository boundaries read-only before any Git mutation.
- Preserve unrelated changes and do not stage generated output or excluded
  evidence.
- Stage explicit paths after reviewing the exact scope. Do not use a broad
  add-all command without first reviewing untracked and ignored boundaries.
- Keep commits small enough for another maintainer to understand and reverse.
- Keep architectural decisions, schema contracts, acceptance criteria, and
  release limitations in versioned public documentation.
- Do not delete evidence, install dependencies, publish, or push without the
  appropriate user authorization.
