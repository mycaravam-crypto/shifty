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
  endsNextDay: boolean
}
interface Assignment {
  id: string
  employeeId: string
  shiftTypeId: string
  date: string
  startTime: string
  endTime: string
  breakMinutes: number
  breakStartTime: string | null
  endsNextDay: boolean
}

// issue #79: once the owning Schedule is Published/Archived, the backend already 409s any
// write against it (issue #68) — this prop keeps the UI in sync with that instead of letting a
// manager fill out a form that will just fail to save.
const props = defineProps<{ assignment: Assignment; shiftTypes: ShiftType[]; readonly?: boolean }>()
const emit = defineEmits<{ close: []; updated: [] }>()

const inputClass =
  'rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500'

const form = ref({
  shiftTypeId: props.assignment.shiftTypeId,
  startTime: props.assignment.startTime.slice(0, 5),
  endTime: props.assignment.endTime.slice(0, 5),
  breakMinutes: props.assignment.breakMinutes,
  // issue #58: optional — blank means "unknown/unspecified break timing", same as the backend's
  // null BreakStartTime, in which case the night-surcharge calculation falls back to its
  // pre-existing (unadjusted) approximation.
  breakStartTime: props.assignment.breakStartTime?.slice(0, 5) ?? '',
  // issue #157: true means EndTime falls on the day after `date` (an overnight shift).
  endsNextDay: props.assignment.endsNextDay,
})
const saving = ref(false)
const error = ref('')

function onShiftTypeChange() {
  const shiftType = props.shiftTypes.find((s) => s.id === form.value.shiftTypeId)
  if (!shiftType) return
  form.value.startTime = shiftType.startTime.slice(0, 5)
  form.value.endTime = shiftType.endTime.slice(0, 5)
  form.value.breakMinutes = shiftType.breakMinutes
  form.value.endsNextDay = shiftType.endsNextDay
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
      breakStartTime: form.value.breakStartTime ? `${form.value.breakStartTime}:00` : null,
      endsNextDay: form.value.endsNextDay,
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
  <ModalShell :title="readonly ? 'Schicht ansehen' : 'Schicht bearbeiten'" @close="emit('close')">
    <p v-if="readonly" class="mb-3 text-sm text-slate-500">
      Dieser Dienstplan ist archiviert und kann nicht mehr bearbeitet werden.
    </p>
    <form class="grid grid-cols-2 gap-3" @submit.prevent="onSave">
      <select
        v-model="form.shiftTypeId"
        class="col-span-2"
        :disabled="readonly"
        :class="inputClass"
        @change="onShiftTypeChange"
      >
        <option v-for="s in shiftTypes" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
      <!-- lang="de-DE" is a no-op in Chromium (picker format is OS-locale-driven, not page-lang) -->
      <input
        v-model="form.startTime"
        type="time"
        lang="de-DE"
        required
        :disabled="readonly"
        :class="inputClass"
      />
      <input
        v-model="form.endTime"
        type="time"
        lang="de-DE"
        required
        :disabled="readonly"
        :class="inputClass"
      />
      <input
        v-model.number="form.breakMinutes"
        type="number"
        min="0"
        max="480"
        placeholder="Pause (Minuten)"
        :disabled="readonly"
        :class="inputClass"
      />
      <!-- issue #58: optional break start time, used to precisely reduce the night-surcharge
           overlap when it falls partly/fully in the 20:00-06:00 window; left blank it falls back
           to the existing approximation. -->
      <input
        v-model="form.breakStartTime"
        type="time"
        lang="de-DE"
        title="Pausenbeginn (optional)"
        :disabled="readonly"
        :class="inputClass"
      />
      <!-- issue #157: EndTime is then interpreted as falling on the day after `date`. -->
      <label class="col-span-2 flex items-center gap-2 text-sm text-slate-300">
        <input v-model="form.endsNextDay" type="checkbox" :disabled="readonly" />
        Endet am nächsten Tag (Nachtschicht)
      </label>
      <p v-if="error" class="col-span-2 text-sm text-rose-400">{{ error }}</p>
      <button
        v-if="!readonly"
        type="submit"
        :disabled="saving"
        class="col-span-2 rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50"
      >
        {{ saving ? 'Speichern…' : 'Speichern' }}
      </button>
      <button
        v-if="!readonly"
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
