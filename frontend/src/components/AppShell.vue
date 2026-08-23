<script setup lang="ts">
import { LayoutDashboard, CalendarDays, Users, Tags, Settings, LogOut } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}

const nav = [
  { to: '/dashboard', label: 'Übersicht', icon: LayoutDashboard },
  { to: '/', label: 'Dienstplan', icon: CalendarDays },
  { to: '/employees', label: 'Mitarbeiter', icon: Users },
  { to: '/stammdaten', label: 'Stammdaten', icon: Tags },
  { to: '/settings', label: 'Einstellungen', icon: Settings },
]
</script>

<template>
  <div class="flex min-h-screen">
    <aside class="w-72 shrink-0 border-r border-white/8 bg-[#0d0f16] flex flex-col">
      <div class="px-6 py-5 text-lg font-semibold tracking-tight">Schichtplaner</div>
      <nav class="flex-1 px-3 space-y-1">
        <router-link
          v-for="item in nav"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-300 hover:bg-white/5 hover:text-white transition-colors"
          active-class="bg-white/8 text-white"
        >
          <component :is="item.icon" :size="16" />
          {{ item.label }}
        </router-link>
      </nav>
      <div class="px-3 py-4 border-t border-white/8">
        <div v-if="auth.claims.email" class="px-3 pb-2 text-xs text-slate-500 truncate">
          {{ auth.claims.email }} · {{ auth.claims.role }}
        </div>
        <button
          class="w-full flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-300 hover:bg-white/5 hover:text-white transition-colors focus-visible:ring-2 focus-visible:ring-indigo-500 outline-none"
          @click="onLogout"
        >
          <LogOut :size="16" />
          Abmelden
        </button>
      </div>
    </aside>
    <main class="flex-1 overflow-y-auto">
      <slot />
    </main>
  </div>
</template>
