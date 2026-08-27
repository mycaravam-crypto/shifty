<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Plus } from '@lucide/vue'
import axios from 'axios'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import ShiftTypeDetailModal from './ShiftTypeDetailModal.vue'

const toast = useToastStore()

interface Team {
  id: string
  name: string
  active: boolean
  // Backend serializes enums as their ordinal, not a string (same as AbsenceType elsewhere) —
  // null = nationwide-only holidays for this team (issue #57).
  bundesland: number | null
}

// issue #57: German labels for the Bundesland enum, indexed by its backend ordinal
// (Domain/Scheduling/Bundesland.cs) — mirrors AbsenceType's client-side label mapping.
const bundeslandLabels = [
  'Baden-Württemberg',
  'Bayern',
  'Berlin',
  'Brandenburg',
  'Bremen',
  'Hamburg',
  'Hessen',
  'Mecklenburg-Vorpommern',
  'Niedersachsen',
  'Nordrhein-Westfalen',
  'Rheinland-Pfalz',
  'Saarland',
  'Sachsen',
  'Sachsen-Anhalt',
  'Schleswig-Holstein',
  'Thüringen',
]
interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  color: string
  active: boolean
  minStaffing: number | null
  maxStaffing: number | null
  endsNextDay: boolean
}

const inputClass =
  'rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500'

const teams = ref<Team[]>([])
const shiftTypes = ref<ShiftType[]>([])
const loading = ref(true)
const error = ref('')

const showTeamForm = ref(false)
const teamForm = ref({ name: '', bundesland: '' })
const savingTeam = ref(false)

const showShiftTypeForm = ref(false)
const shiftTypeForm = ref({
  name: '',
  startTime: '08:00',
  endTime: '16:00',
  breakMinutes: 30,
  color: '#6366f1',
  minStaffing: '',
  maxStaffing: '',
  endsNextDay: false,
})
const savingShiftType = ref(false)
const selectedShiftType = ref<ShiftType | null>(null)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [teamsRes, shiftTypesRes] = await Promise.all([
      api.get('/teams'),
      api.get('/shift-types'),
    ])
    teams.value = teamsRes.data
    shiftTypes.value = shiftTypesRes.data
  } catch {
    error.value = 'Stammdaten konnten nicht geladen werden.'
  } finally {
    loading.value = false
  }
}

async function onCreateTeam() {
  savingTeam.value = true
  try {
    await api.post('/teams', {
      name: teamForm.value.name,
      bundesland: teamForm.value.bundesland === '' ? null : Number(teamForm.value.bundesland),
    })
    teamForm.value = { name: '', bundesland: '' }
    showTeamForm.value = false
    toast.success('Team angelegt.')
    await load()
  } catch (e) {
    toast.error(
      axios.isAxiosError(e) && e.response?.data
        ? e.response.data
        : 'Team konnte nicht angelegt werden.',
    )
  } finally {
    savingTeam.value = false
  }
}

async function onCreateShiftType() {
  savingShiftType.value = true
  try {
    await api.post('/shift-types', {
      name: shiftTypeForm.value.name,
      startTime: `${shiftTypeForm.value.startTime}:00`,
      endTime: `${shiftTypeForm.value.endTime}:00`,
      breakMinutes: shiftTypeForm.value.breakMinutes,
      color: shiftTypeForm.value.color,
      minStaffing: shiftTypeForm.value.minStaffing ? Number(shiftTypeForm.value.minStaffing) : null,
      maxStaffing: shiftTypeForm.value.maxStaffing ? Number(shiftTypeForm.value.maxStaffing) : null,
      endsNextDay: shiftTypeForm.value.endsNextDay,
    })
    shiftTypeForm.value = {
      name: '',
      startTime: '08:00',
      endTime: '16:00',
      breakMinutes: 30,
      color: '#6366f1',
      minStaffing: '',
      maxStaffing: '',
      endsNextDay: false,
    }
    showShiftTypeForm.value = false
    toast.success('Schichttyp angelegt.')
    await load()
  } catch (e) {
    toast.error(
      axios.isAxiosError(e) && e.response?.data
        ? e.response.data
        : 'Schichttyp konnte nicht angelegt werden.',
    )
  } finally {
    savingShiftType.value = false
  }
}

async function onShiftTypeUpdated() {
  selectedShiftType.value = null
  await load()
}

onMounted(load)
</script>

