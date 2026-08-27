<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import axios from 'axios'
import { ChevronsLeft, Search } from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import ModalShell from '@/components/ModalShell.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ShiftAssignmentModal from './ShiftAssignmentModal.vue'
import ShiftSuggestionModal from './ShiftSuggestionModal.vue'
import PlanningToolbar from './PlanningToolbar.vue'
import ShiftPalette from './ShiftPalette.vue'
import ValidationSummary from './ValidationSummary.vue'
import PlanningGrid from './PlanningGrid.vue'
import SchedulePrintSheet from './SchedulePrintSheet.vue'
import { useScheduleFilters } from './composables/useScheduleFilters'
import { usePlanningBoard } from './composables/usePlanningBoard'
import { usePlanningActions } from './composables/usePlanningActions'
import { useScheduleDnD } from './composables/useScheduleDnD'
import { useGridKeyboardNav } from './composables/useGridKeyboardNav'
import { addDays, parseIso, startOfWeek, toIso } from './format'
import type { Assignment, ShiftType, ValidationIssue, ValidationResult } from './types'

const toast = useToastStore()
const route = useRoute()
const router = useRouter()

// issue #73: this file is orchestration only now — every DTO/date-math/formatting helper,
// data-loading concern, drag-and-drop mechanic, and mutation call that used to live here
// directly has moved into the composables/components imported above. Behavior is unchanged;
// see each file's own header comment for what it owns.
//
// issue #74: this view is week-scoped — the route's `:date` param is any date within the
// displayed week (MonthOverviewView.vue always passes the week's first visible day). `Schedule`
// itself stays month-scoped server-side (usePlanningBoard's `days`/`currentSchedule` are
// unchanged), only the *displayed* grid columns narrow to one week here.
const dayMonthFmt = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' })

const filters = useScheduleFilters()
const { search, teamFilter, searchInputRef } = filters

const board = usePlanningBoard(filters)
const {
  teams,
  shiftTypes,
  assignments,
  validation,
  monthStartIso,
  monthLabel,
  activeEmployees,
  activeShiftTypes,
  visibleEmployees,
  currentSchedule,
  isDraft,
  isPublished,
  isEditable,
  blockingErrorCount,
  publishBlockReason,
  days,
  shiftTypeById,
  coverageShiftTypes,
  coverageFor,
  holidayFor,
  isWeekend,
  isAbsentOn,
  assignmentsFor,
  netHoursFor,
  targetHoursFor,
  carriedOverFor,
  laborCostFor,
  totalLaborCost,
  loading,
  error,
  load,
  loadDetail,
} = board

const actions = usePlanningActions(board)
const {
  creatingSchedule,
  publishing,
  archiving,
  confirmingArchive,
  onCreateSchedule,
  onPublish,
  onArchiveConfirmed,
  performDrop,
  onAssignmentUpdated,
} = actions

// The route's `:date` drives which month's Schedule is active (board.anchorDate) — the same
// single-date resolution the old full-month grid used, just fed a week-anchor date instead of
// always "today". Set eagerly (immediate) so it's already correct before board.load() runs.
const routeDateIso = computed(() => {
  const raw = route.params.date
  return typeof raw === 'string' ? raw : toIso(new Date())
})
watch(
  routeDateIso,
  (iso) => {
    board.anchorDate.value = parseIso(iso)
  },
  { immediate: true },
)
const weekStart = computed(() => startOfWeek(parseIso(routeDateIso.value)))
const weekEnd = computed(() => addDays(weekStart.value, 6))
const weekDays = computed(() =>
  days.value.filter((d) => d >= weekStart.value && d <= weekEnd.value),
)
const weekLabel = computed(() => {
  if (!weekDays.value.length)
    return `${dayMonthFmt.format(weekStart.value)}–${dayMonthFmt.format(weekEnd.value)}`
  const first = weekDays.value[0]
  const last = weekDays.value[weekDays.value.length - 1]
  return `${dayMonthFmt.format(first)}–${dayMonthFmt.format(last)} ${last.getFullYear()}`
})
// A week straddling two calendar months only shows the portion belonging to this month's
// Schedule (weekDays is clamped against board.days, which is itself clamped to the Schedule's
// own span) — surface that plainly rather than silently truncating.
const isPartialWeek = computed(() => weekDays.value.length > 0 && weekDays.value.length < 7)

function goToWeek(date: Date) {
  router.push({ name: 'schedule-week', params: { date: toIso(date) }, query: route.query })
}
function prevWeek() {
  goToWeek(addDays(weekStart.value, -7))
}
function nextWeek() {
  goToWeek(addDays(weekStart.value, 7))
}
// Back to the compact month overview (issue #74's other half) — month-copy/auto-fill live
// there now, not per-week, since both operate on the whole (month-scoped) Schedule.
function goToOverview() {
  router.push({ name: 'schedule', query: { ...route.query, month: monthStartIso.value } })
}

