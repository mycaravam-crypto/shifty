import { computed, ref, watch } from 'vue'
import api from '@/services/api'
import {
  addDays,
  addMonths,
  firstOfMonth,
  lastOfMonth,
  parseIso,
  toIso,
  monthFmt,
} from '@/views/Schedule/format'
import type {
  Absence,
  Assignment,
  Contract,
  Employee,
  PublicHoliday,
  Schedule,
  ShiftType,
  Team,
  ValidationResult,
} from '@/views/Schedule/types'
import type { useScheduleFilters } from './useScheduleFilters'

// Loading + normalized state for the Dienstplan (issue #73's `usePlanningBoard`). Owns every
// reference/derived dataset the grid needs (employees, teams, shift types, schedules,
// assignments, contracts, absences, hour balances, holidays, validation) plus month
// navigation and the per-employee stats the grid renders — everything ScheduleView.vue used
// to hold directly. Takes the filters composable so `load()`/`loadHolidays()` can resolve the
// URL/Settings-driven team filter without owning that state itself.
export function usePlanningBoard(filters: ReturnType<typeof useScheduleFilters>) {
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
  const anchorDate = ref(new Date())
  const loading = ref(true)
  const error = ref('')

  const monthStart = computed(() => firstOfMonth(anchorDate.value))
  const monthEnd = computed(() => lastOfMonth(anchorDate.value))
  const monthStartIso = computed(() => toIso(monthStart.value))
  const monthEndIso = computed(() => toIso(monthEnd.value))
  const monthLabel = computed(() => monthFmt.format(monthStart.value))

  const activeEmployees = computed(() => employees.value.filter((e) => e.active))
  const activeShiftTypes = computed(() => shiftTypes.value.filter((s) => s.active))
  const visibleEmployees = computed(() => {
    const term = filters.search.value.trim().toLowerCase()
    return activeEmployees.value.filter((e) => {
      if (filters.teamFilter.value && e.teamId !== filters.teamFilter.value) return false
      if (term && !`${e.firstName} ${e.lastName}`.toLowerCase().includes(term)) return false
      return true
    })
  })

  const currentSchedule = computed(() =>
    schedules.value.find((s) => s.startDate === monthStartIso.value),
  )

  const days = computed(() => {
    const start = currentSchedule.value
      ? parseIso(currentSchedule.value.startDate)
      : monthStart.value
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
  function isWeekend(date: Date): boolean {
    const day = date.getDay()
    return day === 0 || day === 6
  }
  // issue #57: the holiday-dot grid can only reflect one Bundesland at a time — when exactly
  // one team is selected via the filter and it has a Bundesland set, use that; with no filter
  // (or a filter that doesn't resolve to a single state) the dots stay nationwide-only. This
  // is a UI-only limitation — the wage-surcharge calculation (the more important half of this
  // fix) is resolved per-employee server-side via each assignment's own Team, independent of
  // this.
  async function loadHolidays() {
    const params: Record<string, string> = { start: monthStartIso.value, end: monthEndIso.value }
    const team = filters.teamFilter.value
      ? teams.value.find((t) => t.id === filters.teamFilter.value)
      : null
    if (team?.bundesland !== null && team?.bundesland !== undefined) {
      params.bundesland = String(team.bundesland)
    }
    const res = await api.get('/public-holidays', { params })
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
      const hadTeamQuery = filters.applyRouteQuery()

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

      filters.applyDefaultTeamId(hadTeamQuery, teams.value)

      const linked = filters.consumeScheduleDeepLink(schedules.value)
      if (linked) anchorDate.value = parseIso(linked.startDate)

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

  watch(monthStartIso, () => {
    if (!loading.value) {
      loadBalances()
      loadHolidays()
    }
  })
  // issue #57: re-resolve the (at most one) Bundesland the holiday dots use when the team
  // filter changes, independent of the month-nav watch above.
  watch(filters.teamFilter, () => {
    if (!loading.value) loadHolidays()
  })

  function prevMonth() {
    anchorDate.value = addMonths(anchorDate.value, -1)
  }
  function nextMonth() {
    anchorDate.value = addMonths(anchorDate.value, 1)
  }

  return {
    employees,
    teams,
    shiftTypes,
    schedules,
    assignments,
    validation,
    anchorDate,
    loading,
    error,
    monthStart,
    monthEnd,
    monthStartIso,
    monthEndIso,
    monthLabel,
    activeEmployees,
    activeShiftTypes,
    visibleEmployees,
    currentSchedule,
    days,
    shiftTypeById,
    holidayFor,
    isWeekend,
    assignmentsFor,
    netHoursFor,
    targetHoursFor,
    carriedOverFor,
    laborCostFor,
    totalLaborCost,
    load,
    loadDetail,
    loadBalances,
    loadHolidays,
    prevMonth,
    nextMonth,
  }
}
