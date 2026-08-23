<script setup lang="ts">
import { useRoute } from 'vue-router'
import { version } from '../package.json'
import AppShell from './components/AppShell.vue'
import ToastContainer from './components/ToastContainer.vue'

const route = useRoute()
</script>

<template>
  <template v-if="route.meta.public">
    <router-view />
  </template>
  <AppShell v-else>
    <router-view />
  </AppShell>
  <span class="fixed bottom-2 right-3 text-[11px] text-slate-600 select-none">v{{ version }}</span>
  <ToastContainer />
</template>