// issue #74: the validation panel here only shows issues relevant to the displayed week — a
// schedule-wide issue with no specific assignment (e.g. ContractHoursExceeded) still shows in
// every week of its Schedule (it isn't tied to one date), but an issue tied to a specific
// assignment (e.g. InsufficientRest, InsufficientBreak) only shows in the week that assignment
// actually falls in. ScheduleValidator's output shape itself is unchanged.
const weekDayIsoSet = computed(() => new Set(weekDays.value.map(toIso)))
function issueInWeek(issue: ValidationIssue): boolean {
  if (!issue.shiftAssignmentId) return true
  const assignment = assignments.value.find((a) => a.id === issue.shiftAssignmentId)
  return assignment ? weekDayIsoSet.value.has(assignment.date) : true
}
const weekValidation = computed<ValidationResult | null>(() => {
  if (!validation.value) return null
  return {
    isValid: validation.value.isValid,
    errors: validation.value.errors.filter(issueInWeek),
    warnings: validation.value.warnings.filter(issueInWeek),
  }
})

const selectedAssignment = ref<Assignment | null>(null)
const deletingAssignment = ref<Assignment | null>(null)
const showShortcuts = ref(false)
const highlightKey = ref<string | null>(null)
const suggestingShiftType = ref<ShiftType | null>(null)

const { isFocusableCell, cellAriaLabel, onCellFocus, focusedGridCellEl, onGridCellKeydown } =
  useGridKeyboardNav({
    visibleEmployees: () => visibleEmployees.value,
    days: () => weekDays.value,
    assignmentsFor,
    shiftTypeById,
    onOpen: (assignment) => {
      selectedAssignment.value = assignment
    },
    onDelete: (assignment) => {
      deletingAssignment.value = assignment
    },
  })

// issue #80: Delete/Backspace on a focused grid cell — the same delete call and toast pattern
// ShiftAssignmentModal's own delete button uses, just reachable without opening the edit modal
// first.
async function onDeleteAssignmentConfirmed() {
  if (!deletingAssignment.value) return
  try {
    await api.delete(`/assignments/${deletingAssignment.value.id}`, {
      params: { rowVersion: deletingAssignment.value.rowVersion },
    })
    toast.success('Schicht gelöscht.')
    await loadDetail()
  } catch (err) {
    // issue #156: someone else changed this assignment since the grid last loaded.
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      toast.error(
        'Diese Schicht wurde inzwischen von jemand anderem geändert. Ansicht aktualisiert.',
      )
      await loadDetail()
    } else {
      toast.error('Schicht konnte nicht gelöscht werden.')
    }
  } finally {
    deletingAssignment.value = null
  }
}

const gridRef = ref<InstanceType<typeof PlanningGrid> | null>(null)
const { drag, dragOverKey, onChipPointerDown } = useScheduleDnD({
  onDrop: performDrop,
  onTap: (payload) => {
    if (payload.kind !== 'assignment') return
    const assignment = assignments.value.find((a) => a.id === payload.assignmentId)
    if (assignment) selectedAssignment.value = assignment
  },
  onAutoScroll: (clientX) => gridRef.value?.autoScrollTableWrap(clientX),
})

// issue #79: once a Schedule isn't Draft, PlanningGrid/EmployeeScheduleRow don't attach the
// pointerdown-based drag handler at all (it would just 409) — a plain click still needs to open
// the assignment modal in read-only mode instead.
function viewAssignmentReadonly(assignment: Assignment) {
  selectedAssignment.value = assignment
}

async function handleAssignmentUpdated() {
  selectedAssignment.value = null
  await onAssignmentUpdated()
}

// issue #39: jump to and briefly highlight the row/cell a validation issue is about.
function focusIssue(issue: ValidationIssue) {
  if (!issue.employeeId) return
  const assignment = issue.shiftAssignmentId
    ? assignments.value.find((a) => a.id === issue.shiftAssignmentId)
    : undefined
  const selector = assignment
    ? `[data-employee-id="${issue.employeeId}"][data-date="${assignment.date}"]`
    : `[data-employee-id="${issue.employeeId}"]`
  document
    .querySelector(selector)
    ?.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'center' })
  highlightKey.value = assignment ? `${issue.employeeId}|${assignment.date}` : issue.employeeId
  window.setTimeout(() => {
    highlightKey.value = null
  }, 1500)
}

