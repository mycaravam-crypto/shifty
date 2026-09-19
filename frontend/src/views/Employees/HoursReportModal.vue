<script setup lang="ts">
// issue #165: on-screen month picker for the printable "Monatsbericht" — fetches
// GET /employees/{id}/hours-report?from=&to= for the chosen month and hands it to
// HoursReportPrintSheet, then triggers window.print(). Deliberately not built on ModalShell:
// this needs precise control over what's print:hidden vs. what prints (the picker itself must
// vanish on print, the report sheet must not), which ModalShell's shared overlay doesn't expose.
import { nextTick, ref } from 'vue'
import { X } from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import { extractErrorMessage } from '@/utils/errors'
import HoursReportPrintSheet from './HoursReportPrintSheet.vue'
import type { HoursReport, ShiftTypeLite } from './HoursReportPrintSheet.vue'

interface Employee {
  id: string
  firstName: string
  lastName: string
  personnelNumber: string
}

const props = defineProps<{ employee: Employee }>()
const emit = defineEmits<{ close: [] }>()
const toast = useToastStore()

function currentMonth(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
}
const month = ref(currentMonth())
const loading = ref(false)
const report = ref<HoursReport | null>(null)
const shiftTypes = ref<ShiftTypeLite[]>([])

function shiftTypeById(id: string) {
  return shiftTypes.value.find((s) => s.id === id)
}

function monthRange(value: string): { from: string; to: string } {
  const [year, m] = value.split('-').map(Number)
  const from = `${year}-${String(m).padStart(2, '0')}-01`
  const lastDay = new Date(year, m, 0).getDate()
  const to = `${year}-${String(m).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`
  return { from, to }
}

async function onPrint() {
  loading.value = true
  try {
    const { from, to } = monthRange(month.value)
    const [shiftTypesRes, reportRes] = await Promise.all([
      api.get('/shift-types'),
      api.get(`/employees/${props.employee.id}/hours-report`, { params: { from, to } }),
    ])
    shiftTypes.value = shiftTypesRes.data
    report.value = reportRes.data
    await nextTick()
    window.print()
  } catch (e) {
    toast.error(extractErrorMessage(e, 'Monatsbericht konnte nicht geladen werden.'))
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div
    class="print:hidden fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
    @click.self="emit('close')"
  >
    <div class="glass rounded-2xl shadow-xl w-full max-w-sm">
      <div class="flex items-center justify-between px-5 py-4 border-b border-white/8">
        <h2 class="text-base font-semibold">Monatsbericht drucken</h2>
        <button
          class="text-slate-400 hover:text-slate-200 transition-colors"
          @click="emit('close')"
        >
          <X :size="18" />
        </button>
      </div>
      <div class="p-5 space-y-4">
        <p class="text-sm text-slate-400">{{ employee.lastName }}, {{ employee.firstName }}</p>
        <input
          v-model="month"
          type="month"
          lang="de-DE"
          class="w-full rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        />
        <button
          :disabled="loading"
          class="w-full rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
          @click="onPrint"
        >
          {{ loading ? 'Lädt…' : 'Drucken' }}
        </button>
      </div>
    </div>
  </div>

  <HoursReportPrintSheet :employee="employee" :report="report" :shift-type-by-id="shiftTypeById" />
</template>
