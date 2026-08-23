<script setup lang="ts">
import { X } from '@lucide/vue'

defineProps<{ title: string; wide?: boolean }>()
const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
    @click.self="emit('close')"
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
