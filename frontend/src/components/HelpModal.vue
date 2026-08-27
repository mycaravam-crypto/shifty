<script setup lang="ts">
import { ref } from 'vue'
import ModalShell from '@/components/ModalShell.vue'
import { helpTopics } from '@/data/helpTopics'

const props = defineProps<{ initialTopicId?: string }>()
defineEmits<{ close: [] }>()

const activeTopicId = ref(props.initialTopicId ?? helpTopics[0].id)
</script>

<template>
  <ModalShell title="Hilfe" wide @close="$emit('close')">
    <div class="grid grid-cols-1 md:grid-cols-[180px_1fr] gap-4">
      <select
        v-model="activeTopicId"
        class="md:hidden rounded-lg bg-white/5 border border-white/10 px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-indigo-500"
      >
        <option v-for="t in helpTopics" :key="t.id" :value="t.id">{{ t.title }}</option>
      </select>

      <nav class="hidden md:flex flex-col gap-0.5 shrink-0">
        <button
          v-for="t in helpTopics"
          :key="t.id"
          type="button"
          class="text-left rounded-lg px-3 py-2 text-sm transition-colors"
          :class="
            activeTopicId === t.id
              ? 'bg-white/10 text-white'
              : 'text-slate-400 hover:bg-white/5 hover:text-slate-200'
          "
          @click="activeTopicId = t.id"
        >
          {{ t.title }}
        </button>
      </nav>

      <div class="min-w-0 max-h-[65vh] overflow-y-auto pr-1">
        <template v-for="t in helpTopics" :key="t.id">
          <div v-if="t.id === activeTopicId" class="space-y-4">
            <h3 class="text-base font-semibold md:hidden">{{ t.title }}</h3>
            <div v-for="(block, i) in t.blocks" :key="i">
              <h4 v-if="block.h" class="text-sm font-semibold text-slate-200 mb-1.5">
                {{ block.h }}
              </h4>
              <p v-if="block.p" class="text-sm text-slate-400 leading-relaxed">{{ block.p }}</p>
              <ul
                v-if="block.ul"
                class="list-disc pl-5 text-sm text-slate-400 space-y-1.5 mt-1.5 leading-relaxed"
              >
                <li v-for="(item, j) in block.ul" :key="j">{{ item }}</li>
              </ul>
            </div>
          </div>
        </template>
      </div>
    </div>
  </ModalShell>
</template>
