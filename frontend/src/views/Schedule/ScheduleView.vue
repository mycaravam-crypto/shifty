<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ChevronLeft, ChevronRight, Copy, Search } from '@lucide/vue'
import api from '../../services/api'
import ShiftAssignmentModal from './ShiftAssignmentModal.vue'

interface Employee {
  id: string
  firstName: string
  lastName: string
  active: boolean
  teamId: string | null
}
interface Team {
  id: string
  name: string
}
interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  color: string
  active: boolean
}
interface Schedule {
  id: string
  name: string
  startDate: string
  endDate: string
  status: number
}
interface Assignment {
  id: string
  scheduleId: string
  employeeId: string
  shiftTypeId: string
  date: string
  startTime: string
  endTime: string
  breakMinutes: number
  netHours: number
  laborCost: number | null
}
interface Contract {
  employeeId: string
  validFrom: string
  validTo: string | null
  weeklyHours: number
}
interface ValidationIssue {
  type: string
  message: string
  employeeId: string | null
  shiftAssignmentId: string | null
}
interface ValidationResult {
  errors: ValidationIssue[]
  warnings: ValidationIssue[]
  isValid: boolean
}

function firstOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1)
}
function lastOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0)
}
function addDays(date: Date, n: number): Date {
  const d = new Date(date)
  d.setDate(d.getDate() + n)
  return d
}
function addMonths(date: Date, n: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + n, 1)
}
function toIso(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}
function parseIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}
const weekdayFmt = new Intl.DateTimeFormat('de-DE', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
})
const monthFmt = new Intl.DateTimeFormat('de-DE', { month: 'long', year: 'numeric' })

const employees = ref<Employee[]>([])
const teams = ref<Team[]>([])
const shiftTypes = ref<ShiftType[]>([])
const schedules = ref<Schedule[]>([])
const contractsByEmployee = ref<Map<string, Contract[]>>(new Map())
const assignments = ref<Assignment[]>([])
const validation = ref<ValidationResult | null>(null)
const selectedAssignment = ref<Assignment | null>(null)
const anchorDate = ref(new Date())
const loading = ref(true)
const error = ref('')
const creatingSchedule = ref(false)
const copyingMonth = ref(false)
const search = ref('')
const teamFilter = ref('')

const monthStart = computed(() => firstOfMonth(anchorDate.value))
const monthEnd = computed(() => lastOfMonth(anchorDate.value))
const monthStartIso = computed(() => toIso(monthStart.value))
const monthEndIso = computed(() => toIso(monthEnd.value))
const monthLabel = computed(() => monthFmt.format(monthStart.value))

const activeEmployees = computed(() => employees.value.filter((e) => e.active))
const activeShiftTypes = computed(() => shiftTypes.value.filter((s) => s.active))
const visibleEmployees = computed(() => {
  const term = search.value.trim().toLowerCase()
  return activeEmployees.value.filter((e) => {
    if (teamFilter.value && e.teamId !== teamFilter.value) return false
    if (term && !`${e.firstName} ${e.lastName}`.toLowerCase().includes(term)) return false
    return true
  })
})

const currentSchedule = computed(() =>
  schedules.value.find((s) => s.startDate === monthStartIso.value),
)

const days = computed(() => {
  const start = currentSchedule.value ? parseIso(currentSchedule.value.startDate) : monthStart.value
  const end = currentSchedule.value ? parseIso(currentSchedule.value.endDate) : monthEnd.value
  const result: Date[] = []
  for (let d = start; d <= end; d = addDays(d, 1)) result.push(d)
  return result
})

function shiftTypeById(id: string) {
  return shiftTypes.value.find((s) => s.id === id)
}
function assignmentsFor(employeeId: string, dateIso: string) {
  return assignments.value.filter((a) => a.employeeId === employeeId && a.date === dateIso)
}
function netHoursFor(employeeId: string) {
  return assignments.value
    .filter((a) => a.employeeId === employeeId)
    .reduce((sum, a) => sum + a.netHours, 0)
}
function targetHoursFor(employeeId: string): number | null {
  const contracts = contractsByEmployee.value.get(employeeId) ?? []
  if (!contracts.length) return null
  const start = monthStartIso.value
  const active = contracts.find((c) => c.validFrom <= start && (!c.validTo || c.validTo >= start))
  const contract =
    active ?? [...contracts].sort((a, b) => b.validFrom.localeCompare(a.validFrom))[0]
  const daysInMonth = monthEnd.value.getDate()
  return Math.round(((contract.weeklyHours * daysInMonth) / 7) * 10) / 10
}
function barWidth(employeeId: string) {
  const target = targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (netHoursFor(employeeId) / target) * 100)
}
const currencyFmt = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })
function sumLaborCost(list: Assignment[]): number | null {
  if (!list.some((a) => a.laborCost !== null)) return null
  return list.reduce((sum, a) => sum + (a.laborCost ?? 0), 0)
}
function laborCostFor(employeeId: string): number | null {
  return sumLaborCost(assignments.value.filter((a) => a.employeeId === employeeId))
}
const totalLaborCost = computed(() => sumLaborCost(assignments.value))

