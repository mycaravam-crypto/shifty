<script setup lang="ts">
// PDF export, redesigned. The interactive grid (PlanningGrid.vue) is dark, glassy, and built
// for on-screen drag-and-drop — reusing it for print (the previous approach, toggling
// `print:` utility classes on the same DOM) fought the dark theme, sticky positioning, and
// chip padding at every turn and still didn't reliably fit on one page. This component is a
// completely separate, print-only layout with its own light, ink-friendly styling: it renders
// nothing on screen (`hidden print:block`) and is the only thing visible when printing —
// PlanningGrid's wrapper is `print:hidden` now.
//
// Two layouts, chosen by whether a single employee is being exported (the "PDF exportieren"
// toolbar button vs. a row's own printer icon):
// - Single employee: a plain day-by-day list (the shape a member actually wants to check "when
//   do I work this week" or pin to a fridge) plus an hours summary, not a one-row slice of a
//   multi-employee grid.
// - Whole team: a compact grid — the day columns are a fixed share of the page width (via
//   `<colgroup>`, `dayColWidth` below) instead of auto-sizing to content, so a full month's
//   28-31 columns always fits the page width, not just a week's 7. Each cell shows a colored
//   shift-type initial (same abbreviation the interactive month overview uses) rather than the
//   full "name · time" text the single-employee list can afford — there's no room for that at
//   month width, and print has no hover tooltip to fall back on, hence the legend below the grid.
import { computed } from 'vue'
import { currencyFmt, dateLongFmt, toIso, weekdayLongFmt } from './format'
import type { Assignment, Employee, PublicHoliday, ShiftType } from './types'

const weekdayLetterFmt = new Intl.DateTimeFormat('de-DE', { weekday: 'narrow' })

// Same ordinal→label mapping PlanningToolbar.vue uses for the on-screen status badge
// (Domain/Scheduling/Schedule.cs's ScheduleStatus, serialized as its ordinal).
const STATUS_LABELS: Record<number, string> = {
  0: 'Entwurf',
  1: 'Veröffentlicht',
  2: 'Archiviert',
}

const props = defineProps<{
  printEmployeeId: string | null
  employees: Employee[]
  days: Date[]
  scheduleName: string
  scheduleStatus: number | null
  periodLabel: string
  holidayFor: (dateIso: string) => PublicHoliday | undefined
  isWeekend: (d: Date) => boolean
  isAbsentOn: (employeeId: string, dateIso: string) => boolean
  shiftTypeById: (id: string) => ShiftType | undefined
  assignmentsFor: (employeeId: string, dateIso: string) => Assignment[]
  netHoursFor: (employeeId: string) => number
  targetHoursFor: (employeeId: string) => number | null
  carriedOverFor: (employeeId: string) => number
  laborCostFor: (employeeId: string) => number | null
}>()

const generatedAtFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

const printedEmployees = computed(() =>
  props.printEmployeeId
    ? props.employees.filter((e) => e.id === props.printEmployeeId)
    : props.employees,
)
const singleEmployee = computed(() =>
  props.printEmployeeId ? printedEmployees.value[0] : undefined,
)

function shiftLabel(a: Assignment): string {
  const type = props.shiftTypeById(a.shiftTypeId)
  const time = `${a.startTime.slice(0, 5)}–${a.endTime.slice(0, 5)}${a.endsNextDay ? ' (+1)' : ''}`
  return type ? `${type.name} · ${time}` : time
}
function dayNetHours(employeeId: string, dateIso: string): number {
  return props.assignmentsFor(employeeId, dateIso).reduce((sum, a) => sum + a.netHours, 0)
}
// "This week" total, from exactly the days shown on this sheet — distinct from netHoursFor/
// targetHoursFor below, which (matching the interactive row they're read from) are the whole
// month's figures regardless of which week is being viewed/printed.
function periodNetHours(employeeId: string): number {
  return props.days.reduce((sum, d) => sum + dayNetHours(employeeId, toIso(d)), 0)
}

