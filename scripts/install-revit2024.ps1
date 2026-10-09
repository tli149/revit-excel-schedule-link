<#
.SYNOPSIS
    Installs Excel Schedule Link for Revit 2024 for the current user.

.DESCRIPTION
    The deployment directory is %APPDATA%\Autodesk\Revit\Addins\2024\ExcelScheduleLink.
    The installer records the exact package files it copied in an install receipt.
    It unblocks only those copied files and the generated production manifest.
    Revit must be closed before running the script.

.PARAMETER SourceDir
    Distribution directory containing ExcelScheduleLink.Revit2024.dll.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\install-revit2024.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDir = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$receiptName = ".ExcelScheduleLink.install-receipt.txt"

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)
    return (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-SafeTreeFiles {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootItem = Get-Item -LiteralPath $Root -Force
    if (Test-ReparsePoint -Item $rootItem) {
        throw "Refusing to use a source or deployment root that is a reparse point: $Root"
    }

    $pending = New-Object 'System.Collections.Generic.Queue[System.IO.DirectoryInfo]'
    $pending.Enqueue($rootItem)
    $files = New-Object 'System.Collections.Generic.List[System.IO.FileInfo]'
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            if (Test-ReparsePoint -Item $item) {
                throw "Refusing to copy through a reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $pending.Enqueue($item)
            } else {
                $files.Add($item)
            }
        }
    }
    return @($files)
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )
    return $Path.Substring($Root.Length + 1)
}

function Test-HasZoneIdentifier {
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        return $null -ne (Get-Item -LiteralPath $Path -Stream "Zone.Identifier" -ErrorAction SilentlyContinue)
    } catch {
        return $false
    }
}

# ---- Locate and validate the distribution ----
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $candidates = @(
        (Join-Path $repoRoot "dist\ExcelScheduleLink"),
        (Join-Path $scriptDir "dist\ExcelScheduleLink"),
        (Join-Path $scriptDir "ExcelScheduleLink")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "ExcelScheduleLink.Revit2024.dll") -PathType Leaf) {
            $SourceDir = $candidate
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    throw "A distribution directory was not found. Build the package first or supply -SourceDir."
}
$sourceRoot = Get-FullPath -Path $SourceDir
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "The distribution directory does not exist: $sourceRoot"
}
$sourceFiles = @(Get-SafeTreeFiles -Root $sourceRoot)
if (-not ($sourceFiles | Where-Object { $_.Name -eq "ExcelScheduleLink.Revit2024.dll" })) {
    throw "The distribution directory does not contain ExcelScheduleLink.Revit2024.dll: $sourceRoot"
}
Write-Host "[install] Source: $sourceRoot" -ForegroundColor Cyan

# ---- Prevent writes while Revit holds deployment files ----
if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    throw "Revit is running. Close Revit before installing."
}

$appData = [Environment]::GetEnvironmentVariable("APPDATA")
if ([string]::IsNullOrWhiteSpace($appData)) {
    throw "APPDATA is not available for the current user."
}
$addinsRoot = Join-Path $appData "Autodesk\Revit\Addins\2024"
$deployDir = Join-Path $addinsRoot "ExcelScheduleLink"
$receiptPath = Join-Path $deployDir $receiptName

if (Test-Path -LiteralPath $deployDir) {
    $deployItem = Get-Item -LiteralPath $deployDir -Force
    if (-not $deployItem.PSIsContainer -or (Test-ReparsePoint -Item $deployItem)) {
        throw "Refusing to install because the deployment root is not a normal directory: $deployDir"
    }
    # Existing reparse points are not package-owned and must not be followed.
    [void](Get-SafeTreeFiles -Root $deployDir)
}
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null

# ---- Copy the package and remember exactly what was copied ----
$copiedRelativePaths = New-Object 'System.Collections.Generic.List[string]'
foreach ($sourceFile in $sourceFiles) {
    $relative = Get-RelativePath -Root $sourceRoot -Path $sourceFile.FullName
    if ($relative -eq $receiptName) {
        throw "The source package contains the reserved install receipt name: $relative"
    }
    $destination = Join-Path $deployDir $relative
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    [IO.File]::Copy($sourceFile.FullName, $destination, $true)
    $copiedRelativePaths.Add($relative)
}
Write-Host "[install] Copied $($copiedRelativePaths.Count) package file(s) to $deployDir" -ForegroundColor Green

# ---- Generate the production manifest ----
$template = Join-Path $deployDir "ExcelScheduleLink.addin.template"
if (-not (Test-Path -LiteralPath $template -PathType Leaf)) {
    throw "The production manifest template was not copied: $template"
}
$content = [IO.File]::ReadAllText($template, [Text.Encoding]::UTF8)
$content = $content.Replace("__ESL_DEPLOY_DIR__", $deployDir)
$addinPath = Join-Path $addinsRoot "ExcelScheduleLink.addin"
New-Item -ItemType Directory -Force -Path $addinsRoot | Out-Null
[IO.File]::WriteAllText($addinPath, $content, (New-Object Text.UTF8Encoding($true)))
$copiedRelativePaths.Add((Join-Path ".." "ExcelScheduleLink.addin"))
Write-Host "[install] Wrote production manifest: $addinPath" -ForegroundColor Green

# ---- Remove Mark-of-the-Web only from files owned by this install ----
$copiedAbsolutePaths = New-Object 'System.Collections.Generic.List[string]'
foreach ($relative in $copiedRelativePaths) {
    $full = Get-FullPath -Path (Join-Path $deployDir $relative)
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        $copiedAbsolutePaths.Add($full)
    }
}
$blocked = @($copiedAbsolutePaths | Where-Object { Test-HasZoneIdentifier -Path $_ })
if ($blocked.Count -gt 0) {
    Write-Host "[install] Removing Mark-of-the-Web from $($blocked.Count) copied file(s)." -ForegroundColor Yellow
    foreach ($path in $blocked) {
        Unblock-File -LiteralPath $path
    }
}
$stillBlocked = @($copiedAbsolutePaths | Where-Object { Test-HasZoneIdentifier -Path $_ })
if ($stillBlocked.Count -gt 0) {
    Write-Host "[install] Warning: some copied files remain blocked:" -ForegroundColor Red
    foreach ($path in $stillBlocked) { Write-Host ("  " + $path) }
} else {
    Write-Host "[install] Mark-of-the-Web check completed for installed files." -ForegroundColor Green
}

# The receipt is intentionally not listed in itself. It is installer-owned and
# the uninstall script removes it after it has read the exact file list.
$receiptLines = @($copiedRelativePaths)
[IO.File]::WriteAllLines($receiptPath, $receiptLines, (New-Object Text.UTF8Encoding($false)))
Write-Host "[install] Install receipt: $receiptPath" -ForegroundColor Green

Write-Host ""
Write-Host "Installation completed for the production manifest." -ForegroundColor Green
Write-Host "Start Revit 2024 and use the Excel Schedule Link ribbon tab."
