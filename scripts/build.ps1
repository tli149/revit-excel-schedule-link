<#
.SYNOPSIS
    Builds Excel Schedule Link and creates the current distribution package.

.DESCRIPTION
    Builds ExcelScheduleLink.sln in Release by default, runs the unit tests,
    and regenerates dist\ExcelScheduleLink with the runtime files required by
    the Revit 2024 add-in. The script works with a .NET SDK on PATH, a local
    per-user .NET SDK, or Visual Studio MSBuild discovered with vswhere.

.PARAMETER RevitApiDir
    Directory containing RevitAPI.dll and RevitAPIUI.dll. The default is the
    Revit 2024 installation under the machine Program Files directory.

.PARAMETER Configuration
    Build configuration. The default is Release.

.PARAMETER SkipTests
    Skips the unit-test run after the solution build.

.PARAMETER Zip
    Creates ExcelScheduleLink_v<Version>.zip at the repository root.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Zip

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -RevitApiDir "D:\Autodesk\Revit 2024" -Zip
#>
[CmdletBinding()]
param(
    [string]$RevitApiDir = "",
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$solutionPath = Join-Path $repoRoot "ExcelScheduleLink.sln"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$script:generatedTreeAllowedRoots = @(
    [IO.Path]::GetFullPath((Join-Path $repoRoot "dist\ExcelScheduleLink")).TrimEnd([char[]]@('\', '/'))
)

function Register-GeneratedTreeRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/'))
    $tempPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $root.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated staging roots must be beneath the system temporary directory: $Path"
    }
    if ($script:generatedTreeAllowedRoots -notcontains $root) {
        $script:generatedTreeAllowedRoots += $root
    }
}

function Get-DefaultRevitApiDir {
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    if ([string]::IsNullOrWhiteSpace($programFiles)) {
        throw "The machine Program Files directory could not be determined. Supply -RevitApiDir explicitly."
    }
    return (Join-Path $programFiles "Autodesk\Revit 2024")
}

function Test-DotNetSdk {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    try {
        $sdkLines = @(& $Path --list-sdks 2>$null)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            return $false
        }
        foreach ($line in $sdkLines) {
            if (-not [string]::IsNullOrWhiteSpace([string]$line)) {
                return $true
            }
        }
    } catch {
        return $false
    }

    return $false
}

function Get-CommandPath {
    param([Parameter(Mandatory = $true)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        return $null
    }
    if ($command.Path) {
        return $command.Path
    }
    return $command.Source
}

function Find-DotNetSdk {
    $pathCandidate = Get-CommandPath -Name "dotnet"
    $localAppData = [Environment]::GetEnvironmentVariable("LOCALAPPDATA")
    $localCandidate = $null
    if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
        $localCandidate = Join-Path $localAppData "Microsoft\dotnet\dotnet.exe"
    }

    if ($pathCandidate) {
        if (Test-DotNetSdk -Path $pathCandidate) {
            return $pathCandidate
        }
        Write-Host "[toolchain] dotnet on PATH has no usable SDK; checking the per-user SDK." -ForegroundColor Yellow
    }

    if ($localCandidate -and ($localCandidate -ne $pathCandidate)) {
        if (Test-DotNetSdk -Path $localCandidate) {
            return $localCandidate
        }
    }

    return $null
}

function Find-MSBuild {
    $roots = @(
        [Environment]::GetEnvironmentVariable("ProgramFiles(x86)"),
        [Environment]::GetEnvironmentVariable("ProgramFiles")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($root in $roots) {
        $vswhere = Join-Path $root "Microsoft Visual Studio\Installer\vswhere.exe"
        if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
            continue
        }

        $matches = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" 2>$null)
        foreach ($match in $matches) {
            $candidate = ([string]$match).Trim()
            if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                return $candidate
            }
        }
    }

    return $null
}