const EMPLOYEE_COL_WIDTH = '30mm'
// Every day column gets an equal share of whatever page width is left over — this is what
// keeps a 31-column month on one landscape page instead of growing wider than it (see
// `table-layout: fixed` + `<colgroup>` below, which is what makes the browser actually honor
// this instead of auto-sizing columns to their content).
const dayColWidth = computed(
  () => `calc((100% - ${EMPLOYEE_COL_WIDTH}) / ${props.days.length || 1})`,
)

// Print has no hover tooltip to fall back on for the grid's single-letter shift badges, so spell
// out what each color/letter actually means — only for shift types that actually appear in the
// printed range, not every ShiftType in the system.
const usedShiftTypes = computed(() => {
  const seen = new Map<string, ShiftType>()
  for (const e of printedEmployees.value) {
    for (const d of props.days) {
      for (const a of props.assignmentsFor(e.id, toIso(d))) {
        const type = props.shiftTypeById(a.shiftTypeId)
        if (type) seen.set(type.id, type)
      }
    }
  }
  return [...seen.values()]
})
const hasAbsences = computed(() =>
  printedEmployees.value.some((e) => props.days.some((d) => props.isAbsentOn(e.id, toIso(d)))),
)
</script>

<template>
  <div class="hidden print:block print-sheet">
    <header class="print-head">
      <div class="print-head-row">
        <h1>{{ scheduleName || 'Dienstplan' }}</h1>
        <span v-if="scheduleStatus !== null" class="print-status">{{
          STATUS_LABELS[scheduleStatus]
        }}</span>
      </div>
      <p class="print-period">{{ periodLabel }}</p>
      <p v-if="singleEmployee" class="print-employee-name">
        {{ singleEmployee.lastName }}, {{ singleEmployee.firstName }}
      </p>
      <p class="print-generated">Erstellt am {{ generatedAtFmt.format(new Date()) }}</p>
    </header>

    <!-- Single employee: one row per day, always all 7 — a member should see their days off
         (and any Abwesenheit) just as plainly as their shifts, not just a sparse list of what's
         scheduled. -->
    <table v-if="singleEmployee" class="print-table print-table--days">
      <thead>
        <tr>
          <th class="col-day">Tag</th>
          <th class="col-date">Datum</th>
          <th>Schicht</th>
          <th class="col-hours">Std.</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="d in days"
          :key="toIso(d)"
          :class="{ weekend: isWeekend(d), holiday: holidayFor(toIso(d)) }"
        >
          <td class="col-day">{{ weekdayLongFmt.format(d) }}</td>
          <td class="col-date">
            {{ dateLongFmt.format(d) }}
            <span v-if="holidayFor(toIso(d))" class="print-tag" :title="holidayFor(toIso(d))?.name"
              >Feiertag</span
            >
          </td>
          <td v-if="assignmentsFor(singleEmployee.id, toIso(d)).length">
            <div v-for="a in assignmentsFor(singleEmployee.id, toIso(d))" :key="a.id">
              {{ shiftLabel(a) }}
            </div>
          </td>
          <td v-else class="print-off">
            {{ isAbsentOn(singleEmployee.id, toIso(d)) ? 'Abwesend' : 'Frei' }}
          </td>
          <td class="col-hours">
            {{
              dayNetHours(singleEmployee.id, toIso(d))
                ? `${dayNetHours(singleEmployee.id, toIso(d))} h`
                : '–'
            }}
          </td>
        </tr>
      </tbody>
    </table>

    <dl v-if="singleEmployee" class="print-summary">
      <div>
        <dt>Stunden in diesem Zeitraum</dt>
        <dd>{{ periodNetHours(singleEmployee.id) }} h</dd>
      </div>
      <div v-if="targetHoursFor(singleEmployee.id) !== null">
        <dt>Ist / Soll (Monat)</dt>
        <dd>{{ netHoursFor(singleEmployee.id) }} h / {{ targetHoursFor(singleEmployee.id) }} h</dd>
      </div>
      <div v-if="carriedOverFor(singleEmployee.id) !== 0">
        <dt>Übertrag aus Vormonaten</dt>
        <dd>
          {{ carriedOverFor(singleEmployee.id) > 0 ? '+' : ''
          }}{{ carriedOverFor(singleEmployee.id) }} h
        </dd>
      </div>
    </dl>

    <!-- Whole team: a compact grid, still light/print-styled — day columns are a fixed equal
         share of the page width (colgroup below) regardless of how many `days` the caller
         passed, so a full month's columns fit one landscape page just as a week's did. -->
    <table v-else class="print-table print-table--grid">
      <colgroup>
        <col :style="{ width: EMPLOYEE_COL_WIDTH }" />
        <col v-for="d in days" :key="toIso(d)" :style="{ width: dayColWidth }" />
      </colgroup>
      <thead>
        <tr>
          <th>Mitarbeiter</th>
          <th v-for="d in days" :key="toIso(d)" :class="{ weekend: isWeekend(d) }">
            <div>{{ weekdayLetterFmt.format(d) }}</div>
            <div class="print-daynum">{{ d.getDate() }}</div>
            <span
              v-if="holidayFor(toIso(d))"
              class="print-holiday-dot"
              :title="holidayFor(toIso(d))?.name"
            ></span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="e in printedEmployees" :key="e.id">
          <td class="print-employee-cell">
            {{ e.lastName }}, {{ e.firstName }}
            <div class="print-employee-hours">
              {{ netHoursFor(e.id) }} h<template v-if="targetHoursFor(e.id) !== null">
                / {{ targetHoursFor(e.id) }} h</template
              >
            </div>
          </td>
          <td v-for="d in days" :key="toIso(d)" :class="{ weekend: isWeekend(d) }">
            <div class="print-badges">
              <span
                v-for="a in assignmentsFor(e.id, toIso(d)).slice(0, 2)"
                :key="a.id"
                class="print-badge"
                :style="{ backgroundColor: shiftTypeById(a.shiftTypeId)?.color ?? '#94a3b8' }"
                >{{ (shiftTypeById(a.shiftTypeId)?.name ?? '?').slice(0, 1).toUpperCase()
                }}{{ a.endsNextDay ? '+' : '' }}</span
              >
              <span v-if="assignmentsFor(e.id, toIso(d)).length > 2" class="print-badge-more"
                >+{{ assignmentsFor(e.id, toIso(d)).length - 2 }}</span
              >
              <span v-if="isAbsentOn(e.id, toIso(d))" class="print-absent-mark" title="Abwesend"
                >A</span
              >
            </div>
          </td>
        </tr>
        <tr v-if="!printedEmployees.length">
          <td :colspan="days.length + 1" class="print-off">Keine Mitarbeiter.</td>
        </tr>
      </tbody>
    </table>
    <p v-if="!singleEmployee && (usedShiftTypes.length || hasAbsences)" class="print-legend">
      <span v-for="s in usedShiftTypes" :key="s.id" class="print-legend-item">
        <span class="print-badge" :style="{ backgroundColor: s.color }">{{
          s.name.slice(0, 1).toUpperCase()
        }}</span>
        {{ s.name }}
      </span>
      <span v-if="hasAbsences" class="print-legend-item">
        <span class="print-absent-mark">A</span> Abwesenheit
      </span>
    </p>
    <p
      v-if="!singleEmployee && printedEmployees.some((e) => laborCostFor(e.id) !== null)"
      class="print-total"
    >
      Lohnkosten gesamt:
      {{
        currencyFmt.format(printedEmployees.reduce((sum, e) => sum + (laborCostFor(e.id) ?? 0), 0))
      }}
    </p>

    <p class="print-footer">Schichtplaner{{ scheduleName ? ` · ${scheduleName}` : '' }}</p>
  </div>
