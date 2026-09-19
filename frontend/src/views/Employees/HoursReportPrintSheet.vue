<script setup lang="ts">
// issue #165: the printable, signable monthly Ist/Soll report — same "separate print-only
// component, its own light/ink-friendly styling" pattern as
// views/Schedule/SchedulePrintSheet.vue (see that file's own header comment for why: reusing a
// dark interactive layout for print fights the theme at every turn). Renders nothing on screen
// (`hidden print:block`) and is the only thing visible on the page when printing, alongside
// HoursReportModal's own on-screen picker being `print:hidden`.
import { computed } from 'vue'

export interface ShiftTypeLite {
  id: string
  name: string
}
export interface ReportShift {
  shiftTypeId: string
  startTime: string
  endTime: string
  netHours: number
  endsNextDay: boolean
}
export interface ReportDay {
  date: string
  shifts: ReportShift[]
  netHours: number
  absence: 0 | 1 | 2 | 3 | null
  isHoliday: boolean
}
export interface ReportAdjustment {
  id: string
  date: string
  hoursDelta: number
  reason: string
  createdBy: string
}
export interface HoursReport {
  from: string
  to: string
  sollHours: number
  istHours: number
  deviation: number
  balanceBefore: number
  balanceAfter: number
  days: ReportDay[]
  adjustments: ReportAdjustment[]
}

const props = defineProps<{
  employee: { firstName: string; lastName: string; personnelNumber: string }
  report: HoursReport | null
  shiftTypeById: (id: string) => ShiftTypeLite | undefined
}>()

const ABSENCE_TYPE_LABELS: Record<number, string> = {
  0: 'Urlaub',
  1: 'Krankheit',
  2: 'Fortbildung',
  3: 'Sonstiges',
}

const weekdayFmt = new Intl.DateTimeFormat('de-DE', { weekday: 'long' })
const dateFmt = new Intl.DateTimeFormat('de-DE', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
})
const generatedAtFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
const periodFmt = new Intl.DateTimeFormat('de-DE', { month: 'long', year: 'numeric' })

// Parses an ISO date string as a local calendar date (not UTC midnight), same reasoning
// utils/date.ts's formatDate avoids Date/toLocaleDateString directly.
function parseIso(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number)
  return new Date(year, month - 1, day)
}

function shiftLabel(s: ReportShift): string {
  const type = props.shiftTypeById(s.shiftTypeId)
  const time = `${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)}${s.endsNextDay ? ' (+1)' : ''}`
  return type ? `${type.name} · ${time}` : time
}

const periodLabel = computed(() =>
  props.report ? periodFmt.format(parseIso(props.report.from)) : '',
)
</script>

