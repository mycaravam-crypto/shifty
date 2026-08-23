<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <div class="p-8 max-w-lg">
    <h1 class="text-2xl font-semibold mb-6">Einstellungen</h1>
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
  </div>
</template>
