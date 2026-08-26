<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowUpRight, ChevronLeft, ChevronRight, Copy, Search, Wand2 } from '@lucide/vue'
import AutoFillModal from './AutoFillModal.vue'
import { useScheduleFilters } from './composables/useScheduleFilters'
import { usePlanningBoard } from './composables/usePlanningBoard'
import { usePlanningActions } from './composables/usePlanningActions'
import { parseIso, startOfWeek, toIso } from './format'
import type { ValidationIssue } from './types'

// issue #74: the Dienstplan's default landing view — a compact month-wide coverage/absence/
// conflict glance, replacing what used to be the single full-month drag-and-drop grid trying
// to be both an editor and an overview at once. Detailed editing (drag-and-drop, the
// assignment modal, the grouped validation panel, keyboard shortcuts) now lives in the
// week-scoped ScheduleView.vue, reached by clicking a week/day header here. `Schedule` itself
// stays month-scoped server-side — this reuses the exact same usePlanningBoard/usePlanningActions
// composables the week view uses, just rendered as a dense per-day-per-employee grid instead of
// full shift chips.

const route = useRoute()
const router = useRouter()

const dayMonthFmt = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' })
const weekdayLetterFmt = new Intl.DateTimeFormat('de-DE', { weekday: 'narrow' })

const filters = useScheduleFilters()
const { search, teamFilter } = filters

const board = usePlanningBoard(filters)
const {
  teams,
  assignments,
  validation,
  monthStartIso,
  monthLabel,
  activeEmployees,
  activeShiftTypes,
  visibleEmployees,
  currentSchedule,
  days,
  shiftTypeById,
  holidayFor,
  isWeekend,
  isAbsentOn,
  assignmentsFor,
  loading,
  error,
  load,
  loadDetail,
  prevMonth,
  nextMonth,
} = board

const actions = usePlanningActions(board)
const { creatingSchedule, copyingMonth, onCreateSchedule, onCopyMonth } = actions

// issue #74's own compact grouping: days of the visible month grouped by calendar week
// (Monday-start), purely for the header's "Woche öffnen" action — a month never starts/ends
// on a Monday/Sunday, so the first/last groups are naturally partial, matching how the
// week-detail view itself clamps a boundary week down to just the days in this month's
// Schedule.
interface WeekGroup {
  key: string
  days: Date[]
  label: string
}
const weekGroups = computed<WeekGroup[]>(() => {
  const groups: WeekGroup[] = []
  for (const d of days.value) {
    const key = toIso(startOfWeek(d))
    let group = groups[groups.length - 1]
    if (!group || group.key !== key) {
      group = { key, days: [], label: '' }
      groups.push(group)
    }
    group.days.push(d)
  }
  for (const g of groups) {
    const first = g.days[0]
    const last = g.days[g.days.length - 1]
    g.label =
      first === last
        ? dayMonthFmt.format(first)
        : `${dayMonthFmt.format(first)}–${dayMonthFmt.format(last)}`
  }
  return groups
})

// One (employeeId, date) -> severity map, built from the same `/validate` ValidationResult the
// week-detail view uses, but only for issues resolvable to a specific assignment — drives the
// overview's per-cell conflict dot. Errors win over warnings when both land on the same cell.
const cellSeverity = computed(() => {
  const map = new Map<string, 'error' | 'warning'>()
  if (!validation.value) return map
  const consider = (issues: ValidationIssue[], severity: 'error' | 'warning') => {
    for (const issue of issues) {
      if (!issue.employeeId || !issue.shiftAssignmentId) continue
      const assignment = assignments.value.find((a) => a.id === issue.shiftAssignmentId)
      if (!assignment) continue
      const key = `${issue.employeeId}|${assignment.date}`
      if (severity === 'error' || map.get(key) !== 'error') map.set(key, severity)
    }
  }
  consider(validation.value.warnings, 'warning')
  consider(validation.value.errors, 'error')
  return map
})
function severityFor(employeeId: string, dateIso: string): 'error' | 'warning' | null {
  return cellSeverity.value.get(`${employeeId}|${dateIso}`) ?? null
}
// Issues that aren't tied to one specific assignment (e.g. ContractHoursExceeded) still belong
// to an employee for the whole month — surfaced as a small count badge next to their name
// rather than smeared across every day cell.
const monthlyIssueCountByEmployee = computed(() => {
  const map = new Map<string, number>()
  if (!validation.value) return map
  for (const issue of [...validation.value.errors, ...validation.value.warnings]) {
    if (!issue.employeeId || issue.shiftAssignmentId) continue
    map.set(issue.employeeId, (map.get(issue.employeeId) ?? 0) + 1)
  }
  return map
})

