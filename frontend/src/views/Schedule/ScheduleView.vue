<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ChevronLeft, ChevronRight, Copy, Search, Printer } from '@lucide/vue'
import api from '../../services/api'
import ShiftAssignmentModal from './ShiftAssignmentModal.vue'
import SkeletonBlock from '../../components/SkeletonBlock.vue'
import { useToastStore } from '../../stores/toast'

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
interface Absence {
  employeeId: string
  from: string
  to: string
}
interface PublicHoliday {
  date: string
  name: string
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

const toast = useToastStore()
const route = useRoute()
const router = useRouter()
const employees = ref<Employee[]>([])
const teams = ref<Team[]>([])
const shiftTypes = ref<ShiftType[]>([])
const schedules = ref<Schedule[]>([])
const contractsByEmployee = ref<Map<string, Contract[]>>(new Map())
const absencesByEmployee = ref<Map<string, Absence[]>>(new Map())
const balanceByEmployee = ref<Map<string, number>>(new Map())
const holidays = ref<PublicHoliday[]>([])
const assignments = ref<Assignment[]>([])
const validation = ref<ValidationResult | null>(null)
const selectedAssignment = ref<Assignment | null>(null)
const anchorDate = ref(new Date())
const loading = ref(true)
const error = ref('')
const creatingSchedule = ref(false)
const copyingMonth = ref(false)
// issue #41: the sidebar's nav links are plain `to="/"` with no query string, so a URL-based
// approach doesn't actually survive the "navigate away and back" case this was reported for —
// only a direct/bookmarked link with the query already on it would restore it. localStorage
// does actually persist across that navigation.
const FILTER_STORAGE_KEY = 'schichtplaner.scheduleFilter'
function loadPersistedFilter(): { search: string; team: string } {
  try {
    const parsed = JSON.parse(localStorage.getItem(FILTER_STORAGE_KEY) ?? '{}')
    return {
      search: typeof parsed.search === 'string' ? parsed.search : '',
      team: typeof parsed.team === 'string' ? parsed.team : '',
    }
  } catch {
    return { search: '', team: '' }
  }
}
const persistedFilter = loadPersistedFilter()
const search = ref(persistedFilter.search)
const teamFilter = ref(persistedFilter.team)
const searchInputRef = ref<HTMLInputElement | null>(null)
const tableWrapRef = ref<HTMLElement | null>(null)

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
function holidayFor(dateIso: string): PublicHoliday | undefined {
  return holidays.value.find((h) => h.date === dateIso)
}
async function loadHolidays() {
  const res = await api.get('/public-holidays', {
    params: { start: monthStartIso.value, end: monthEndIso.value },
  })
  holidays.value = res.data
}
function assignmentsFor(employeeId: string, dateIso: string) {
  return assignments.value.filter((a) => a.employeeId === employeeId && a.date === dateIso)
}
function netHoursFor(employeeId: string) {
  return assignments.value
    .filter((a) => a.employeeId === employeeId)
    .reduce((sum, a) => sum + a.netHours, 0)
}
// Mirrors ContractValidator.OverlapDays (backend) — days of [from, to] that fall within the
// visible month, so a week of vacation doesn't count toward the expected hours.
function overlapDays(from: string, to: string): number {
  const start = from > monthStartIso.value ? from : monthStartIso.value
  const end = to < monthEndIso.value ? to : monthEndIso.value
  if (end < start) return 0
  return Math.round((parseIso(end).getTime() - parseIso(start).getTime()) / 86400000) + 1
}
function targetHoursFor(employeeId: string): number | null {
  const contracts = contractsByEmployee.value.get(employeeId) ?? []
  if (!contracts.length) return null
  const start = monthStartIso.value
  const active = contracts.find((c) => c.validFrom <= start && (!c.validTo || c.validTo >= start))
  const contract =
    active ?? [...contracts].sort((a, b) => b.validFrom.localeCompare(a.validFrom))[0]
  const daysInMonth = monthEnd.value.getDate()
  const absenceDays = (absencesByEmployee.value.get(employeeId) ?? []).reduce(
    (sum, a) => sum + overlapDays(a.from, a.to),
    0,
  )
  const effectiveDays = Math.max(0, daysInMonth - absenceDays)
  return Math.round(((contract.weeklyHours * effectiveDays) / 7) * 10) / 10
}
function barWidth(employeeId: string) {
  const target = targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (netHoursFor(employeeId) / target) * 100)
}
function carriedOverFor(employeeId: string): number {
  return Math.round((balanceByEmployee.value.get(employeeId) ?? 0) * 10) / 10
}
async function loadBalances() {
  if (!activeEmployees.value.length) return
  const results = await Promise.all(
    activeEmployees.value.map((e) =>
      api.get(`/employees/${e.id}/hours-balance`, { params: { before: monthStartIso.value } }),
    ),
  )
  balanceByEmployee.value = new Map(activeEmployees.value.map((e, i) => [e.id, results[i].data]))
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

    // Deep link from the dashboard's pain-point/planning-status links (issue #30/#31):
    // jump to the month of the linked schedule instead of always showing today's month.
    const linkedId = route.query.scheduleId
    if (typeof linkedId === 'string') {
      const linked = schedules.value.find((s) => s.id === linkedId)
      if (linked) anchorDate.value = parseIso(linked.startDate)
      router.replace({ query: {} })
    }

    const [contractsResults, absencesResults] = await Promise.all([
      Promise.all(employees.value.map((e) => api.get(`/employees/${e.id}/contracts`))),
      Promise.all(employees.value.map((e) => api.get(`/employees/${e.id}/absences`))),
    ])
    contractsByEmployee.value = new Map(
      employees.value.map((e, i) => [e.id, contractsResults[i].data]),
    )
    absencesByEmployee.value = new Map(
      employees.value.map((e, i) => [e.id, absencesResults[i].data]),
    )
    await Promise.all([loadBalances(), loadHolidays()])
  } catch {
    error.value = 'Dienstplan konnte nicht geladen werden.'
  } finally {
    loading.value = false
  }
}
function isTyping(): boolean {
  const tag = document.activeElement?.tagName
  return tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA'
}
function onKeydown(e: KeyboardEvent) {
  if (selectedAssignment.value) return
  if (e.key === '/' && !isTyping()) {
    e.preventDefault()
    searchInputRef.value?.focus()
  } else if (e.key === 'ArrowLeft' && !isTyping()) {
    prevMonth()
  } else if (e.key === 'ArrowRight' && !isTyping()) {
    nextMonth()
  }
}

