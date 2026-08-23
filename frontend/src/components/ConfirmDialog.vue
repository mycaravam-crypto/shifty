<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import ModalShell from './ModalShell.vue'
import { useConfirmStore } from '../stores/confirm'

const confirmStore = useConfirmStore()

// ModalShell suppresses its own Escape handling while a confirm is pending
// (so it doesn't also close an underlying modal this dialog is stacked on
// top of) — so this dialog needs its own independent Escape handling here.
function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && confirmStore.request) confirmStore.resolve(false)
}
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <ModalShell
    v-if="confirmStore.request"
    :title="confirmStore.request.title"
    @close="confirmStore.resolve(false)"
  >
    <p class="text-sm text-slate-400 mb-5">{{ confirmStore.request.message }}</p>
    <div class="flex justify-end gap-2">
      <button
        class="rounded-lg bg-white/5 hover:bg-white/10 transition-colors px-4 py-2 text-sm font-medium"
        @click="confirmStore.resolve(false)"
      >
        Abbrechen
      </button>
      <button
        class="rounded-lg bg-rose-600 hover:bg-rose-500 transition-colors px-4 py-2 text-sm font-medium"
        @click="confirmStore.resolve(true)"
      >
        {{ confirmStore.request.confirmLabel }}
      </button>
    </div>
  </ModalShell>
</template>
