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

  // issue #79: the grid is only editable (drag/drop create+move, delete, auto-fill, suggestions)
  // while the Schedule is still Draft — the backend already 409s all of that once it isn't
  // (issue #68), this just keeps the UI from offering actions that would fail.
  const SCHEDULE_STATUS_DRAFT = 0
  const SCHEDULE_STATUS_PUBLISHED = 1
  const isDraft = computed(() => currentSchedule.value?.status === SCHEDULE_STATUS_DRAFT)
  const isPublished = computed(() => currentSchedule.value?.status === SCHEDULE_STATUS_PUBLISHED)
  const blockingErrorCount = computed(() => validation.value?.errors.length ?? 0)
  const publishBlockReason = computed(() =>
    blockingErrorCount.value > 0
      ? `${blockingErrorCount.value} Fehler müssen zuerst behoben werden.`
      : undefined,
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
  // issue #70: mirrors WorkingTimeCalculator.ExpectedHours (backend) — resolves the applicable
  // Contract PER DAY of the visible month via Contract.ActiveOn's same rule, then applies the
  // WeeklyHours*days/7 formula once per contract encountered (not once for the whole month) so a
  // contract that changes mid-month correctly blends both segments. This used to resolve one
  // contract active on the month's *first* day and scale the whole month by it, silently
  // ignoring any mid-month contract change.
  function activeContractOn(contracts: Contract[], dateIso: string): Contract | undefined {
    return [...contracts]
      .filter((c) => c.validFrom <= dateIso && (!c.validTo || c.validTo >= dateIso))
      .sort((a, b) => b.validFrom.localeCompare(a.validFrom))[0]
  }
  function absenceDaySet(absences: Absence[]): Set<string> {
    const days = new Set<string>()
    for (const a of absences) {
      const start = a.from > monthStartIso.value ? a.from : monthStartIso.value
      const end = a.to < monthEndIso.value ? a.to : monthEndIso.value
      for (let d = parseIso(start); d <= parseIso(end); d = addDays(d, 1)) {
        days.add(toIso(d))
      }
    }
    return days
  }
  // Groups each non-absence day of the visible month by which contract (identified by its
  // unique-per-employee validFrom) applies. weeklyHours*days/7 is then applied once per contract
  // rather than once for the whole month — the single-contract common case reduces to exactly the
  // old flat formula, while a mid-month contract change now correctly gets two segments.
  function daysByContract(contracts: Contract[], absenceDays: Set<string>): Map<string, number> {
    const days = new Map<string, number>()
    for (
      let d = parseIso(monthStartIso.value);
      d <= parseIso(monthEndIso.value);
      d = addDays(d, 1)
    ) {
      const dateIso = toIso(d)
      if (absenceDays.has(dateIso)) continue
      const contract = activeContractOn(contracts, dateIso)
      if (contract) days.set(contract.validFrom, (days.get(contract.validFrom) ?? 0) + 1)
    }
    return days
  }
  function targetHoursFor(employeeId: string): number | null {
    const contracts = contractsByEmployee.value.get(employeeId) ?? []
    if (!contracts.length) return null

    const absences = absencesByEmployee.value.get(employeeId) ?? []
    const byContract = daysByContract(contracts, absenceDaySet(absences))
    if (byContract.size === 0) return null

    let total = 0
    for (const [validFrom, days] of byContract) {
      const contract = contracts.find((c) => c.validFrom === validFrom)
      if (contract) total += (contract.weeklyHours * days) / 7
    }
    return Math.round(total * 10) / 10
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

  // issue #79: replaces the Schedule in the already-loaded list with the backend's response,
  // rather than a full reload — /publish and /archive don't change anything else about the
  // month (assignments, other schedules), so the rest of the page's already-fetched state stays
  // valid as-is.
  function updateCurrentScheduleFrom(dto: Schedule) {
    const idx = schedules.value.findIndex((s) => s.id === dto.id)
    if (idx !== -1) schedules.value[idx] = dto
  }

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
    isDraft,
    isPublished,
    blockingErrorCount,
    publishBlockReason,
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
    updateCurrentScheduleFrom,
    prevMonth,
    nextMonth,
  }
}
