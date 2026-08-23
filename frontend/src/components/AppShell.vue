<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { LayoutDashboard, CalendarDays, Users, Tags, Settings, LogOut, Menu, X } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

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

const mobileOpen = ref(false)

// Below md, the sidebar is an off-canvas drawer - lock body scroll while it's open.
watch(mobileOpen, (open) => {
  document.body.classList.toggle('overflow-hidden', open)
})
onUnmounted(() => document.body.classList.remove('overflow-hidden'))

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') mobileOpen.value = false
}
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="flex min-h-screen">
    <div
      class="md:hidden fixed top-0 inset-x-0 z-30 flex items-center gap-3 px-4 py-3 bg-[#0d0f16] border-b border-white/8 print:hidden"
    >
      <button
        class="text-slate-300 hover:text-white transition-colors focus-visible:ring-2 focus-visible:ring-indigo-500 outline-none rounded-lg"
        aria-label="Menü öffnen"
        @click="mobileOpen = true"
      >
        <Menu :size="22" />
      </button>
      <div class="text-base font-semibold tracking-tight">Schichtplaner</div>
    </div>

    <div
      v-if="mobileOpen"
      class="md:hidden fixed inset-0 bg-black/60 backdrop-blur-sm z-40 print:hidden"
      @click="mobileOpen = false"
    ></div>

    <aside
      class="fixed md:static inset-y-0 left-0 z-50 w-72 shrink-0 border-r border-white/8 bg-[#0d0f16] flex flex-col print:hidden transition-transform duration-200 md:translate-x-0"
      :class="mobileOpen ? 'translate-x-0' : '-translate-x-full'"
    >
      <div class="flex items-center justify-between px-6 py-5">
        <span class="text-lg font-semibold tracking-tight">Schichtplaner</span>
        <button
          class="md:hidden text-slate-500 hover:text-slate-300 transition-colors"
          aria-label="Menü schließen"
          @click="mobileOpen = false"
        >
          <X :size="18" />
        </button>
      </div>
      <nav class="flex-1 px-3 space-y-1">
        <router-link
          v-for="item in nav"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-300 hover:bg-white/5 hover:text-white transition-colors"
          active-class="bg-white/8 text-white"
          @click="mobileOpen = false"
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
    <main class="flex-1 overflow-y-auto pt-14 md:pt-0">
      <slot />
    </main>
  </div>
</template>
