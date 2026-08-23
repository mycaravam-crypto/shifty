import { defineStore } from 'pinia'

interface ConfirmRequest {
  title: string
  message: string
  confirmLabel: string
}

export const useConfirmStore = defineStore('confirm', {
  state: () => ({
    request: null as ConfirmRequest | null,
    resolver: null as ((value: boolean) => void) | null,
  }),
  actions: {
    // Mirrors the native confirm()'s call shape (a message, resolved to a boolean) so call
    // sites read the same as before, just with `await` and a styled dialog instead of the
    // browser's native one.
    ask(
      message: string,
      options: { title?: string; confirmLabel?: string } = {},
    ): Promise<boolean> {
      this.resolver?.(false) // a still-pending prior request loses, same as a native confirm() would block on one at a time
      this.request = {
        title: options.title ?? 'Wirklich löschen?',
        message,
        confirmLabel: options.confirmLabel ?? 'Löschen',
      }
      return new Promise((resolve) => {
        this.resolver = resolve
      })
    },
    resolve(value: boolean) {
      this.resolver?.(value)
      this.resolver = null
      this.request = null
    },
  },
})