<template>
  <div v-if="report" class="hidden print:block print-sheet">
    <header class="print-head">
      <div class="print-head-row">
        <h1>Monatsnachweis</h1>
      </div>
      <p class="print-period">{{ periodLabel }}</p>
      <p class="print-employee-name">
        {{ employee.lastName }}, {{ employee.firstName }} · {{ employee.personnelNumber }}
      </p>
      <p class="print-generated">Erstellt am {{ generatedAtFmt.format(new Date()) }}</p>
    </header>

    <table class="print-table">
      <thead>
        <tr>
          <th class="col-day">Tag</th>
          <th class="col-date">Datum</th>
          <th>Schicht</th>
          <th class="col-hours">Std.</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="d in report.days" :key="d.date" :class="{ holiday: d.isHoliday }">
          <td class="col-day">{{ weekdayFmt.format(parseIso(d.date)) }}</td>
          <td class="col-date">
            {{ dateFmt.format(parseIso(d.date)) }}
            <span v-if="d.isHoliday" class="print-tag">Feiertag</span>
          </td>
          <td v-if="d.shifts.length">
            <div v-for="s in d.shifts" :key="s.shiftTypeId + s.startTime">{{ shiftLabel(s) }}</div>
          </td>
          <td v-else class="print-off">
            {{ d.absence !== null ? `Abwesend (${ABSENCE_TYPE_LABELS[d.absence]})` : 'Frei' }}
          </td>
          <td class="col-hours">{{ d.netHours ? `${d.netHours} h` : '–' }}</td>
        </tr>
      </tbody>
    </table>

    <table v-if="report.adjustments.length" class="print-table print-adjustments">
      <thead>
        <tr>
          <th colspan="4">Korrekturen</th>
        </tr>
        <tr>
          <th class="col-date">Datum</th>
          <th class="col-hours">Std.</th>
          <th>Grund</th>
          <th>Von</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="a in report.adjustments" :key="a.id">
          <td class="col-date">{{ dateFmt.format(parseIso(a.date)) }}</td>
          <td class="col-hours">{{ a.hoursDelta >= 0 ? '+' : '' }}{{ a.hoursDelta }} h</td>
          <td>{{ a.reason }}</td>
          <td>{{ a.createdBy }}</td>
        </tr>
      </tbody>
    </table>

    <dl class="print-summary">
      <div>
        <dt>Soll (Zeitraum)</dt>
        <dd>{{ report.sollHours }} h</dd>
      </div>
      <div>
        <dt>Ist (Zeitraum)</dt>
        <dd>{{ report.istHours }} h</dd>
      </div>
      <div>
        <dt>Abweichung</dt>
        <dd :class="report.deviation >= 0 ? 'positive' : 'negative'">
          {{ report.deviation >= 0 ? '+' : '' }}{{ report.deviation }} h
        </dd>
      </div>
      <div>
        <dt>Übertrag davor</dt>
        <dd>{{ report.balanceBefore >= 0 ? '+' : '' }}{{ report.balanceBefore }} h</dd>
      </div>
      <div>
        <dt>Übertrag danach</dt>
        <dd :class="report.balanceAfter >= 0 ? 'positive' : 'negative'">
          {{ report.balanceAfter >= 0 ? '+' : '' }}{{ report.balanceAfter }} h
        </dd>
      </div>
    </dl>

    <div class="print-signatures">
      <div class="print-signature">
        <div class="print-signature-line"></div>
        <p>Mitarbeiter, Datum/Unterschrift</p>
      </div>
      <div class="print-signature">
        <div class="print-signature-line"></div>
        <p>Vorgesetzte:r, Datum/Unterschrift</p>
      </div>
    </div>

    <p class="print-footer">Schichtplaner · Monatsnachweis</p>
  </div>
</template>

<style scoped>
/* Print-only, deliberately not sharing anything from the dark theme — same reasoning as
   SchedulePrintSheet.vue's own style block. */
@media print {
  @page {
    size: portrait;
    margin: 16mm;
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
  .print-head h1 {
    font-size: 15pt;
    font-weight: 700;
    margin: 0;
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
    margin-top: 3mm;
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
  .col-day {
    width: 26mm;
  }
  .col-date {
    width: 32mm;
  }
  .col-hours {
    width: 18mm;
    text-align: right;
    font-variant-numeric: tabular-nums;
  }

  .print-adjustments {
    break-inside: avoid;
  }

  .print-summary {
    display: flex;
    flex-wrap: wrap;
    gap: 8mm;
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
  .print-summary dd.positive {
    color: #047857;
  }
  .print-summary dd.negative {
    color: #be123c;
  }

  .print-signatures {
    display: flex;
    gap: 16mm;
    margin-top: 16mm;
    break-inside: avoid;
  }
  .print-signature {
    flex: 1;
  }
  .print-signature-line {
    border-top: 0.75pt solid #111827;
    margin-bottom: 1.5mm;
  }
  .print-signature p {
    font-size: 8pt;
    color: #64748b;
    margin: 0;
  }

  .print-footer {
    margin-top: 6mm;
    font-size: 7.5pt;
    color: #94a3b8;
    text-align: center;
  }
}
</style>
