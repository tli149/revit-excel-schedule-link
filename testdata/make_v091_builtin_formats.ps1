# Generate the v0.9.1 built-in date/time formatter fixture without Python.
# This is a companion for make_v091_builtin_formats.py for constrained Windows
# environments. The fixture is mechanism coverage, not desktop-Excel evidence.

[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'v0.9.1_BuiltinDateFormats.xlsx'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$invariant = [Globalization.CultureInfo]::InvariantCulture
$base = [DateTime]::new(1899, 12, 30)

function Get-ExcelSerial([DateTime]$Value) {
    return (($Value - $base).TotalSeconds / 86400.0).ToString('0.###############', $invariant)
}

function New-InlineCell([string]$Reference, [string]$Value, [int]$Style = 0) {
    $escaped = [System.Security.SecurityElement]::Escape($Value)
    return '<c r="{0}" s="{1}" t="inlineStr"><is><t>{2}</t></is></c>' -f $Reference, $Style, $escaped
}

function New-NumberCell([string]$Reference, [string]$Value, [int]$Style) {
    return '<c r="{0}" s="{1}"><v>{2}</v></c>' -f $Reference, $Style, $Value
}

$date = Get-ExcelSerial ([DateTime]::new(2026, 6, 10))
$time = Get-ExcelSerial ([DateTime]::new(1899, 12, 30, 13, 45, 0))
$combined = Get-ExcelSerial ([DateTime]::new(2026, 6, 10, 13, 45, 0))
$rows = @(
    '<row r="1">' + (New-InlineCell 'A1' 'Case') + (New-InlineCell 'B1' 'Numeric serial') + '</row>'
    '<row r="2">' + (New-InlineCell 'A2' 'Built-in 14 short date') + (New-NumberCell 'B2' $date 1) + '</row>'
    '<row r="3">' + (New-InlineCell 'A3' 'Built-in 20 short time') + (New-NumberCell 'B3' $time 2) + '</row>'
    '<row r="4">' + (New-InlineCell 'A4' 'Built-in 22 short date and time') + (New-NumberCell 'B4' $combined 3) + '</row>'
    '<row r="5">' + (New-InlineCell 'A5' 'Custom yyyy/m/d control') + (New-NumberCell 'B5' $date 4) + '</row>'
)
$ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$sheet = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{0}"><dimension ref="A1:B5"/><sheetData>{1}</sheetData></worksheet>' -f $ns, ($rows -join '')
$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{0}"><numFmts count="1"><numFmt numFmtId="165" formatCode="yyyy/m/d"/></numFmts><fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/><xf numFmtId="20" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/><xf numFmtId="22" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/></cellXfs></styleSheet>' -f $ns
$workbook = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{0}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><workbookPr date1904="0"/><sheets><sheet name="BuiltinFormats" sheetId="1" r:id="rId1"/></sheets></workbook>' -f $ns
$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>'
$rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
$workbookRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'

$parent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $parent)) {
    [IO.Directory]::CreateDirectory($parent) | Out-Null
}
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $parts = [ordered]@{
            '[Content_Types].xml' = $contentTypes
            '_rels/.rels' = $rootRels
            'xl/workbook.xml' = $workbook
            'xl/_rels/workbook.xml.rels' = $workbookRels
            'xl/worksheets/sheet1.xml' = $sheet
            'xl/styles.xml' = $styles
        }
        foreach ($part in $parts.GetEnumerator()) {
            $entry = $archive.CreateEntry($part.Key)
            $entryStream = $entry.Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($part.Value)
                $entryStream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$hash = [Security.Cryptography.SHA256]::Create()
try {
    $digest = [BitConverter]::ToString($hash.ComputeHash([IO.File]::ReadAllBytes($OutputPath))).Replace('-', '').ToLowerInvariant()
}
finally {
    $hash.Dispose()
}
Write-Output "wrote $OutputPath"
Write-Output "verify OK: built-in numFmtIds 14, 20, 22 + custom 165 / sha-256 $digest"
Write-Output 'acceptance still required: open and save a copy in desktop Excel and independently confirm built-in ID 14'
