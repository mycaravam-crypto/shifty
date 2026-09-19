<script setup lang="ts">
import { employeeDragPayload, type DragPayload, type DragState } from './composables/useScheduleDnD'
import { currencyFmt, toIso } from './format'
import type { Employee, ShiftType } from './types'

// The "Nach Schicht" view's palette (requested directly — with only a handful of shift types
// but 20+ employees, dragging shifts onto employee rows is the wrong way round). Employees are
// listed here instead of as grid rows, each draggable onto a (shiftType, date) cell — the same
// per-employee Xh/Yh/Übertrag/Lohnkosten readouts EmployeeScheduleRow.vue shows per row move
// here, since there's no employee row left to show them on in this view.
//
// Eligibility hints (requested directly, follow-up): dragging blind meant only finding out an
// employee couldn't take a shift via a toast after the drop. Each chip now shows a dot per
// visible ShiftType (dim/ringed when the employee isn't eligible for it — same rule
// EligibilityValidator itself uses, an empty eligible list means unrestricted) and an
// "Abwesend" badge when they're absent on any day of the displayed period — both purely
// informational, dropping on a cell the hints flag still works the same as before (the backend
// validator is still the source of truth), this just surfaces what it would say up front.
const props = defineProps<{
  employees: Employee[]
  activeEmployeesCount: number
  activeShiftTypes: ShiftType[]
  isEligibleFor: (employeeId: string, shiftTypeId: string) => boolean
  days: Date[]
  isAbsentOn: (employeeId: string, dateIso: string) => boolean
  targetHoursFor: (employeeId: string) => number | null
  netHoursFor: (employeeId: string) => number
  carriedOverFor: (employeeId: string) => number
  laborCostFor: (employeeId: string) => number | null
  drag: DragState | null
  isEditable: boolean
  chipPointerDown: (e: PointerEvent, payload: DragPayload) => void
}>()

function barWidth(employeeId: string): number {
  const target = props.targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (props.netHoursFor(employeeId) / target) * 100)
}
function isDragging(employeeId: string): boolean {
  return !!(
    props.drag?.active &&
    props.drag.payload.kind === 'employee' &&
    props.drag.payload.employeeId === employeeId
  )
}
function isAbsentInPeriod(employeeId: string): boolean {
  return props.days.some((d) => props.isAbsentOn(employeeId, toIso(d)))
}
// Distinguishes over-target (rose, a harder signal than "just doesn't match") from
// under-target (amber, the existing behavior) — this sidebar's own refinement, since it's the
// view a manager is actively assigning more work from.
function hourLineClass(employeeId: string): string {
  const target = props.targetHoursFor(employeeId)
  if (target === null) return 'text-slate-500'
  const net = props.netHoursFor(employeeId)
  if (net > target) return 'text-rose-400'
  if (net !== target) return 'text-amber-400'
  return 'text-slate-500'
}
</script>

<template>
  <div
    class="glass rounded-xl p-3 w-64 shrink-0 max-h-[70vh] overflow-y-auto space-y-1.5 print:hidden"
  >
    <p class="text-[10px] uppercase tracking-wider font-bold text-slate-500 px-1 mb-2">
      Mitarbeiter ziehen
    </p>
    <div
      v-for="e in employees"
      :key="e.id"
      class="rounded-lg bg-white/5 border border-white/10 px-2.5 py-2 touch-none select-none transition-colors"
      :class="[
        isEditable ? 'cursor-grab hover:bg-white/10' : 'cursor-default opacity-70',
        { 'opacity-40': isDragging(e.id) },
      ]"
      @pointerdown="isEditable && chipPointerDown($event, employeeDragPayload(e))"
    >
      <div class="text-sm">{{ e.lastName }}, {{ e.firstName }}</div>
      <div
        v-if="isAbsentInPeriod(e.id)"
        class="inline-flex items-center rounded-full border border-dashed border-slate-400 px-1.5 py-px text-[10px] text-slate-400 mt-1"
      >
        Abwesend
      </div>
      <div v-if="activeShiftTypes.length" class="flex items-center gap-1 mt-1">
        <span
          v-for="s in activeShiftTypes"
          :key="s.id"
          class="w-2 h-2 rounded-full shrink-0"
          :class="{ 'opacity-25 ring-1 ring-inset ring-white/40': !isEligibleFor(e.id, s.id) }"
          :style="{ backgroundColor: isEligibleFor(e.id, s.id) ? s.color : 'transparent' }"
          :title="`${s.name}: ${isEligibleFor(e.id, s.id) ? 'geeignet' : 'nicht freigegeben'}`"
        ></span>
      </div>
      <template v-if="targetHoursFor(e.id) !== null">
        <div class="font-mono text-[11px] mt-1" :class="hourLineClass(e.id)">
          {{ netHoursFor(e.id) }}h / {{ targetHoursFor(e.id) }}h
        </div>
        <div class="w-full h-1 rounded-full bg-white/10 mt-1 overflow-hidden">
          <div
            class="h-full bg-linear-to-r from-blue-600 to-indigo-600"
            :style="{ width: barWidth(e.id) + '%' }"
          ></div>
        </div>
      </template>
      <div
        v-if="carriedOverFor(e.id) !== 0"
        class="font-mono text-[10px] mt-0.5"
        :class="carriedOverFor(e.id) > 0 ? 'text-emerald-400' : 'text-rose-400'"
      >
        Übertrag: {{ carriedOverFor(e.id) > 0 ? '+' : '' }}{{ carriedOverFor(e.id) }}h
      </div>
      <div v-if="laborCostFor(e.id) !== null" class="font-mono text-[10px] text-emerald-400 mt-0.5">
        {{ currencyFmt.format(laborCostFor(e.id)!) }}
      </div>
    </div>
    <p v-if="!employees.length" class="text-sm text-slate-500 px-1">
      {{ activeEmployeesCount ? 'Keine Treffer.' : 'Keine Mitarbeiter.' }}
    </p>
  </div>
</template>