function Find-VsTest {
    $roots = @(
        [Environment]::GetEnvironmentVariable("ProgramFiles(x86)"),
        [Environment]::GetEnvironmentVariable("ProgramFiles")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($root in $roots) {
        $vswhere = Join-Path $root "Microsoft Visual Studio\Installer\vswhere.exe"
        if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
            continue
        }

        $matches = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" 2>$null)
        foreach ($match in $matches) {
            $candidate = ([string]$match).Trim()
            if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                return $candidate
            }
        }
    }

    return $null
}

function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Directory.Build.props was not found: $Path"
    }

    try {
        $xml = [xml](Get-Content -Encoding UTF8 -Raw -LiteralPath $Path)
    } catch {
        throw "Directory.Build.props is not valid XML: $Path"
    }

    foreach ($group in @($xml.Project.PropertyGroup)) {
        if ($group.Version -and -not [string]::IsNullOrWhiteSpace([string]$group.Version)) {
            return ([string]$group.Version).Trim()
        }
    }

    throw "The Version property is missing from Directory.Build.props."
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required file is missing: $Source"
    }
    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    Copy-Item -LiteralPath $Source -Destination $DestinationDirectory -Force
}

function Copy-RequiredFileAs {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required file is missing: $Source"
    }
    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-EndUserDocumentation {
    param([Parameter(Mandatory = $true)][string]$DestinationRoot)

    $sourceDocs = Join-Path $repoRoot "docs"
    $destinationDocs = Join-Path $DestinationRoot "docs"
    New-Item -ItemType Directory -Force -Path $destinationDocs | Out-Null

    $documentationNames = @(
        "QUICKSTART.md",
        "INSTALL.md",
        "USER_GUIDE.md",
        "TROUBLESHOOTING.md",
        "PILOT_FEEDBACK.md"
    )
    foreach ($name in $documentationNames) {
        Copy-RequiredFile -Source (Join-Path $sourceDocs $name) -DestinationDirectory $destinationDocs
    }

    # Copy every available image referenced by the bundled Markdown guides.
    $imageSource = Join-Path $sourceDocs "images"
    $imageDestination = Join-Path $destinationDocs "images"
    if (Test-Path -LiteralPath $imageSource -PathType Container) {
        foreach ($sourceFile in @(Get-ChildItem -LiteralPath $imageSource -Recurse -File)) {
            $relative = $sourceFile.FullName.Substring($imageSource.Length + 1)
            $destination = Join-Path $imageDestination $relative
            $destinationDirectory = Split-Path -Parent $destination
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination -Force
        }
    }
}

function Remove-GeneratedTree {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = [IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
    if ($script:generatedTreeAllowedRoots -notcontains $root) {
        throw "Refusing to remove a path that is not an exact generated distribution or staging root: $Path"
    }

    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        return
    }

    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove a generated tree whose root is a reparse point: $root"
    }

    # Remove only the exact generated dist tree or this script's registered ZIP
    # staging tree. Walk it explicitly, reject every reparse point, and delete
    # known child files/directories one at a time; this is not an unrestricted
    # recursive delete operation.
    $pending = New-Object 'System.Collections.Generic.Queue[System.IO.DirectoryInfo]'
    $directories = New-Object 'System.Collections.Generic.List[System.IO.DirectoryInfo]'
    $pending.Enqueue($rootItem)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to descend into a reparse point while removing generated output: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $directories.Add($item)
                $pending.Enqueue($item)
            } else {
                [IO.File]::SetAttributes($item.FullName, [IO.FileAttributes]::Normal)
                [IO.File]::Delete($item.FullName)
            }
        }
    }

    foreach ($directory in ($directories | Sort-Object @{ Expression = { $_.FullName.Length }; Descending = $true })) {
        [IO.File]::SetAttributes($directory.FullName, [IO.FileAttributes]::Normal)
        [IO.Directory]::Delete($directory.FullName, $false)
    }
    [IO.Directory]::Delete($root, $false)
}

