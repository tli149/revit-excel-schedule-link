# UI Harness (developer tool)

Renders the add-in's WinForms dialogs **outside Revit** for automated layout
checks. It is not part of the add-in, is not in `ExcelScheduleLink.sln`, and is
never shipped to end users.

## What it does

For each case it writes two files to the output folder:

- `<case>.png` — a picture of the dialog (`Control.DrawToBitmap`).
- `<case>.txt` — every control's bounds vs. its parent's client area; anything
  outside is marked `<<< CLIPPED`.

Cases: Column Widths with 1 / 3 / 50 columns, mm and inch, plus font scale
1.5x and 2.0x; Link Excel at normal and 2.0x font scale; and the Link Manager
sample preview. These runs stand in for a high-DPI display (the form keeps its
designed size while the controls grow — a harsher test than the real thing).

The release documentation screenshots are `link-dialog.png` and
`link-manager.png`. They use neutral sample paths. The production Link Manager
requires a live Revit `Document`, so its screenshot is a harness-only preview
with representative status and integrity rows; no preview code is shipped.

## Run

```powershell
dotnet build tools\UiHarness\UiHarness.csproj -c Release
tools\UiHarness\bin\Release\net48\UiHarness.exe C:\temp\ui-out
```

Then check that no `.txt` contains `CLIPPED` (ignore lines marked `[hidden]` —
those are WinForms' internal, invisible scrollbars):

```powershell
Select-String -Path C:\temp\ui-out\*.txt -Pattern CLIPPED | Where-Object { $_.Line -notmatch '\[hidden\]' }
```

## Notes

- It compiles the real sources (`..\..\src\...`) instead of copying them, so it
  always renders what ships. If a dialog gains a new dependency, add that file
  to the `Compile Include` list in `UiHarness.csproj`.
- `RevitAPI.dll` is referenced only so `LengthUnitHelper` compiles; unit
  conversion falls back gracefully when Revit is not running.
- Add a new dialog by constructing it in `Program.cs` and calling `Render`.
