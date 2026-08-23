import { defineStore } from 'pinia'

export interface Toast {
  id: number
  type: 'success' | 'error'
  message: string
}

let nextId = 0
const DEFAULT_DURATION_MS = 4000

export const useToastStore = defineStore('toast', {
  state: () => ({
    toasts: [] as Toast[],
  }),
  actions: {
    push(type: Toast['type'], message: string, duration = DEFAULT_DURATION_MS) {
      const id = nextId++
      this.toasts.push({ id, type, message })
      window.setTimeout(() => this.dismiss(id), duration)
    },
    success(message: string) {
      this.push('success', message)
    },
    error(message: string) {
      this.push('error', message)
    },
    dismiss(id: number) {
      this.toasts = this.toasts.filter((t) => t.id !== id)
    },
  },
})
