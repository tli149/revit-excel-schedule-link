<#
.SYNOPSIS
    Uninstalls Excel Schedule Link for Revit 2024 for the current user.

.DESCRIPTION
    The script removes only paths recorded by the install receipt. If no receipt
    exists, it uses the fixed allowlist of package files below. Unknown files
    are reported and left in place. The deployment directory is removed only
    when it is empty. Revit must be closed before running the script.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-revit2024.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$receiptName = ".ExcelScheduleLink.install-receipt.txt"

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)
    return (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Get-RemainingTreeItems {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootItem = Get-Item -LiteralPath $Root -Force
    $pending = New-Object 'System.Collections.Generic.Queue[System.IO.DirectoryInfo]'
    $pending.Enqueue($rootItem)
    $remaining = New-Object 'System.Collections.Generic.List[System.IO.FileSystemInfo]'
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            $remaining.Add($item)
            if ($item.PSIsContainer -and -not (Test-ReparsePoint -Item $item)) {
                $pending.Enqueue($item)
            }
        }
    }
    return @($remaining)
}

function Assert-ParentPathIsSafe {
    param([Parameter(Mandatory = $true)][string]$Path)

    $parent = [IO.Directory]::GetParent($Path)
    while ($parent -and (Test-Path -LiteralPath $parent.FullName -PathType Container)) {
        $item = Get-Item -LiteralPath $parent.FullName -Force
        if (Test-ReparsePoint -Item $item) {
            throw "Refusing to remove a file beneath a reparse-point directory: $($parent.FullName)"
        }
        $parent = $parent.Parent
    }
}

function Resolve-OwnedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Entry,
        [Parameter(Mandatory = $true)][string]$DeployRoot,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    if ([string]::IsNullOrWhiteSpace($Entry) -or [IO.Path]::IsPathRooted($Entry)) {
        throw "The install receipt contains an invalid path entry: $Entry"
    }
    $full = Get-FullPath -Path (Join-Path $DeployRoot $Entry)
    $deployPrefix = $DeployRoot + [IO.Path]::DirectorySeparatorChar
    $isInsideDeployment = $full.StartsWith($deployPrefix, [StringComparison]::OrdinalIgnoreCase)
    $isManifest = [StringComparer]::OrdinalIgnoreCase.Equals($full, $ManifestPath)
    if (-not $isInsideDeployment -and -not $isManifest) {
        throw "The install receipt points outside the expected deployment: $Entry"
    }
    if ([StringComparer]::OrdinalIgnoreCase.Equals($full, $DeployRoot)) {
        throw "The install receipt names the deployment directory instead of a file."
    }
    return $full
}

function Remove-OwnedFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }
    Assert-ParentPathIsSafe -Path $Path
    $item = Get-Item -LiteralPath $Path -Force
    if (Test-ReparsePoint -Item $item) {
        throw "Refusing to remove a reparse-point file: $Path"
    }
    [IO.File]::SetAttributes($Path, [IO.FileAttributes]::Normal)
    [IO.File]::Delete($Path)
    Write-Host ("[uninstall] Removed: " + $Path) -ForegroundColor Green
    return $true
}

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    throw "Revit is running. Close Revit before uninstalling."
}

$appData = [Environment]::GetEnvironmentVariable("APPDATA")
if ([string]::IsNullOrWhiteSpace($appData)) {
    throw "APPDATA is not available for the current user."
}
$addinsRoot = Get-FullPath -Path (Join-Path $appData "Autodesk\Revit\Addins\2024")
$deployDir = Get-FullPath -Path (Join-Path $addinsRoot "ExcelScheduleLink")
$manifestPath = Get-FullPath -Path (Join-Path $addinsRoot "ExcelScheduleLink.addin")
$receiptPath = Get-FullPath -Path (Join-Path $deployDir $receiptName)

if (Test-Path -LiteralPath $deployDir) {
    $deployItem = Get-Item -LiteralPath $deployDir -Force
    if (-not $deployItem.PSIsContainer) {
        throw "Refusing to operate because the deployment path is not a directory: $deployDir"
    }
    if (Test-ReparsePoint -Item $deployItem) {
        throw "Refusing to operate because the deployment root is a reparse point: $deployDir"
    }
}

$knownPackageFiles = @(
    "DocumentFormat.OpenXml.dll",
    "DocumentFormat.OpenXml.Framework.dll",
    "ExcelScheduleLink.addin.template",
    "ExcelScheduleLink.Contracts.dll",
    "ExcelScheduleLink.Excel.OpenXml.dll",
    "ExcelScheduleLink.Layout.dll",
    "ExcelScheduleLink.Revit2024.dll",
    "LICENSE",
    "README.md",
    "THIRD-PARTY-NOTICES.txt",
    "docs\INSTALL.md",
    "docs\QUICKSTART.md",
    "docs\TROUBLESHOOTING.md",
    "docs\USER_GUIDE.md",
    "docs\images\link-dialog.png",
    "docs\images\link-manager.png"
)

$entries = New-Object 'System.Collections.Generic.List[string]'
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    $receiptLines = [IO.File]::ReadAllLines($receiptPath, [Text.Encoding]::UTF8)
    if ($receiptLines.Count -eq 0) {
        throw "The install receipt is empty and cannot authorize an uninstall: $receiptPath"
    }
    foreach ($line in $receiptLines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            throw "The install receipt contains an empty path entry: $receiptPath"
        }
        $entries.Add($line)
    }
    Write-Host "[uninstall] Using the exact install receipt: $receiptPath" -ForegroundColor Cyan
} else {
    foreach ($name in $knownPackageFiles) {
        $entries.Add($name)
    }
    $entries.Add((Join-Path ".." "ExcelScheduleLink.addin"))
    Write-Host "[uninstall] No receipt found; using the fixed package allowlist." -ForegroundColor Yellow
}

$targets = New-Object 'System.Collections.Generic.List[string]'
foreach ($entry in $entries) {
    $target = Resolve-OwnedPath -Entry $entry -DeployRoot $deployDir -ManifestPath $manifestPath
    if (-not $targets.Contains($target)) {
        $targets.Add($target)
    }
}
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    if (-not $targets.Contains($receiptPath)) {
        $targets.Add($receiptPath)
    }
}

$removed = $false
foreach ($target in $targets) {
    if (Remove-OwnedFile -Path $target) {
        $removed = $true
    }
}

if (Test-Path -LiteralPath $deployDir -PathType Container) {
    $remaining = @(Get-RemainingTreeItems -Root $deployDir)
    if ($remaining.Count -eq 0) {
        Assert-ParentPathIsSafe -Path $deployDir
        [IO.Directory]::Delete($deployDir, $false)
        Write-Host ("[uninstall] Removed empty deployment directory: " + $deployDir) -ForegroundColor Green
        $removed = $true
    } else {
        Write-Host ("[uninstall] Left the deployment directory because it still contains $($remaining.Count) item(s):") -ForegroundColor Yellow
        foreach ($item in $remaining) {
            Write-Host ("  " + $item.FullName)
        }
    }
}

if ($removed) {
    Write-Host "Uninstallation completed for owned files." -ForegroundColor Green
} else {
    Write-Host "No owned installation files were found." -ForegroundColor Yellow
}
Write-Host "User preferences and approved network servers in %APPDATA%\ExcelScheduleLink\ui-settings.txt were kept."
