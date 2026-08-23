import { defineStore } from 'pinia'

// Access token lives in memory only (not localStorage) to limit XSS exposure.
// The refresh token is an httpOnly cookie the browser sends automatically.
export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: null as string | null,
  }),
  actions: {
    setAccessToken(token: string | null) {
      this.accessToken = token
    },
  },
})
