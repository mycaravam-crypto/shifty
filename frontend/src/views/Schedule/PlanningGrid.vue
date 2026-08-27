<script setup lang="ts">
import { ref } from 'vue'
import EmployeeScheduleRow from './EmployeeScheduleRow.vue'
import type { DragPayload, DragState } from './composables/useScheduleDnD'
import { toIso, weekdayFmt } from './format'
import type { Assignment, Employee, PublicHoliday, ShiftType } from './types'
import type { Coverage } from './composables/usePlanningBoard'

defineProps<{
  days: Date[]
  visibleEmployees: Employee[]
  activeEmployeesCount: number
  holidayFor: (dateIso: string) => PublicHoliday | undefined
  isWeekend: (d: Date) => boolean
  dragOverKey: string | null
  highlightKey: string | null
  printEmployeeId: string | null
  shiftTypeById: (id: string) => ShiftType | undefined
  coverageShiftTypes: ShiftType[]
  coverageFor: (shiftTypeId: string, dateIso: string) => Coverage
  assignmentsFor: (employeeId: string, dateIso: string) => Assignment[]
  targetHoursFor: (employeeId: string) => number | null
  netHoursFor: (employeeId: string) => number
  carriedOverFor: (employeeId: string) => number
  laborCostFor: (employeeId: string) => number | null
  drag: DragState | null
  isEditable: boolean
  chipPointerDown: (e: PointerEvent, payload: DragPayload) => void
  isFocusableCell: (employeeId: string, dateIso: string) => boolean
  cellAriaLabel: (employeeId: string, dateIso: string) => string
  onCellFocus: (employeeId: string, dateIso: string) => void
}>()
const emit = defineEmits<{
  'export-employee-pdf': [employeeId: string]
  'view-readonly': [assignment: Assignment]
}>()

// Auto-scrolls the horizontally-scrolling table while dragging near its edge — otherwise a
// month with 28+ day columns has no way to reach off-screen days mid-drag. Driven by
// useScheduleDnD's onAutoScroll callback (via ScheduleView's template ref to this component),
// so the DOM node this needs stays local to the component that owns it.
const DRAG_SCROLL_EDGE_PX = 60
const DRAG_SCROLL_SPEED_PX = 12
const tableWrapRef = ref<HTMLElement | null>(null)
function autoScrollTableWrap(clientX: number) {
  const wrap = tableWrapRef.value
  if (!wrap) return
  const rect = wrap.getBoundingClientRect()
  if (clientX < rect.left + DRAG_SCROLL_EDGE_PX) wrap.scrollLeft -= DRAG_SCROLL_SPEED_PX
  else if (clientX > rect.right - DRAG_SCROLL_EDGE_PX) wrap.scrollLeft += DRAG_SCROLL_SPEED_PX
}
defineExpose({ autoScrollTableWrap })
</script>

<template>
  <div
    ref="tableWrapRef"
    class="glass rounded-xl overflow-auto max-h-[70vh] print:overflow-visible print:max-h-none"
  >
    <table class="w-full text-sm">
      <thead>
        <tr
          class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8 sticky top-0 z-20 bg-[#11141c] shadow-[0_4px_8px_-4px_rgba(0,0,0,0.5)] print:static print:shadow-none"
        >
          <th
            class="px-4 py-3 sticky left-0 z-30 bg-[#11141c] shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)] print:static print:shadow-none"
          >
            Mitarbeiter
          </th>
          <th
            v-for="d in days"
            :key="toIso(d)"
            class="px-3 py-3 min-w-[130px]"
            :class="{
              'text-amber-400': holidayFor(toIso(d)),
              'bg-white/[0.03]': isWeekend(d),
            }"
            :title="holidayFor(toIso(d))?.name"
          >
            <span class="inline-flex items-center gap-1">
              {{ weekdayFmt.format(d) }}
              <span
                v-if="holidayFor(toIso(d))"
                class="w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0"
              ></span>
            </span>
            <div
              v-if="coverageShiftTypes.length"
              class="mt-1 space-y-0.5 normal-case tracking-normal"
            >
              <div
                v-for="s in coverageShiftTypes"
                :key="s.id"
                class="flex items-center gap-1 font-mono text-[10px] font-normal"
                :class="{
                  'text-rose-400': coverageFor(s.id, toIso(d)).status === 'under',
                  'text-amber-400': coverageFor(s.id, toIso(d)).status === 'over',
                  'text-slate-600': coverageFor(s.id, toIso(d)).status === 'ok',
                }"
                :title="`${s.name}: ${coverageFor(s.id, toIso(d)).count} / ${coverageFor(s.id, toIso(d)).target} besetzt`"
              >
                <span
                  class="w-1.5 h-1.5 rounded-full shrink-0"
                  :style="{ backgroundColor: s.color }"
                ></span>
                {{ coverageFor(s.id, toIso(d)).count }}/{{ coverageFor(s.id, toIso(d)).target }}
                <span v-if="coverageFor(s.id, toIso(d)).status !== 'ok'">⚠</span>
              </div>
            </div>
          </th>
        </tr>
      </thead>
      <tbody>
        <EmployeeScheduleRow
          v-for="e in visibleEmployees"
          :key="e.id"
          :employee="e"
          :days="days"
          :is-weekend="isWeekend"
          :drag-over-key="dragOverKey"
          :highlight-key="highlightKey"
          :print-employee-id="printEmployeeId"
          :shift-type-by-id="shiftTypeById"
          :assignments-for="assignmentsFor"
          :target-hours-for="targetHoursFor"
          :net-hours-for="netHoursFor"
          :carried-over-for="carriedOverFor"
          :labor-cost-for="laborCostFor"
          :drag="drag"
          :is-editable="isEditable"
          :chip-pointer-down="chipPointerDown"
          :is-focusable-cell="isFocusableCell"
          :cell-aria-label="cellAriaLabel"
          :on-cell-focus="onCellFocus"
          @export-pdf="emit('export-employee-pdf', $event)"
          @view-readonly="emit('view-readonly', $event)"
        />
        <tr v-if="!visibleEmployees.length">
          <td :colspan="days.length + 1" class="px-4 py-8 text-center text-slate-500">
            {{ activeEmployeesCount ? 'Keine Treffer.' : 'Keine Mitarbeiter.' }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