function Test-ByteSequence {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][byte[]]$Pattern
    )

    if ($Pattern.Length -eq 0 -or $Pattern.Length -gt $Bytes.Length) {
        return $false
    }

    for ($start = 0; $start -le $Bytes.Length - $Pattern.Length; $start++) {
        $matches = $true
        for ($offset = 0; $offset -lt $Pattern.Length; $offset++) {
            $left = [int]$Bytes[$start + $offset]
            $right = [int]$Pattern[$offset]
            if ($left -ge 65 -and $left -le 90) { $left += 32 }
            if ($right -ge 65 -and $right -le 90) { $right += 32 }
            if ($left -ne $right) {
                $matches = $false
                break
            }
        }
        if ($matches) {
            return $true
        }
    }

    return $false
}

function Find-BytePatternMatches {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    $encodings = @(
        @{ Name = "ASCII"; Encoding = [Text.Encoding]::ASCII },
        @{ Name = "UTF-16LE"; Encoding = [Text.Encoding]::Unicode }
    )
    $matches = @()
    foreach ($item in $encodings) {
        $pattern = $item.Encoding.GetBytes($Value)
        if (Test-ByteSequence -Bytes $Bytes -Pattern $pattern) {
            $matches += [pscustomobject]@{
                Label = $Label
                Encoding = $item.Name
                Value = $Value
            }
        }
    }
    return $matches
}