function openWeek(dateInWeek: Date) {
  router.push({
    name: 'schedule-week',
    params: { date: toIso(dateInWeek) },
    query: filters.filterQuery(),
  })
}

async function init() {
  // A back-link from the week-detail view (issue #74) or a plain reload carries the visible
  // month as `?month=` — applied once before loading, same one-shot read pattern the
  // dashboard's own `?scheduleId=` deep link already uses (handled inside board.load()).
  if (typeof route.query.month === 'string') {
    board.anchorDate.value = parseIso(route.query.month)
  }
  await load()
}
onMounted(init)

const showAutoFill = ref(false)
</script>

<template>
  <div class="p-8">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-semibold">Dienstplan</h1>
      <div class="flex items-center gap-3">
        <button class="text-slate-400 hover:text-slate-200 transition-colors" @click="prevMonth">
          <ChevronLeft :size="18" />
        </button>
        <span class="font-mono text-sm text-slate-400 capitalize">{{ monthLabel }}</span>
        <button class="text-slate-400 hover:text-slate-200 transition-colors" @click="nextMonth">
          <ChevronRight :size="18" />
        </button>
      </div>
    </div>

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>
    <div v-if="loading" class="space-y-4" aria-label="Lädt…">
      <div class="flex gap-2">
        <div v-for="i in 3" :key="i" class="h-9 w-32 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
      <div class="glass rounded-xl p-4 space-y-3">
        <div v-for="i in 6" :key="i" class="h-10 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
    </div>

    <template v-else>
      <div v-if="!currentSchedule" class="glass rounded-xl p-8 text-center">
        <p class="text-sm text-slate-500 mb-4">Für diesen Monat existiert noch kein Dienstplan.</p>
        <button
          :disabled="creatingSchedule"
          class="rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
          @click="onCreateSchedule"
        >
          {{ creatingSchedule ? 'Anlegen…' : 'Diesen Monat anlegen' }}
        </button>
      </div>

      <template v-else>
        <div class="flex flex-wrap items-center gap-2 mb-4">
          <button
            v-if="assignments.length"
            :disabled="copyingMonth"
            class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors disabled:opacity-50"
            @click="onCopyMonth"
          >
            <Copy :size="14" />
            {{ copyingMonth ? 'Kopiere…' : 'Monat kopieren' }}
          </button>
          <button
            v-if="activeShiftTypes.length"
            class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors"
            @click="showAutoFill = true"
          >
            <Wand2 :size="14" />
            Automatisch füllen
          </button>
          <div class="relative">
            <Search :size="14" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-500" />
            <input
              v-model="search"
              type="text"
              placeholder="Mitarbeiter suchen…"
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

        <p class="flex flex-wrap items-center gap-3 text-xs text-slate-500 mb-3">
          <span class="flex items-center gap-1"
            ><span class="w-2 h-2 rounded-full bg-rose-400 shrink-0"></span> Fehler</span
          >
          <span class="flex items-center gap-1"
            ><span class="w-2 h-2 rounded-full bg-amber-400 shrink-0"></span> Warnung</span
          >
          <span class="flex items-center gap-1"
            ><span class="w-2.5 h-2.5 rounded-[3px] bg-slate-500 shrink-0"></span> Schichtart
            (Kürzel)</span
          >
          <span class="flex items-center gap-1"
            ><span
              class="w-2.5 h-2.5 rounded-full border border-dashed border-slate-400 shrink-0"
            ></span>
            Abwesenheit</span
          >
        </p>

        <div class="glass rounded-xl overflow-auto max-h-[70vh]">
          <table class="w-full text-sm">
            <thead>
              <tr class="border-b border-white/8 sticky top-0 z-20 bg-[#11141c]">
                <th
                  rowspan="2"
                  class="px-4 py-2 text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 sticky left-0 z-30 bg-[#11141c] shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)]"
                >
                  Mitarbeiter
                </th>
                <th
                  v-for="g in weekGroups"
                  :key="g.key"
                  :colspan="g.days.length"
                  class="px-2 py-1.5 border-l border-white/5"
                >
                  <button
                    class="w-full flex items-center justify-center gap-1 text-[11px] font-mono text-slate-400 hover:text-indigo-300 transition-colors"
                    title="Woche öffnen"
                    @click="openWeek(g.days[0])"
                  >
                    {{ g.label }}
                    <ArrowUpRight :size="11" />
                  </button>
                </th>
              </tr>
              <tr
                class="text-center text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8 sticky top-[29px] z-20 bg-[#11141c]"
              >
                <th
                  v-for="d in days"
                  :key="toIso(d)"
                  class="px-1 py-1.5 min-w-[34px] cursor-pointer hover:text-indigo-300 transition-colors"
                  :class="{
                    'text-amber-400': holidayFor(toIso(d)),
                    'bg-white/[0.03]': isWeekend(d),
                  }"
                  :title="holidayFor(toIso(d))?.name ?? 'Woche öffnen'"
                  @click="openWeek(d)"
                >
                  <div>{{ weekdayLetterFmt.format(d) }}</div>
                  <div class="font-mono normal-case text-[10px]">{{ d.getDate() }}</div>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="e in visibleEmployees"
                :key="e.id"
                class="border-b border-white/5 last:border-0"
              >
                <td
                  class="px-4 py-2 align-middle sticky left-0 z-10 bg-[#11141c] shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)] whitespace-nowrap"
                >
                  {{ e.lastName }}, {{ e.firstName }}
                  <span
                    v-if="monthlyIssueCountByEmployee.get(e.id)"
                    class="ml-1 inline-flex items-center justify-center w-4 h-4 rounded-full bg-rose-500/20 text-rose-400 text-[9px] font-mono"
                    :title="`${monthlyIssueCountByEmployee.get(e.id)} Problem(e) für den ganzen Monat`"
                  >
                    {{ monthlyIssueCountByEmployee.get(e.id) }}
                  </span>
                </td>
                <td
                  v-for="d in days"
                  :key="toIso(d)"
                  class="relative px-0.5 py-1 align-middle text-center cursor-pointer hover:bg-white/5 transition-colors"
                  :class="{ 'bg-white/[0.03]': isWeekend(d) }"
                  @click="openWeek(d)"
                >
                  <div class="flex items-center justify-center gap-0.5 min-h-[18px]">
                    <span
                      v-for="a in assignmentsFor(e.id, toIso(d)).slice(0, 3)"
                      :key="a.id"
                      class="w-4 h-4 rounded-[3px] flex items-center justify-center text-[9px] font-bold text-white/90 shrink-0"
                      :style="{ backgroundColor: shiftTypeById(a.shiftTypeId)?.color ?? '#64748b' }"
                      :title="`${shiftTypeById(a.shiftTypeId)?.name ?? ''} ${a.startTime.slice(0, 5)}–${a.endTime.slice(0, 5)}`"
                    >
                      {{ (shiftTypeById(a.shiftTypeId)?.name ?? '?').slice(0, 1).toUpperCase() }}
                    </span>
                    <span
                      v-if="assignmentsFor(e.id, toIso(d)).length > 3"
                      class="text-[9px] text-slate-400"
                    >
                      +{{ assignmentsFor(e.id, toIso(d)).length - 3 }}
                    </span>
                    <span
                      v-if="isAbsentOn(e.id, toIso(d))"
                      class="w-3.5 h-3.5 rounded-full border border-dashed border-slate-400 text-slate-400 text-[8px] flex items-center justify-center shrink-0"
                      title="Abwesend"
                    >
                      A
                    </span>
                  </div>
                  <span
                    v-if="severityFor(e.id, toIso(d))"
                    class="absolute bottom-0.5 right-0.5 w-1.5 h-1.5 rounded-full"
                    :class="
                      severityFor(e.id, toIso(d)) === 'error' ? 'bg-rose-400' : 'bg-amber-400'
                    "
                    :title="severityFor(e.id, toIso(d)) === 'error' ? 'Fehler' : 'Warnung'"
                  ></span>
                </td>
              </tr>
              <tr v-if="!visibleEmployees.length">
                <td :colspan="days.length + 1" class="px-4 py-8 text-center text-slate-500">
                  {{ activeEmployees.length ? 'Keine Treffer.' : 'Keine Mitarbeiter.' }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </template>

    <AutoFillModal
      v-if="showAutoFill && currentSchedule"
      :schedule-id="currentSchedule.id"
      :month-start="monthStartIso"
      :month-end="board.monthEndIso.value"
      :shift-types="board.shiftTypes.value"
      @close="showAutoFill = false"
      @committed="loadDetail"
    />
  </div>
</template>
