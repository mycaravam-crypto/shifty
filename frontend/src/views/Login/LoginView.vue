<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const email = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

async function onSubmit() {
  error.value = ''
  loading.value = true
  try {
    await auth.login(email.value, password.value)
    router.push((route.query.redirect as string) ?? { name: 'schedule' })
  } catch {
    error.value = 'E-Mail oder Passwort ist falsch.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center px-4">
    <form class="glass w-full max-w-sm rounded-2xl p-8 space-y-5" @submit.prevent="onSubmit">
      <h1 class="text-xl font-semibold text-center">Schichtplaner</h1>
      <div class="space-y-1">
        <label class="text-[10px] uppercase tracking-wider font-bold text-slate-500">E-Mail</label>
        <input
          v-model="email"
          type="email"
          required
          autofocus
          class="w-full rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        />
      </div>
      <div class="space-y-1">
        <label class="text-[10px] uppercase tracking-wider font-bold text-slate-500"
          >Passwort</label
        >
        <input
          v-model="password"
          type="password"
          required
          class="w-full rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
        />
      </div>
      <p v-if="error" class="text-sm text-rose-400">{{ error }}</p>
      <button
        type="submit"
        :disabled="loading"
        class="w-full rounded-lg bg-linear-to-r from-blue-600 to-indigo-600 py-2 text-sm font-medium hover:opacity-90 transition-opacity disabled:opacity-50 focus-visible:ring-2 focus-visible:ring-indigo-400 outline-none"
      >
        {{ loading ? 'Anmelden…' : 'Anmelden' }}
      </button>
    </form>
  </div>
</template>
