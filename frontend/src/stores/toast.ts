import { defineStore } from 'pinia'

export type ToastKind = 'success' | 'error'

export interface Toast {
  id: number
  kind: ToastKind
  message: string
}

const SUCCESS_DURATION_MS = 4000
const ERROR_DURATION_MS = 6000

let nextId = 1

export const useToastStore = defineStore('toast', {
  state: () => ({
    toasts: [] as Toast[],
  }),
  actions: {
    push(kind: ToastKind, message: string) {
      const id = nextId++
      this.toasts.push({ id, kind, message })
      setTimeout(() => this.dismiss(id), kind === 'error' ? ERROR_DURATION_MS : SUCCESS_DURATION_MS)
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
