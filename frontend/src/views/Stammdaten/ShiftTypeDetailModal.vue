<script setup lang="ts">
import { ref } from 'vue'
import axios from 'axios'
import api from '../../services/api'
import ModalShell from '../../components/ModalShell.vue'

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
}

const props = defineProps<{ shiftType: ShiftType }>()
const emit = defineEmits<{ close: []; updated: [] }>()

const inputClass =
  'rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500'

const form = ref({
  name: props.shiftType.name,
  startTime: props.shiftType.startTime.slice(0, 5),
  endTime: props.shiftType.endTime.slice(0, 5),
  breakMinutes: props.shiftType.breakMinutes,
  color: props.shiftType.color,
  active: props.shiftType.active,
  minStaffing: props.shiftType.minStaffing?.toString() ?? '',
  maxStaffing: props.shiftType.maxStaffing?.toString() ?? '',
})
const saving = ref(false)
const error = ref('')

async function onSave() {
  saving.value = true
  error.value = ''
  try {
    await api.put(`/shift-types/${props.shiftType.id}`, {
      name: form.value.name,
      startTime: `${form.value.startTime}:00`,
      endTime: `${form.value.endTime}:00`,
      breakMinutes: form.value.breakMinutes,
      color: form.value.color,
      active: form.value.active,
      minStaffing: form.value.minStaffing ? Number(form.value.minStaffing) : null,
      maxStaffing: form.value.maxStaffing ? Number(form.value.maxStaffing) : null,
    })
    emit('updated')
  } catch (e) {
    error.value = axios.isAxiosError(e) && e.response?.data ? e.response.data : 'Speichern fehlgeschlagen.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <ModalShell title="Schichttyp bearbeiten" @close="emit('close')">
    <form class="grid grid-cols-2 gap-3" @submit.prevent="onSave">
      <input v-model="form.name" placeholder="Name" required class="col-span-2" :class="inputClass" />
      <!-- lang="de-DE" is a no-op in Chromium (picker format is OS-locale-driven, not page-lang) -->
      <input v-model="form.startTime" type="time" lang="de-DE" required :class="inputClass" />
      <input v-model="form.endTime" type="time" lang="de-DE" required :class="inputClass" />
      <input
        v-model.number="form.breakMinutes"
        type="number"
        min="0"
        max="480"
        placeholder="Pause (Minuten)"
        :class="inputClass"
      />
      <input v-model="form.color" type="color" class="h-10 w-full rounded-lg bg-white/5 border border-white/10" />
      <input v-model="form.minStaffing" type="number" min="1" placeholder="Min. Besetzung" :class="inputClass" />
      <input v-model="form.maxStaffing" type="number" min="1" placeholder="Max. Besetzung" :class="inputClass" />
      <label class="col-span-2 flex items-center gap-2 text-sm text-slate-400">
        <input v-model="form.active" type="checkbox" class="rounded border-white/10" />
        Aktiv
      </label>
      <p v-if="error" class="col-span-2 text-sm text-rose-400">{{ error }}</p>
      <button
        type="submit"
        :disabled="saving"
        class="col-span-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
      >
        {{ saving ? 'Speichern…' : 'Speichern' }}
      </button>
    </form>
  </ModalShell>
</template>
