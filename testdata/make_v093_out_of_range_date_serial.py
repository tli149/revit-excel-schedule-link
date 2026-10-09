# -*- coding: utf-8 -*-
"""Generate the isolated out-of-range Excel date-serial regression fixture.

The cells in column B deliberately use numeric serials outside DateTime's OLE
Automation date range while carrying built-in date format id 14. The reader
must fall back to invariant General text for these cells. This fixture is
separate from the v0.9.1 desktop-Excel acceptance fixture.
"""
from __future__ import annotations

import argparse
import hashlib
import zipfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import escape


OUT = Path(__file__).parent / "v0.9.3_OutOfRangeDateSerial.xlsx"
NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"


def inline_cell(reference: str, value: str, style: int = 0) -> str:
    return (
        f'<c r="{reference}" s="{style}" t="inlineStr">'
        f'<is><t>{escape(value)}</t></is></c>'
    )


def number_cell(reference: str, value: float, style: int) -> str:
    return f'<c r="{reference}" s="{style}"><v>{value:.15g}</v></c>'


def parts() -> dict[str, bytes]:
    rows = [
        '<row r="1">' + inline_cell("A1", "Case") + inline_cell("B1", "Numeric serial") + '</row>',
        '<row r="2">' + inline_cell("A2", "Out-of-range positive date serial") + number_cell("B2", 1e10, 1) + '</row>',
        '<row r="3">' + inline_cell("A3", "Out-of-range negative date serial") + number_cell("B3", -1e10, 1) + '</row>',
    ]
    sheet = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<worksheet xmlns="{NS}"><dimension ref="A1:B3"/><sheetData>'
        + "".join(rows) + "</sheetData></worksheet>"
    )
    styles = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<styleSheet xmlns="{NS}">'
        '<fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts>'
        '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>'
        '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
        '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
        '<cellXfs count="2">'
        '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
        '<xf numFmtId="14" fontId="0" fillId="0" borderId="0" applyNumberFormat="1" xfId="0"/>'
        '</cellXfs></styleSheet>'
    )
    workbook = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<workbook xmlns="{NS}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        '<workbookPr date1904="0"/><sheets><sheet name="OutOfRangeDates" sheetId="1" r:id="rId1"/></sheets>'
        '</workbook>'
    )
    content_types = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
        '</Types>'
    )
    root_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
        '</Relationships>'
    )
    workbook_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
        '</Relationships>'
    )
    return {
        "[Content_Types].xml": content_types.encode("utf-8"),
        "_rels/.rels": root_rels.encode("utf-8"),
        "xl/workbook.xml": workbook.encode("utf-8"),
        "xl/_rels/workbook.xml.rels": workbook_rels.encode("utf-8"),
        "xl/worksheets/sheet1.xml": sheet.encode("utf-8"),
        "xl/styles.xml": styles.encode("utf-8"),
    }


def build(output: Path = OUT) -> Path:
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, payload in parts().items():
            archive.writestr(name, payload)
    return output


def verify(output: Path = OUT) -> str:
    with zipfile.ZipFile(output, "r") as archive:
        styles = ElementTree.fromstring(archive.read("xl/styles.xml"))
        sheet = ElementTree.fromstring(archive.read("xl/worksheets/sheet1.xml"))
    cell_xfs = styles.find(f"{{{NS}}}cellXfs")
    assert cell_xfs is not None
    ids = [int(xf.attrib["numFmtId"]) for xf in cell_xfs]
    assert ids == [0, 14], ids
    cells = {cell.attrib["r"]: int(cell.attrib["s"]) for cell in sheet.iter(f"{{{NS}}}c")}
    assert cells == {"A1": 0, "B1": 0, "A2": 0, "B2": 1, "A3": 0, "B3": 1}, cells
    values = {
        cell.attrib["r"]: cell.find(f"{{{NS}}}v").text
        for cell in sheet.iter(f"{{{NS}}}c")
        if cell.find(f"{{{NS}}}v") is not None
    }
    assert values == {"B2": "10000000000", "B3": "-10000000000"}, values
    return hashlib.sha256(output.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUT)
    args = parser.parse_args()
    output = build(args.output)
    digest = verify(output)
    print(f"wrote {output}")
    print(f"verify OK: built-in numFmtId 14 with out-of-range serials / sha-256 {digest}")
    print("separate regression fixture: no desktop-Excel acceptance claim")


if __name__ == "__main__":
    main()
