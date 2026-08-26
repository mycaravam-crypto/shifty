<script setup lang="ts">
import { Printer } from '@lucide/vue'
import {
  assignmentDragPayload,
  type DragPayload,
  type DragState,
} from './composables/useScheduleDnD'
import { currencyFmt, toIso } from './format'
import type { Assignment, Employee, ShiftType } from './types'

const props = defineProps<{
  employee: Employee
  days: Date[]
  isWeekend: (d: Date) => boolean
  dragOverKey: string | null
  highlightKey: string | null
  printEmployeeId: string | null
  shiftTypeById: (id: string) => ShiftType | undefined
  assignmentsFor: (employeeId: string, dateIso: string) => Assignment[]
  targetHoursFor: (employeeId: string) => number | null
  netHoursFor: (employeeId: string) => number
  carriedOverFor: (employeeId: string) => number
  laborCostFor: (employeeId: string) => number | null
  drag: DragState | null
  isDraft: boolean
  chipPointerDown: (e: PointerEvent, payload: DragPayload) => void
}>()
const emit = defineEmits<{
  'export-pdf': [employeeId: string]
  'view-readonly': [assignment: Assignment]
}>()

function isCellHighlighted(dateIso: string): boolean {
  const key = `${props.employee.id}|${dateIso}`
  return props.dragOverKey === key || props.highlightKey === key
}
function barWidth(employeeId: string): number {
  const target = props.targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (props.netHoursFor(employeeId) / target) * 100)
}
</script>

<template>
  <tr
    class="border-b border-white/5 last:border-0"
    :class="{
      'print:hidden': printEmployeeId && printEmployeeId !== employee.id,
      'bg-blue-500/10': highlightKey === employee.id,
    }"
  >
    <td
      class="px-4 py-3 align-top sticky left-0 z-10 shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)] print:static print:shadow-none"
      :style="{ backgroundColor: highlightKey === employee.id ? '#161d2f' : '#11141c' }"
    >
      <div class="flex items-center gap-1.5">
        {{ employee.lastName }}, {{ employee.firstName }}
        <button
          class="text-slate-500 hover:text-slate-200 transition-colors print:hidden"
          title="Nur diesen Mitarbeiter als PDF exportieren"
          @click="emit('export-pdf', employee.id)"
        >
          <Printer :size="12" />
        </button>
      </div>
      <template v-if="targetHoursFor(employee.id) !== null">
        <div
          class="font-mono text-xs mt-1"
          :class="
            netHoursFor(employee.id) !== targetHoursFor(employee.id)
              ? 'text-amber-400'
              : 'text-slate-500'
          "
        >
          {{ netHoursFor(employee.id) }}h / {{ targetHoursFor(employee.id) }}h
          <span v-if="netHoursFor(employee.id) !== targetHoursFor(employee.id)">⚠</span>
        </div>
        <div
          v-if="carriedOverFor(employee.id) !== 0"
          class="font-mono text-[11px] mt-0.5"
          :class="carriedOverFor(employee.id) > 0 ? 'text-emerald-400' : 'text-rose-400'"
        >
          Übertrag: {{ carriedOverFor(employee.id) > 0 ? '+' : ''
          }}{{ carriedOverFor(employee.id) }}h
        </div>
        <div class="w-24 h-1 rounded-full bg-white/10 mt-1 overflow-hidden">
          <div
            class="h-full bg-linear-to-r from-blue-600 to-indigo-600"
            :style="{ width: barWidth(employee.id) + '%' }"
          ></div>
        </div>
      </template>
      <div
        v-if="laborCostFor(employee.id) !== null"
        class="font-mono text-xs text-emerald-400 mt-1"
      >
        {{ currencyFmt.format(laborCostFor(employee.id)!) }}
      </div>
    </td>
    <td
      v-for="d in days"
      :key="toIso(d)"
      class="px-2 py-2 align-top transition-colors"
      :class="{
        'bg-blue-500/10 ring-1 ring-inset ring-blue-500/50': isCellHighlighted(toIso(d)),
        'bg-white/[0.03]': isWeekend(d) && !isCellHighlighted(toIso(d)),
      }"
      :data-employee-id="employee.id"
      :data-date="toIso(d)"
    >
      <div
        v-for="a in assignmentsFor(employee.id, toIso(d))"
        :key="a.id"
        class="rounded-lg bg-white/5 border border-white/10 px-2 py-1 mb-1 cursor-pointer hover:bg-white/10 transition-colors touch-none select-none"
        :class="{
          'opacity-40':
            drag?.active &&
            drag.payload.kind === 'assignment' &&
            drag.payload.assignmentId === a.id,
        }"
        @pointerdown="
          isDraft && chipPointerDown($event, assignmentDragPayload(a, shiftTypeById(a.shiftTypeId)))
        "
        @click="!isDraft && emit('view-readonly', a)"
      >
        <div class="flex items-center gap-1.5 text-xs">
          <span
            class="w-2 h-2 rounded-full shrink-0"
            :style="{ backgroundColor: shiftTypeById(a.shiftTypeId)?.color }"
          ></span>
          {{ shiftTypeById(a.shiftTypeId)?.name }}
        </div>
        <div class="font-mono text-[11px] text-slate-500">
          {{ a.startTime.slice(0, 5) }}–{{ a.endTime.slice(0, 5) }}
        </div>
      </div>
    </td>
  </tr>
</template>
