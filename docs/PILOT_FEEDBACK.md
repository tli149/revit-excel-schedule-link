# Excel Schedule Link — Pilot Feedback Form

Thanks for testing. Specific notes, including "this wording confused me", are
especially useful. Fill in what you can; partial answers are fine. Send your
answers by opening an issue on the project's GitHub page and pasting them there.

> **GitHub issues are public.** Do not attach client or project files
> (`.rvt`, `.xlsx`). Leave out project names, client names, user names and
> server paths. Describe your workbooks instead — the questions below are
> designed for that.

---

## 1. About you and your setup

- Your role / typical Revit work:
- Revit version & build (Help → About):
- How often do you bring Excel data into Revit today, and how (copy-paste,
  screenshots, manual retyping, other tools)?

## 2. The workbook(s) you tried

For each workbook (copy this block if you tried more than one):

- Rough size (rows × columns):
- What is in it (equipment schedule, load calc, room data, ...):
- Number formats present (check all that apply):
  - [ ] plain numbers / decimals
  - [ ] 1,000 separators
  - [ ] percentages
  - [ ] dates / times
  - [ ] leading zeros (e.g. 00123)
  - [ ] units in the format (e.g. 12.3 A, 1234 CFM)
  - [ ] fractions (e.g. 1 1/2)
  - [ ] currency
  - [ ] other: ______
- Merged cells? [ ] yes [ ] no
- Where does the file live? [ ] local disk [ ] mapped network drive (Z:\...)
  [ ] network path (\\server\...) [ ] OneDrive/SharePoint

## 3. Tasks — did they work?

| Task | Worked? | Notes |
|---|---|---|
| Install via the script | ☐ yes ☐ no | |
| Link a worksheet (used range) | ☐ yes ☐ no | |
| Link with a cell range (e.g. A1:F30) | ☐ yes ☐ no ☐ didn't try | |
| Place the schedule on a sheet | ☐ yes ☐ no | |
| Print / PDF the sheet | ☐ yes ☐ no ☐ didn't try | |
| Edit Excel, then Reload | ☐ yes ☐ no | |
| Reload with the workbook open in Excel | ☐ yes ☐ no ☐ didn't try | |
| Use the Link Manager | ☐ yes ☐ no ☐ didn't try | |
| Approve a network server when asked | ☐ yes ☐ no ☐ didn't try | |
| Drag column widths, then reload | ☐ yes ☐ no ☐ didn't try | Did the widths survive? |
| Use Column Widths and Table width / Fit to Total | ☐ yes ☐ no ☐ didn't try | |
| Choose column and row reload policies | ☐ yes ☐ no ☐ didn't try | |

## 4. Readability of the result

- Were the values displayed the way Excel shows them? If not, paste examples
  (Excel shows ___ / Revit shows ___):
- Were the initial column widths usable? If you dragged or changed them, did they
  remain usable after reload?
- Did **Fit Content** produce readable results on the sheet? Did you need to use
  **Table width** and **Fit to Total**?
- When you chose **Fit rows to text on reload** or **Keep current row heights**, did
  the row behavior match what you expected?
- Anything about the table on the sheet (size, alignment, printing) that
  bothered you?

## 5. Friction log

- Moments when you were not sure what to do next (be specific — which dialog,
  which wording):
- Any error messages you saw (paste the text or attach screenshots):
- Anything that felt slow?

## 6. Wishlist

- The one thing you would add or change first:
- Nice-to-haves:
- Would you use this on a real project as-is? ☐ yes ☐ yes, with fixes ☐ no
  - If "with fixes" / "no" — what's blocking:

## 7. Overall

- Overall experience: ☐ 1 ☐ 2 ☐ 3 ☐ 4 ☐ 5 (5 = great)
- Any other comments:
