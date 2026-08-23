<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { X } from '@lucide/vue'

defineProps<{ title: string; wide?: boolean }>()
const emit = defineEmits<{ close: [] }>()

// A tap that opens this modal (e.g. an assignment chip on a touch device)
// is followed by the browser's synthetic "click" compatibility event at the
// same coordinates — which, once the backdrop exists, lands squarely on it
// and would immediately close what the tap just opened. Ignore backdrop
// clicks in the brief window right after mount so only a real outside tap
// closes the modal.
const openedAt = Date.now()
function onBackdropClick() {
  if (Date.now() - openedAt < 500) return
  emit('close')
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close')
}
onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
    @click.self="onBackdropClick"
  >
    <div
      class="glass rounded-2xl shadow-xl w-full max-h-[90vh] overflow-y-auto"
      :class="wide ? 'max-w-2xl' : 'max-w-md'"
    >
      <div
        class="flex items-center justify-between px-5 py-4 border-b border-white/8 sticky top-0 bg-[#11141c]"
      >
        <h2 class="text-base font-semibold">{{ title }}</h2>
        <button
          class="text-slate-500 hover:text-slate-300 transition-colors"
          @click="emit('close')"
        >
          <X :size="18" />
        </button>
      </div>
      <div class="p-5">
        <slot />
      </div>
    </div>
  </div>
</template>
