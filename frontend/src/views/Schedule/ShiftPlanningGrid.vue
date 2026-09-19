<script setup lang="ts">
import { ref } from 'vue'
import ShiftTypeScheduleRow from './ShiftTypeScheduleRow.vue'
import type { DragPayload, DragState } from './composables/useScheduleDnD'
import type { Coverage } from './composables/usePlanningBoard'
import { toIso, weekdayFmt } from './format'
import type { Assignment, Employee, PublicHoliday, ShiftType } from './types'

// The "Nach Schicht" view's grid — PlanningGrid.vue's counterpart with the axes swapped
// (rows = ShiftType, not Employee). Mirrors its sticky-header/sticky-column and edge
// auto-scroll setup exactly; see PlanningGrid.vue for why both exist (overflow-x-auto forces
// overflow-y auto too, so a bounded, genuinely-scrolling panel is what sticky positioning
// actually binds to — issue #76).
defineProps<{
  days: Date[]
  shiftTypes: ShiftType[]
  holidayFor: (dateIso: string) => PublicHoliday | undefined
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
    class="glass rounded-xl overflow-auto max-h-[70vh] flex-1 min-w-0 print:hidden"
  >
    <table class="w-full text-sm">
      <thead>
        <tr
          class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8 sticky top-0 z-20 bg-[#11141c] shadow-[0_4px_8px_-4px_rgba(0,0,0,0.5)]"
        >
          <th
            class="px-4 py-3 sticky left-0 z-30 bg-[#11141c] shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)] min-w-[140px]"
          >
            Schicht
          </th>
          <th
            v-for="d in days"
            :key="toIso(d)"
            class="px-3 py-3 min-w-[110px]"
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
          </th>
        </tr>
      </thead>
      <tbody>
        <ShiftTypeScheduleRow
          v-for="s in shiftTypes"
          :key="s.id"
          :shift-type="s"
          :days="days"
          :is-weekend="isWeekend"
          :drag-over-key="dragOverKey"
          :highlight-key="highlightKey"
          :employee-by-id="employeeById"
          :assignments-for-shift="assignmentsForShift"
          :coverage-for="coverageFor"
          :drag="drag"
          :is-editable="isEditable"
          :chip-pointer-down="chipPointerDown"
          :is-focusable-cell="isFocusableCell"
          :cell-aria-label="cellAriaLabel"
          :on-cell-focus="onCellFocus"
          @view-readonly="emit('view-readonly', $event)"
          @suggest="emit('suggest', $event)"
        />
        <tr v-if="!shiftTypes.length">
          <td :colspan="days.length + 1" class="px-4 py-8 text-center text-slate-500">
            Keine Schichtarten angelegt.
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
