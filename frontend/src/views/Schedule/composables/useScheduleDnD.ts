import { onUnmounted, ref } from 'vue'
import type { Assignment, Employee, ShiftType } from '@/views/Schedule/types'

// Pointer-events-based drag (works for mouse and touch alike — native HTML5 DnD has no touch
// support). Drag only becomes "active" past a small movement threshold so a plain tap/click
// still opens the assignment modal instead of misfiring as a zero-distance drag.
//
// Cells are identified generically by a "row id" (via each cell's `data-row-id`/`data-date`
// attributes) rather than anything employee-specific — the employee-rows grid uses the
// employee's id as the row id, the shift-type-rows grid (drag employees onto shifts, requested
// directly since a handful of shift types with 20+ employees makes dragging shifts onto
// employees the wrong way round) uses the ShiftType's id instead. This composable doesn't care
// which; the caller's `onDrop` interprets `rowId` based on which grid is currently shown.
export interface DragPayload {
  kind: 'shiftType' | 'assignment' | 'employee'
  shiftTypeId?: string
  assignmentId?: string
  employeeId?: string
  label: string
  color: string
  time: string
}
export interface DragState {
  payload: DragPayload
  pointerId: number
  startX: number
  startY: number
  x: number
  y: number
  active: boolean
}

export function paletteDragPayload(s: ShiftType): DragPayload {
  return {
    kind: 'shiftType',
    shiftTypeId: s.id,
    label: s.name,
    color: s.color,
    time: `${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)}`,
  }
}
export function assignmentDragPayload(
  a: Assignment,
  shiftType: ShiftType | undefined,
): DragPayload {
  return {
    kind: 'assignment',
    assignmentId: a.id,
    label: shiftType?.name ?? '',
    color: shiftType?.color ?? '#64748b',
    time: `${a.startTime.slice(0, 5)}–${a.endTime.slice(0, 5)}`,
  }
}
// The "Nach Schicht" view's palette equivalent — dropped onto a (shiftType, date) cell to
// create an assignment for this employee, mirroring how a shiftType chip dropped onto an
// (employee, date) cell creates one in the "Nach Mitarbeiter" view.
export function employeeDragPayload(e: Employee): DragPayload {
  return {
    kind: 'employee',
    employeeId: e.id,
    label: `${e.firstName} ${e.lastName}`,
    color: '#64748b',
    time: '',
  }
}

const DRAG_ACTIVATE_PX = 6

export function useScheduleDnD(options: {
  // Drag ended on a valid cell — create-or-move, depending on payload.kind. rowId is the
  // dropped-on cell's `data-row-id` (an employeeId in the employee-rows grid, a ShiftType id
  // in the shift-rows grid) — the caller knows which grid is active and interprets it.
  onDrop: (payload: DragPayload, rowId: string, dateIso: string) => void | Promise<void>
  // Pointer went up without ever crossing the activation threshold — a plain tap/click.
  onTap: (payload: DragPayload) => void
  // Ticks (via setInterval, not pointermove) while an active drag's pointer sits near the
  // table's scroll edge — a pointer parked there stops firing move events, but the scroll
  // should keep going while it's held there.
  onAutoScroll: (clientX: number) => void
}) {
  const drag = ref<DragState | null>(null)
  const dragOverKey = ref<string | null>(null)
  let dragScrollTimer: number | null = null

  function onChipPointerDown(e: PointerEvent, payload: DragPayload) {
    if (e.button !== 0) {
      return
    }
    ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
    drag.value = {
      payload,
      pointerId: e.pointerId,
      startX: e.clientX,
      startY: e.clientY,
      x: e.clientX,
      y: e.clientY,
      active: false,
    }
    window.addEventListener('pointermove', onDragPointerMove)
    window.addEventListener('pointerup', onDragPointerUp)
    window.addEventListener('pointercancel', onDragPointerCancel)
    dragScrollTimer = window.setInterval(() => {
      if (drag.value?.active) options.onAutoScroll(drag.value.x)
    }, 16)
  }
  function onDragPointerMove(e: PointerEvent) {
    if (!drag.value || e.pointerId !== drag.value.pointerId) return
    drag.value.x = e.clientX
    drag.value.y = e.clientY
    if (!drag.value.active) {
      const dx = e.clientX - drag.value.startX
      const dy = e.clientY - drag.value.startY
      if (Math.hypot(dx, dy) < DRAG_ACTIVATE_PX) return
      drag.value.active = true
    }
    e.preventDefault()
    const cell = document
      .elementFromPoint(e.clientX, e.clientY)
      ?.closest<HTMLElement>('[data-row-id]')
    dragOverKey.value = cell ? `${cell.dataset.rowId}|${cell.dataset.date}` : null
  }
  async function onDragPointerUp(e: PointerEvent) {
    if (!drag.value || e.pointerId !== drag.value.pointerId) return
    const { payload, active } = drag.value
    const cell = document
      .elementFromPoint(e.clientX, e.clientY)
      ?.closest<HTMLElement>('[data-row-id]')
    cleanupDrag()

    if (!active) {
      options.onTap(payload)
      return
    }
    if (cell?.dataset.rowId && cell.dataset.date) {
      await options.onDrop(payload, cell.dataset.rowId, cell.dataset.date)
    }
  }
  function onDragPointerCancel(e: PointerEvent) {
    if (!drag.value || e.pointerId !== drag.value.pointerId) return
    cleanupDrag()
  }
  function cleanupDrag() {
    drag.value = null
    dragOverKey.value = null
    window.removeEventListener('pointermove', onDragPointerMove)
    window.removeEventListener('pointerup', onDragPointerUp)
    window.removeEventListener('pointercancel', onDragPointerCancel)
    if (dragScrollTimer !== null) {
      window.clearInterval(dragScrollTimer)
      dragScrollTimer = null
    }
  }
  onUnmounted(cleanupDrag)

  return { drag, dragOverKey, onChipPointerDown, cleanupDrag }
}
