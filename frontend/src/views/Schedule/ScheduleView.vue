<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import axios from 'axios'
import { useRoute, useRouter } from 'vue-router'
import {
  ChevronLeft,
  ChevronRight,
  ChevronDown,
  Copy,
  Search,
  Printer,
  HelpCircle,
  Sparkles,
  Wand2,
  CheckCircle2,
  Archive,
} from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import { useSettingsStore } from '@/stores/settings'
import ModalShell from '@/components/ModalShell.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ShiftAssignmentModal from './ShiftAssignmentModal.vue'
import ShiftSuggestionModal from './ShiftSuggestionModal.vue'
import AutoFillModal from './AutoFillModal.vue'

const toast = useToastStore()
const settings = useSettingsStore()

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
  // Backend serializes enums as their ordinal, not a string (same as AbsenceType elsewhere) —
  // null = nationwide-only.
  bundesland: number | null
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
  publishedAt: string | null
  publishedBy: string | null
}
// issue #68's ScheduleStatus enum (Domain/Scheduling/Schedule.cs), serialized as its ordinal
// same as every other backend enum this frontend consumes.
const SCHEDULE_STATUS_DRAFT = 0
const SCHEDULE_STATUS_PUBLISHED = 1
const STATUS_LABELS: Record<number, string> = {
  0: 'Entwurf',
  1: 'Veröffentlicht',
  2: 'Archiviert',
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
  breakStartTime: string | null
  netHours: number
  laborCost: number | null
}
// issue #72: one GET /planning-board response replaces the old per-employee contracts/
// absences/hours-balance N+1 requests — target/planned/balance hours arrive precomputed
// instead of being derived client-side from raw Contract/Absence rows.
interface PlanningBoardEntry {
  targetHours: number | null
  plannedHours: number
  balanceHours: number
}
interface PlanningBoardEmployeeApi {
  employeeId: string
  assignments: Assignment[]
  targetHours: number | null
  plannedHours: number
  balanceHours: number
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
interface ValidationIssueGroup {
  type: string
  label: string
  severity: 'error' | 'warning'
  issues: ValidationIssue[]
}

// issue #78: German labels for ScheduleValidator's rule types (Application/Validation/*.cs),
// used to group the validation panel by rule instead of a flat list.
const ISSUE_TYPE_LABELS: Record<string, string> = {
  AssignedDuringAbsence: 'Einsatz während Abwesenheit',
  InsufficientBreak: 'Pause unterschritten',
  TooManyConsecutiveDays: 'Zu viele Arbeitstage in Folge',
  ContractHoursExceeded: 'Vertragsstunden überschritten',
  ShiftTypeNotEligible: 'Nicht freigegebene Schichtart',
  InsufficientRest: 'Ruhezeit unterschritten',
  ShiftOverlap: 'Überlappende Schichten',
  Understaffed: 'Unterbesetzung',
  Overstaffed: 'Überbesetzung',
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
const planningBoard = ref<Map<string, PlanningBoardEntry>>(new Map())
const assignmentsByEmployeeAndDate = ref<Map<string, Map<string, Assignment[]>>>(new Map())
const holidays = ref<PublicHoliday[]>([])
const assignments = ref<Assignment[]>([])
const validation = ref<ValidationResult | null>(null)
const expandedIssueGroups = ref<Set<string>>(new Set())
const selectedAssignment = ref<Assignment | null>(null)
const anchorDate = ref(new Date())
const loading = ref(true)
const error = ref('')
const creatingSchedule = ref(false)
const copyingMonth = ref(false)
const publishing = ref(false)
const archiving = ref(false)
const confirmingArchive = ref(false)
const search = ref('')
const teamFilter = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)
const route = useRoute()
const router = useRouter()
const tableWrapRef = ref<HTMLElement | null>(null)
const showShortcuts = ref(false)
const highlightKey = ref<string | null>(null)
const suggestingShiftType = ref<ShiftType | null>(null)
const showAutoFill = ref(false)

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
// issue #79: the grid is only editable (drag/drop create+move, delete, auto-fill, suggestions)
// while the Schedule is still Draft — the backend already 409s all of that once it isn't
// (issue #68), this just keeps the UI from offering actions that would fail.
const isDraft = computed(() => currentSchedule.value?.status === SCHEDULE_STATUS_DRAFT)
const isPublished = computed(() => currentSchedule.value?.status === SCHEDULE_STATUS_PUBLISHED)
const blockingErrorCount = computed(() => validation.value?.errors.length ?? 0)
const publishBlockReason = computed(() =>
  blockingErrorCount.value > 0
    ? `${blockingErrorCount.value} Fehler müssen zuerst behoben werden.`
    : undefined,
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
// issue #57: the holiday-dot grid can only reflect one Bundesland at a time — when exactly
// one team is selected via the filter and it has a Bundesland set, use that; with no filter
// (or a filter that doesn't resolve to a single state) the dots stay nationwide-only. This is
// a UI-only limitation — the wage-surcharge calculation (the more important half of this fix)
// is resolved per-employee server-side via each assignment's own Team, independent of this.
function isWeekend(date: Date): boolean {
  const day = date.getDay()
  return day === 0 || day === 6
}
function isCellHighlighted(employeeId: string, dateIso: string): boolean {
  const key = `${employeeId}|${dateIso}`
  return dragOverKey.value === key || highlightKey.value === key
}
async function loadHolidays() {
  const params: Record<string, string> = { start: monthStartIso.value, end: monthEndIso.value }
  const team = teamFilter.value ? teams.value.find((t) => t.id === teamFilter.value) : null
  if (team?.bundesland !== null && team?.bundesland !== undefined) {
    params.bundesland = String(team.bundesland)
  }
  const res = await api.get('/public-holidays', { params })
  holidays.value = res.data
}
function assignmentsFor(employeeId: string, dateIso: string) {
  return assignmentsByEmployeeAndDate.value.get(employeeId)?.get(dateIso) ?? []
}
function netHoursFor(employeeId: string) {
  return planningBoard.value.get(employeeId)?.plannedHours ?? 0
}
function targetHoursFor(employeeId: string): number | null {
  const target = planningBoard.value.get(employeeId)?.targetHours
  return target == null ? null : Math.round(target * 10) / 10
}
function barWidth(employeeId: string) {
  const target = targetHoursFor(employeeId)
  if (!target) return 0
  return Math.min(100, (netHoursFor(employeeId) / target) * 100)
}
function carriedOverFor(employeeId: string): number {
  return Math.round((planningBoard.value.get(employeeId)?.balanceHours ?? 0) * 10) / 10
}
// issue #72: one GET /planning-board request for every employee in the schedule's date range,
// replacing the old per-employee contracts/absences/hours-balance N+1 calls. Normalizes the
// response once into assignmentsByEmployeeAndDate (for the grid's per-cell lookups) and
// planningBoard (for the per-employee target/planned/balance readouts) instead of filtering
// flat arrays repeatedly.
async function loadPlanningBoard() {
  if (!currentSchedule.value) {
    assignments.value = []
    assignmentsByEmployeeAndDate.value = new Map()
    planningBoard.value = new Map()
    return
  }
  const res = await api.get('/planning-board', {
    params: { from: currentSchedule.value.startDate, to: currentSchedule.value.endDate },
  })
  const flatAssignments: Assignment[] = []
  const byEmployeeAndDate = new Map<string, Map<string, Assignment[]>>()
  const board = new Map<string, PlanningBoardEntry>()
  for (const e of res.data.employees as PlanningBoardEmployeeApi[]) {
    flatAssignments.push(...e.assignments)
    const byDate = new Map<string, Assignment[]>()
    for (const a of e.assignments) {
      const list = byDate.get(a.date) ?? []
      list.push(a)
      byDate.set(a.date, list)
    }
    byEmployeeAndDate.set(e.employeeId, byDate)
    board.set(e.employeeId, {
      targetHours: e.targetHours,
      plannedHours: e.plannedHours,
      balanceHours: e.balanceHours,
    })
  }
  assignments.value = flatAssignments
  assignmentsByEmployeeAndDate.value = byEmployeeAndDate
  planningBoard.value = board
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
    await loadPlanningBoard()
    validation.value = null
    return
  }
  const scheduleId = currentSchedule.value.id
  const [, validationRes] = await Promise.all([
    loadPlanningBoard(),
    api.get(`/schedules/${scheduleId}/validate`),
  ])
  validation.value = validationRes.data
}
watch(currentSchedule, loadDetail, { immediate: true })

// issue #41: search/team filter persisted to the URL query string, so navigating away and
// back (or bookmarking/sharing a filtered view) doesn't lose the selection.
function filterQuery(): Record<string, string> {
  const query: Record<string, string> = {}
  if (search.value) query.q = search.value
  if (teamFilter.value) query.team = teamFilter.value
  return query
}
watch([search, teamFilter], () => {
  router.replace({ query: filterQuery() })
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const hadTeamQuery = typeof route.query.team === 'string'
    if (typeof route.query.q === 'string') search.value = route.query.q
    if (hadTeamQuery) teamFilter.value = route.query.team as string

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

    // issue #43: fall back to the Settings-configured default team filter, but only when
    // the URL didn't already specify one — an explicit ?team= (e.g. a shared/bookmarked
    // link) always wins over the user's own default.
    if (!hadTeamQuery && settings.defaultTeamId) {
      if (teams.value.some((t) => t.id === settings.defaultTeamId)) {
        teamFilter.value = settings.defaultTeamId
      }
    }

    // Deep link from the dashboard's pain-point/planning-status links (issue #30/#31):
    // jump to the month of the linked schedule instead of always showing today's month.
    // Strips scheduleId once consumed but keeps the q/team filter params intact.
    const linkedId = route.query.scheduleId
    if (typeof linkedId === 'string') {
      const linked = schedules.value.find((s) => s.id === linkedId)
      if (linked) anchorDate.value = parseIso(linked.startDate)
      router.replace({ query: filterQuery() })
    }

    await loadHolidays()
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
  if (selectedAssignment.value || showShortcuts.value) return
  if (e.key === '/' && !isTyping()) {
    e.preventDefault()
    searchInputRef.value?.focus()
  } else if (e.key === '?' && !isTyping()) {
    showShortcuts.value = true
  } else if (e.key === 'ArrowLeft' && !isTyping()) {
    prevMonth()
  } else if (e.key === 'ArrowRight' && !isTyping()) {
    nextMonth()
  }
}

// issue #78: group the flat errors/warnings lists by rule type for the panel's collapsible
// sections, sorted errors-before-warnings then by group size — ScheduleValidator's output
// shape itself is unchanged, this is purely a frontend presentation grouping.
const validationGroups = computed<ValidationIssueGroup[]>(() => {
  if (!validation.value) return []
  const groups = new Map<string, ValidationIssueGroup>()
  const addAll = (issues: ValidationIssue[], severity: 'error' | 'warning') => {
    for (const issue of issues) {
      const existing = groups.get(issue.type)
      if (existing) existing.issues.push(issue)
      else
        groups.set(issue.type, {
          type: issue.type,
          label: ISSUE_TYPE_LABELS[issue.type] ?? issue.type,
          severity,
          issues: [issue],
        })
    }
  }
  addAll(validation.value.errors, 'error')
  addAll(validation.value.warnings, 'warning')
  return [...groups.values()].sort((a, b) => {
    if (a.severity !== b.severity) return a.severity === 'error' ? -1 : 1
    return b.issues.length - a.issues.length
  })
})
function toggleIssueGroup(type: string) {
  const next = new Set(expandedIssueGroups.value)
  if (next.has(type)) next.delete(type)
  else next.add(type)
  expandedIssueGroups.value = next
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

onMounted(load)
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(cleanupDrag)
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
watch(monthStartIso, () => {
  if (!loading.value) loadHolidays()
})
// issue #57: re-resolve the (at most one) Bundesland the holiday dots use when the team
// filter changes, independent of the month-nav watch above.
watch(teamFilter, () => {
  if (!loading.value) loadHolidays()
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
    toast.success('Dienstplan angelegt.')
  } catch {
    toast.error('Dienstplan konnte nicht angelegt werden.')
  } finally {
    creatingSchedule.value = false
  }
}

// issue #79: replaces the Schedule in the already-loaded list with the backend's response,
// rather than a full reload — /publish and /archive don't change anything else about the
// month (assignments, other schedules), so the rest of the page's already-fetched state stays
// valid as-is.
function updateCurrentScheduleFrom(dto: Schedule) {
  const idx = schedules.value.findIndex((s) => s.id === dto.id)
  if (idx !== -1) schedules.value[idx] = dto
}

// issue #68/#79: the button is already disabled while blockingErrorCount > 0, but re-checks
// the 409 case too (e.g. another manager changed something between page load and this click).
async function onPublish() {
  if (!currentSchedule.value) return
  publishing.value = true
  try {
    const res = await api.post(`/schedules/${currentSchedule.value.id}/publish`)
    updateCurrentScheduleFrom(res.data)
    toast.success('Dienstplan veröffentlicht.')
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      await loadDetail()
      toast.error('Veröffentlichen nicht möglich — es bestehen noch ungelöste Fehler.')
    } else {
      toast.error('Dienstplan konnte nicht veröffentlicht werden.')
    }
  } finally {
    publishing.value = false
  }
}

async function onArchiveConfirmed() {
  if (!currentSchedule.value) return
  archiving.value = true
  try {
    const res = await api.post(`/schedules/${currentSchedule.value.id}/archive`)
    updateCurrentScheduleFrom(res.data)
    toast.success('Dienstplan archiviert.')
  } catch {
    toast.error('Dienstplan konnte nicht archiviert werden.')
  } finally {
    archiving.value = false
    confirmingArchive.value = false
  }
}

async function onCopyMonth() {
  if (!currentSchedule.value) return
  copyingMonth.value = true
  try {
    const nextStart = addMonths(anchorDate.value, 1)
    // issue #82: the whole copy (target-schedule creation + every assignment) is computed and
    // applied atomically server-side in one request, instead of a per-assignment POST loop that
    // could leave a partial copy behind if one request in the middle failed.
    const res = await api.post(`/schedules/${currentSchedule.value.id}/copy`, {
      targetName: monthFmt.format(nextStart),
      targetStartDate: toIso(nextStart),
      targetEndDate: toIso(lastOfMonth(nextStart)),
    })
    if (!schedules.value.some((s) => s.id === res.data.target.id)) {
      schedules.value.push(res.data.target)
    }
    toast.success('Monat kopiert.')
    nextMonth()
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      error.value = 'Nächster Monat hat bereits Schichten — Kopieren abgebrochen.'
      toast.error(error.value)
    } else {
      toast.error('Monat konnte nicht kopiert werden.')
    }
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
  if (e.button !== 0) {
    return
  }
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

  try {
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
        breakStartTime: assignment.breakStartTime,
      })
    }
    await loadDetail()
  } catch {
    toast.error('Schicht konnte nicht gespeichert werden.')
  }
}

