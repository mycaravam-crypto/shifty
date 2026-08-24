<script setup lang="ts">
import { ref } from 'vue'
import { Trash2 } from '@lucide/vue'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import ModalShell from '@/components/ModalShell.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'

const toast = useToastStore()

interface ShiftType {
  id: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  color: string
  active: boolean
}
interface Assignment {
  id: string
  employeeId: string
  shiftTypeId: string
  date: string
  startTime: string
  endTime: string
  breakMinutes: number
}

const props = defineProps<{ assignment: Assignment; shiftTypes: ShiftType[] }>()
const emit = defineEmits<{ close: []; updated: [] }>()

const inputClass =
  'rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500'

const form = ref({
  shiftTypeId: props.assignment.shiftTypeId,
  startTime: props.assignment.startTime.slice(0, 5),
  endTime: props.assignment.endTime.slice(0, 5),
  breakMinutes: props.assignment.breakMinutes,
})
const saving = ref(false)
const error = ref('')

function onShiftTypeChange() {
  const shiftType = props.shiftTypes.find((s) => s.id === form.value.shiftTypeId)
  if (!shiftType) return
  form.value.startTime = shiftType.startTime.slice(0, 5)
  form.value.endTime = shiftType.endTime.slice(0, 5)
  form.value.breakMinutes = shiftType.breakMinutes
}

async function onSave() {
  saving.value = true
  error.value = ''
  try {
    await api.put(`/assignments/${props.assignment.id}`, {
      employeeId: props.assignment.employeeId,
      shiftTypeId: form.value.shiftTypeId,
      date: props.assignment.date,
      startTime: `${form.value.startTime}:00`,
      endTime: `${form.value.endTime}:00`,
      breakMinutes: form.value.breakMinutes,
    })
    toast.success('Schicht gespeichert.')
    emit('updated')
  } catch {
    error.value = 'Speichern fehlgeschlagen.'
    toast.error(error.value)
  } finally {
    saving.value = false
  }
}

const confirmingDelete = ref(false)

async function onDeleteConfirmed() {
  try {
    await api.delete(`/assignments/${props.assignment.id}`)
    toast.success('Schicht gelöscht.')
    emit('updated')
  } catch {
    toast.error('Schicht konnte nicht gelöscht werden.')
  }
}
</script>

<template>
  <ModalShell title="Schicht bearbeiten" @close="emit('close')">
    <form class="grid grid-cols-2 gap-3" @submit.prevent="onSave">
      <select
        v-model="form.shiftTypeId"
        class="col-span-2"
        :class="inputClass"
        @change="onShiftTypeChange"
      >
        <option v-for="s in shiftTypes" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
      <!-- lang="de-DE" is a no-op in Chromium (picker format is OS-locale-driven, not page-lang) -->
      <input v-model="form.startTime" type="time" lang="de-DE" required :class="inputClass" />
      <input v-model="form.endTime" type="time" lang="de-DE" required :class="inputClass" />
      <input
        v-model.number="form.breakMinutes"
        type="number"
        min="0"
        max="480"
        placeholder="Pause (Minuten)"
        class="col-span-2"
        :class="inputClass"
      />
      <p v-if="error" class="col-span-2 text-sm text-rose-400">{{ error }}</p>
      <button
        type="submit"
        :disabled="saving"
        class="col-span-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
      >
        {{ saving ? 'Speichern…' : 'Speichern' }}
      </button>
      <button
        type="button"
        class="col-span-2 flex items-center justify-center gap-2 rounded-lg bg-white/5 hover:bg-rose-500/15 hover:text-rose-400 transition-colors py-2 text-sm font-medium"
        @click="confirmingDelete = true"
      >
        <Trash2 :size="14" /> Löschen
      </button>
    </form>

    <ConfirmDialog
      v-if="confirmingDelete"
      title="Schicht löschen"
      message="Diese Schicht wirklich löschen?"
      @confirm="onDeleteConfirmed"
      @close="confirmingDelete = false"
    />
  </ModalShell>
</template>
