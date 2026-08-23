<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Trash2 } from '@lucide/vue'
import axios from 'axios'
import api from '../../services/api'
import ModalShell from '../../components/ModalShell.vue'

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
}
interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
}
interface Contract {
  id: string
  employeeId: string
  validFrom: string
  validTo: string | null
  weeklyHours: number
  workingDaysPerWeek: number
  dailyTargetHours: number
}

const props = defineProps<{ employee: Employee; teams: Team[] }>()
const emit = defineEmits<{ close: []; updated: [] }>()

const inputClass =
  'rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500'

const form = ref({ ...props.employee })
const savingEmployee = ref(false)
const employeeError = ref('')

async function onSaveEmployee() {
  savingEmployee.value = true
  employeeError.value = ''
  try {
    await api.put(`/employees/${props.employee.id}`, {
      personnelNumber: form.value.personnelNumber,
      firstName: form.value.firstName,
      lastName: form.value.lastName,
      email: form.value.email || null,
      active: form.value.active,
      teamId: form.value.teamId || null,
    })
    emit('updated')
  } catch (e) {
    employeeError.value =
      axios.isAxiosError(e) && e.response?.data ? e.response.data : 'Speichern fehlgeschlagen.'
  } finally {
    savingEmployee.value = false
  }
}

const shiftTypes = ref<ShiftType[]>([])
const eligibleIds = ref<Set<string>>(new Set())
const savingEligible = ref(false)

async function loadEligibleShiftTypes() {
  const [allRes, eligibleRes] = await Promise.all([
    api.get('/shift-types'),
    api.get(`/employees/${props.employee.id}/eligible-shift-types`),
  ])
  shiftTypes.value = allRes.data
  eligibleIds.value = new Set((eligibleRes.data as ShiftType[]).map((s) => s.id))
}

function toggleEligible(id: string) {
  if (eligibleIds.value.has(id)) eligibleIds.value.delete(id)
  else eligibleIds.value.add(id)
  eligibleIds.value = new Set(eligibleIds.value)
}

async function onSaveEligible() {
  savingEligible.value = true
  try {
    await api.put(`/employees/${props.employee.id}/eligible-shift-types`, [...eligibleIds.value])
  } finally {
    savingEligible.value = false
  }
}

const contracts = ref<Contract[]>([])
const contractForm = ref({
  validFrom: '',
  validTo: '',
  weeklyHours: 40,
  workingDaysPerWeek: 5,
  dailyTargetHours: 8,
})
const savingContract = ref(false)
const contractError = ref('')

async function loadContracts() {
  const res = await api.get(`/employees/${props.employee.id}/contracts`)
  contracts.value = res.data
}

async function onCreateContract() {
  savingContract.value = true
  contractError.value = ''
  try {
    await api.post(`/employees/${props.employee.id}/contracts`, {
      validFrom: contractForm.value.validFrom,
      validTo: contractForm.value.validTo || null,
      weeklyHours: contractForm.value.weeklyHours,
      workingDaysPerWeek: contractForm.value.workingDaysPerWeek,
      dailyTargetHours: contractForm.value.dailyTargetHours,
    })
    contractForm.value = {
      validFrom: '',
      validTo: '',
      weeklyHours: 40,
      workingDaysPerWeek: 5,
      dailyTargetHours: 8,
    }
    await loadContracts()
  } catch (e) {
    contractError.value =
      axios.isAxiosError(e) && e.response?.data
        ? e.response.data
        : 'Vertrag konnte nicht angelegt werden.'
  } finally {
    savingContract.value = false
  }
}

async function onDeleteContract(id: string) {
  if (!confirm('Vertrag wirklich löschen?')) return
  await api.delete(`/contracts/${id}`)
  await loadContracts()
}

onMounted(() => {
  loadEligibleShiftTypes()
  loadContracts()
})
</script>

