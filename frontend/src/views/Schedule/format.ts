// Pure date-math and formatting helpers used across the Dienstplan's decomposed
// components/composables (issue #73). No Vue reactivity, no API calls — safe to import
// from anywhere without pulling in component/composable state.

export function firstOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1)
}
export function lastOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0)
}
export function addDays(date: Date, n: number): Date {
  const d = new Date(date)
  d.setDate(d.getDate() + n)
  return d
}
export function addMonths(date: Date, n: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + n, 1)
}
export function toIso(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}
export function parseIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}
// Monday of the calendar week containing `date` (ISO 8601 week start) — issue #74's
// week-scoped ScheduleView.vue and the MonthOverviewView.vue grouping both need this.
export function startOfWeek(date: Date): Date {
  const day = date.getDay() // 0 = Sunday .. 6 = Saturday
  const diff = day === 0 ? -6 : 1 - day
  return addDays(date, diff)
}

export const weekdayFmt = new Intl.DateTimeFormat('de-DE', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
})
export const monthFmt = new Intl.DateTimeFormat('de-DE', { month: 'long', year: 'numeric' })
export const currencyFmt = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })
