<script setup lang="ts">
import { computed, ref } from 'vue'
import { Trash2 } from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import ModalShell from '@/components/ModalShell.vue'

const toast = useToastStore()

interface ShiftType {
  id: string
  name: string
  color: string
}
interface Proposal {
  employeeId: string
  firstName: string
  lastName: string
  shiftTypeId: string
  shiftTypeName: string
  date: string
  score: number
}

const props = defineProps<{
  scheduleId: string
  monthStart: string
  monthEnd: string
  shiftTypes: ShiftType[]
}>()
const emit = defineEmits<{ close: []; committed: [] }>()

const loading = ref(true)
const committing = ref(false)
const error = ref('')
const proposals = ref<Proposal[]>([])
// Dropped rows are tracked by index rather than spliced out of `proposals` — keeps the
// "N vorgeschlagen, M verworfen" summary stable while the manager reviews.
const dropped = ref<Set<number>>(new Set())

const dateFmt = new Intl.DateTimeFormat('de-DE', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
})

function colorFor(shiftTypeId: string): string {
  return props.shiftTypes.find((s) => s.id === shiftTypeId)?.color ?? '#64748b'
}

const keptProposals = computed(() => proposals.value.filter((_, i) => !dropped.value.has(i)))

async function load() {
  loading.value = true
  error.value = ''
  dropped.value = new Set()
  try {
    const res = await api.get(`/schedules/${props.scheduleId}/auto-fill-preview`, {
      params: { from: props.monthStart, to: props.monthEnd },
    })
    proposals.value = res.data
  } catch {
    error.value = 'Vorschau konnte nicht geladen werden.'
  } finally {
    loading.value = false
  }
}
load()

function drop(index: number) {
  dropped.value = new Set(dropped.value).add(index)
}

async function onConfirm() {
  if (!keptProposals.value.length) return
  committing.value = true
  try {
    await api.post(`/schedules/${props.scheduleId}/auto-fill`, {
      assignments: keptProposals.value.map((p) => ({
        employeeId: p.employeeId,
        shiftTypeId: p.shiftTypeId,
        date: p.date,
      })),
    })
    toast.success(`${keptProposals.value.length} Schicht(en) automatisch zugewiesen.`)
    emit('committed')
    emit('close')
  } catch {
    toast.error('Automatisches Füllen konnte nicht übernommen werden.')
  } finally {
    committing.value = false
  }
}
</script>

<template>
  <ModalShell title="Automatisch füllen" wide @close="emit('close')">
    <div class="space-y-4">
      <p class="text-sm text-slate-400">
        Vorschlag für jede unterbesetzte Schicht (Mindestbesetzung) im aktuellen Monat — vor der
        Übernahme können einzelne Zeilen entfernt werden.
      </p>

      <p v-if="error" class="text-sm text-rose-400">{{ error }}</p>
      <div v-if="loading" class="space-y-2">
        <div v-for="i in 4" :key="i" class="h-12 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
      <template v-else>
        <ul class="space-y-2 max-h-[50vh] overflow-y-auto">
          <li
            v-for="(p, i) in proposals"
            :key="`${p.date}-${p.shiftTypeId}-${p.employeeId}`"
            class="flex items-center gap-3 rounded-lg border px-3 py-2.5 transition-opacity"
            :class="
              dropped.has(i) ? 'bg-white/5 border-white/5 opacity-40' : 'bg-white/5 border-white/10'
            "
          >
            <span
              class="w-2.5 h-2.5 rounded-full shrink-0"
              :style="{ backgroundColor: colorFor(p.shiftTypeId) }"
            ></span>
            <div class="min-w-0 flex-1 text-sm">
              <span class="font-medium">{{ p.lastName }}, {{ p.firstName }}</span>
              <span class="text-slate-400"> — {{ p.shiftTypeName }}</span>
              <span class="font-mono text-xs text-slate-500 ml-2">{{
                dateFmt.format(new Date(p.date))
              }}</span>
              <span class="font-mono text-xs text-slate-500 ml-2">Score {{ p.score }}</span>
            </div>
            <button
              v-if="!dropped.has(i)"
              type="button"
              title="Vorschlag verwerfen"
              class="shrink-0 text-slate-500 hover:text-rose-400 transition-colors"
              @click="drop(i)"
            >
              <Trash2 :size="15" />
            </button>
          </li>
          <li v-if="!proposals.length" class="text-sm text-slate-500 text-center py-6">
            Keine unterbesetzten Schichten gefunden.
          </li>
        </ul>

        <div class="flex items-center justify-between pt-2 border-t border-white/8">
          <span class="text-sm text-slate-400">
            {{ keptProposals.length }} von {{ proposals.length }} werden übernommen
          </span>
          <button
            :disabled="committing || !keptProposals.length"
            class="rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
            @click="onConfirm"
          >
            {{ committing ? 'Übernehme…' : 'Bestätigen' }}
          </button>
        </div>
      </template>
    </div>
  </ModalShell>
</template>
