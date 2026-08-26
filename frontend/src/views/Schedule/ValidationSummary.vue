<script setup lang="ts">
import { computed, ref } from 'vue'
import { ChevronDown } from '@lucide/vue'
import type { ValidationIssue, ValidationResult } from './types'

interface ValidationIssueGroup {
  type: string
  label: string
  severity: 'error' | 'warning'
  issues: ValidationIssue[]
}

// issue #78: German labels for ScheduleValidator's rule types (Application/Validation/*.cs),
// used to group the validation panel by rule instead of a flat list.
const ISSUE_TYPE_LABELS: Record<string, string> = {
  AssignedDuringAbsence: 'Einsatz während Abwesenheit',
  InsufficientBreak: 'Pause unterschritten',
  TooManyConsecutiveDays: 'Zu viele Arbeitstage in Folge',
  ContractHoursExceeded: 'Vertragsstunden überschritten',
  ShiftTypeNotEligible: 'Nicht freigegebene Schichtart',
  InsufficientRest: 'Ruhezeit unterschritten',
  ShiftOverlap: 'Überlappende Schichten',
  Understaffed: 'Unterbesetzung',
  Overstaffed: 'Überbesetzung',
}

const props = defineProps<{ validation: ValidationResult | null }>()
const emit = defineEmits<{ focus: [issue: ValidationIssue] }>()

const expandedIssueGroups = ref<Set<string>>(new Set())

// issue #78: group the flat errors/warnings lists by rule type for the panel's collapsible
// sections, sorted errors-before-warnings then by group size — ScheduleValidator's output
// shape itself is unchanged, this is purely a frontend presentation grouping.
const validationGroups = computed<ValidationIssueGroup[]>(() => {
  if (!props.validation) return []
  const groups = new Map<string, ValidationIssueGroup>()
  const addAll = (issues: ValidationIssue[], severity: 'error' | 'warning') => {
    for (const issue of issues) {
      const existing = groups.get(issue.type)
      if (existing) existing.issues.push(issue)
      else
        groups.set(issue.type, {
          type: issue.type,
          label: ISSUE_TYPE_LABELS[issue.type] ?? issue.type,
          severity,
          issues: [issue],
        })
    }
  }
  addAll(props.validation.errors, 'error')
  addAll(props.validation.warnings, 'warning')
  return [...groups.values()].sort((a, b) => {
    if (a.severity !== b.severity) return a.severity === 'error' ? -1 : 1
    return b.issues.length - a.issues.length
  })
})
function toggleIssueGroup(type: string) {
  const next = new Set(expandedIssueGroups.value)
  if (next.has(type)) next.delete(type)
  else next.add(type)
  expandedIssueGroups.value = next
}
</script>

<template>
  <div
    v-if="validation && (validation.errors.length || validation.warnings.length)"
    class="glass rounded-xl mb-4 text-sm print:hidden"
  >
    <div class="flex flex-wrap items-center gap-4 px-4 py-3 border-b border-white/8">
      <span
        v-if="validation.errors.length"
        class="flex items-center gap-1.5 font-semibold text-rose-400"
      >
        <span class="w-2 h-2 rounded-full bg-rose-400 shrink-0"></span>
        {{ validation.errors.length }} Fehler
      </span>
      <span
        v-if="validation.warnings.length"
        class="flex items-center gap-1.5 font-semibold text-amber-400"
      >
        ▲ {{ validation.warnings.length }} Warnungen
      </span>
    </div>
    <div class="divide-y divide-white/5">
      <div v-for="group in validationGroups" :key="group.type">
        <button
          class="w-full flex items-center justify-between gap-2 px-4 py-2 text-left hover:bg-white/5 transition-colors"
          @click="toggleIssueGroup(group.type)"
        >
          <span
            class="flex items-center gap-2"
            :class="group.severity === 'error' ? 'text-rose-400' : 'text-amber-400'"
          >
            {{ group.severity === 'error' ? '❌' : '⚠' }} {{ group.label }}
            <span class="text-slate-500 font-mono text-xs">({{ group.issues.length }})</span>
          </span>
          <ChevronDown
            :size="14"
            class="text-slate-500 transition-transform shrink-0"
            :class="{ 'rotate-180': expandedIssueGroups.has(group.type) }"
          />
        </button>
        <div v-if="expandedIssueGroups.has(group.type)" class="pb-2">
          <p
            v-for="(issue, i) in group.issues"
            :key="i"
            class="px-4 py-1 text-xs"
            :class="[
              group.severity === 'error' ? 'text-rose-400/90' : 'text-amber-400/90',
              { 'cursor-pointer hover:underline': issue.employeeId },
            ]"
            @click="emit('focus', issue)"
          >
            {{ issue.message }}
          </p>
        </div>
      </div>
    </div>
  </div>
</template>
