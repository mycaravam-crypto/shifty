<script setup lang="ts">
import { Sparkles } from '@lucide/vue'
import {
  assignmentDragPayload,
  type DragPayload,
  type DragState,
} from './composables/useScheduleDnD'
import type { Coverage } from './composables/usePlanningBoard'
import { toIso } from './format'
import type { Assignment, Employee, ShiftType } from './types'

// The "Nach Schicht" view's row (requested directly — the counterpart to
// EmployeeScheduleRow.vue with the axes swapped: one row per ShiftType instead of per Employee,
// each day cell holding the employees assigned to that shift rather than the shifts assigned to
// that employee).
const props = defineProps<{
  shiftType: ShiftType
  days: Date[]
  isWeekend: (d: Date) => boolean
  dragOverKey: string | null
  highlightKey: string | null
  employeeById: (id: string) => Employee | undefined
  assignmentsForShift: (shiftTypeId: string, dateIso: string) => Assignment[]
  coverageFor: (shiftTypeId: string, dateIso: string) => Coverage
  drag: DragState | null
  isEditable: boolean
  chipPointerDown: (e: PointerEvent, payload: DragPayload) => void
  isFocusableCell: (shiftTypeId: string, dateIso: string) => boolean
  cellAriaLabel: (shiftTypeId: string, dateIso: string) => string
  onCellFocus: (shiftTypeId: string, dateIso: string) => void
}>()
const emit = defineEmits<{
  'view-readonly': [assignment: Assignment]
  suggest: [shiftType: ShiftType]
}>()

function isCellHighlighted(dateIso: string): boolean {
  const key = `${props.shiftType.id}|${dateIso}`
  return props.dragOverKey === key || props.highlightKey === key
}
</script>

<template>
  <tr class="border-b border-white/5 last:border-0">
    <td
      class="px-4 py-3 align-top sticky left-0 z-10 bg-[#11141c] shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)]"
    >
      <div class="flex items-center gap-1.5 text-sm">
        <span
          class="w-2.5 h-2.5 rounded-full shrink-0"
          :style="{ backgroundColor: shiftType.color }"
        ></span>
        {{ shiftType.name }}
        <button
          v-if="isEditable"
          type="button"
          title="Mitarbeiter vorschlagen"
          class="relative text-slate-500 hover:text-indigo-300 transition-colors"
          @click="emit('suggest', shiftType)"
        >
          <Sparkles :size="12" />
          <!-- issue #80: invisible hit-slop, same reasoning as ShiftPalette's own sparkle. -->
          <span class="absolute -inset-3.5" aria-hidden="true"></span>
        </button>
      </div>
      <div class="font-mono text-[11px] text-slate-500 mt-0.5">
        {{ shiftType.startTime.slice(0, 5) }}–{{ shiftType.endTime.slice(0, 5) }}
      </div>
    </td>
    <td
      v-for="d in days"
      :key="toIso(d)"
      class="px-2 py-2 align-top transition-colors outline-none focus-visible:ring-2 focus-visible:ring-indigo-400 focus-visible:ring-inset"
      :class="{
        'bg-blue-500/10 ring-1 ring-inset ring-blue-500/50': isCellHighlighted(toIso(d)),
        'bg-white/[0.03]': isWeekend(d) && !isCellHighlighted(toIso(d)),
      }"
      :data-row-id="shiftType.id"
      :data-date="toIso(d)"
      :tabindex="isFocusableCell(shiftType.id, toIso(d)) ? 0 : -1"
      role="gridcell"
      :aria-label="cellAriaLabel(shiftType.id, toIso(d))"
      @focus="onCellFocus(shiftType.id, toIso(d))"
    >
      <div
        v-if="coverageFor(shiftType.id, toIso(d)).target > 0"
        class="font-mono text-[10px] mb-1"
        :class="{
          'text-rose-400': coverageFor(shiftType.id, toIso(d)).status === 'under',
          'text-amber-400': coverageFor(shiftType.id, toIso(d)).status === 'over',
          'text-slate-600': coverageFor(shiftType.id, toIso(d)).status === 'ok',
        }"
      >
        {{ coverageFor(shiftType.id, toIso(d)).count }}/{{
          coverageFor(shiftType.id, toIso(d)).target
        }}
        <span v-if="coverageFor(shiftType.id, toIso(d)).status !== 'ok'">⚠</span>
      </div>
      <div
        v-for="a in assignmentsForShift(shiftType.id, toIso(d))"
        :key="a.id"
        class="rounded-lg bg-white/5 border border-white/10 px-2 py-1 mb-1 cursor-pointer hover:bg-white/10 transition-colors touch-none select-none"
        :class="{
          'opacity-40':
            drag?.active &&
            drag.payload.kind === 'assignment' &&
            drag.payload.assignmentId === a.id,
        }"
        @pointerdown="isEditable && chipPointerDown($event, assignmentDragPayload(a, shiftType))"
        @click="!isEditable && emit('view-readonly', a)"
      >
        <div class="text-xs">
          {{ employeeById(a.employeeId)?.lastName }}, {{ employeeById(a.employeeId)?.firstName }}
        </div>
        <div class="font-mono text-[11px] text-slate-500">
          {{ a.startTime.slice(0, 5) }}–{{ a.endTime.slice(0, 5) }}
          <span v-if="a.endsNextDay" title="Endet am nächsten Tag">(+1)</span>
        </div>
      </div>
    </td>
  </tr>
</template>
