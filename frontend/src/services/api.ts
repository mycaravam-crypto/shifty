import axios from 'axios'
import { useAuthStore } from '@/stores/auth'

const api = axios.create({
  baseURL: '/api',
  withCredentials: true, // send the httpOnly refresh-token cookie
})

api.interceptors.request.use((config) => {
  const { accessToken } = useAuthStore()
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

let refreshing: Promise<string> | null = null

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const auth = useAuthStore()
    const original = error.config
    if (error.response?.status === 401 && !original._retried) {
      original._retried = true
      refreshing ??= axios
        .post('/api/v1/auth/refresh', {}, { withCredentials: true })
        .catch((err) => {
          auth.setAccessToken(null)
          throw err
        })
        .then((res) => res.data.accessToken)
        .finally(() => {
          refreshing = null
        })
      const token = await refreshing
      auth.setAccessToken(token)
      original.headers.Authorization = `Bearer ${token}`
      return api(original)
    }
    return Promise.reject(error)
  },
)

export default api
