<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import api from '../../services/api'
import ShiftAssignmentModal from './ShiftAssignmentModal.vue'

interface Employee {
  id: string
  firstName: string
  lastName: string
  active: boolean
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

function mondayOf(date: Date): Date {
  const d = new Date(date)
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7))
  d.setHours(0, 0, 0, 0)
  return d
}
function addDays(date: Date, n: number): Date {
  const d = new Date(date)
  d.setDate(d.getDate() + n)
  return d
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
function isoWeekNumber(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}
const weekdayFmt = new Intl.DateTimeFormat('de-DE', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
})

const employees = ref<Employee[]>([])
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

const weekStart = computed(() => mondayOf(anchorDate.value))
const weekEnd = computed(() => addDays(weekStart.value, 6))
const weekStartIso = computed(() => toIso(weekStart.value))
const weekEndIso = computed(() => toIso(weekEnd.value))

const activeEmployees = computed(() => employees.value.filter((e) => e.active))
const activeShiftTypes = computed(() => shiftTypes.value.filter((s) => s.active))

const currentSchedule = computed(() =>
  schedules.value.find((s) => s.startDate === weekStartIso.value),
)

const days = computed(() => {
  const start = currentSchedule.value ? parseIso(currentSchedule.value.startDate) : weekStart.value
  const end = currentSchedule.value ? parseIso(currentSchedule.value.endDate) : weekEnd.value
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
  const start = weekStartIso.value
  const active = contracts.find((c) => c.validFrom <= start && (!c.validTo || c.validTo >= start))
  const contract =
    active ?? [...contracts].sort((a, b) => b.validFrom.localeCompare(a.validFrom))[0]
  return contract.weeklyHours
}
function barWidth(employeeId: string) {
  const target = targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (netHoursFor(employeeId) / target) * 100)
}

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
    const [schedulesRes, employeesRes, shiftTypesRes] = await Promise.all([
      api.get('/schedules'),
      api.get('/employees'),
      api.get('/shift-types'),
    ])
    schedules.value = schedulesRes.data
    employees.value = employeesRes.data
    shiftTypes.value = shiftTypesRes.data

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

function prevWeek() {
  anchorDate.value = addDays(weekStart.value, -7)
}
function nextWeek() {
  anchorDate.value = addDays(weekStart.value, 7)
}

async function onCreateSchedule() {
  creatingSchedule.value = true
  try {
    await api.post('/schedules', {
      name: `Woche ${isoWeekNumber(weekStart.value)}`,
      startDate: weekStartIso.value,
      endDate: weekEndIso.value,
    })
    schedules.value = (await api.get('/schedules')).data
  } finally {
    creatingSchedule.value = false
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
        <button class="text-slate-400 hover:text-slate-200 transition-colors" @click="prevWeek">
          <ChevronLeft :size="18" />
        </button>
        <span class="font-mono text-sm text-slate-400">{{ weekStartIso }} – {{ weekEndIso }}</span>
        <button class="text-slate-400 hover:text-slate-200 transition-colors" @click="nextWeek">
          <ChevronRight :size="18" />
        </button>
      </div>
    </div>

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>
    <p v-if="loading" class="text-sm text-slate-500">Lädt…</p>

    <template v-else>
      <div v-if="!currentSchedule" class="glass rounded-xl p-8 text-center">
        <p class="text-sm text-slate-500 mb-4">Für diese Woche existiert noch kein Dienstplan.</p>
        <button
          :disabled="creatingSchedule"
          class="rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
          @click="onCreateSchedule"
        >
          {{ creatingSchedule ? 'Anlegen…' : 'Diese Woche anlegen' }}
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

        <div class="flex flex-wrap gap-2 mb-4">
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
                v-for="e in activeEmployees"
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
              <tr v-if="!activeEmployees.length">
                <td :colspan="days.length + 1" class="px-4 py-8 text-center text-slate-500">
                  Keine Mitarbeiter.
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
