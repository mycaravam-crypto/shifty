<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import api from '@/services/api'

interface Team {
  id: string
  name: string
}
interface ShiftType {
  id: string
  name: string
}
interface DashboardKpis {
  staffingCoveragePercent: number
  workforceUtilizationPercent: number
  laborCost: number
  laborCostDeltaPercent: number | null
  planningCompletionPercent: number
  openIssuesCount: number
  criticalIssuesCount: number
  overtimeHours: number
  overtimeHoursDeltaPercent: number | null
}
interface CoverageDay {
  date: string
  shiftTypeId: string
  shiftTypeName: string
  scheduled: number
  minStaffing: number
  coveragePercent: number
  status: 'Green' | 'Yellow' | 'Red'
}
interface ScheduleRef {
  id: string
  name: string
  startDate: string
  status: number
}
interface PlanningStatus {
  draftCount: number
  publishedCount: number
  conflictCount: number
  completionPercent: number
  affectedSchedules: ScheduleRef[]
}
interface PainPoint {
  type: string
  severity: 'Error' | 'Warning'
  message: string
  scheduleId: string
  scheduleName: string
  employeeId: string | null
  employeeName: string | null
}
interface EmployeeUtilization {
  employeeId: string
  employeeName: string
  contractCapacityHours: number
  plannedHours: number
  utilizationPercent: number
  overtimeHours: number
}
interface Utilization {
  contractCapacityHours: number
  plannedHours: number
  utilizationPercent: number
  byEmployee: EmployeeUtilization[]
}
// issue #56: regular/night/Sunday/holiday cost breakdown restored from the dashboard mockup's
// trimmed scope — mapped onto WageCalculator's actual surcharge types rather than a literal
// "overtime" cost bucket, since there's no separate overtime pay rate anywhere in this app.
interface CostBreakdown {
  regular: number
  night: number
  sunday: number
  holiday: number
}
interface CostOverview {
  currentTotal: number
  previousTotal: number
  deltaPercent: number | null
  breakdown: CostBreakdown
}
interface Dashboard {
  from: string
  to: string
  kpis: DashboardKpis
  coverage: CoverageDay[]
  planningStatus: PlanningStatus
  painPoints: PainPoint[]
  cost: CostOverview
  utilization: Utilization
}

const router = useRouter()
const weekdayFmt = new Intl.DateTimeFormat('de-DE', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
})
const currencyFmt = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })

const teams = ref<Team[]>([])
const shiftTypes = ref<ShiftType[]>([])
const dashboard = ref<Dashboard | null>(null)
const loading = ref(true)
const error = ref('')

const fromDate = ref('')
const toDate = ref('')
const teamFilter = ref('')
const shiftTypeFilter = ref('')
const ready = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await api.get('/dashboard', {
      params: {
        from: fromDate.value || undefined,
        to: toDate.value || undefined,
        teamId: teamFilter.value || undefined,
        shiftTypeId: shiftTypeFilter.value || undefined,
      },
    })
    dashboard.value = res.data
    fromDate.value = res.data.from
    toDate.value = res.data.to
  } catch {
    error.value = 'Übersicht konnte nicht geladen werden.'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  const [teamsRes, shiftTypesRes] = await Promise.all([api.get('/teams'), api.get('/shift-types')])
  teams.value = teamsRes.data
  shiftTypes.value = shiftTypesRes.data
  await load()
  ready.value = true
})
watch([fromDate, toDate, teamFilter, shiftTypeFilter], () => {
  if (ready.value) load()
})

const statusColors: Record<string, string> = {
  Green: 'text-emerald-400',
  Yellow: 'text-amber-400',
  Red: 'text-rose-400',
}
function statusColor(status: string) {
  return statusColors[status] ?? statusColors.Red
}
const statusBars: Record<string, string> = {
  Green: 'bg-emerald-500',
  Yellow: 'bg-amber-500',
  Red: 'bg-rose-500',
}
function statusBar(status: string) {
  return statusBars[status] ?? statusBars.Red
}
function delta(pct: number | null): string {
  if (pct === null) return ''
  let arrow = ''
  if (pct > 0) arrow = '▲ +'
  else if (pct < 0) arrow = '▼ '
  return arrow + pct + '%'
}
function deltaColor(pct: number | null, positiveIsGood: boolean): string {
  if (pct === null || pct === 0) return 'text-slate-500'
  const isGood = positiveIsGood ? pct > 0 : pct < 0
  return isGood ? 'text-emerald-400' : 'text-rose-400'
}

