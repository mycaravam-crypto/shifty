import { defineStore } from 'pinia'
import axios from 'axios'
import api from '@/services/api'

interface AccessTokenClaims {
  email?: string
  role?: string
}

// JwtTokenFactory writes claims using the long ClaimTypes URIs, not short JSON keys.
const NAME_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'

function decodeClaims(token: string): AccessTokenClaims {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    return { email: payload[NAME_CLAIM], role: payload[ROLE_CLAIM] }
  } catch {
    return {}
  }
}

// Access token lives in memory only (not localStorage) to limit XSS exposure.
// The refresh token is an httpOnly cookie the browser sends automatically.
export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: null as string | null,
    claims: {} as AccessTokenClaims,
    ready: false, // true once the initial silent-refresh attempt has completed
  }),
  actions: {
    setAccessToken(token: string | null) {
      this.accessToken = token
      this.claims = token ? decodeClaims(token) : {}
    },
    async login(email: string, password: string) {
      const res = await api.post('/v1/auth/login', { email, password })
      this.setAccessToken(res.data.accessToken)
    },
    async logout() {
      // issue #55: refresh tokens are now DB-backed, so logging out actually revokes the
      // current session's RefreshToken row server-side (and clears the httpOnly cookie via
      // Set-Cookie) instead of just discarding local state — an httpOnly cookie can't be
      // cleared from JS anyway, so the old client-side `document.cookie` write below never
      // actually worked. Best-effort: if the call fails (e.g. the access token already
      // expired), still clear local state so the user ends up logged out either way.
      try {
        await api.post('/v1/auth/logout')
      } catch {
        // ignore — falling through to local cleanup regardless
      }
      this.setAccessToken(null)
    },
    // "Log out other sessions" (issue #55) — revokes every refresh-token session for this
    // user, including this one, so the caller is left logged out locally too.
    async logoutAll() {
      try {
        await api.post('/v1/auth/logout-all')
      } catch {
        // ignore — falling through to local cleanup regardless
      }
      this.setAccessToken(null)
    },
    // Attempts to exchange the httpOnly refresh cookie for an access token, e.g. on page load.
    async tryRefresh() {
      try {
        const res = await axios.post('/api/v1/auth/refresh', {}, { withCredentials: true })
        this.setAccessToken(res.data.accessToken)
      } catch {
        this.setAccessToken(null)
      } finally {
        this.ready = true
      }
    },
  },
})
