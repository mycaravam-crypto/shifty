<script setup lang="ts">
import { Archive, CheckCircle2, ChevronLeft, ChevronRight, HelpCircle, Printer } from '@lucide/vue'
import type { Schedule } from './types'

// issue #68's ScheduleStatus enum (Domain/Scheduling/Schedule.cs), serialized as its ordinal
// same as every other backend enum this frontend consumes.
const STATUS_LABELS: Record<number, string> = {
  0: 'Entwurf',
  1: 'Veröffentlicht',
  2: 'Archiviert',
}

defineProps<{
  monthLabel: string
  currentSchedule: Schedule | null
  isDraft: boolean
  isPublished: boolean
  publishing: boolean
  archiving: boolean
  blockingErrorCount: number
  publishBlockReason: string | undefined
}>()
const emit = defineEmits<{
  prev: []
  next: []
  'show-shortcuts': []
  'export-all': []
  publish: []
  archive: []
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
      <span
        v-if="currentSchedule"
        class="rounded-full px-2 py-0.5 text-[10px] uppercase tracking-wider font-bold"
        :class="{
          'bg-slate-500/15 text-slate-400': currentSchedule.status === 0,
          'bg-emerald-500/15 text-emerald-400': currentSchedule.status === 1,
          'bg-violet-500/15 text-violet-400': currentSchedule.status === 2,
        }"
        :title="
          currentSchedule.publishedAt
            ? `Veröffentlicht am ${new Date(currentSchedule.publishedAt).toLocaleString('de-DE')}${currentSchedule.publishedBy ? ' von ' + currentSchedule.publishedBy : ''}`
            : undefined
        "
      >
        {{ STATUS_LABELS[currentSchedule.status] }}
      </span>
      <button
        class="text-slate-400 hover:text-slate-200 transition-colors print:hidden"
        title="Tastenkürzel anzeigen (?)"
        @click="emit('show-shortcuts')"
      >
        <HelpCircle :size="18" />
      </button>
      <button
        v-if="currentSchedule && isDraft"
        :disabled="publishing || blockingErrorCount > 0"
        :title="publishBlockReason"
        class="flex items-center gap-1.5 rounded-lg bg-linear-to-r from-emerald-600 to-emerald-500 px-3 py-1.5 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-40 disabled:cursor-not-allowed print:hidden"
        @click="emit('publish')"
      >
        <CheckCircle2 :size="14" />
        {{ publishing ? 'Veröffentlichen…' : 'Veröffentlichen' }}
      </button>
      <button
        v-if="currentSchedule && isPublished"
        :disabled="archiving"
        class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors disabled:opacity-50 print:hidden"
        @click="emit('archive')"
      >
        <Archive :size="14" />
        Archivieren
      </button>
      <button
        v-if="currentSchedule"
        class="flex items-center gap-1.5 rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm hover:bg-white/10 transition-colors print:hidden"
        @click="emit('export-all')"
      >
        <Printer :size="14" />
        PDF exportieren
      </button>
    </div>
  </div>
</template>