// issue #56: mini stacked-bar segments for the cost breakdown — widths are shares of the total,
// not raw amounts, so the bar always fills 0-100% regardless of the absolute cost.
const costSegmentColors: Record<keyof CostBreakdown, string> = {
  regular: 'bg-blue-500',
  night: 'bg-indigo-400',
  sunday: 'bg-violet-400',
  holiday: 'bg-fuchsia-400',
}
const costSegmentLabels: Record<keyof CostBreakdown, string> = {
  regular: 'Regulär',
  night: 'Nachtzuschlag',
  sunday: 'Sonntagszuschlag',
  holiday: 'Feiertagszuschlag',
}
const costSegments = computed(() => {
  const b = dashboard.value?.cost.breakdown
  if (!b) return []
  const total = b.regular + b.night + b.sunday + b.holiday
  return (Object.keys(b) as (keyof CostBreakdown)[])
    .map((key) => ({
      key,
      label: costSegmentLabels[key],
      color: costSegmentColors[key],
      amount: b[key],
      percent: total === 0 ? 0 : (b[key] / total) * 100,
    }))
    .filter((s) => s.amount > 0)
})

// issue #31: no per-shift date on PainPointDto, so severity is the only sort key available
// without a new backend field — ties keep the backend's own ordering.
const actionFeed = computed(() =>
  [...(dashboard.value?.painPoints ?? [])]
    .sort((a, b) => Number(b.severity === 'Error') - Number(a.severity === 'Error'))
    .slice(0, 8),
)

function openSchedule(scheduleId: string) {
  router.push({ name: 'schedule', query: { scheduleId } })
}
</script>

