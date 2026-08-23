<script setup lang="ts">
import { version } from '../package.json'
</script>

<template>
  <router-view />
  <span class="fixed bottom-2 right-3 text-[11px] text-slate-600 select-none">v{{ version }}</span>
</template>