async function loadDetail() {
  if (!currentSchedule.value) {
    assignments.value = []
    validation.value = null
    return
  }
  const [detailRes, validationRes] = await Promise.all([
    api.get(`/schedules/${currentSchedule.value.id}`),
    api.get(`/schedules/${currentSchedule.value.id}/validate`),
  ])
  assignments.value = detailRes.data.assignments
  validation.value = validationRes.data
}
watch(currentSchedule, loadDetail, { immediate: true })

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [schedulesRes, employeesRes, shiftTypesRes, teamsRes] = await Promise.all([
      api.get('/schedules'),
      api.get('/employees'),
      api.get('/shift-types'),
      api.get('/teams'),
    ])
    schedules.value = schedulesRes.data
    employees.value = employeesRes.data
    shiftTypes.value = shiftTypesRes.data
    teams.value = teamsRes.data

    const contractsResults = await Promise.all(
      employees.value.map((e) => api.get(`/employees/${e.id}/contracts`)),
    )
    contractsByEmployee.value = new Map(
      employees.value.map((e, i) => [e.id, contractsResults[i].data]),
    )
  } catch {
    error.value = 'Dienstplan konnte nicht geladen werden.'
  } finally {
    loading.value = false
  }
}
onMounted(load)

function prevMonth() {
  anchorDate.value = addMonths(anchorDate.value, -1)
}
function nextMonth() {
  anchorDate.value = addMonths(anchorDate.value, 1)
}

async function onCreateSchedule() {
  creatingSchedule.value = true
  try {
    await api.post('/schedules', {
      name: monthLabel.value,
      startDate: monthStartIso.value,
      endDate: monthEndIso.value,
    })
    schedules.value = (await api.get('/schedules')).data
  } finally {
    creatingSchedule.value = false
  }
}

async function onCopyMonth() {
  if (!currentSchedule.value) return
  copyingMonth.value = true
  try {
    const nextStart = addMonths(anchorDate.value, 1)
    const nextStartIso = toIso(nextStart)
    const nextMonthDays = lastOfMonth(nextStart).getDate()
    let target = schedules.value.find((s) => s.startDate === nextStartIso)

    if (!target) {
      const created = await api.post('/schedules', {
        name: monthFmt.format(nextStart),
        startDate: nextStartIso,
        endDate: toIso(lastOfMonth(nextStart)),
      })
      target = created.data
      schedules.value.push(target!)
    } else {
      const existing = await api.get(`/schedules/${target.id}`)
      if (existing.data.assignments.length) {
        error.value = 'Nächster Monat hat bereits Schichten — Kopieren abgebrochen.'
        return
      }
    }

    for (const a of assignments.value) {
      // Same day-of-month next month; clamped into shorter months (e.g. 31 → 28/29/30).
      const day = Math.min(parseIso(a.date).getDate(), nextMonthDays)
      await api.post(`/schedules/${target!.id}/assignments`, {
        employeeId: a.employeeId,
        shiftTypeId: a.shiftTypeId,
        date: toIso(new Date(nextStart.getFullYear(), nextStart.getMonth(), day)),
        startTime: a.startTime,
        endTime: a.endTime,
        breakMinutes: a.breakMinutes,
      })
    }
    nextMonth()
  } finally {
    copyingMonth.value = false
  }
}

function onPaletteDragStart(e: DragEvent, shiftTypeId: string) {
  e.dataTransfer?.setData('application/json', JSON.stringify({ kind: 'shiftType', shiftTypeId }))
}
function onAssignmentDragStart(e: DragEvent, assignmentId: string) {
  e.dataTransfer?.setData('application/json', JSON.stringify({ kind: 'assignment', assignmentId }))
}
async function onDrop(e: DragEvent, employeeId: string, dateIso: string) {
  const raw = e.dataTransfer?.getData('application/json')
  if (!raw || !currentSchedule.value) return
  const payload = JSON.parse(raw)

  if (payload.kind === 'shiftType') {
    const shiftType = shiftTypeById(payload.shiftTypeId)
    if (!shiftType) return
    await api.post(`/schedules/${currentSchedule.value.id}/assignments`, {
      employeeId,
      shiftTypeId: shiftType.id,
      date: dateIso,
      startTime: shiftType.startTime,
      endTime: shiftType.endTime,
      breakMinutes: shiftType.breakMinutes,
    })
  } else if (payload.kind === 'assignment') {
    const assignment = assignments.value.find((a) => a.id === payload.assignmentId)
    if (!assignment) return
    await api.put(`/assignments/${assignment.id}`, {
      employeeId,
      shiftTypeId: assignment.shiftTypeId,
      date: dateIso,
      startTime: assignment.startTime,
      endTime: assignment.endTime,
      breakMinutes: assignment.breakMinutes,
    })
  }
  await loadDetail()
}