<template>
  <ModalShell :title="`${employee.lastName}, ${employee.firstName}`" wide @close="emit('close')">
    <div class="space-y-6">
      <section>
        <h3 class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
          Stammdaten
        </h3>
        <form class="grid grid-cols-2 gap-3" @submit.prevent="onSaveEmployee">
          <input
            v-model="form.personnelNumber"
            placeholder="Personalnummer"
            required
            :class="inputClass"
          />
          <select v-model="form.teamId" :class="inputClass">
            <option :value="null">Kein Team</option>
            <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
          </select>
          <input v-model="form.firstName" placeholder="Vorname" required :class="inputClass" />
          <input v-model="form.lastName" placeholder="Nachname" required :class="inputClass" />
          <input
            v-model="form.email"
            type="email"
            placeholder="E-Mail"
            class="col-span-2"
            :class="inputClass"
          />
          <label class="col-span-2 flex items-center gap-2 text-sm text-slate-400">
            <input v-model="form.active" type="checkbox" class="rounded border-white/10" />
            Aktiv
          </label>
          <p v-if="employeeError" class="col-span-2 text-sm text-rose-400">{{ employeeError }}</p>
          <button
            type="submit"
            :disabled="savingEmployee"
            class="col-span-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
          >
            {{ savingEmployee ? 'Speichern…' : 'Speichern' }}
          </button>
        </form>
      </section>

      <section>
        <h3 class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">
          Mögliche Schichten
        </h3>
        <div class="flex flex-wrap gap-2 mb-3">
          <label
            v-for="s in shiftTypes"
            :key="s.id"
            class="flex items-center gap-2 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm cursor-pointer"
          >
            <input
              type="checkbox"
              :checked="eligibleIds.has(s.id)"
              class="rounded border-white/10"
              @change="toggleEligible(s.id)"
            />
            {{ s.name }}
          </label>
          <p v-if="!shiftTypes.length" class="text-sm text-slate-500">
            Keine Schichtarten angelegt.
          </p>
        </div>
        <button
          :disabled="savingEligible"
          class="rounded-lg bg-white/10 hover:bg-white/15 transition-colors px-4 py-1.5 text-sm font-medium disabled:opacity-50"
          @click="onSaveEligible"
        >
          {{ savingEligible ? 'Speichern…' : 'Speichern' }}
        </button>
      </section>

      <section>
        <h3 class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">Verträge</h3>
        <div class="rounded-xl border border-white/8 overflow-hidden mb-3">
          <table class="w-full text-sm">
            <thead>
              <tr
                class="text-left text-[10px] uppercase tracking-wider font-bold text-slate-500 border-b border-white/8"
              >
                <th class="px-3 py-2">Gültig ab</th>
                <th class="px-3 py-2">Gültig bis</th>
                <th class="px-3 py-2 font-mono">Std/Wo</th>
                <th class="px-3 py-2">Tage/Wo</th>
                <th class="px-3 py-2 font-mono">Std/Tag</th>
                <th class="px-3 py-2"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="c in contracts" :key="c.id" class="border-b border-white/5 last:border-0">
                <td class="px-3 py-2">{{ c.validFrom }}</td>
                <td class="px-3 py-2 text-slate-400">{{ c.validTo ?? '—' }}</td>
                <td class="px-3 py-2 font-mono">{{ c.weeklyHours }}</td>
                <td class="px-3 py-2">{{ c.workingDaysPerWeek }}</td>
                <td class="px-3 py-2 font-mono">{{ c.dailyTargetHours }}</td>
                <td class="px-3 py-2 text-right">
                  <button
                    class="text-slate-500 hover:text-rose-400 transition-colors"
                    @click="onDeleteContract(c.id)"
                  >
                    <Trash2 :size="14" />
                  </button>
                </td>
              </tr>
              <tr v-if="!contracts.length">
                <td colspan="6" class="px-3 py-4 text-center text-slate-500">Keine Verträge.</td>
              </tr>
            </tbody>
          </table>
        </div>
        <form class="grid grid-cols-3 gap-2" @submit.prevent="onCreateContract">
          <label class="text-xs text-slate-500 col-span-3 -mb-1">Neuer Vertrag</label>
          <input v-model="contractForm.validFrom" type="date" required :class="inputClass" />
          <input
            v-model="contractForm.validTo"
            type="date"
            :class="inputClass"
            placeholder="Gültig bis (optional)"
          />
          <input
            v-model.number="contractForm.weeklyHours"
            type="number"
            step="0.5"
            min="0"
            max="168"
            placeholder="Std/Woche"
            required
            :class="inputClass"
          />
          <input
            v-model.number="contractForm.workingDaysPerWeek"
            type="number"
            min="0"
            max="7"
            placeholder="Tage/Woche"
            required
            :class="inputClass"
          />
          <input
            v-model.number="contractForm.dailyTargetHours"
            type="number"
            step="0.5"
            min="0"
            max="24"
            placeholder="Std/Tag"
            required
            :class="inputClass"
          />
          <button
            type="submit"
            :disabled="savingContract"
            class="rounded-lg bg-white/10 hover:bg-white/15 transition-colors py-2 text-sm font-medium disabled:opacity-50"
          >
            {{ savingContract ? 'Anlegen…' : 'Anlegen' }}
          </button>
          <p v-if="contractError" class="col-span-3 text-sm text-rose-400">{{ contractError }}</p>
        </form>
      </section>
    </div>
  </ModalShell>
</template>
