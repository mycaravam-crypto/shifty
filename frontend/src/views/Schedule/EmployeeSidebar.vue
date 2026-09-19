<script setup lang="ts">
import { employeeDragPayload, type DragPayload, type DragState } from './composables/useScheduleDnD'
import { currencyFmt } from './format'
import type { Employee } from './types'

// The "Nach Schicht" view's palette (requested directly — with only a handful of shift types
// but 20+ employees, dragging shifts onto employee rows is the wrong way round). Employees are
// listed here instead of as grid rows, each draggable onto a (shiftType, date) cell — the same
// per-employee Xh/Yh/Übertrag/Lohnkosten readouts EmployeeScheduleRow.vue shows per row move
// here, since there's no employee row left to show them on in this view.
const props = defineProps<{
  employees: Employee[]
  activeEmployeesCount: number
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
      <template v-if="targetHoursFor(e.id) !== null">
        <div
          class="font-mono text-[11px] mt-0.5"
          :class="netHoursFor(e.id) !== targetHoursFor(e.id) ? 'text-amber-400' : 'text-slate-500'"
        >
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