async function onAssignmentUpdated() {
  selectedAssignment.value = null
  await loadDetail()
}
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
    <p v-if="loading" class="text-sm text-slate-500">Lädt…</p>

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
        <div
          v-if="validation && (validation.errors.length || validation.warnings.length)"
          class="glass rounded-xl p-4 mb-4 text-sm space-y-1"
        >
          <p v-for="(issue, i) in validation.errors" :key="'e' + i" class="text-rose-400">
            ❌ {{ issue.message }}
          </p>
          <p v-for="(issue, i) in validation.warnings" :key="'w' + i" class="text-amber-400">
            ⚠ {{ issue.message }}
          </p>
        </div>

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
          <div
            v-for="s in activeShiftTypes"
            :key="s.id"
            draggable="true"
            class="flex items-center gap-2 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm cursor-grab"
            @dragstart="onPaletteDragStart($event, s.id)"
          >
            <span
              class="w-2.5 h-2.5 rounded-full shrink-0"
              :style="{ backgroundColor: s.color }"
            ></span>
            {{ s.name }}
            <span class="font-mono text-slate-500 text-xs"
              >{{ s.startTime.slice(0, 5) }}–{{ s.endTime.slice(0, 5) }}</span
            >
          </div>
          <p v-if="!activeShiftTypes.length" class="text-sm text-slate-500">
            Keine Schichtarten angelegt.
          </p>
          <div v-if="totalLaborCost !== null" class="ml-auto font-mono text-sm text-emerald-400">
            Lohnkosten: {{ currencyFmt.format(totalLaborCost) }}
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-2 mb-4">
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

        <div class="glass rounded-xl overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8"
              >
                <th class="px-4 py-3">Mitarbeiter</th>
                <th v-for="d in days" :key="toIso(d)" class="px-3 py-3 min-w-[130px]">
                  {{ weekdayFmt.format(d) }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="e in visibleEmployees"
                :key="e.id"
                class="border-b border-white/5 last:border-0"
              >
                <td class="px-4 py-3 align-top">
                  <div>{{ e.lastName }}, {{ e.firstName }}</div>
                  <template v-if="targetHoursFor(e.id) !== null">
                    <div
                      class="font-mono text-xs mt-1"
                      :class="
                        netHoursFor(e.id) !== targetHoursFor(e.id)
                          ? 'text-amber-400'
                          : 'text-slate-500'
                      "
                    >
                      {{ netHoursFor(e.id) }}h / {{ targetHoursFor(e.id) }}h
                      <span v-if="netHoursFor(e.id) !== targetHoursFor(e.id)">⚠</span>
                    </div>
                    <div class="w-24 h-1 rounded-full bg-white/10 mt-1 overflow-hidden">
                      <div
                        class="h-full bg-linear-to-r from-blue-600 to-indigo-600"
                        :style="{ width: barWidth(e.id) + '%' }"
                      ></div>
                    </div>
                  </template>
                  <div v-if="laborCostFor(e.id) !== null" class="font-mono text-xs text-emerald-400 mt-1">
                    {{ currencyFmt.format(laborCostFor(e.id)!) }}
                  </div>
                </td>
                <td
                  v-for="d in days"
                  :key="toIso(d)"
                  class="px-2 py-2 align-top"
                  @dragover.prevent
                  @drop="onDrop($event, e.id, toIso(d))"
                >
                  <div
                    v-for="a in assignmentsFor(e.id, toIso(d))"
                    :key="a.id"
                    draggable="true"
                    class="rounded-lg bg-white/5 border border-white/10 px-2 py-1 mb-1 cursor-pointer hover:bg-white/10 transition-colors"
                    @dragstart="onAssignmentDragStart($event, a.id)"
                    @click="selectedAssignment = a"
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

    <ShiftAssignmentModal
      v-if="selectedAssignment"
      :assignment="selectedAssignment"
      :shift-types="shiftTypes"
      @close="selectedAssignment = null"
      @updated="onAssignmentUpdated"
    />
  </div>
</template>
