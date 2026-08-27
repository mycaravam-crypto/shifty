// Shared DTOs for the Dienstplan (Wochenansicht) — split out of ScheduleView.vue (issue #73)
// so the composables/components decomposed from it can all reference the same shapes.

export interface Employee {
  id: string
  firstName: string
  lastName: string
  active: boolean
  teamId: string | null
}
export interface Team {
  id: string
  name: string
  // Backend serializes enums as their ordinal, not a string (same as AbsenceType elsewhere) —
  // null = nationwide-only.
  bundesland: number | null
}
export interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  color: string
  active: boolean
  minStaffing: number | null
  maxStaffing: number | null
  // issue #157: lets a template itself represent a recurring overnight shift (e.g. 22:00-06:00)
  // so an assignment created from it carries the flag through automatically.
  endsNextDay: boolean
}
export interface Schedule {
  id: string
  name: string
  startDate: string
  endDate: string
  status: number
  publishedAt: string | null
  publishedBy: string | null
}
export interface Assignment {
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
  // issue #157: true means EndTime falls on the calendar day after `date`.
  endsNextDay: boolean
}
export interface Contract {
  employeeId: string
  validFrom: string
  validTo: string | null
  weeklyHours: number
}
export interface Absence {
  employeeId: string
  from: string
  to: string
}
export interface PublicHoliday {
  date: string
  name: string
}
export interface ValidationIssue {
  type: string
  message: string
  employeeId: string | null
  shiftAssignmentId: string | null
}
export interface ValidationResult {
  errors: ValidationIssue[]
  warnings: ValidationIssue[]
  isValid: boolean
}
