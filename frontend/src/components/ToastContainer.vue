<script setup lang="ts">
import { CheckCircle2, XCircle, X } from '@lucide/vue'
import { useToastStore } from '@/stores/toast'

const toastStore = useToastStore()
</script>

<template>
  <div
    class="fixed bottom-4 right-4 z-100 flex flex-col gap-2 w-full max-w-sm print:hidden"
    aria-live="polite"
  >
    <TransitionGroup name="toast">
      <div
        v-for="toast in toastStore.toasts"
        :key="toast.id"
        class="glass rounded-xl shadow-xl border-l-4 px-4 py-3 flex items-start gap-3"
        :class="toast.kind === 'success' ? 'border-l-emerald-500' : 'border-l-rose-500'"
      >
        <CheckCircle2
          v-if="toast.kind === 'success'"
          :size="18"
          class="text-emerald-400 shrink-0 mt-0.5"
        />
        <XCircle v-else :size="18" class="text-rose-400 shrink-0 mt-0.5" />
        <p class="text-sm flex-1">{{ toast.message }}</p>
        <button
          class="text-slate-500 hover:text-slate-300 transition-colors shrink-0"
          aria-label="Schließen"
          @click="toastStore.dismiss(toast.id)"
        >
          <X :size="16" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
.toast-leave-active {
  position: absolute;
}
</style>
