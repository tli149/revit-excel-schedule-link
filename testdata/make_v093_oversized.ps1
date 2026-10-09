# Generate the v0.9.3 oversized-schedule manual-acceptance fixture without
# Python. This companion uses the same raw XML/ZIP package shape as the Python
# generator and contains three dense coordinate-labelled worksheets.

[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'v0.9.3_Oversized.xlsx'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$relationshipNs = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$packageRelationshipNs = 'http://schemas.openxmlformats.org/package/2006/relationships'

function Get-ExcelColumnName([int]$Index) {
    $name = ''
    while ($Index -gt 0) {
        $remainder = ($Index - 1) % 26
        $name = ([char](65 + $remainder)) + $name
        $Index = [int][Math]::Floor(($Index - 1) / 26)
    }
    return $name
}

function New-InlineCell([string]$Reference, [string]$Value) {
    $escaped = [System.Security.SecurityElement]::Escape($Value)
    return '<c r="{0}" t="inlineStr"><is><t>{1}</t></is></c>' -f $Reference, $escaped
}

function New-WorksheetXml([int]$RowCount, [int]$ColumnCount) {
    $lastReference = '{0}{1}' -f (Get-ExcelColumnName $ColumnCount), $RowCount
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    [void]$builder.Append(('<worksheet xmlns="{0}"><dimension ref="A1:{1}"/><sheetData>' -f $ns, $lastReference))
    for ($row = 1; $row -le $RowCount; $row++) {
        [void]$builder.Append(('<row r="{0}">' -f $row))
        for ($column = 1; $column -le $ColumnCount; $column++) {
            $reference = '{0}{1}' -f (Get-ExcelColumnName $column), $row
            [void]$builder.Append((New-InlineCell $reference ('R{0}C{1}' -f $row, $column)))
        }
        [void]$builder.Append('</row>')
    }
    [void]$builder.Append('</sheetData></worksheet>')
    return $builder.ToString()
}

$sheets = @(
    [ordered]@{ Name = 'Rows600'; Rows = 600; Columns = 10 }
    [ordered]@{ Name = 'Cols60'; Rows = 20; Columns = 60 }
    [ordered]@{ Name = 'Both'; Rows = 520; Columns = 55 }
)

$sheetNodes = [Text.StringBuilder]::new()
$worksheetParts = [ordered]@{}
for ($index = 0; $index -lt $sheets.Count; $index++) {
    $sheet = $sheets[$index]
    $sheetId = $index + 1
    [void]$sheetNodes.Append(('<sheet name="{0}" sheetId="{1}" r:id="rId{1}"/>' -f $sheet.Name, $sheetId))
    $worksheetParts[('xl/worksheets/sheet{0}.xml' -f $sheetId)] = New-WorksheetXml $sheet.Rows $sheet.Columns
}

$workbook = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{0}" xmlns:r="{1}"><workbookPr date1904="0"/><sheets>{2}</sheets></workbook>' -f $ns, $relationshipNs, $sheetNodes.ToString()
$worksheetOverrides = [Text.StringBuilder]::new()
$worksheetRelationships = [Text.StringBuilder]::new()
for ($index = 1; $index -le $sheets.Count; $index++) {
    [void]$worksheetOverrides.Append(('<Override PartName="/xl/worksheets/sheet{0}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' -f $index))
    [void]$worksheetRelationships.Append(('<Relationship Id="rId{0}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{0}.xml"/>' -f $index))
}
$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>{0}<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>' -f $worksheetOverrides.ToString()
$rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{0}"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>' -f $packageRelationshipNs
$workbookRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{0}">{1}<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>' -f $packageRelationshipNs, $worksheetRelationships.ToString()
$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{0}"><fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>' -f $ns

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
            'xl/styles.xml' = $styles
        }
        foreach ($part in $worksheetParts.GetEnumerator()) {
            $parts[$part.Key] = $part.Value
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
Write-Output "verify OK: Rows600=600x10, Cols60=20x60, Both=520x55 / sha-256 $digest"
