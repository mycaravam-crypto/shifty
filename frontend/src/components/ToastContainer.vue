<script setup lang="ts">
import { CheckCircle2, XCircle, X } from '@lucide/vue'
import { useToastStore } from '../stores/toast'

const toast = useToastStore()
</script>

<template>
  <div
    class="fixed bottom-4 right-4 z-[100] flex flex-col gap-2 print:hidden pointer-events-none w-full max-w-sm px-4 sm:px-0"
  >
    <TransitionGroup name="toast">
      <div
        v-for="t in toast.toasts"
        :key="t.id"
        class="glass rounded-xl shadow-xl px-4 py-3 flex items-center gap-2.5 text-sm pointer-events-auto"
      >
        <CheckCircle2 v-if="t.type === 'success'" :size="16" class="text-emerald-400 shrink-0" />
        <XCircle v-else :size="16" class="text-rose-400 shrink-0" />
        <span class="flex-1">{{ t.message }}</span>
        <button
          class="text-slate-500 hover:text-slate-300 transition-colors shrink-0"
          @click="toast.dismiss(t.id)"
        >
          <X :size="14" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: all 0.2s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
.toast-leave-active {
  position: absolute;
  right: 0;
}
</style>