function isTyping(): boolean {
  const tag = document.activeElement?.tagName
  return tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA'
}
function onKeydown(e: KeyboardEvent) {
  if (selectedAssignment.value || showShortcuts.value || deletingAssignment.value) return
  if (e.key === '/' && !isTyping()) {
    e.preventDefault()
    searchInputRef.value?.focus()
    return
  }
  if (e.key === '?' && !isTyping()) {
    showShortcuts.value = true
    return
  }
  // issue #80: keyboard nav for the grid coexists with month-nav below — it only fires once a
  // day cell already has keyboard focus (Tab into the grid, or click a cell); otherwise the
  // pre-existing ArrowLeft/Right month-nav applies as before.
  const gridCell = focusedGridCellEl()
  if (gridCell) {
    onGridCellKeydown(e, gridCell.dataset.employeeId!, gridCell.dataset.date!)
    return
  }
  if (e.key === 'ArrowLeft' && !isTyping()) {
    prevWeek()
  } else if (e.key === 'ArrowRight' && !isTyping()) {
    nextWeek()
  }
}

onMounted(load)
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))

// PDF export = the browser's own print-to-PDF, scoped via CSS rather than a PDF-generation
// library. `printEmployeeId` narrows the printed table to one row; "all" export is just
// printing with it unset.
const printEmployeeId = ref<string | null>(null)
async function exportAllPdf() {
  printEmployeeId.value = null
  await nextTick()
  window.print()
}
async function exportEmployeePdf(employeeId: string) {
  printEmployeeId.value = employeeId
  await nextTick()
  window.print()
}
window.addEventListener('afterprint', () => {
  printEmployeeId.value = null
})
</script>

