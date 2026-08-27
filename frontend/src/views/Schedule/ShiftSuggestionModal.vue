<script setup lang="ts">
import { ref, watch } from 'vue'
import { Check, X, ThumbsUp, ThumbsDown } from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import ModalShell from '@/components/ModalShell.vue'

const toast = useToastStore()

interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  color: string
  endsNextDay: boolean
}
interface SuggestionReason {
  code: number
  message: string
}
interface Suggestion {
  employeeId: string
  firstName: string
  lastName: string
  eligible: boolean
  score: number
  reasons: SuggestionReason[]
}

// Mirrors ShiftSuggestionEngine.SuggestionReasonCode — used only to pick an icon per reason,
// the message text itself comes fully formed from the backend.
const POSITIVE_REASON_CODES = new Set([5, 7, 9]) // ShiftTypePreferred, WeekdayPreferred, UnderContractTarget

const props = defineProps<{
  scheduleId: string
  shiftType: ShiftType
  defaultDate: string
  minDate: string
  maxDate: string
}>()
const emit = defineEmits<{ close: []; assigned: [] }>()

const date = ref(props.defaultDate)
const suggestions = ref<Suggestion[]>([])
const loading = ref(true)
const error = ref('')
const assigningId = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await api.get(`/schedules/${props.scheduleId}/suggestions`, {
      params: { date: date.value, shiftTypeId: props.shiftType.id },
    })
    suggestions.value = res.data
  } catch {
    error.value = 'Vorschläge konnten nicht geladen werden.'
  } finally {
    loading.value = false
  }
}
watch(date, load, { immediate: true })

async function onAssign(s: Suggestion) {
  assigningId.value = s.employeeId
  try {
    await api.post(`/schedules/${props.scheduleId}/assignments`, {
      employeeId: s.employeeId,
      shiftTypeId: props.shiftType.id,
      date: date.value,
      startTime: props.shiftType.startTime,
      endTime: props.shiftType.endTime,
      breakMinutes: props.shiftType.breakMinutes,
      endsNextDay: props.shiftType.endsNextDay,
    })
    toast.success(`${s.firstName} ${s.lastName} zugewiesen.`)
    emit('assigned')
    await load()
  } catch {
    toast.error('Schicht konnte nicht zugewiesen werden.')
  } finally {
    assigningId.value = null
  }
}
</script>

<template>
  <ModalShell :title="`Vorschlagen — ${shiftType.name}`" wide @close="emit('close')">
    <div class="space-y-4">
      <div class="flex items-center gap-3">
        <span
          class="w-2.5 h-2.5 rounded-full shrink-0"
          :style="{ backgroundColor: shiftType.color }"
        ></span>
        <span class="font-mono text-sm text-slate-400"
          >{{ shiftType.startTime.slice(0, 5) }}–{{ shiftType.endTime.slice(0, 5) }}</span
        >
        <input
          v-model="date"
          type="date"
          lang="de-DE"
          :min="minDate"
          :max="maxDate"
          class="ml-auto rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        />
      </div>

      <p v-if="error" class="text-sm text-rose-400">{{ error }}</p>
      <div v-if="loading" class="space-y-2">
        <div v-for="i in 4" :key="i" class="h-14 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
      <ul v-else class="space-y-2 max-h-[50vh] overflow-y-auto">
        <li
          v-for="s in suggestions"
          :key="s.employeeId"
          class="rounded-lg border px-3 py-2.5"
          :class="
            s.eligible
              ? 'bg-white/5 border-white/10'
              : 'bg-rose-500/5 border-rose-500/20 opacity-75'
          "
        >
          <div class="flex items-center justify-between gap-3">
            <div class="min-w-0">
              <div class="flex items-center gap-1.5 text-sm">
                <Check v-if="s.eligible" :size="14" class="text-emerald-400 shrink-0" />
                <X v-else :size="14" class="text-rose-400 shrink-0" />
                {{ s.lastName }}, {{ s.firstName }}
                <span class="font-mono text-xs text-slate-500">Score {{ s.score }}</span>
              </div>
              <ul v-if="s.reasons.length" class="mt-1 space-y-0.5">
                <li
                  v-for="(r, i) in s.reasons"
                  :key="i"
                  class="flex items-center gap-1 text-xs text-slate-400"
                >
                  <ThumbsUp
                    v-if="POSITIVE_REASON_CODES.has(r.code)"
                    :size="11"
                    class="text-emerald-400 shrink-0"
                  />
                  <ThumbsDown v-else :size="11" class="text-amber-400 shrink-0" />
                  {{ r.message }}
                </li>
              </ul>
            </div>
            <button
              :disabled="assigningId === s.employeeId"
              class="shrink-0 rounded-lg px-3 py-1.5 text-sm font-medium transition-opacity disabled:opacity-50"
              :class="
                s.eligible
                  ? 'bg-linear-to-r from-blue-600 to-indigo-600 hover:opacity-90'
                  : 'bg-white/10 hover:bg-white/15'
              "
              @click="onAssign(s)"
            >
              {{ assigningId === s.employeeId ? 'Zuweisen…' : 'Zuweisen' }}
            </button>
          </div>
        </li>
        <li v-if="!suggestions.length" class="text-sm text-slate-500 text-center py-6">
          Keine aktiven Mitarbeiter.
        </li>
      </ul>
    </div>
  </ModalShell>
</template>