async function onAssignmentUpdated() {
  selectedAssignment.value = null
  await loadDetail()
}

// PDF export = the browser's own print-to-PDF, scoped via CSS rather than a
// PDF-generation library. `printEmployeeId` narrows the printed table to one
// row; "all" export is just printing with it unset.
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
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-semibold">Dienstplan</h1>
      <div class="flex items-center gap-3">
        <button
          class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
          @click="prevMonth"
        >
          <ChevronLeft :size="18" />
        </button>
        <span class="font-mono text-sm text-slate-400 capitalize">{{ monthLabel }}</span>
        <button
          class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
          @click="nextMonth"
        >
          <ChevronRight :size="18" />
        </button>
        <span
          v-if="currentSchedule"
          class="rounded-full px-2 py-0.5 text-[10px] uppercase tracking-wider font-bold"
          :class="{
            'bg-slate-500/15 text-slate-400': currentSchedule.status === 0,
            'bg-emerald-500/15 text-emerald-400': currentSchedule.status === 1,
            'bg-violet-500/15 text-violet-400': currentSchedule.status === 2,
          }"
          :title="
            currentSchedule.publishedAt
              ? `Veröffentlicht am ${new Date(currentSchedule.publishedAt).toLocaleString('de-DE')}${currentSchedule.publishedBy ? ' von ' + currentSchedule.publishedBy : ''}`
              : undefined
          "
        >
          {{ STATUS_LABELS[currentSchedule.status] }}
        </span>
        <button
          class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
          title="Tastenkürzel anzeigen (?)"
          @click="showShortcuts = true"
        >
          <HelpCircle :size="18" />
        </button>
        <button
          v-if="currentSchedule && isDraft"
          :disabled="publishing || blockingErrorCount > 0"
          :title="publishBlockReason"
          class="flex items-center gap-1.5 rounded-lg bg-linear-to-r from-emerald-600 to-emerald-500 px-3 py-1.5 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-40 disabled:cursor-not-allowed print:hidden"
          @click="onPublish"
        >
          <CheckCircle2 :size="14" />
          {{ publishing ? 'Veröffentlichen…' : 'Veröffentlichen' }}
        </button>
        <button
          v-if="currentSchedule && isPublished"
          :disabled="archiving"
          class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors disabled:opacity-50 print:hidden"
          @click="confirmingArchive = true"
        >
          <Archive :size="14" />
          Archivieren
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
          class="glass rounded-xl mb-4 text-sm print:hidden"
        >
          <div class="flex flex-wrap items-center gap-4 px-4 py-3 border-b border-white/8">
            <span
              v-if="validation.errors.length"
              class="flex items-center gap-1.5 font-semibold text-rose-400"
            >
              <span class="w-2 h-2 rounded-full bg-rose-400 shrink-0"></span>
              {{ validation.errors.length }} Fehler
            </span>
            <span
              v-if="validation.warnings.length"
              class="flex items-center gap-1.5 font-semibold text-amber-400"
            >
              ▲ {{ validation.warnings.length }} Warnungen
            </span>
          </div>
          <div class="divide-y divide-white/5">
            <div v-for="group in validationGroups" :key="group.type">
              <button
                class="w-full flex items-center justify-between gap-2 px-4 py-2 text-left hover:bg-white/5 transition-colors"
                @click="toggleIssueGroup(group.type)"
              >
                <span
                  class="flex items-center gap-2"
                  :class="group.severity === 'error' ? 'text-rose-400' : 'text-amber-400'"
                >
                  {{ group.severity === 'error' ? '❌' : '⚠' }} {{ group.label }}
                  <span class="text-slate-500 font-mono text-xs">({{ group.issues.length }})</span>
                </span>
                <ChevronDown
                  :size="14"
                  class="text-slate-500 transition-transform shrink-0"
                  :class="{ 'rotate-180': expandedIssueGroups.has(group.type) }"
                />
              </button>
              <div v-if="expandedIssueGroups.has(group.type)" class="pb-2">
                <p
                  v-for="(issue, i) in group.issues"
                  :key="i"
                  class="px-4 py-1 text-xs"
                  :class="[
                    group.severity === 'error' ? 'text-rose-400/90' : 'text-amber-400/90',
                    { 'cursor-pointer hover:underline': issue.employeeId },
                  ]"
                  @click="focusIssue(issue)"
                >
                  {{ issue.message }}
                </p>
              </div>
            </div>
          </div>
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
          <button
            v-if="activeShiftTypes.length && isDraft"
            class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors"
            @click="showAutoFill = true"
          >
            <Wand2 :size="14" />
            Automatisch füllen
          </button>
          <!-- issue #79: once the Schedule isn't Draft, these stay visible as a color legend
               but lose drag-to-create and the suggestion action — both would just 409. -->
          <div
            v-for="s in activeShiftTypes"
            :key="s.id"
            class="flex items-center gap-2 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm touch-none select-none"
            :class="isDraft ? 'cursor-grab' : 'cursor-default'"
            @pointerdown="isDraft && onChipPointerDown($event, paletteDragPayload(s))"
          >
            <span
              class="w-2.5 h-2.5 rounded-full shrink-0"
              :style="{ backgroundColor: s.color }"
            ></span>
            {{ s.name }}
            <span class="font-mono text-slate-500 text-xs"
              >{{ s.startTime.slice(0, 5) }}–{{ s.endTime.slice(0, 5) }}</span
            >
            <button
              v-if="isDraft"
              type="button"
              title="Mitarbeiter vorschlagen"
              class="text-slate-500 hover:text-indigo-300 transition-colors"
              @pointerdown.stop
              @click.stop="suggestingShiftType = s"
            >
              <Sparkles :size="13" />
            </button>
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
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="e in visibleEmployees"
                :key="e.id"
                class="border-b border-white/5 last:border-0"
                :class="{
                  'print:hidden': printEmployeeId && printEmployeeId !== e.id,
                  'bg-blue-500/10': highlightKey === e.id,
                }"
              >
                <td
                  class="px-4 py-3 align-top sticky left-0 z-10 shadow-[4px_0_8px_-4px_rgba(0,0,0,0.5)] print:static print:shadow-none"
                  :style="{ backgroundColor: highlightKey === e.id ? '#161d2f' : '#11141c' }"
                >
                  <div class="flex items-center gap-1.5">
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
                    <div
                      v-if="carriedOverFor(e.id) !== 0"
                      class="font-mono text-[11px] mt-0.5"
                      :class="carriedOverFor(e.id) > 0 ? 'text-emerald-400' : 'text-rose-400'"
                    >
                      Übertrag: {{ carriedOverFor(e.id) > 0 ? '+' : '' }}{{ carriedOverFor(e.id) }}h
                    </div>
                    <div class="w-24 h-1 rounded-full bg-white/10 mt-1 overflow-hidden">
                      <div
                        class="h-full bg-linear-to-r from-blue-600 to-indigo-600"
                        :style="{ width: barWidth(e.id) + '%' }"
                      ></div>
                    </div>
                  </template>
                  <div
                    v-if="laborCostFor(e.id) !== null"
                    class="font-mono text-xs text-emerald-400 mt-1"
                  >
                    {{ currencyFmt.format(laborCostFor(e.id)!) }}
                  </div>
                </td>
                <td
                  v-for="d in days"
                  :key="toIso(d)"
                  class="px-2 py-2 align-top transition-colors"
                  :class="{
                    'bg-blue-500/10 ring-1 ring-inset ring-blue-500/50': isCellHighlighted(
                      e.id,
                      toIso(d),
                    ),
                    'bg-white/[0.03]': isWeekend(d) && !isCellHighlighted(e.id, toIso(d)),
                  }"
                  :data-employee-id="e.id"
                  :data-date="toIso(d)"
                >
                  <div
                    v-for="a in assignmentsFor(e.id, toIso(d))"
                    :key="a.id"
                    class="rounded-lg bg-white/5 border border-white/10 px-2 py-1 mb-1 cursor-pointer hover:bg-white/10 transition-colors touch-none select-none"
                    :class="{
                      'opacity-40':
                        drag?.active &&
                        drag.payload.kind === 'assignment' &&
                        drag.payload.assignmentId === a.id,
                    }"
                    @pointerdown="isDraft && onChipPointerDown($event, assignmentDragPayload(a))"
                    @click="!isDraft && (selectedAssignment = a)"
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
      :readonly="!isDraft"
      @close="selectedAssignment = null"
      @updated="onAssignmentUpdated"
    />

    <ConfirmDialog
      v-if="confirmingArchive"
      title="Dienstplan archivieren"
      message="Diesen veröffentlichten Dienstplan als archiviert markieren? Er bleibt einsehbar, kann aber nicht mehr bearbeitet werden."
      confirm-label="Archivieren"
      @confirm="onArchiveConfirmed"
      @close="confirmingArchive = false"
    />

    <ShiftSuggestionModal
      v-if="suggestingShiftType && currentSchedule"
      :schedule-id="currentSchedule.id"
      :shift-type="suggestingShiftType"
      :default-date="monthStartIso"
      :min-date="monthStartIso"
      :max-date="monthEndIso"
      @close="suggestingShiftType = null"
      @assigned="loadDetail"
    />

    <AutoFillModal
      v-if="showAutoFill && currentSchedule"
      :schedule-id="currentSchedule.id"
      :month-start="monthStartIso"
      :month-end="monthEndIso"
      :shift-types="shiftTypes"
      @close="showAutoFill = false"
      @committed="loadDetail"
    />

    <ModalShell v-if="showShortcuts" title="Tastenkürzel" @close="showShortcuts = false">
      <ul class="text-sm divide-y divide-white/5">
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Suche fokussieren</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">/</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Vorheriger Monat</span>
          <kbd class="font-mono text-xs rounded bg-white/10 px-1.5 py-0.5">←</kbd>
        </li>
        <li class="flex items-center justify-between py-2">
          <span class="text-slate-400">Nächster Monat</span>
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
      </ul>
    </ModalShell>
  </div>
</template>

<style scoped>
@media print {
  @page {
    size: landscape;
  }
}
</style>
