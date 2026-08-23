<script setup lang="ts">
import ModalShell from './ModalShell.vue'

defineProps<{ title: string; message: string; confirmLabel?: string }>()
const emit = defineEmits<{ confirm: []; close: [] }>()
</script>

<template>
  <ModalShell :title="title" @close="emit('close')">
    <p class="text-sm text-slate-300 mb-5">{{ message }}</p>
    <div class="flex justify-end gap-3">
      <button
        class="rounded-lg px-4 py-2 text-sm font-medium text-slate-300 hover:bg-white/5 transition-colors"
        @click="emit('close')"
      >
        Abbrechen
      </button>
      <button
        class="rounded-lg bg-rose-600 hover:bg-rose-500 px-4 py-2 text-sm font-medium transition-colors"
        @click="emit('confirm')"
      >
        {{ confirmLabel ?? 'Löschen' }}
      </button>
    </div>
  </ModalShell>
</template>