<template>
  <div class="p-8" :class="{ 'select-none': drag?.active }">
    <div
      v-if="drag?.active"
      class="fixed z-50 pointer-events-none flex items-center gap-2 rounded-lg bg-[#11141c] border border-white/20 px-3 py-1.5 text-sm shadow-lg"
      :style="{ left: drag.x + 14 + 'px', top: drag.y + 14 + 'px' }"
    >
      <span
        class="w-2.5 h-2.5 rounded-full shrink-0"
        :style="{ backgroundColor: drag.payload.color }"
      ></span>
      {{ drag.payload.label }}
      <span class="font-mono text-slate-500 text-xs">{{ drag.payload.time }}</span>
    </div>

    <button
      class="flex items-center gap-1 text-xs text-slate-500 hover:text-slate-300 transition-colors print:hidden mb-2"
      title="Zur Monatsübersicht"
      @click="goToOverview"
    >
      <ChevronsLeft :size="12" />
      Monatsübersicht
    </button>

    <PlanningToolbar
      :month-label="weekLabel"
      :current-schedule="currentSchedule ?? null"
      :is-draft="!!isDraft"
      :is-published="!!isPublished"
      :publishing="publishing"
      :archiving="archiving"
      :blocking-error-count="blockingErrorCount"
      :publish-block-reason="publishBlockReason"
      @prev="prevWeek"
      @next="nextWeek"
      @show-shortcuts="showShortcuts = true"
      @export-all="exportAllPdf"
      @publish="onPublish"
      @archive="confirmingArchive = true"
    />

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>
    <div v-if="loading" class="space-y-4" aria-label="Lädt…">
      <div class="flex gap-2">
        <div v-for="i in 4" :key="i" class="h-9 w-32 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
      <div class="glass rounded-xl p-4 space-y-3">
        <div v-for="i in 6" :key="i" class="h-10 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
    </div>

    <template v-else>
      <div v-if="!currentSchedule" class="glass rounded-xl p-8 text-center">
        <p class="text-sm text-slate-500 mb-4">
          Für diesen Monat ({{ monthLabel }}) existiert noch kein Dienstplan.
        </p>
        <button
          :disabled="creatingSchedule"
          class="rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
          @click="onCreateSchedule"
        >
          {{ creatingSchedule ? 'Anlegen…' : 'Diesen Monat anlegen' }}
        </button>
      </div>

      <template v-else>
        <p v-if="isPartialWeek" class="text-xs text-slate-500 mb-3 print:hidden">
          Diese Woche liegt teilweise in einem anderen Monat — hier wird nur der Teil im
          {{ monthLabel }}-Dienstplan angezeigt.
        </p>
        <ValidationSummary :validation="weekValidation" @focus="focusIssue" />

        <ShiftPalette
          :active-shift-types="activeShiftTypes"
          :has-assignments="assignments.length > 0"
          :copying-month="false"
          :total-labor-cost="totalLaborCost"
          :is-editable="!!isEditable"
          :show-month-actions="false"
          :chip-pointer-down="onChipPointerDown"
          @suggest="suggestingShiftType = $event"
        />

        <div class="flex flex-wrap items-center gap-2 mb-4 print:hidden">
          <div class="relative">
            <Search :size="14" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-500" />
            <input
              ref="searchInputRef"
              v-model="search"
              type="text"
              placeholder="Mitarbeiter suchen… (/)"
              class="rounded-lg bg-white/5 border border-white/10 pl-8 pr-3 py-1.5 text-sm placeholder:text-slate-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
            />
          </div>
          <select
            v-model="teamFilter"
            class="rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
          >
            <option value="">Alle Teams</option>
            <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
          </select>
        </div>

        <PlanningGrid
          ref="gridRef"
          :days="weekDays"
          :visible-employees="visibleEmployees"
          :active-employees-count="activeEmployees.length"
          :holiday-for="holidayFor"
          :is-weekend="isWeekend"
          :drag-over-key="dragOverKey"
          :highlight-key="highlightKey"
          :print-employee-id="printEmployeeId"
          :shift-type-by-id="shiftTypeById"
          :coverage-shift-types="coverageShiftTypes"
          :coverage-for="coverageFor"
          :assignments-for="assignmentsFor"
          :target-hours-for="targetHoursFor"
          :net-hours-for="netHoursFor"
          :carried-over-for="carriedOverFor"
          :labor-cost-for="laborCostFor"
          :drag="drag"
          :is-editable="!!isEditable"
          :chip-pointer-down="onChipPointerDown"
          :is-focusable-cell="isFocusableCell"
          :cell-aria-label="cellAriaLabel"
          :on-cell-focus="onCellFocus"
          @export-employee-pdf="exportEmployeePdf"
          @view-readonly="viewAssignmentReadonly"
        />

        <SchedulePrintSheet
          :print-employee-id="printEmployeeId"
          :employees="visibleEmployees"
          :days="weekDays"
          :schedule-name="currentSchedule?.name ?? ''"
          :schedule-status="currentSchedule?.status ?? null"
          :period-label="weekLabel"
          :holiday-for="holidayFor"
          :is-weekend="isWeekend"
          :is-absent-on="isAbsentOn"
          :shift-type-by-id="shiftTypeById"
          :assignments-for="assignmentsFor"
          :net-hours-for="netHoursFor"
          :target-hours-for="targetHoursFor"
          :carried-over-for="carriedOverFor"
          :labor-cost-for="laborCostFor"
        />
      </template>
    </template>

    <ShiftAssignmentModal
      v-if="selectedAssignment"
      :assignment="selectedAssignment"
      :shift-types="shiftTypes"
      :readonly="!isEditable"
      @close="selectedAssignment = null"
      @updated="handleAssignmentUpdated"
    />

    <ConfirmDialog
      v-if="confirmingArchive"
      title="Dienstplan archivieren"
      message="Diesen veröffentlichten Dienstplan als archiviert markieren? Er bleibt einsehbar, kann aber nicht mehr bearbeitet werden."
      confirm-label="Archivieren"
      @confirm="onArchiveConfirmed"
      @close="confirmingArchive = false"
    />

    <!-- issue #80: Delete/Backspace on a focused grid cell -->
    <ConfirmDialog
      v-if="deletingAssignment"
      title="Schicht löschen"
      message="Diese Schicht wirklich löschen?"
      @confirm="onDeleteAssignmentConfirmed"
      @close="deletingAssignment = null"
    />

    <ShiftSuggestionModal
      v-if="suggestingShiftType && currentSchedule && weekDays.length"
      :schedule-id="currentSchedule.id"
      :shift-type="suggestingShiftType"
      :default-date="toIso(weekDays[0])"
      :min-date="toIso(weekDays[0])"
      :max-date="toIso(weekDays[weekDays.length - 1])"
      @close="suggestingShiftType = null"
      @assigned="loadDetail"
    />

    <ModalShell v-if="showShortcuts" title="Tastenkürzel" @close="showShortcuts = false">
      <ul class="text-sm divide-y divide-white/5">
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Suche fokussieren</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">/</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Vorherige Woche (außerhalb des Rasters)</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">←</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Nächste Woche (außerhalb des Rasters)</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">→</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Diese Übersicht öffnen</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">?</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Dialog schließen</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">Esc</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Zwischen Tageszellen bewegen (nach Fokus im Raster)</span>
          <span class="flex gap-1">
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">↑</kbd>
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">↓</kbd>
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">←</kbd>
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">→</kbd>
          </span>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Schicht der fokussierten Zelle öffnen</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">Enter</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Schicht der fokussierten Zelle löschen</span>
          <span class="flex gap-1">
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">Entf</kbd>
            <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">⌫</kbd>
          </span>
        </li>
      </ul>
    </ModalShell>
  </div>
</template>
