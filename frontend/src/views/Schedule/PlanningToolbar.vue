<script setup lang="ts">
import { ChevronLeft, ChevronRight, HelpCircle, Printer } from '@lucide/vue'

defineProps<{ monthLabel: string; hasCurrentSchedule: boolean }>()
const emit = defineEmits<{
  prev: []
  next: []
  'show-shortcuts': []
  'export-all': []
}>()
</script>

<template>
  <div class="flex items-center justify-between mb-6">
    <h1 class="text-2xl font-semibold">Dienstplan</h1>
    <div class="flex items-center gap-3">
      <button
        class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
        @click="emit('prev')"
      >
        <ChevronLeft :size="18" />
      </button>
      <span class="font-mono text-sm text-slate-400 capitalize">{{ monthLabel }}</span>
      <button
        class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
        @click="emit('next')"
      >
        <ChevronRight :size="18" />
      </button>
      <button
        class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
        title="Tastenkürzel anzeigen (?)"
        @click="emit('show-shortcuts')"
      >
        <HelpCircle :size="18" />
      </button>
      <button
        v-if="hasCurrentSchedule"
        class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors print:hidden"
        @click="emit('export-all')"
      >
        <Printer :size="14" />
        PDF exportieren
      </button>
    </div>
  </div>
</template>
