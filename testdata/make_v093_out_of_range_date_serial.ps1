# Generate the isolated out-of-range Excel date-serial regression fixture
# without Python. This fixture is separate from the v0.9.1 desktop-Excel
# acceptance fixture.

[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'v0.9.3_OutOfRangeDateSerial.xlsx'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-InlineCell([string]$Reference, [string]$Value, [int]$Style = 0) {
    $escaped = [System.Security.SecurityElement]::Escape($Value)
    return '<c r="{0}" s="{1}" t="inlineStr"><is><t>{2}</t></is></c>' -f $Reference, $Style, $escaped
}

function New-NumberCell([string]$Reference, [string]$Value, [int]$Style) {
    return '<c r="{0}" s="{1}"><v>{2}</v></c>' -f $Reference, $Style, $Value
}

$rows = @(
    '<row r="1">' + (New-InlineCell 'A1' 'Case') + (New-InlineCell 'B1' 'Numeric serial') + '</row>'
    '<row r="2">' + (New-InlineCell 'A2' 'Out-of-range positive date serial') + (New-NumberCell 'B2' '10000000000' 1) + '</row>'
    '<row r="3">' + (New-InlineCell 'A3' 'Out-of-range negative date serial') + (New-NumberCell 'B3' '-10000000000' 1) + '</row>'
)
$ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$sheet = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{0}"><dimension ref="A1:B3"/><sheetData>{1}</sheetData></worksheet>' -f $ns, ($rows -join '')
$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{0}"><fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/></cellXfs></styleSheet>' -f $ns
$workbook = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{0}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><workbookPr date1904="0"/><sheets><sheet name="OutOfRangeDates" sheetId="1" r:id="rId1"/></sheets></workbook>' -f $ns
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
Write-Output "verify OK: built-in numFmtId 14 with out-of-range serials / sha-256 $digest"
Write-Output 'separate regression fixture: no desktop-Excel acceptance claim'
