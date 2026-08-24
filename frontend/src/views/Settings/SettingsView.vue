<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useSettingsStore } from '@/stores/settings'
import { useToastStore } from '@/stores/toast'
import api from '@/services/api'

interface Team {
  id: string
  name: string
}

const auth = useAuthStore()
const settings = useSettingsStore()
const toast = useToastStore()

const teams = ref<Team[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    teams.value = (await api.get('/teams')).data
  } finally {
    loading.value = false
  }
})

function onDefaultTeamChange(e: Event) {
  const value = (e.target as HTMLSelectElement).value
  settings.setDefaultTeamId(value || null)
  toast.success('Einstellung gespeichert.')
}

function onNotificationsToggle(e: Event) {
  const checked = (e.target as HTMLInputElement).checked
  settings.setNotificationsEnabled(checked)
  toast.success('Einstellung gespeichert.')
}
</script>

<template>
  <div class="p-8 max-w-lg">
    <h1 class="text-2xl font-semibold mb-6">Einstellungen</h1>

    <div class="glass rounded-xl p-5 space-y-2 text-sm mb-6">
      <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-2">Konto</div>
      <div class="flex justify-between">
        <span class="text-slate-500">E-Mail</span>
        <span>{{ auth.claims.email }}</span>
      </div>
      <div class="flex justify-between">
        <span class="text-slate-500">Rolle</span>
        <span>{{ auth.claims.role }}</span>
      </div>
    </div>

    <div class="glass rounded-xl p-5 space-y-3 text-sm">
      <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500">Dienstplan</div>
      <div>
        <label class="block text-slate-400 mb-1.5" for="default-team">Standard-Team-Filter</label>
        <select
          id="default-team"
          :value="settings.defaultTeamId ?? ''"
          :disabled="loading"
          class="w-full rounded-lg bg-white/5 border border-white/10 px-3 py-1.5 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 disabled:opacity-50"
          @change="onDefaultTeamChange"
        >
          <option value="">Alle Teams</option>
          <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
        </select>
        <p class="text-xs text-slate-500 mt-1.5">
          Wird beim Öffnen des Dienstplans als Team-Filter vorausgewählt, solange kein anderer
          Filter über die URL gesetzt ist.
        </p>
      </div>
    </div>

    <div class="glass rounded-xl p-5 space-y-3 text-sm mt-6">
      <div class="text-[10px] uppercase tracking-wider font-bold text-slate-500">
        Benachrichtigungen
      </div>
      <label class="flex items-center gap-2.5 cursor-pointer">
        <input
          type="checkbox"
          :checked="settings.notificationsEnabled"
          class="h-4 w-4 rounded border-white/10 bg-white/5 text-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
          @change="onNotificationsToggle"
        />
        <span>Benachrichtigungen für neue Probleme</span>
      </label>
      <p class="text-xs text-slate-500">
        Markiert in der Übersicht Pain Points, die seit deinem letzten Besuch neu hinzugekommen
        sind, mit einem "Neu"-Badge. Rein clientseitig — es gibt (noch) kein E-Mail- oder
        Push-Digest, dafür fehlt bislang jede Mail-Integration in dieser Codebase; "neu" heißt hier
        "war beim letzten Dashboard-Besuch nicht in der Problemliste", nicht "seit einem echten
        Zeitstempel entstanden" (`PainPointDto` trägt kein Erstellungsdatum).
      </p>
    </div>
  </div>
</template>
