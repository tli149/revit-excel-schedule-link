<#
.SYNOPSIS
    Checks the current user's prerequisites for the Revit 2024 package.

.DESCRIPTION
    Requires no administrator rights. The only write is a temporary probe file
    in the current user's Revit 2024 Addins folder. The check covers Revit 2024,
    the writable Addins folder, .NET Framework 4.8, and Mark-of-the-Web on a
    local distribution. A .NET SDK or MSBuild is reported for developers, but
    is not required when using the prebuilt package.

.PARAMETER RevitApiDir
    Revit 2024 installation folder. Default:
    C:\Program Files\Autodesk\Revit 2024

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\run-preflight-check.ps1
#>
[CmdletBinding()]
param(
    [string]$RevitApiDir = "C:\Program Files\Autodesk\Revit 2024"
)

$ErrorActionPreference = "Continue"
$failCount = 0
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

function Write-Check {
    param([bool]$Ok, [string]$Label, [string]$Detail = "")
    if ($Ok) {
        Write-Host ("[PASS] " + $Label) -ForegroundColor Green
    } else {
        Write-Host ("[FAIL] " + $Label) -ForegroundColor Red
        $script:failCount++
    }
    if ($Detail) { Write-Host ("       " + $Detail) }
}

function Test-DotNetSdk {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }
    try {
        $sdkLines = @(& $Path --list-sdks 2>$null)
        return $LASTEXITCODE -eq 0 -and $sdkLines.Count -gt 0
    } catch {
        return $false
    }
}

Write-Host "=== Excel Schedule Link preflight check ===" -ForegroundColor Cyan
Write-Host ("Run time: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Write-Host ("Windows : " + [System.Environment]::OSVersion.VersionString)
Write-Host ""

# ---- 1. Revit 2024 ----
$revitExe = Join-Path $RevitApiDir "Revit.exe"
Write-Check (Test-Path -LiteralPath $revitExe -PathType Leaf) "Revit 2024 is installed (Revit.exe)" $revitExe
$apiOk = (Test-Path -LiteralPath (Join-Path $RevitApiDir "RevitAPI.dll") -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $RevitApiDir "RevitAPIUI.dll") -PathType Leaf)
Write-Check $apiOk "Revit 2024 API files are present" $RevitApiDir
if ($apiOk) {
    $version = (Get-Item -LiteralPath (Join-Path $RevitApiDir "RevitAPI.dll")).VersionInfo.FileVersion
    Write-Host ("       RevitAPI.dll FileVersion: " + $version)
}

# ---- 2. Developer build tools (informational only) ----
$dotnetPath = $null
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand -and (Test-DotNetSdk -Path $dotnetCommand.Source)) {
    $dotnetPath = $dotnetCommand.Source
}
if (-not $dotnetPath) {
    $localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-DotNetSdk -Path $localDotnet) { $dotnetPath = $localDotnet }
}

$msbuildPath = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
    $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
}
$buildDetail = ""
if ($dotnetPath) { $buildDetail += ("dotnet SDK: " + $dotnetPath + "  ") }
if ($msbuildPath) { $buildDetail += ("MSBuild: " + $msbuildPath) }
if ($buildDetail) {
    Write-Host ("[INFO] Developer build tools found: " + $buildDetail.Trim()) -ForegroundColor Cyan
} else {
    Write-Host "[INFO] .NET SDK/MSBuild not found; not needed for the prebuilt package." -ForegroundColor Cyan
}

# ---- 3. User Addins folder write access ----
$addinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$writeOk = $false
$probe = Join-Path $addinsRoot (".esl_write_probe_" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Force -Path $addinsRoot | Out-Null
    [IO.File]::WriteAllText($probe, "probe")
    [IO.File]::Delete($probe)
    $writeOk = $true
} catch {
    if (Test-Path -LiteralPath $probe -PathType Leaf) {
        try { [IO.File]::Delete($probe) } catch { }
    }
}
Write-Check $writeOk "Revit 2024 Addins folder is writable" $addinsRoot

# ---- 4. .NET Framework 4.8 ----
$net48 = $false
try {
    $release = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -ErrorAction Stop).Release
    $net48 = ($release -ge 528040)
    Write-Check $net48 ".NET Framework 4.8 or later is available" ("Release=" + $release)
} catch {
    Write-Check $false ".NET Framework 4.8 or later is available" "The framework release value could not be read."
}

# ---- 5. Mark-of-the-Web on the local distribution ----
$distCandidates = @(
    (Join-Path $repoRoot "dist\ExcelScheduleLink"),
    (Join-Path $scriptDir "dist\ExcelScheduleLink"),
    (Join-Path $scriptDir "ExcelScheduleLink")
) | Where-Object { Test-Path -LiteralPath (Join-Path $_ "ExcelScheduleLink.Revit2024.dll") -PathType Leaf }
if ($distCandidates) {
    $distDir = $distCandidates | Select-Object -First 1
    $blocked = @(Get-ChildItem -LiteralPath $distDir -Recurse -File | Where-Object {
        Get-Item -LiteralPath $_.FullName -Stream "Zone.Identifier" -ErrorAction SilentlyContinue
    })
    Write-Check ($blocked.Count -eq 0) "Distribution files are not blocked (Mark of the Web)" $distDir
    if ($blocked.Count -gt 0) {
        Write-Host ("       " + $blocked.Count + " file(s) are blocked.") -ForegroundColor Yellow
        Write-Host "       install-revit2024.ps1 removes the block from files it installs."
        Write-Host ("       Manual option: Get-ChildItem '" + $distDir + "' -Recurse | Unblock-File")
    }
} else {
    Write-Host "[INFO] No distribution folder was found; Mark-of-the-Web check skipped." -ForegroundColor Cyan
}

# ---- Result ----
Write-Host ""
if ($failCount -eq 0) {
    Write-Host "=== preflight: all required checks PASS ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host ("=== preflight: FAIL " + $failCount + " check(s) ===") -ForegroundColor Red
    exit 1
}
