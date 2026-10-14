# Excel Schedule Link — Installation Guide

This guide is for regular Revit users. You do not need administrator rights or
developer tools. The installer puts everything in your own Windows user
profile.

## Requirements

- Autodesk Revit **2024** on Windows
- The release ZIP `ExcelScheduleLink_v0.9.4.zip` from the project's GitHub
  **Releases** page
- Nothing else — the runtime it needs comes with Revit 2024

> **Get the right download.** On GitHub, use the ZIP attached to the release
> under **Assets**. The **Source code** archives and the green
> **Code → Download ZIP** button contain the source, not the add-in.

## About the warnings Windows may show

The 0.9.4 pilot DLLs and PowerShell scripts are **not digitally signed**. Your
Windows settings may show an unknown publisher, SmartScreen may warn you, or
PowerShell may refuse to run the scripts.

- Use only the ZIP from this project's GitHub Releases page. You can compare its
  SHA-256 checksum with the `SHA256SUMS.txt` file published next to it:
  ```powershell
  Get-FileHash .\ExcelScheduleLink_v0.9.4.zip -Algorithm SHA256
  ```
- Do not turn off SmartScreen or your company's security policies to install
  it. On a managed office computer, ask your IT team to approve it.

## Step 1 — Unblock the ZIP (do this first)

Windows marks files downloaded from the internet or e-mail as "blocked". If
you extract the ZIP while it is blocked, Revit will quietly refuse to load the
add-in later.

1. Right-click the ZIP → **Properties**.
2. At the bottom of the **General** tab, if you see *"This file came from
   another computer..."*, tick **Unblock**.
3. Click **OK**.

No Unblock checkbox? The file is already fine.

## Step 2 — Extract

Extract the ZIP to a folder you plan to keep, for example
`C:\Tools\ExcelScheduleLink`. You can delete that folder after installing; the
installer copies what it needs.

## Step 3 — Optional: pre-flight check

In the extracted folder, right-click `scripts\run-preflight-check.ps1` →
**Run with PowerShell**. This checks that Revit 2024 is installed, that your
add-ins folder is writable, and that the files are not blocked.

Every line should start with `[PASS]` or `[INFO]`, and the last line should say
`=== preflight: all required checks PASS ===`. `[INFO]` lines are just notes;
for example, developer build tools are not needed for the release ZIP. If you
see `[FAIL]`, the line says what is wrong — see
[TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Step 4 — Install

Close Revit. Then right-click `scripts\install-revit2024.ps1` → **Run with PowerShell**.

The script copies the add-in to your user add-ins folder, writes the Revit
manifest, unblocks only the files it copied, and records what it installed. The
uninstaller uses that record to remove exactly those files. The installer
refuses to run while Revit is open.

Command-line version (same thing):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-revit2024.ps1
```

`-ExecutionPolicy Bypass` only applies to this one command. It does not change
your PowerShell settings and does not give the script administrator rights. If
your organization blocks it, ask your IT team.

## Step 5 — Check

Start Revit 2024 and open any project. Look for the **Excel Schedule Link**
ribbon tab with two panels:

- **Excel Links** — Link Excel, Reload Link, Link Manager
- **Layout** — Column Widths…, Quick Layout

No tab? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md), sections 1–2.

## Updating to a new version

1. Close Revit.
2. Run the old version's `scripts\uninstall-revit2024.ps1` (optional — the new
   installer also replaces the files).
3. Unblock and extract the new ZIP, then run its installer.

Links you already created stay in your models and keep working.

## Uninstall

Close Revit, then right-click `scripts\uninstall-revit2024.ps1` → **Run with PowerShell**.

It removes the manifest and exactly the files recorded by the installer. If you
put other files in the add-in folder, they are left in place and listed, and
the folder is kept.

No registry entries are created. Links inside your `.rvt` models stay there as
inactive data; the tables themselves stay visible.

Your preferences file `%AppData%\ExcelScheduleLink\ui-settings.txt` is not
removed. It holds window sizes, your last Link Excel choices, and the network
servers you approved. Delete the `%AppData%\ExcelScheduleLink\` folder by hand
if you want a completely clean uninstall.

## What gets installed where

| Item | Location |
|---|---|
| Add-in files (DLLs, manifest template, install record) | `%AppData%\Autodesk\Revit\Addins\2024\ExcelScheduleLink\` |
| Revit manifest | `%AppData%\Autodesk\Revit\Addins\2024\ExcelScheduleLink.addin` |
| Your preferences (window sizes, last Link Excel choices, approved network servers) | `%AppData%\ExcelScheduleLink\ui-settings.txt` |
| Temporary copies while reading Excel | `%TEMP%\ExcelScheduleLink\` (deleted automatically) |

The add-in has no online features and only **reads** your Excel files through a
temporary copy, so the workbook can stay open in Excel. It contacts a network
server only when a workbook is stored there, and asks you first — see
[USER_GUIDE.md](USER_GUIDE.md#workbooks-on-a-network-server).
