<script setup lang="ts">
import { Copy, Sparkles, Wand2 } from '@lucide/vue'
import { paletteDragPayload, type DragPayload } from './composables/useScheduleDnD'
import { currencyFmt } from './format'
import type { ShiftType } from './types'

defineProps<{
  activeShiftTypes: ShiftType[]
  hasAssignments: boolean
  copyingMonth: boolean
  totalLaborCost: number | null
  isDraft: boolean
  chipPointerDown: (e: PointerEvent, payload: DragPayload) => void
}>()
const emit = defineEmits<{
  'copy-month': []
  'open-auto-fill': []
  suggest: [shiftType: ShiftType]
}>()
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 mb-4 print:hidden">
    <button
      v-if="hasAssignments"
      :disabled="copyingMonth"
      class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors disabled:opacity-50"
      @click="emit('copy-month')"
    >
      <Copy :size="14" />
      {{ copyingMonth ? 'Kopiere…' : 'Monat kopieren' }}
    </button>
    <button
      v-if="activeShiftTypes.length && isDraft"
      class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors"
      @click="emit('open-auto-fill')"
    >
      <Wand2 :size="14" />
      Automatisch füllen
    </button>
    <!-- issue #79: once the Schedule isn't Draft, these stay visible as a color legend but lose
         drag-to-create and the suggestion action — both would just 409. -->
    <div
      v-for="s in activeShiftTypes"
      :key="s.id"
      class="flex items-center gap-2 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm touch-none select-none"
      :class="isDraft ? 'cursor-grab' : 'cursor-default'"
      @pointerdown="isDraft && chipPointerDown($event, paletteDragPayload(s))"
    >
      <span class="w-2.5 h-2.5 rounded-full shrink-0" :style="{ backgroundColor: s.color }"></span>
      {{ s.name }}
      <span class="font-mono text-slate-500 text-xs"
        >{{ s.startTime.slice(0, 5) }}–{{ s.endTime.slice(0, 5) }}</span
      >
      <button
        v-if="isDraft"
        type="button"
        title="Mitarbeiter vorschlagen"
        class="text-slate-500 hover:text-indigo-300 transition-colors"
        @pointerdown.stop
        @click.stop="emit('suggest', s)"
      >
        <Sparkles :size="13" />
      </button>
    </div>
    <p v-if="!activeShiftTypes.length" class="text-sm text-slate-500">
      Keine Schichtarten angelegt.
    </p>
    <div v-if="totalLaborCost !== null" class="ml-auto font-mono text-sm text-emerald-400">
      Lohnkosten: {{ currencyFmt.format(totalLaborCost) }}
    </div>
  </div>
</template>