onMounted(load)
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(cleanupDrag)
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
onUnmounted(() => {
  if (jumpHighlightTimer !== null) window.clearTimeout(jumpHighlightTimer)
})
watch(monthStartIso, () => {
  if (!loading.value) {
    loadBalances()
    loadHolidays()
  }
})
// issue #41: keep localStorage in sync as the filter changes.
watch([search, teamFilter], () => {
  try {
    localStorage.setItem(
      FILTER_STORAGE_KEY,
      JSON.stringify({ search: search.value, team: teamFilter.value }),
    )
  } catch {
    // Private-browsing/storage-full edge cases: the filter still works for this session, it
    // just won't survive a reload — not worth surfacing to the user over.
  }
})

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
    toast.success('Monat angelegt.')
  } catch {
    toast.error('Monat konnte nicht angelegt werden.')
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
        toast.error(error.value)
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
    toast.success('Monat kopiert.')
    nextMonth()
  } catch {
    toast.error('Monat konnte nicht kopiert werden.')
  } finally {
    copyingMonth.value = false
  }
}

// Pointer-events-based drag (works for mouse and touch alike — native HTML5
// DnD has no touch support). Drag only becomes "active" past a small movement
// threshold so a plain tap/click still opens the assignment modal.
interface DragPayload {
  kind: 'shiftType' | 'assignment'
  shiftTypeId?: string
  assignmentId?: string
  label: string
  color: string
  time: string
}
interface DragState {
  payload: DragPayload
  pointerId: number
  startX: number
  startY: number
  x: number
  y: number
  active: boolean
}
const DRAG_ACTIVATE_PX = 6
const drag = ref<DragState | null>(null)
const dragOverKey = ref<string | null>(null)

