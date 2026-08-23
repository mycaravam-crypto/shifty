<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useAuthStore } from '../../stores/auth'
import { useToastStore } from '../../stores/toast'
import { getDefaultTeamFilter, setDefaultTeamFilter } from '../../utils/settings'
import api from '../../services/api'

interface Team {
  id: string
  name: string
}

const auth = useAuthStore()
const toast = useToastStore()

const teams = ref<Team[]>([])
const defaultTeam = ref(getDefaultTeamFilter())

onMounted(async () => {
  teams.value = (await api.get('/teams')).data
})

function onDefaultTeamChange() {
  setDefaultTeamFilter(defaultTeam.value)
  toast.success('Einstellung gespeichert.')
}
</script>

<template>
  <div class="p-8 max-w-lg space-y-6">
    <h1 class="text-2xl font-semibold">Einstellungen</h1>

    <section>
      <h2 class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">Konto</h2>
      <div class="glass rounded-xl p-5 space-y-2 text-sm">
        <div class="flex justify-between">
          <span class="text-slate-500">E-Mail</span>
          <span>{{ auth.claims.email }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-slate-500">Rolle</span>
          <span>{{ auth.claims.role }}</span>
        </div>
      </div>
    </section>

    <section>
      <h2 class="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3">Dienstplan</h2>
      <div class="glass rounded-xl p-5">
        <label class="block text-sm text-slate-400 mb-2" for="default-team">
          Standard-Team-Filter
        </label>
        <select
          id="default-team"
          v-model="defaultTeam"
          class="w-full rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
          @change="onDefaultTeamChange"
        >
          <option value="">Alle Teams</option>
          <option v-for="t in teams" :key="t.id" :value="t.id">{{ t.name }}</option>
        </select>
        <p class="text-xs text-slate-500 mt-2">
          Wird im Dienstplan vorausgewählt, solange dort noch kein eigener Filter gesetzt wurde.
        </p>
      </div>
    </section>
  </div>
</template>
