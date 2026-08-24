<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import api from '@/services/api'
import { useSettingsStore } from '@/stores/settings'

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
interface Utilization {
  contractCapacityHours: number
  plannedHours: number
  utilizationPercent: number
}
interface Dashboard {
  from: string
  to: string
  kpis: DashboardKpis
  coverage: CoverageDay[]
  planningStatus: PlanningStatus
  painPoints: PainPoint[]
  utilization: Utilization
}

const router = useRouter()
const settings = useSettingsStore()
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

// issue #59: "new since last visit" digest for Pain Points, computed entirely client-side
// (no backend/DB change, no persisted notification log — per the issue's own framing this
// should NOT need one). `PainPointDto` carries no per-issue timestamp (same limitation
// `actionFeed`'s sort below already documents), so a real "since <lastSeenAt>" check isn't
// possible. Instead a Pain Point's identity is approximated as this composite key, and
// "new" means "not present in the identity set captured at the previous Dashboard visit" —
// a coarser signal than a true creation timestamp would give (e.g. a resolved-then-recreated
// identical issue won't re-flag as new), documented here the same way the WageCalculator's
// `ponytail:` comment documents its own break-adjustment approximation.
function painPointKey(p: PainPoint): string {
  return [p.type, p.scheduleId, p.employeeId ?? '', p.message].join('|')
}
const newPainPointKeys = ref<Set<string>>(new Set())
function isNewPainPoint(p: PainPoint): boolean {
  return newPainPointKeys.value.has(painPointKey(p))
}

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

    const currentKeys: string[] = (res.data.painPoints ?? []).map(painPointKey)
    if (settings.notificationsEnabled && settings.lastSeenAt) {
      const previouslySeen = new Set(settings.seenPainPointKeys)
      newPainPointKeys.value = new Set(currentKeys.filter((k) => !previouslySeen.has(k)))
    } else {
      // Notifications off, or this is the very first Dashboard visit ever (no prior
      // snapshot to diff against) — treat everything already there as the baseline, not
      // as "new", so a first-time user isn't shown a wall of "Neu" badges.
      newPainPointKeys.value = new Set()
    }
    settings.markDashboardSeen(currentKeys)
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
          <span
            v-if="isNewPainPoint(p)"
            class="ml-1 rounded bg-blue-500/20 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider text-blue-300"
            >Neu</span
          >
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
          <span
            v-if="isNewPainPoint(p)"
            class="ml-1 rounded bg-blue-500/20 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider text-blue-300"
            >Neu</span
          >
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
            <span
              v-if="isNewPainPoint(p)"
              class="ml-1 rounded bg-blue-500/20 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider text-blue-300"
              >Neu</span
            >
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