function Get-PublishPathPatterns {
    $userProfile = [string]$env:USERPROFILE
    $username = [string]$env:USERNAME
    $repositoryRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd([char[]]@('\', '/'))
    $patterns = @()

    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $patterns += [pscustomobject]@{
            Label = "USERPROFILE"
            Value = $userProfile
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($repositoryRoot)) {
        $patterns += [pscustomobject]@{
            Label = "repository root"
            Value = $repositoryRoot
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($username)) {
        $patterns += [pscustomobject]@{
            Label = "user profile segment"
            Value = "\Users\$username\"
        }
    }

    return @($patterns | Sort-Object Value -Unique)
}

function Get-PathLeakMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object[]]$Patterns
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    $matches = @()
    foreach ($pattern in $Patterns) {
        $matches += Find-BytePatternMatches -Bytes $bytes -Label $pattern.Label -Value $pattern.Value
    }
    return $matches
}

function Assert-NoPublishPathLeaks {
    param(
        [Parameter(Mandatory = $true)][string[]]$FilePaths,
        [Parameter(Mandatory = $true)][string]$Scope
    )

    $patterns = @(Get-PublishPathPatterns)
    $findings = @()
    foreach ($path in $FilePaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        $matches = @(Get-PathLeakMatches -Path $path -Patterns $patterns)
        foreach ($match in $matches) {
            $findings += ("{0} [{1}, {2}]" -f $path, $match.Label, $match.Encoding)
        }
    }

    if ($findings.Count -gt 0) {
        throw "[guard] $Scope contains personal or builder-specific paths:`n$($findings -join "`n")"
    }

    Write-Host "[guard] $Scope PASS: scanned $($FilePaths.Count) file(s); no personal or repository paths found." -ForegroundColor Green
}

function Assert-NoPublishPathLeaksInZip {
    param([Parameter(Mandatory = $true)][string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $patterns = @(Get-PublishPathPatterns)
    $findings = @()
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    $entryCount = 0
    try {
        foreach ($entry in $archive.Entries) {
            $entryCount++
            $memory = New-Object IO.MemoryStream
            $stream = $null
            try {
                $stream = $entry.Open()
                $stream.CopyTo($memory)
                $bytes = $memory.ToArray()
            } finally {
                if ($stream) { $stream.Dispose() }
                $memory.Dispose()
            }

            foreach ($pattern in $patterns) {
                $matches = @(Find-BytePatternMatches -Bytes $bytes -Label $pattern.Label -Value $pattern.Value)
                foreach ($match in $matches) {
                    $findings += ("{0} [{1}, {2}]" -f $entry.FullName, $match.Label, $match.Encoding)
                }
            }
        }
    } finally {
        $archive.Dispose()
    }

    if ($findings.Count -gt 0) {
        throw "[guard] ZIP contains personal or builder-specific paths:`n$($findings -join "`n")"
    }

    Write-Host "[guard] ZIP PASS: scanned $entryCount entry/entries; no personal or repository paths found." -ForegroundColor Green
}

function New-ForwardSlashZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([char[]]@('\', '/'))
    $archive = [IO.Compression.ZipFile]::Open(
        $DestinationPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
            $relative = $file.FullName.Substring($root.Length).TrimStart([char[]]@('\', '/'))
            $entryName = $relative.Replace('\', '/')
            $entry = $archive.CreateEntry(
                $entryName, [IO.Compression.CompressionLevel]::Optimal)
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $null
            try {
                $output = $entry.Open()
                $input.CopyTo($output)
            } finally {
                if ($output) { $output.Dispose() }
                $input.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Invoke-SolutionBuild {
    param(
        [Parameter(Mandatory = $false)][string]$DotNetPath,
        [Parameter(Mandatory = $false)][string]$MsBuildPath,
        [Parameter(Mandatory = $true)][string]$ApiDirectory
    )

    if ($DotNetPath) {
        Write-Host "[build] Using .NET SDK: $DotNetPath" -ForegroundColor Cyan
        & $DotNetPath build $solutionPath "-c" $Configuration -m:1 "-p:RevitApiDir=$ApiDirectory"
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed (exit code $LASTEXITCODE)."
        }
        return
    }

    Write-Host "[build] Using Visual Studio MSBuild: $MsBuildPath" -ForegroundColor Cyan
    & $MsBuildPath $solutionPath /restore /m:1 "/p:Configuration=$Configuration" "/p:RevitApiDir=$ApiDirectory" /v:m
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed (exit code $LASTEXITCODE)."
    }
}

function Invoke-UnitTests {
    param(
        [Parameter(Mandatory = $false)][string]$DotNetPath,
        [Parameter(Mandatory = $false)][string]$MsBuildPath,
        [Parameter(Mandatory = $true)][string]$ApiDirectory
    )

    if ($SkipTests) {
        Write-Host "[test] Skipped because -SkipTests was specified." -ForegroundColor Yellow
        return
    }

    if ($DotNetPath) {
        Write-Host "[test] Running unit tests." -ForegroundColor Cyan
        & $DotNetPath test $solutionPath "-c" $Configuration -m:1 --no-build --no-restore "-p:RevitApiDir=$ApiDirectory"
        if ($LASTEXITCODE -ne 0) {
            throw "Unit tests failed (exit code $LASTEXITCODE)."
        }
        return
    }

    $testAssembly = Join-Path $repoRoot "tests\ExcelScheduleLink.Excel.OpenXml.Tests\bin\$Configuration\net8.0\ExcelScheduleLink.Excel.OpenXml.Tests.dll"
    $vsTest = Find-VsTest
    if ($vsTest -and (Test-Path -LiteralPath $testAssembly -PathType Leaf)) {
        Write-Host "[test] Running unit tests with Visual Studio Test Platform." -ForegroundColor Cyan
        & $vsTest $testAssembly /Logger:console
        if ($LASTEXITCODE -ne 0) {
            throw "Unit tests failed (exit code $LASTEXITCODE)."
        }
        return
    }

    Write-Host "[test] Running the MSBuild VSTest target." -ForegroundColor Cyan
    & $MsBuildPath $solutionPath /t:VSTest /m:1 "/p:Configuration=$Configuration" "/p:VSTestNoBuild=true" "/p:VSTestNoRestore=true" "/p:RevitApiDir=$ApiDirectory" /v:m
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed (exit code $LASTEXITCODE)."
    }
}

function New-Distribution {
    param(
        [Parameter(Mandatory = $true)][string]$Version
    )

    $dist = Join-Path $repoRoot "dist\ExcelScheduleLink"
    if (Test-Path -LiteralPath $dist) {
        Remove-GeneratedTree -Path $dist
    }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    $revitOutput = Join-Path $repoRoot "src\ExcelScheduleLink.Revit2024\bin\$Configuration\net48"
    if (-not (Test-Path -LiteralPath $revitOutput -PathType Container)) {
        throw "The Revit add-in output directory was not found: $revitOutput"
    }

    $outputDlls = @(Get-ChildItem -LiteralPath $revitOutput -File -Filter "*.dll")
    if ($outputDlls.Count -eq 0) {
        throw "No DLLs were found in the Revit add-in output directory: $revitOutput"
    }

    $revitApiOutput = @($outputDlls | Where-Object { $_.Name -like "RevitAPI*.dll" })
    if ($revitApiOutput.Count -gt 0) {
        Write-Host "[package] Revit API DLLs were present in build output and will not be copied: $($revitApiOutput.Name -join ', ')" -ForegroundColor Yellow
    }
    foreach ($dll in ($outputDlls | Where-Object { $_.Name -notlike "RevitAPI*.dll" })) {
        Copy-Item -LiteralPath $dll.FullName -Destination $dist -Force
    }

    $mainAssembly = Join-Path $dist "ExcelScheduleLink.Revit2024.dll"
    if (-not (Test-Path -LiteralPath $mainAssembly -PathType Leaf)) {
        throw "The packaged add-in assembly is missing: $mainAssembly"
    }

    $manifest = Join-Path $repoRoot "src\ExcelScheduleLink.Revit2024\ExcelScheduleLink.addin.template"
    Copy-RequiredFile -Source $manifest -DestinationDirectory $dist

    $forbiddenApiDlls = @(Get-ChildItem -LiteralPath $dist -Recurse -File -Filter "RevitAPI*.dll")
    if ($forbiddenApiDlls.Count -gt 0) {
        throw "The end-user package contains Revit API DLLs, which must not be redistributed: $($forbiddenApiDlls.FullName -join '; ')"
    }

    Write-Host "[package] Current v$Version distribution generated: $dist" -ForegroundColor Green
    return $dist
}

function New-DistributionZip {
    param(
        [Parameter(Mandatory = $true)][string]$DistDirectory,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $stage = Join-Path ([IO.Path]::GetTempPath()) ("ExcelScheduleLink_zip_" + [Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $repoRoot ("ExcelScheduleLink_v" + $Version + ".zip")
    $zipScripts = @(
        "install-revit2024.ps1",
        "uninstall-revit2024.ps1",
        "run-preflight-check.ps1"
    )

    Register-GeneratedTreeRoot -Path $stage
    try {
        New-Item -ItemType Directory -Force -Path $stage | Out-Null
        $stageDist = Join-Path $stage "dist"
        $stageDistPackage = Join-Path $stageDist "ExcelScheduleLink"
        New-Item -ItemType Directory -Force -Path $stageDistPackage | Out-Null
        foreach ($item in (Get-ChildItem -LiteralPath $DistDirectory -Force)) {
            Copy-Item -LiteralPath $item.FullName -Destination $stageDistPackage -Recurse -Force
        }

        $stageScripts = Join-Path $stage "scripts"
        New-Item -ItemType Directory -Force -Path $stageScripts | Out-Null
        foreach ($name in $zipScripts) {
            Copy-RequiredFile -Source (Join-Path $scriptDir $name) -DestinationDirectory $stageScripts
        }

        Copy-RequiredFileAs -Source (Join-Path $repoRoot "docs\PACKAGE_README.md") -Destination (Join-Path $stage "README.md")
        Copy-RequiredFile -Source (Join-Path $repoRoot "THIRD-PARTY-NOTICES.txt") -DestinationDirectory $stage
        Copy-RequiredFile -Source (Join-Path $repoRoot "SECURITY.md") -DestinationDirectory $stage
        Copy-EndUserDocumentation -DestinationRoot $stage

        $license = Join-Path $repoRoot "LICENSE"
        if (Test-Path -LiteralPath $license -PathType Leaf) {
            Copy-Item -LiteralPath $license -Destination $stage -Force
        }

        $stageFiles = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
        $forbidden = @($stageFiles | Where-Object {
            $relative = $_.FullName.Substring($stage.Length + 1)
            $relative -match '^(src|testdata)([\\/]|$)' -or
            $relative -match '(^|[\\/])(bin|obj)([\\/]|$)' -or
            $relative -match '^scripts\\build\.ps1$' -or
            $relative -match '(^|[\\/])PACKAGE_README\.md$'
        })
        if ($forbidden.Count -gt 0) {
            throw "The end-user ZIP staging area contains forbidden source/build/test files: $($forbidden.FullName -join '; ')"
        }

        Assert-NoPublishPathLeaks -FilePaths @($stageFiles.FullName) -Scope "ZIP staging"

        if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
            [IO.File]::SetAttributes($zipPath, [IO.FileAttributes]::Normal)
            [IO.File]::Delete($zipPath)
        }
        New-ForwardSlashZip -SourceRoot $stage -DestinationPath $zipPath
        Assert-NoPublishPathLeaksInZip -ZipPath $zipPath
        $checksumPath = Join-Path $repoRoot "SHA256SUMS.txt"
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksumLine = "{0}  {1}{2}" -f $zipHash, (Split-Path -Leaf $zipPath), [Environment]::NewLine
        [IO.File]::WriteAllText($checksumPath, $checksumLine, (New-Object Text.UTF8Encoding($false)))
        Assert-NoPublishPathLeaks -FilePaths @($checksumPath) -Scope "SHA256SUMS.txt"
        Write-Host "[zip] Distribution ZIP generated: $zipPath" -ForegroundColor Green
        Write-Host "[zip] SHA-256 checksum written: $checksumPath" -ForegroundColor Green
        return $zipPath
    } finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-GeneratedTree -Path $stage
        }
    }
}

if (-not $RevitApiDir) {
    $RevitApiDir = Get-DefaultRevitApiDir
}

$version = Get-ProjectVersion -Path $propsPath
if ([string]::IsNullOrWhiteSpace($Configuration)) {
    throw "Configuration cannot be empty."
}
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "The solution was not found: $solutionPath"
}

$localRevitApi = (Test-Path -LiteralPath (Join-Path $RevitApiDir "RevitAPI.dll") -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $RevitApiDir "RevitAPIUI.dll") -PathType Leaf)
if ($localRevitApi) {
    Write-Host "[build] Revit API reference mode: local installation ($RevitApiDir)" -ForegroundColor Cyan
} else {
    Write-Host "[build] Revit API reference mode: Nice3point NuGet fallback ($RevitApiDir does not contain both API DLLs)" -ForegroundColor Yellow
}

$dotnet = Find-DotNetSdk
$msbuild = $null
if (-not $dotnet) {
    Write-Host "[toolchain] No usable .NET SDK was found; checking Visual Studio MSBuild with vswhere." -ForegroundColor Yellow
    $msbuild = Find-MSBuild
}
if (-not $dotnet -and -not $msbuild) {
    throw "No usable .NET SDK or Visual Studio MSBuild was found. Install a .NET SDK or Visual Studio Build Tools with MSBuild."
}

Invoke-SolutionBuild -DotNetPath $dotnet -MsBuildPath $msbuild -ApiDirectory $RevitApiDir
Invoke-UnitTests -DotNetPath $dotnet -MsBuildPath $msbuild -ApiDirectory $RevitApiDir
$distribution = New-Distribution -Version $version
$distributionFiles = @(Get-ChildItem -LiteralPath $distribution -Recurse -File -Force)
Assert-NoPublishPathLeaks -FilePaths @($distributionFiles.FullName) -Scope "distribution"

if ($Zip) {
    New-DistributionZip -DistDirectory $distribution -Version $version | Out-Null
}

Write-Host "[complete] Excel Schedule Link v$version build and packaging completed." -ForegroundColor Green