<template>
  <div class="p-4 sm:p-8 max-w-4xl space-y-10">
    <div>
      <h1 class="text-2xl font-semibold">Stammdaten</h1>
      <p class="text-sm text-slate-500 mt-1">Teams und Schichttypen</p>
    </div>

    <p v-if="error" class="text-sm text-rose-400">{{ error }}</p>
    <div v-if="loading" class="space-y-6" aria-label="Lädt…">
      <div class="glass rounded-xl p-4 space-y-3">
        <div v-for="i in 3" :key="i" class="h-10 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
      <div class="glass rounded-xl p-4 space-y-3">
        <div v-for="i in 4" :key="i" class="h-10 rounded-lg bg-white/5 animate-pulse"></div>
      </div>
    </div>

    <section v-if="!loading">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold">Teams</h2>
        <button
          class="flex items-center gap-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity"
          @click="showTeamForm = !showTeamForm"
        >
          <Plus :size="16" /> Team
        </button>
      </div>

      <form
        v-if="showTeamForm"
        class="glass rounded-xl p-5 mb-4 flex flex-col sm:flex-row gap-3"
        @submit.prevent="onCreateTeam"
      >
        <input
          v-model="teamForm.name"
          placeholder="Teamname"
          required
          class="flex-1"
          :class="inputClass"
        />
        <select v-model="teamForm.bundesland" class="sm:w-56" :class="inputClass">
          <option value="">Bundesland (optional)</option>
          <option v-for="(label, i) in bundeslandLabels" :key="i" :value="i">{{ label }}</option>
        </select>
        <button
          type="submit"
          :disabled="savingTeam"
          class="rounded-lg bg-white/10 hover:bg-white/15 transition-colors px-4 py-2 text-sm font-medium disabled:opacity-50"
        >
          {{ savingTeam ? 'Speichern…' : 'Anlegen' }}
        </button>
      </form>

      <div class="glass rounded-xl overflow-hidden">
        <div class="md:hidden divide-y divide-white/5">
          <div v-for="t in teams" :key="t.id" class="p-4 flex items-center justify-between gap-3">
            <div class="min-w-0 text-sm">
              <div class="truncate">{{ t.name }}</div>
              <div class="text-xs text-slate-500 truncate">
                {{ t.bundesland !== null ? bundeslandLabels[t.bundesland] : '–' }}
              </div>
            </div>
            <span
              class="rounded-full px-2 py-0.5 text-xs shrink-0"
              :class="
                t.active ? 'bg-emerald-500/15 text-emerald-400' : 'bg-slate-500/15 text-slate-400'
              "
            >
              {{ t.active ? 'Aktiv' : 'Inaktiv' }}
            </span>
          </div>
          <p v-if="!teams.length" class="px-4 py-8 text-center text-slate-500 text-sm">
            Keine Teams.
          </p>
        </div>

        <div class="hidden md:block overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8"
              >
                <th class="px-4 py-3">Name</th>
                <th class="px-4 py-3">Bundesland</th>
                <th class="px-4 py-3">Status</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="t in teams" :key="t.id" class="border-b border-white/5 last:border-0">
                <td class="px-4 py-3">{{ t.name }}</td>
                <td class="px-4 py-3 text-slate-400">
                  {{ t.bundesland !== null ? bundeslandLabels[t.bundesland] : '–' }}
                </td>
                <td class="px-4 py-3">
                  <span
                    class="rounded-full px-2 py-0.5 text-xs"
                    :class="
                      t.active
                        ? 'bg-emerald-500/15 text-emerald-400'
                        : 'bg-slate-500/15 text-slate-400'
                    "
                  >
                    {{ t.active ? 'Aktiv' : 'Inaktiv' }}
                  </span>
                </td>
              </tr>
              <tr v-if="!teams.length">
                <td colspan="3" class="px-4 py-8 text-center text-slate-500">Keine Teams.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section v-if="!loading">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold">Schichttypen</h2>
        <button
          class="flex items-center gap-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity"
          @click="showShiftTypeForm = !showShiftTypeForm"
        >
          <Plus :size="16" /> Schichttyp
        </button>
      </div>

      <form
        v-if="showShiftTypeForm"
        class="glass rounded-xl p-5 mb-4 grid grid-cols-1 sm:grid-cols-2 gap-3"
        @submit.prevent="onCreateShiftType"
      >
        <input
          v-model="shiftTypeForm.name"
          placeholder="Name"
          required
          class="sm:col-span-2"
          :class="inputClass"
        />
        <!-- lang="de-DE" is a no-op in Chromium (picker format is OS-locale-driven, not page-lang) -->
        <input
          v-model="shiftTypeForm.startTime"
          type="time"
          lang="de-DE"
          required
          :class="inputClass"
        />
        <input
          v-model="shiftTypeForm.endTime"
          type="time"
          lang="de-DE"
          required
          :class="inputClass"
        />
        <input
          v-model.number="shiftTypeForm.breakMinutes"
          type="number"
          min="0"
          max="480"
          placeholder="Pause (Minuten)"
          :class="inputClass"
        />
        <input
          v-model="shiftTypeForm.color"
          type="color"
          class="h-10 w-full rounded-lg bg-white/5 border border-white/10 outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        />
        <input
          v-model="shiftTypeForm.minStaffing"
          type="number"
          min="1"
          placeholder="Min. Besetzung (optional)"
          :class="inputClass"
        />
        <input
          v-model="shiftTypeForm.maxStaffing"
          type="number"
          min="1"
          placeholder="Max. Besetzung (optional)"
          :class="inputClass"
        />
        <!-- issue #157: lets a template represent a recurring overnight shift (e.g. 22:00-06:00) -->
        <label class="sm:col-span-2 flex items-center gap-2 text-sm text-slate-300">
          <input v-model="shiftTypeForm.endsNextDay" type="checkbox" />
          Endet am nächsten Tag (Nachtschicht)
        </label>
        <button
          type="submit"
          :disabled="savingShiftType"
          class="sm:col-span-2 rounded-lg bg-white/10 hover:bg-white/15 transition-colors py-2 text-sm font-medium disabled:opacity-50"
        >
          {{ savingShiftType ? 'Speichern…' : 'Anlegen' }}
        </button>
      </form>

      <div class="glass rounded-xl overflow-hidden">
        <div class="md:hidden divide-y divide-white/5">
          <div
            v-for="s in shiftTypes"
            :key="s.id"
            class="p-4 flex items-center justify-between gap-3 hover:bg-white/3 cursor-pointer"
            @click="selectedShiftType = s"
          >
            <div class="min-w-0">
              <div class="text-sm truncate">
                <span
                  class="inline-block w-2.5 h-2.5 rounded-full mr-2 align-middle"
                  :style="{ background: s.color }"
                />
                {{ s.name }}
              </div>
              <div class="text-xs text-slate-500 font-mono mt-0.5 truncate">
                {{ s.startTime.slice(0, 5) }}–{{ s.endTime.slice(0, 5)
                }}<span v-if="s.endsNextDay">(+1)</span> · {{ s.breakMinutes }}m Pause ·
                {{ s.minStaffing ?? '–' }}/{{ s.maxStaffing ?? '–' }}
              </div>
            </div>
            <span
              class="rounded-full px-2 py-0.5 text-xs shrink-0"
              :class="
                s.active ? 'bg-emerald-500/15 text-emerald-400' : 'bg-slate-500/15 text-slate-400'
              "
            >
              {{ s.active ? 'Aktiv' : 'Inaktiv' }}
            </span>
          </div>
          <p v-if="!shiftTypes.length" class="px-4 py-8 text-center text-slate-500 text-sm">
            Keine Schichttypen.
          </p>
        </div>

        <div class="hidden md:block overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8"
              >
                <th class="px-4 py-3">Name</th>
                <th class="px-4 py-3">Zeit</th>
                <th class="px-4 py-3">Pause</th>
                <th class="px-4 py-3">Besetzung</th>
                <th class="px-4 py-3">Status</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="s in shiftTypes"
                :key="s.id"
                class="border-b border-white/5 last:border-0 hover:bg-white/3 cursor-pointer"
                @click="selectedShiftType = s"
              >
                <td class="px-4 py-3">
                  <span
                    class="inline-block w-2.5 h-2.5 rounded-full mr-2 align-middle"
                    :style="{ background: s.color }"
                  />
                  {{ s.name }}
                </td>
                <td class="px-4 py-3 font-mono text-slate-400">
                  {{ s.startTime.slice(0, 5) }}–{{ s.endTime.slice(0, 5)
                  }}<span v-if="s.endsNextDay">(+1)</span>
                </td>
                <td class="px-4 py-3 font-mono text-slate-400">{{ s.breakMinutes }}m</td>
                <td class="px-4 py-3 text-slate-400">
                  {{ s.minStaffing ?? '–' }} / {{ s.maxStaffing ?? '–' }}
                </td>
                <td class="px-4 py-3">
                  <span
                    class="rounded-full px-2 py-0.5 text-xs"
                    :class="
                      s.active
                        ? 'bg-emerald-500/15 text-emerald-400'
                        : 'bg-slate-500/15 text-slate-400'
                    "
                  >
                    {{ s.active ? 'Aktiv' : 'Inaktiv' }}
                  </span>
                </td>
              </tr>
              <tr v-if="!shiftTypes.length">
                <td colspan="5" class="px-4 py-8 text-center text-slate-500">
                  Keine Schichttypen.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <ShiftTypeDetailModal
      v-if="selectedShiftType"
      :shift-type="selectedShiftType"
      @close="selectedShiftType = null"
      @updated="onShiftTypeUpdated"
    />
  </div>
</template>
