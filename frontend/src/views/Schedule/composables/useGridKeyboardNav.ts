import { ref } from 'vue'
import { parseIso, toIso, weekdayFmt } from '@/views/Schedule/format'
import type { Assignment, Employee, ShiftType } from '@/views/Schedule/types'

// issue #80: keyboard grid navigation, an ADDITION alongside drag-and-drop, not a replacement
// for it. Uses a roving-tabindex model — exactly one day cell is a Tab stop at a time (the
// currently-focused cell, or the first employee/first day cell before anything's been focused),
// so Tab-ing into the grid doesn't walk through every cell in the month. Arrow keys then move
// that focus between cells; Enter reuses the same "open the edit modal" logic a chip's own
// click/tap already triggers; Delete/Backspace reuses the same ConfirmDialog + DELETE call
// ShiftAssignmentModal's own delete button uses.
export function useGridKeyboardNav(options: {
  visibleEmployees: () => Employee[]
  days: () => Date[]
  assignmentsFor: (employeeId: string, dateIso: string) => Assignment[]
  shiftTypeById: (id: string) => ShiftType | undefined
  onOpen: (assignment: Assignment) => void
  onDelete: (assignment: Assignment) => void
}) {
  // null before the grid has ever received focus, in which case the first employee/first day
  // cell is the tab-stop by default (see isFocusableCell).
  const focusedCell = ref<{ employeeId: string; date: string } | null>(null)

  function isFocusableCell(employeeId: string, dateIso: string): boolean {
    if (focusedCell.value) {
      return focusedCell.value.employeeId === employeeId && focusedCell.value.date === dateIso
    }
    const firstEmployee = options.visibleEmployees()[0]
    const firstDay = options.days()[0]
    return (
      !!firstEmployee &&
      !!firstDay &&
      firstEmployee.id === employeeId &&
      toIso(firstDay) === dateIso
    )
  }
  function cellAriaLabel(employeeId: string, dateIso: string): string {
    const employee = options.visibleEmployees().find((e) => e.id === employeeId)
    const who = employee ? `${employee.firstName} ${employee.lastName}` : ''
    const when = weekdayFmt.format(parseIso(dateIso))
    const shifts = options
      .assignmentsFor(employeeId, dateIso)
      .map((a) => options.shiftTypeById(a.shiftTypeId)?.name)
      .filter(Boolean)
    return `${who}, ${when}${shifts.length ? ', ' + shifts.join(', ') : ', frei'}`
  }
  function onCellFocus(employeeId: string, dateIso: string) {
    focusedCell.value = { employeeId, date: dateIso }
  }

  // The grid's focused day cell, if the browser's current focus is on one — the day <td>s are
  // the only elements carrying both data attributes, so this doubles as the "is a grid cell
  // focused right now" check.
  function focusedGridCellEl(): HTMLElement | null {
    const el = document.activeElement as HTMLElement | null
    if (el?.dataset.employeeId && el?.dataset.date) return el
    return null
  }
  function moveCellFocus(employeeId: string, dateIso: string) {
    document
      .querySelector<HTMLElement>(`td[data-employee-id="${employeeId}"][data-date="${dateIso}"]`)
      ?.focus()
  }
  // Only kicks in once a day cell already has keyboard focus (Tab into the grid, or click a
  // cell), so it coexists with the pre-existing ArrowLeft/Right month-nav: those still fire
  // whenever focus is anywhere else (search box, body, closed grid) — the caller is expected to
  // check `focusedGridCellEl()` first and only reach here when it's non-null.
  function onGridCellKeydown(e: KeyboardEvent, employeeId: string, dateIso: string) {
    const employeeIdx = options.visibleEmployees().findIndex((emp) => emp.id === employeeId)
    const dayIdx = options.days().findIndex((d) => toIso(d) === dateIso)
    if (employeeIdx === -1 || dayIdx === -1) return
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
        const prev = options.visibleEmployees()[employeeIdx - 1]
        if (prev) moveCellFocus(prev.id, dateIso)
        break
      }
      case 'ArrowDown': {
        const next = options.visibleEmployees()[employeeIdx + 1]
        if (next) moveCellFocus(next.id, dateIso)
        break
      }
      case 'ArrowLeft': {
        const prevDay = options.days()[dayIdx - 1]
        if (prevDay) moveCellFocus(employeeId, toIso(prevDay))
        break
      }
      case 'ArrowRight': {
        const nextDay = options.days()[dayIdx + 1]
        if (nextDay) moveCellFocus(employeeId, toIso(nextDay))
        break
      }
      case 'Enter': {
        const assignment = options.assignmentsFor(employeeId, dateIso)[0]
        if (assignment) options.onOpen(assignment)
        break
      }
      case 'Delete':
      case 'Backspace': {
        const assignment = options.assignmentsFor(employeeId, dateIso)[0]
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
