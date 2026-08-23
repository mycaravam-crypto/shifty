<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Plus, Trash2 } from '@lucide/vue'
import axios from 'axios'
import api from '../../services/api'
import EmployeeDetailModal from './EmployeeDetailModal.vue'
import { useToastStore } from '../../stores/toast'

interface Employee {
  id: string
  personnelNumber: string
  firstName: string
  lastName: string
  email: string | null
  active: boolean
  teamId: string | null
}
interface Team {
  id: string
  name: string
  active: boolean
}

const toast = useToastStore()
const employees = ref<Employee[]>([])
const teams = ref<Team[]>([])
const loading = ref(true)
const error = ref('')
const showForm = ref(false)
const saving = ref(false)

const form = ref({ personnelNumber: '', firstName: '', lastName: '', email: '', teamId: '' })
const selectedEmployee = ref<Employee | null>(null)

function teamName(teamId: string | null) {
  return teams.value.find((t) => t.id === teamId)?.name ?? '—'
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [employeesRes, teamsRes] = await Promise.all([api.get('/employees'), api.get('/teams')])
    employees.value = employeesRes.data
    teams.value = teamsRes.data
  } catch {
    error.value = 'Mitarbeiter konnten nicht geladen werden.'
  } finally {
    loading.value = false
  }
}

async function onCreate() {
  saving.value = true
  error.value = ''
  try {
    await api.post('/employees', {
      personnelNumber: form.value.personnelNumber,
      firstName: form.value.firstName,
      lastName: form.value.lastName,
      email: form.value.email || null,
      teamId: form.value.teamId || null,
    })
    form.value = { personnelNumber: '', firstName: '', lastName: '', email: '', teamId: '' }
    showForm.value = false
    toast.success('Mitarbeiter angelegt.')
    await load()
  } catch (e) {
    error.value =
      axios.isAxiosError(e) && e.response?.data
        ? e.response.data
        : 'Mitarbeiter konnte nicht angelegt werden.'
  } finally {
    saving.value = false
  }
}

async function onDelete(id: string) {
  if (!confirm('Mitarbeiter wirklich löschen?')) return
  try {
    await api.delete(`/employees/${id}`)
    toast.success('Mitarbeiter gelöscht.')
    await load()
  } catch {
    toast.error('Mitarbeiter konnte nicht gelöscht werden.')
  }
}

async function onEmployeeUpdated() {
  selectedEmployee.value = null
  await load()
}

onMounted(load)
</script>

<template>
  <div class="p-8 max-w-4xl">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-semibold">Mitarbeiter</h1>
      <button
        class="flex items-center gap-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 px-4 py-2 text-sm font-medium hover:opacity-90 transition-opacity"
        @click="showForm = !showForm"
      >
        <Plus :size="16" /> Mitarbeiter
      </button>
    </div>

    <p v-if="error" class="mb-4 text-sm text-rose-400">{{ error }}</p>

    <form
      v-if="showForm"
      class="glass rounded-xl p-5 mb-6 grid grid-cols-2 gap-4"
      @submit.prevent="onCreate"
    >
      <input
        v-model="form.personnelNumber"
        placeholder="Personalnummer"
        required
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      />
      <select
        v-model="form.teamId"
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      >
        <option value="">Kein Team</option>
        <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
      </select>
      <input
        v-model="form.firstName"
        placeholder="Vorname"
        required
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      />
      <input
        v-model="form.lastName"
        placeholder="Nachname"
        required
        class="rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      />
      <input
        v-model="form.email"
        type="email"
        placeholder="E-Mail (optional)"
        class="col-span-2 rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      />
      <button
        type="submit"
        :disabled="saving"
        class="col-span-2 rounded-lg bg-white/10 hover:bg-white/15 transition-colors py-2 text-sm font-medium disabled:opacity-50"
      >
        {{ saving ? 'Speichern…' : 'Anlegen' }}
      </button>
    </form>

    <p v-if="loading" class="text-sm text-slate-500">Lädt…</p>
    <div v-else class="glass rounded-xl overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr
            class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8"
          >
            <th class="px-4 py-3">Name</th>
            <th class="px-4 py-3">Personalnummer</th>
            <th class="px-4 py-3">Team</th>
            <th class="px-4 py-3">Status</th>
            <th class="px-4 py-3"></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="e in employees"
            :key="e.id"
            class="border-b border-white/5 last:border-0 hover:bg-white/3 cursor-pointer"
            @click="selectedEmployee = e"
          >
            <td class="px-4 py-3">{{ e.lastName }}, {{ e.firstName }}</td>
            <td class="px-4 py-3 font-mono text-slate-400">{{ e.personnelNumber }}</td>
            <td class="px-4 py-3 text-slate-400">{{ teamName(e.teamId) }}</td>
            <td class="px-4 py-3">
              <span
                class="rounded-full px-2 py-0.5 text-xs"
                :class="
                  e.active ? 'bg-emerald-500/15 text-emerald-400' : 'bg-slate-500/15 text-slate-400'
                "
              >
                {{ e.active ? 'Aktiv' : 'Inaktiv' }}
              </span>
            </td>
            <td class="px-4 py-3 text-right">
              <button
                class="text-slate-500 hover:text-rose-400 transition-colors"
                @click.stop="onDelete(e.id)"
              >
                <Trash2 :size="16" />
              </button>
            </td>
          </tr>
          <tr v-if="!employees.length">
            <td colspan="5" class="px-4 py-8 text-center text-slate-500">Keine Mitarbeiter.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <EmployeeDetailModal
      v-if="selectedEmployee"
      :employee="selectedEmployee"
      :teams="teams"
      @close="selectedEmployee = null"
      @updated="onEmployeeUpdated"
    />
  </div>
</template>