</template>

<style scoped>
/* Print-only, and deliberately not sharing anything from the dark theme in style.css — a
   printed page starts from plain black-on-white, not "the dark UI with overrides". */
@media print {
  @page {
    size: landscape;
    margin: 14mm;
  }

  .print-sheet {
    color: #111827;
    font-family: 'Inter', ui-sans-serif, system-ui, sans-serif;
  }

  .print-head {
    margin-bottom: 4mm;
    border-bottom: 1.5pt solid #111827;
    padding-bottom: 2mm;
  }
  .print-head-row {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }
  .print-head h1 {
    font-size: 15pt;
    font-weight: 700;
    margin: 0;
  }
  .print-status {
    font-size: 8pt;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: #475569;
    border: 0.5pt solid #cbd5e1;
    border-radius: 3pt;
    padding: 0.5mm 2mm;
  }
  .print-period {
    font-size: 10pt;
    margin: 1mm 0 0;
    text-transform: capitalize;
  }
  .print-employee-name {
    font-size: 12pt;
    font-weight: 600;
    margin: 1mm 0 0;
  }
  .print-generated {
    font-size: 8pt;
    color: #6b7280;
    margin: 1mm 0 0;
  }

  .print-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 9.5pt;
  }
  .print-table th,
  .print-table td {
    border: 0.5pt solid #cbd5e1;
    padding: 1.5mm 2mm;
    text-align: left;
    vertical-align: top;
  }
  .print-table thead th {
    background: #f1f5f9;
    font-size: 8pt;
    text-transform: uppercase;
    letter-spacing: 0.03em;
    color: #475569;
  }
  .print-table tr {
    break-inside: avoid;
  }
  .print-table .weekend {
    background: #f8fafc;
  }
  .print-table tr.holiday td {
    background: #fffbeb;
  }
  .print-tag {
    font-size: 7pt;
    text-transform: uppercase;
    color: #b45309;
    margin-left: 1.5mm;
  }
  .print-off {
    color: #94a3b8;
    font-style: italic;
  }

  .print-table--days .col-day {
    width: 24mm;
  }
  .print-table--days .col-date {
    width: 32mm;
  }
  .print-table--days .col-hours {
    width: 16mm;
    text-align: right;
    font-variant-numeric: tabular-nums;
  }

  .print-table--grid {
    table-layout: fixed;
  }
  .print-table--grid th,
  .print-table--grid td {
    font-size: 7pt;
    padding: 1mm 0.8mm;
    text-align: center;
    overflow: hidden;
  }
  .print-table--grid th {
    position: relative;
    text-align: center;
  }
  .print-daynum {
    font-variant-numeric: tabular-nums;
  }
  .print-holiday-dot {
    display: inline-block;
    width: 1.2mm;
    height: 1.2mm;
    border-radius: 50%;
    background: #b45309;
    margin-top: 0.5mm;
  }
  .print-employee-cell {
    font-weight: 600;
    text-align: left;
    white-space: normal;
    word-break: break-word;
  }
  .print-employee-hours {
    font-weight: 400;
    font-size: 6.5pt;
    color: #64748b;
  }
  .print-badges {
    display: flex;
    flex-wrap: wrap;
    justify-content: center;
    align-items: center;
    gap: 0.5mm;
  }
  .print-badge {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 3.5mm;
    height: 3.5mm;
    border-radius: 1pt;
    color: #fff;
    font-size: 6pt;
    font-weight: 700;
    line-height: 1;
  }
  .print-badge-more {
    font-size: 6pt;
    color: #64748b;
  }
  .print-absent-mark {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 3.5mm;
    height: 3.5mm;
    border: 0.5pt dashed #94a3b8;
    border-radius: 50%;
    color: #64748b;
    font-size: 6pt;
    font-weight: 700;
    line-height: 1;
  }
  .print-legend {
    display: flex;
    flex-wrap: wrap;
    gap: 3mm;
    margin: 3mm 0 0;
    font-size: 7.5pt;
    color: #475569;
  }
  .print-legend-item {
    display: inline-flex;
    align-items: center;
    gap: 1mm;
  }

  .print-summary {
    display: flex;
    gap: 12mm;
    margin: 5mm 0 0;
    padding-top: 3mm;
    border-top: 0.5pt solid #cbd5e1;
  }
  .print-summary dt {
    font-size: 7.5pt;
    text-transform: uppercase;
    letter-spacing: 0.03em;
    color: #64748b;
  }
  .print-summary dd {
    margin: 0.5mm 0 0;
    font-size: 11pt;
    font-weight: 700;
  }

  .print-total {
    text-align: right;
    font-size: 9.5pt;
    font-weight: 600;
    margin: 3mm 0 0;
  }

  .print-footer {
    margin-top: 6mm;
    font-size: 7.5pt;
    color: #94a3b8;
    text-align: center;
  }
}
</style>
