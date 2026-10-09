# -*- coding: utf-8 -*-
"""Generate the v0.9.3 oversized-schedule manual-acceptance fixture.

Each worksheet is a dense text matrix whose value identifies its row and
column. The three sheets exercise row-only, column-only, and combined
truncation at the v0.9.3 schedule limits. This generator uses only the
standard library and writes the XLSX package directly as raw XML in a ZIP.
"""
from __future__ import annotations

import argparse
import hashlib
import zipfile
from pathlib import Path
from xml.etree import ElementTree
from xml.sax.saxutils import escape


OUT = Path(__file__).parent / "v0.9.3_Oversized.xlsx"
NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PACKAGE_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
SHEETS = (
    ("Rows600", 600, 10),
    ("Cols60", 20, 60),
    ("Both", 520, 55),
)


def excel_column(index: int) -> str:
    """Return the 1-based Excel column name for an integer index."""
    letters = []
    while index:
        index, remainder = divmod(index - 1, 26)
        letters.append(chr(ord("A") + remainder))
    return "".join(reversed(letters))


def inline_cell(reference: str, value: str) -> str:
    return (
        f'<c r="{reference}" t="inlineStr">'
        f'<is><t>{escape(value)}</t></is></c>'
    )


def worksheet_xml(row_count: int, column_count: int) -> str:
    last_reference = f"{excel_column(column_count)}{row_count}"
    chunks = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        f'<worksheet xmlns="{NS}"><dimension ref="A1:{last_reference}"/><sheetData>',
    ]
    for row in range(1, row_count + 1):
        cells = [
            inline_cell(f"{excel_column(column)}{row}", f"R{row}C{column}")
            for column in range(1, column_count + 1)
        ]
        chunks.append(f'<row r="{row}">{"".join(cells)}</row>')
    chunks.append("</sheetData></worksheet>")
    return "".join(chunks)


def styles_xml() -> str:
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<styleSheet xmlns="{NS}">'
        '<fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts>'
        '<fills count="2"><fill><patternFill patternType="none"/></fill>'
        '<fill><patternFill patternType="gray125"/></fill></fills>'
        '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
        '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
        '<cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>'
        '</styleSheet>'
    )


def workbook_xml() -> str:
    sheet_xml = "".join(
        f'<sheet name="{name}" sheetId="{index}" r:id="rId{index}"/>'
        for index, (name, _, _) in enumerate(SHEETS, 1)
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<workbook xmlns="{NS}" xmlns:r="{REL_NS}">'
        f'<workbookPr date1904="0"/><sheets>{sheet_xml}</sheets></workbook>'
    )


def content_types_xml() -> str:
    worksheet_overrides = "".join(
        f'<Override PartName="/xl/worksheets/sheet{index}.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        for index, _ in enumerate(SHEETS, 1)
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/workbook.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        f'{worksheet_overrides}'
        '<Override PartName="/xl/styles.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
        '</Types>'
    )


def root_relationships_xml() -> str:
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<Relationships xmlns="{PACKAGE_REL_NS}">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="xl/workbook.xml"/></Relationships>'
    )


def workbook_relationships_xml() -> str:
    relationships = "".join(
        f'<Relationship Id="rId{index}" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
        f'Target="worksheets/sheet{index}.xml"/>'
        for index, _ in enumerate(SHEETS, 1)
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<Relationships xmlns="{PACKAGE_REL_NS}">'
        f'{relationships}'
        f'<Relationship Id="rId{len(SHEETS) + 1}" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
        'Target="styles.xml"/></Relationships>'
    )


def parts() -> dict[str, bytes]:
    result = {
        "[Content_Types].xml": content_types_xml().encode("utf-8"),
        "_rels/.rels": root_relationships_xml().encode("utf-8"),
        "xl/workbook.xml": workbook_xml().encode("utf-8"),
        "xl/_rels/workbook.xml.rels": workbook_relationships_xml().encode("utf-8"),
        "xl/styles.xml": styles_xml().encode("utf-8"),
    }
    for index, (_, row_count, column_count) in enumerate(SHEETS, 1):
        result[f"xl/worksheets/sheet{index}.xml"] = worksheet_xml(
            row_count, column_count
        ).encode("utf-8")
    return result


def build(output: Path = OUT) -> Path:
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, payload in parts().items():
            archive.writestr(name, payload)
    return output


def verify(output: Path = OUT) -> str:
    expected_parts = {
        "[Content_Types].xml",
        "_rels/.rels",
        "xl/workbook.xml",
        "xl/_rels/workbook.xml.rels",
        "xl/styles.xml",
        *(f"xl/worksheets/sheet{index}.xml" for index, _ in enumerate(SHEETS, 1)),
    }
    with zipfile.ZipFile(output, "r") as archive:
        assert set(archive.namelist()) == expected_parts
        workbook = ElementTree.fromstring(archive.read("xl/workbook.xml"))
        workbook_sheets = workbook.find(f"{{{NS}}}sheets")
        assert workbook_sheets is not None
        sheet_nodes = list(workbook_sheets)
        assert len(sheet_nodes) == len(SHEETS)
        for index, (expected_name, row_count, column_count) in enumerate(SHEETS, 1):
            node = sheet_nodes[index - 1]
            assert node.attrib["name"] == expected_name
            assert node.attrib[f"{{{REL_NS}}}id"] == f"rId{index}"

            sheet = ElementTree.fromstring(
                archive.read(f"xl/worksheets/sheet{index}.xml")
            )
            last_reference = f"{excel_column(column_count)}{row_count}"
            assert sheet.find(f"{{{NS}}}dimension").attrib["ref"] == (
                f"A1:{last_reference}"
            )
            sheet_data = sheet.find(f"{{{NS}}}sheetData")
            assert sheet_data is not None
            rows = list(sheet_data)
            assert len(rows) == row_count
            for row_index, row_node in enumerate(rows, 1):
                assert row_node.attrib["r"] == str(row_index)
                cells = list(row_node)
                assert len(cells) == column_count
                for column_index, cell in enumerate(cells, 1):
                    reference = f"{excel_column(column_index)}{row_index}"
                    assert cell.attrib["r"] == reference
                    assert cell.attrib["t"] == "inlineStr"
                    text_node = cell.find(f"{{{NS}}}is/{{{NS}}}t")
                    assert text_node is not None
                    assert text_node.text == f"R{row_index}C{column_index}"
    return hashlib.sha256(output.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUT)
    args = parser.parse_args()
    output = build(args.output)
    digest = verify(output)
    print(f"wrote {output}")
    print(
        "verify OK: Rows600=600x10, Cols60=20x60, Both=520x55 "
        f"/ sha-256 {digest}"
    )


if __name__ == "__main__":
    main()