<template>
  <div class="p-8">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-semibold">Übersicht</h1>
    </div>

    <div class="flex flex-wrap items-center gap-2 mb-6">
      <input
        v-model="fromDate"
        type="date"
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
      />
      <span class="text-slate-500 text-sm">–</span>
      <input
        v-model="toDate"
        type="date"
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
      />
      <select
        v-model="teamFilter"
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
      >
        <option value="">Alle Teams</option>
        <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
      </select>
      <select
        v-model="shiftTypeFilter"
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
      >
        <option value="">Alle Schichtarten</option>
        <option v-for="s in shiftTypes" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
    </div>

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>
    <div v-if="loading" class="space-y-6" aria-label="Lädt…">
      <div class="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4">
        <div
          v-for="i in 6"
          :key="i"
          class="glass rounded-xl p-4 h-20 animate-pulse bg-white/5"
        ></div>
      </div>
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div class="glass rounded-xl p-4 h-40 animate-pulse bg-white/5"></div>
        <div class="glass rounded-xl p-4 h-40 animate-pulse bg-white/5"></div>
      </div>
    </div>

    <template v-else-if="dashboard">
      <div class="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4 mb-6">
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Besetzung
          </div>
          <div
            class="font-mono text-2xl"
            :class="
              statusColor(
                dashboard.kpis.staffingCoveragePercent >= 95
                  ? 'Green'
                  : dashboard.kpis.staffingCoveragePercent >= 85
                    ? 'Yellow'
                    : 'Red',
              )
            "
          >
            {{ dashboard.kpis.staffingCoveragePercent }}%
          </div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Auslastung
          </div>
          <div class="font-mono text-2xl">{{ dashboard.kpis.workforceUtilizationPercent }}%</div>
          <div class="font-mono text-[11px] text-slate-500 mt-1">
            {{ dashboard.utilization.plannedHours }}h /
            {{ dashboard.utilization.contractCapacityHours }}h
          </div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Lohnkosten
          </div>
          <div class="font-mono text-2xl">{{ currencyFmt.format(dashboard.kpis.laborCost) }}</div>
          <div
            class="font-mono text-[11px] mt-1"
            :class="deltaColor(dashboard.kpis.laborCostDeltaPercent, false)"
          >
            {{ delta(dashboard.kpis.laborCostDeltaPercent) || '—' }}
          </div>
          <!-- issue #56: regular/night/Sunday/holiday cost breakdown, restored from the
               dashboard mockup's originally-trimmed scope. -->
          <div
            v-if="costSegments.length"
            class="flex h-1.5 rounded-full overflow-hidden mt-2 bg-white/10"
          >
            <div
              v-for="s in costSegments"
              :key="s.key"
              :class="s.color"
              :style="{ width: s.percent + '%' }"
              :title="`${s.label}: ${currencyFmt.format(s.amount)}`"
            ></div>
          </div>
          <div v-if="costSegments.length > 1" class="flex flex-wrap gap-x-2 gap-y-0.5 mt-1.5">
            <span
              v-for="s in costSegments"
              :key="s.key"
              class="flex items-center gap-1 text-[10px] text-slate-500"
            >
              <span class="w-1.5 h-1.5 rounded-full shrink-0" :class="s.color"></span>
              {{ s.label }}
            </span>
          </div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Planung
          </div>
          <div class="font-mono text-2xl">{{ dashboard.kpis.planningCompletionPercent }}%</div>
          <div class="font-mono text-[11px] text-slate-500 mt-1">
            {{ dashboard.planningStatus.publishedCount }} veröffentlicht ·
            {{ dashboard.planningStatus.draftCount }} Entwurf
          </div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Offene Probleme
          </div>
          <div
            class="font-mono text-2xl"
            :class="dashboard.kpis.criticalIssuesCount ? 'text-rose-400' : ''"
          >
            {{ dashboard.kpis.openIssuesCount }}
          </div>
          <div class="font-mono text-[11px] text-slate-500 mt-1">
            {{ dashboard.kpis.criticalIssuesCount }} kritisch
          </div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1">
            Überstunden
          </div>
          <div class="font-mono text-2xl">{{ dashboard.kpis.overtimeHours }}h</div>
          <div
            class="font-mono text-[11px] mt-1"
            :class="deltaColor(dashboard.kpis.overtimeHoursDeltaPercent, false)"
          >
            {{ delta(dashboard.kpis.overtimeHoursDeltaPercent) || '—' }}
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-6">
        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
            Besetzungsgrad
          </div>
          <div v-if="!dashboard.coverage.length" class="text-sm text-slate-500">
            Keine Daten im Zeitraum.
          </div>
          <div
            v-for="c in dashboard.coverage"
            :key="c.date + c.shiftTypeId"
            class="flex items-center gap-2 text-sm py-1"
          >
            <span class="font-mono text-xs text-slate-500 w-16 shrink-0">{{
              weekdayFmt.format(new Date(c.date))
            }}</span>
            <span class="flex-1 truncate">{{ c.shiftTypeName }}</span>
            <div class="w-24 h-1.5 rounded-full bg-white/10 overflow-hidden shrink-0">
              <div
                class="h-full"
                :class="statusBar(c.status)"
                :style="{ width: Math.min(100, c.coveragePercent) + '%' }"
              ></div>
            </div>
            <span class="font-mono text-xs w-14 text-right shrink-0" :class="statusColor(c.status)">
              {{ c.scheduled }}/{{ c.minStaffing }}
            </span>
          </div>
        </div>

        <div class="glass rounded-xl p-4">
          <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
            Planungsstatus
          </div>
          <div class="flex gap-6 text-sm mb-3">
            <div>
              <span class="font-mono text-lg">{{ dashboard.planningStatus.publishedCount }}</span>
              veröffentlicht
            </div>
            <div>
              <span class="font-mono text-lg">{{ dashboard.planningStatus.draftCount }}</span>
              Entwurf
            </div>
            <div class="text-rose-400">
              <span class="font-mono text-lg">{{ dashboard.planningStatus.conflictCount }}</span>
              Konflikte
            </div>
          </div>
          <button
            v-for="s in dashboard.planningStatus.affectedSchedules"
            :key="s.id"
            class="block text-left text-sm text-rose-400 hover:underline py-0.5"
            @click="openSchedule(s.id)"
          >
            ❌ {{ s.name }}
          </button>
        </div>
      </div>

      <!-- issue #56: per-employee utilization table restored from the dashboard mockup's
           originally-trimmed scope. Same numbers the schedule-wide Auslastung KPI card
           above already sums, just broken out per employee. -->
      <div class="glass rounded-xl p-4 mb-6">
        <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
          Auslastung nach Mitarbeiter
        </div>
        <div v-if="!dashboard.utilization.byEmployee.length" class="text-sm text-slate-500">
          Keine Daten im Zeitraum.
        </div>
        <div v-else class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/10"
              >
                <th class="text-left font-bold py-1.5 pr-3">Mitarbeiter</th>
                <th class="text-right font-bold py-1.5 px-3">Soll</th>
                <th class="text-right font-bold py-1.5 px-3">Ist</th>
                <th class="text-right font-bold py-1.5 px-3">Auslastung</th>
                <th class="text-right font-bold py-1.5 pl-3">Überstunden</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="u in dashboard.utilization.byEmployee"
                :key="u.employeeId"
                class="border-b border-white/5 last:border-0"
              >
                <td class="py-1.5 pr-3">{{ u.employeeName }}</td>
                <td class="text-right font-mono py-1.5 px-3">{{ u.contractCapacityHours }}h</td>
                <td class="text-right font-mono py-1.5 px-3">{{ u.plannedHours }}h</td>
                <td
                  class="text-right font-mono py-1.5 px-3"
                  :class="
                    statusColor(
                      u.utilizationPercent >= 95 && u.utilizationPercent <= 110
                        ? 'Green'
                        : u.utilizationPercent >= 85
                          ? 'Yellow'
                          : 'Red',
                    )
                  "
                >
                  {{ u.utilizationPercent }}%
                </td>
                <td
                  class="text-right font-mono py-1.5 pl-3"
                  :class="u.overtimeHours > 0 ? 'text-amber-400' : 'text-slate-500'"
                >
                  {{ u.overtimeHours > 0 ? `+${u.overtimeHours}h` : '—' }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="glass rounded-xl p-4 mb-6">
        <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
          Pain Points
        </div>
        <div v-if="!dashboard.painPoints.length" class="text-sm text-slate-500">
          Keine Probleme im Zeitraum.
        </div>
        <p
          v-for="(p, i) in dashboard.painPoints.filter((p) => p.severity === 'Error')"
          :key="'e' + i"
          class="text-sm text-rose-400 py-0.5"
        >
          ❌ {{ p.message }}
          <button class="text-slate-500 hover:underline" @click="openSchedule(p.scheduleId)">
            ({{ p.scheduleName }})
          </button>
        </p>
        <p
          v-for="(p, i) in dashboard.painPoints.filter((p) => p.severity === 'Warning')"
          :key="'w' + i"
          class="text-sm text-amber-400 py-0.5"
        >
          ⚠ {{ p.message }}
          <button class="text-slate-500 hover:underline" @click="openSchedule(p.scheduleId)">
            ({{ p.scheduleName }})
          </button>
        </p>
      </div>

      <div class="glass rounded-xl p-4">
        <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
          Handlungsbedarf
        </div>
        <div v-if="!actionFeed.length" class="text-sm text-slate-500">Nichts zu tun.</div>
        <div
          v-for="(p, i) in actionFeed"
          :key="i"
          class="flex items-center justify-between gap-3 py-1.5 border-b border-white/5 last:border-0"
        >
          <span
            class="text-sm"
            :class="p.severity === 'Error' ? 'text-rose-400' : 'text-amber-400'"
          >
            {{ p.severity === 'Error' ? '❌' : '⚠' }} {{ p.message }}
            <span class="text-slate-500">— {{ p.scheduleName }}</span>
          </span>
          <button
            class="shrink-0 rounded-lg bg-white/5 border border-white/10 px-2.5 py-1 text-xs hover:bg-white/10 transition-colors"
            @click="openSchedule(p.scheduleId)"
          >
            Öffnen
          </button>
        </div>
      </div>
    </template>
  </div>
</template>
