import { ref } from 'vue'
import { toIso } from '@/views/Schedule/format'
import type { Assignment } from '@/views/Schedule/types'

// issue #80: keyboard grid navigation, an ADDITION alongside drag-and-drop, not a replacement
// for it. Uses a roving-tabindex model — exactly one day cell is a Tab stop at a time (the
// currently-focused cell, or the first row/first day cell before anything's been focused), so
// Tab-ing into the grid doesn't walk through every cell in the month. Arrow keys then move that
// focus between cells; Enter reuses the same "open the edit modal" logic a chip's own click/tap
// already triggers; Delete/Backspace reuses the same ConfirmDialog + DELETE call
// ShiftAssignmentModal's own delete button uses.
//
// Generic over what a "row" is (an id-bearing object) rather than specifically an Employee, so
// it serves both the employee-rows grid (rows = employees) and the shift-type-rows grid (rows =
// ShiftTypes, requested directly — a handful of shift types with 20+ employees makes dragging
// employees onto shifts the more natural direction). Each instantiation supplies its own
// `describeCell` for the aria-label, since what a cell "contains" differs by grid.
export function useGridKeyboardNav(options: {
  rows: () => { id: string }[]
  days: () => Date[]
  assignmentsForCell: (rowId: string, dateIso: string) => Assignment[]
  describeCell: (rowId: string, dateIso: string) => string
  onOpen: (assignment: Assignment) => void
  onDelete: (assignment: Assignment) => void
}) {
  // null before the grid has ever received focus, in which case the first row/first day cell
  // is the tab-stop by default (see isFocusableCell).
  const focusedCell = ref<{ rowId: string; date: string } | null>(null)

  function isFocusableCell(rowId: string, dateIso: string): boolean {
    if (focusedCell.value) {
      return focusedCell.value.rowId === rowId && focusedCell.value.date === dateIso
    }
    const firstRow = options.rows()[0]
    const firstDay = options.days()[0]
    return !!firstRow && !!firstDay && firstRow.id === rowId && toIso(firstDay) === dateIso
  }
  function cellAriaLabel(rowId: string, dateIso: string): string {
    return options.describeCell(rowId, dateIso)
  }
  function onCellFocus(rowId: string, dateIso: string) {
    focusedCell.value = { rowId, date: dateIso }
  }

  // The grid's focused day cell, if the browser's current focus is on one — the day <td>s are
  // the only elements carrying both data attributes, so this doubles as the "is a grid cell
  // focused right now" check.
  function focusedGridCellEl(): HTMLElement | null {
    const el = document.activeElement as HTMLElement | null
    if (el?.dataset.rowId && el?.dataset.date) return el
    return null
  }
  function moveCellFocus(rowId: string, dateIso: string) {
    document
      .querySelector<HTMLElement>(`td[data-row-id="${rowId}"][data-date="${dateIso}"]`)
      ?.focus()
  }
  // Only kicks in once a day cell already has keyboard focus (Tab into the grid, or click a
  // cell), so it coexists with the pre-existing ArrowLeft/Right month-nav: those still fire
  // whenever focus is anywhere else (search box, body, closed grid) — the caller is expected to
  // check `focusedGridCellEl()` first and only reach here when it's non-null.
  function onGridCellKeydown(e: KeyboardEvent, rowId: string, dateIso: string) {
    const rowIdx = options.rows().findIndex((r) => r.id === rowId)
    const dayIdx = options.days().findIndex((d) => toIso(d) === dateIso)
    if (rowIdx === -1 || dayIdx === -1) return
    if (
      !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Enter', 'Delete', 'Backspace'].includes(
        e.key,
      )
    ) {
      return
    }
    e.preventDefault()
    switch (e.key) {
      case 'ArrowUp': {
        const prev = options.rows()[rowIdx - 1]
        if (prev) moveCellFocus(prev.id, dateIso)
        break
      }
      case 'ArrowDown': {
        const next = options.rows()[rowIdx + 1]
        if (next) moveCellFocus(next.id, dateIso)
        break
      }
      case 'ArrowLeft': {
        const prevDay = options.days()[dayIdx - 1]
        if (prevDay) moveCellFocus(rowId, toIso(prevDay))
        break
      }
      case 'ArrowRight': {
        const nextDay = options.days()[dayIdx + 1]
        if (nextDay) moveCellFocus(rowId, toIso(nextDay))
        break
      }
      case 'Enter': {
        const assignment = options.assignmentsForCell(rowId, dateIso)[0]
        if (assignment) options.onOpen(assignment)
        break
      }
      case 'Delete':
      case 'Backspace': {
        const assignment = options.assignmentsForCell(rowId, dateIso)[0]
        if (assignment) options.onDelete(assignment)
        break
      }
    }
  }

  return {
    focusedCell,
    isFocusableCell,
    cellAriaLabel,
    onCellFocus,
    focusedGridCellEl,
    onGridCellKeydown,
  }
}