// issue #39: clicking a validation issue scrolls to and briefly highlights the row/cell it's
// about, instead of the panel being a static text list.
const JUMP_HIGHLIGHT_MS = 2000
const jumpTarget = ref<{ employeeId: string; dateIso: string | null } | null>(null)
let jumpHighlightTimer: number | null = null
function jumpToIssue(issue: ValidationIssue) {
  if (!issue.employeeId) return
  const assignment = issue.shiftAssignmentId
    ? assignments.value.find((a) => a.id === issue.shiftAssignmentId)
    : undefined
  jumpTarget.value = { employeeId: issue.employeeId, dateIso: assignment?.date ?? null }
  if (jumpHighlightTimer !== null) window.clearTimeout(jumpHighlightTimer)
  jumpHighlightTimer = window.setTimeout(() => {
    jumpTarget.value = null
    jumpHighlightTimer = null
  }, JUMP_HIGHLIGHT_MS)
  nextTick(() => {
    document
      .querySelector(`[data-employee-row="${issue.employeeId}"]`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  })
}

function paletteDragPayload(s: ShiftType): DragPayload {
  return {
    kind: 'shiftType',
    shiftTypeId: s.id,
    label: s.name,
    color: s.color,
    time: `${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)}`,
  }
}
function assignmentDragPayload(a: Assignment): DragPayload {
  const shiftType = shiftTypeById(a.shiftTypeId)
  return {
    kind: 'assignment',
    assignmentId: a.id,
    label: shiftType?.name ?? '',
    color: shiftType?.color ?? '#64748b',
    time: `${a.startTime.slice(0, 5)}–${a.endTime.slice(0, 5)}`,
  }
}

function onChipPointerDown(e: PointerEvent, payload: DragPayload) {
  if (e.button !== 0) return
  ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
  drag.value = {
    payload,
    pointerId: e.pointerId,
    startX: e.clientX,
    startY: e.clientY,
    x: e.clientX,
    y: e.clientY,
    active: false,
  }
  window.addEventListener('pointermove', onDragPointerMove)
  window.addEventListener('pointerup', onDragPointerUp)
  window.addEventListener('pointercancel', onDragPointerCancel)
  // Ticks on an interval rather than off pointermove alone — a pointer parked
  // right at the table edge stops firing move events, but the scroll should
  // still keep going while it's held there.
  dragScrollTimer = window.setInterval(() => {
    if (drag.value?.active) autoScrollTableWrap(drag.value.x)
  }, 16)
}
function onDragPointerMove(e: PointerEvent) {
  if (!drag.value || e.pointerId !== drag.value.pointerId) return
  drag.value.x = e.clientX
  drag.value.y = e.clientY
  if (!drag.value.active) {
    const dx = e.clientX - drag.value.startX
    const dy = e.clientY - drag.value.startY
    if (Math.hypot(dx, dy) < DRAG_ACTIVATE_PX) return
    drag.value.active = true
  }
  e.preventDefault()
  const cell = document
    .elementFromPoint(e.clientX, e.clientY)
    ?.closest<HTMLElement>('[data-employee-id]')
  dragOverKey.value = cell ? `${cell.dataset.employeeId}|${cell.dataset.date}` : null
}
// Auto-scrolls the horizontally-scrolling table while dragging near its edge —
// otherwise a month with 28+ day columns has no way to reach off-screen days
// mid-drag.
const DRAG_SCROLL_EDGE_PX = 60
const DRAG_SCROLL_SPEED_PX = 12
let dragScrollTimer: number | null = null
function autoScrollTableWrap(clientX: number) {
  const wrap = tableWrapRef.value
  if (!wrap) return
  const rect = wrap.getBoundingClientRect()
  if (clientX < rect.left + DRAG_SCROLL_EDGE_PX) wrap.scrollLeft -= DRAG_SCROLL_SPEED_PX
  else if (clientX > rect.right - DRAG_SCROLL_EDGE_PX) wrap.scrollLeft += DRAG_SCROLL_SPEED_PX
}
async function onDragPointerUp(e: PointerEvent) {
  if (!drag.value || e.pointerId !== drag.value.pointerId) return
  const { payload, active } = drag.value
  const cell = document
    .elementFromPoint(e.clientX, e.clientY)
    ?.closest<HTMLElement>('[data-employee-id]')
  cleanupDrag()

  if (!active) {
    if (payload.kind === 'assignment') {
      const assignment = assignments.value.find((a) => a.id === payload.assignmentId)
      if (assignment) selectedAssignment.value = assignment
    }
    return
  }
  if (cell?.dataset.employeeId && cell.dataset.date) {
    await performDrop(payload, cell.dataset.employeeId, cell.dataset.date)
  }
}
function onDragPointerCancel(e: PointerEvent) {
  if (!drag.value || e.pointerId !== drag.value.pointerId) return
  cleanupDrag()
}
function cleanupDrag() {
  drag.value = null
  dragOverKey.value = null
  window.removeEventListener('pointermove', onDragPointerMove)
  window.removeEventListener('pointerup', onDragPointerUp)
  window.removeEventListener('pointercancel', onDragPointerCancel)
  if (dragScrollTimer !== null) {
    window.clearInterval(dragScrollTimer)
    dragScrollTimer = null
  }
}

async function performDrop(payload: DragPayload, employeeId: string, dateIso: string) {
  if (!currentSchedule.value) return

  if (payload.kind === 'shiftType') {
    const shiftType = shiftTypeById(payload.shiftTypeId!)
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

// PDF export = the browser's own print-to-PDF, scoped via CSS rather than a
// PDF-generation library. `printEmployeeId` narrows the printed table to one
// row; "all" export is just printing with it unset.
const printEmployeeId = ref<string | null>(null)
// A month can have 28-31 day columns and any number of employee rows — at the
// table's normal on-screen size that reliably overflows a single landscape
// page both across and down, so the old export spilled the plan across many
// pages. `zoom` (unlike `transform: scale`, which Chrome's print pagination
// ignores) actually shrinks the layout box, so it can be sized down until the
// whole table's natural (unscaled) footprint fits the printable area.
const PRINT_PAGE_WIDTH_PX = 1046 // A4 landscape, ~10mm margins, 96dpi
const PRINT_PAGE_HEIGHT_PX = 718
// Measurement happens under normal screen styles, before the `print:` compact
// classes (smaller padding/font, hidden progress bar) kick in — so it slightly
// overestimates the table's real printed footprint. That's the safe direction
// to be wrong in: the actual print render ends up a little smaller than this
// computes for, never bigger, so the one-page fit is never violated by it.
// No floor on the zoom itself — a very dense month legitimately needs to
// shrink further than a "readable" minimum to still land on one page, which
// is the whole point of this export.
const printZoom = ref(1)
function computePrintZoom() {
  const table = tableWrapRef.value?.firstElementChild as HTMLElement | null
  if (!table) return
  let height = table.scrollHeight
  // Single-employee export only prints one <tbody> row (via print:hidden on
  // the rest), but that row's still in the DOM and counted by scrollHeight —
  // measure just the header + that one row instead, or zoom would over-shrink.
  if (printEmployeeId.value) {
    const thead = table.querySelector('thead') as HTMLElement | null
    const row = table.querySelector(
      `tbody tr[data-employee-row="${printEmployeeId.value}"]`,
    ) as HTMLElement | null
    if (thead && row) height = thead.offsetHeight + row.offsetHeight
  }
  const widthZoom = PRINT_PAGE_WIDTH_PX / table.scrollWidth
  const heightZoom = PRINT_PAGE_HEIGHT_PX / height
  printZoom.value = Math.min(1, widthZoom, heightZoom)
}
async function exportAllPdf() {
  printEmployeeId.value = null
  await nextTick()
  computePrintZoom()
  await nextTick()
  window.print()
}
async function exportEmployeePdf(employeeId: string) {
  printEmployeeId.value = employeeId
  await nextTick()
  computePrintZoom()
  await nextTick()
  window.print()
}
window.addEventListener('afterprint', () => {
  printEmployeeId.value = null
  printZoom.value = 1
})
</script>

<template>
  <div class="p-8" :class="{ 'select-none': drag?.active }">
    <div
      v-if="drag?.active"
      class="fixed z-50 pointer-events-none flex items-center gap-2 rounded-lg bg-[#11141c] border border-white/20 px-3 py-1.5 text-sm shadow-lg"
      :style="{ left: drag.x + 14 + 'px', top: drag.y + 14 + 'px' }"
    >
      <span class="w-2.5 h-2.5 rounded-full shrink-0" :style="{ backgroundColor: drag.payload.color }"></span>
      {{ drag.payload.label }}
      <span class="font-mono text-slate-500 text-xs">{{ drag.payload.time }}</span>
    </div>
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-semibold">Dienstplan</h1>
      <div class="flex items-center gap-3">
        <button class="text-slate-400 hover:text-slate-200 transition-colors print:hidden" @click="prevMonth">
          <ChevronLeft :size="18" />
        </button>
        <span class="font-mono text-sm text-slate-400 capitalize">{{ monthLabel }}</span>
        <button class="text-slate-400 hover:text-slate-200 transition-colors print:hidden" @click="nextMonth">
          <ChevronRight :size="18" />
        </button>
        <button
          v-if="currentSchedule"
          class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors print:hidden"
          @click="exportAllPdf"
        >
          <Printer :size="14" />
          PDF exportieren
        </button>
      </div>
    </div>

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>

    <div v-if="loading" class="glass rounded-xl overflow-hidden">
      <table class="w-full text-sm">
        <tbody>
          <tr v-for="row in 6" :key="row" class="border-b border-white/5 last:border-0">
            <td class="px-4 py-3">
              <SkeletonBlock class="h-4 w-28 mb-1.5" />
              <SkeletonBlock class="h-3 w-16" />
            </td>
            <td v-for="col in 8" :key="col" class="px-2 py-3">
              <SkeletonBlock v-if="(row + col) % 3 !== 0" class="h-8 w-full" />
            </td>
          </tr>
        </tbody>
      </table>
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
        <div
          v-if="validation && (validation.errors.length || validation.warnings.length)"
          class="glass rounded-xl p-4 mb-4 text-sm space-y-1 print:hidden"
        >
          <p v-for="(issue, i) in validation.errors" :key="'e' + i" class="text-rose-400">
            <button
              v-if="issue.employeeId"
              class="text-left hover:underline decoration-dotted underline-offset-2"
              @click="jumpToIssue(issue)"
            >
              ❌ {{ issue.message }}
            </button>
            <template v-else>❌ {{ issue.message }}</template>
          </p>
          <p v-for="(issue, i) in validation.warnings" :key="'w' + i" class="text-amber-400">
            <button
              v-if="issue.employeeId"
              class="text-left hover:underline decoration-dotted underline-offset-2"
              @click="jumpToIssue(issue)"
            >
              ⚠ {{ issue.message }}
            </button>
            <template v-else>⚠ {{ issue.message }}</template>
          </p>
        </div>

        <div class="flex flex-wrap items-center gap-2 mb-4 print:hidden">
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
            class="flex items-center gap-2 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm cursor-grab touch-none select-none"
            @pointerdown="onChipPointerDown($event, paletteDragPayload(s))"
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

        <div
          ref="tableWrapRef"
          class="glass rounded-xl overflow-x-auto print:overflow-visible"
          :style="{ zoom: printZoom }"
        >
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8 print:text-[7px]"
              >
                <th class="px-4 py-3 print:px-1 print:py-1">Mitarbeiter</th>
                <th
                  v-for="d in days"
                  :key="toIso(d)"
                  class="px-3 py-3 min-w-[130px] print:px-0.5 print:py-1 print:min-w-0"
                  :class="{ 'text-amber-400': holidayFor(toIso(d)) }"
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
              <tr
                v-for="e in visibleEmployees"
                :key="e.id"
                class="border-b border-white/5 last:border-0 transition-colors"
                :class="{
                  'print:hidden': printEmployeeId && printEmployeeId !== e.id,
                  'bg-blue-500/5': jumpTarget?.employeeId === e.id,
                }"
                :data-employee-row="e.id"
              >
                <td class="px-4 py-3 align-top print:px-1 print:py-1">
                  <div class="flex items-center gap-1.5 print:text-[8px]">
                    {{ e.lastName }}, {{ e.firstName }}
                    <button
                      class="text-slate-500 hover:text-slate-200 transition-colors print:hidden"
                      title="Nur diesen Mitarbeiter als PDF exportieren"
                      @click="exportEmployeePdf(e.id)"
                    >
                      <Printer :size="12" />
                    </button>
                  </div>
                  <template v-if="targetHoursFor(e.id) !== null">
                    <div
                      class="font-mono text-xs mt-1 print:text-[7px] print:mt-0.5"
                      :class="
                        netHoursFor(e.id) !== targetHoursFor(e.id)
                          ? 'text-amber-400'
                          : 'text-slate-500'
                      "
                    >
                      {{ netHoursFor(e.id) }}h / {{ targetHoursFor(e.id) }}h
                      <span v-if="netHoursFor(e.id) !== targetHoursFor(e.id)">⚠</span>
                    </div>
                    <div
                      v-if="carriedOverFor(e.id) !== 0"
                      class="font-mono text-[11px] mt-0.5 print:text-[7px] print:mt-0"
                      :class="carriedOverFor(e.id) > 0 ? 'text-emerald-400' : 'text-rose-400'"
                    >
                      Übertrag: {{ carriedOverFor(e.id) > 0 ? '+' : '' }}{{ carriedOverFor(e.id) }}h
                    </div>
                    <div class="w-24 h-1 rounded-full bg-white/10 mt-1 overflow-hidden print:hidden">
                      <div
                        class="h-full bg-linear-to-r from-blue-600 to-indigo-600"
                        :style="{ width: barWidth(e.id) + '%' }"
                      ></div>
                    </div>
                  </template>
                  <div
                    v-if="laborCostFor(e.id) !== null"
                    class="font-mono text-xs text-emerald-400 mt-1 print:text-[7px] print:mt-0"
                  >
                    {{ currencyFmt.format(laborCostFor(e.id)!) }}
                  </div>
                </td>
                <td
                  v-for="d in days"
                  :key="toIso(d)"
                  class="px-2 py-2 align-top transition-colors print:px-0.5 print:py-0.5"
                  :class="{
                    'bg-blue-500/10 ring-1 ring-inset ring-blue-500/50':
                      dragOverKey === `${e.id}|${toIso(d)}` ||
                      (jumpTarget?.employeeId === e.id && jumpTarget.dateIso === toIso(d)),
                  }"
                  :data-employee-id="e.id"
                  :data-date="toIso(d)"
                >
                  <div
                    v-for="a in assignmentsFor(e.id, toIso(d))"
                    :key="a.id"
                    class="rounded-lg bg-white/5 border border-white/10 px-2 py-1 mb-1 cursor-pointer hover:bg-white/10 transition-colors touch-none select-none print:rounded-none print:bg-transparent print:border-0 print:border-b print:border-white/10 print:px-0 print:py-0.5 print:mb-0"
                    :class="{
                      'opacity-40':
                        drag?.active && drag.payload.kind === 'assignment' && drag.payload.assignmentId === a.id,
                    }"
                    @pointerdown="onChipPointerDown($event, assignmentDragPayload(a))"
                  >
                    <div class="flex items-center gap-1.5 text-xs print:text-[7px] print:gap-1">
                      <span
                        class="w-2 h-2 rounded-full shrink-0 print:w-1.5 print:h-1.5"
                        :style="{ backgroundColor: shiftTypeById(a.shiftTypeId)?.color }"
                      ></span>
                      {{ shiftTypeById(a.shiftTypeId)?.name }}
                    </div>
                    <div class="font-mono text-[11px] text-slate-500 print:text-[7px]">
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

<style scoped>
@media print {
  @page {
    size: landscape;
  }
}
</style>
