import { defineStore } from 'pinia'
import adminApi, { getAdminToken, setAdminToken } from '@/api/adminClient'

export const useAdminAuthStore = defineStore('adminAuth', {
  state: () => ({
    userId: null,
    username: '',
    loaded: false,
  }),
  getters: {
    isLoggedIn: (s) => !!getAdminToken() && s.userId != null,
  },
  actions: {
    async login(username, password) {
      const res = await adminApi.post('/auth/login', { username, password })
      const { token, user_id, username: name } = res.data.data
      setAdminToken(token)
      this.userId = user_id
      this.username = name
      this.loaded = true
    },
    logout() {
      setAdminToken(null)
      this.userId = null
      this.username = ''
      this.loaded = false
    },
    async fetchMe() {
      if (!getAdminToken()) {
        this.loaded = true
        return false
      }
      try {
        const res = await adminApi.get('/auth/me')
        this.userId = res.data.data.user_id
        this.username = res.data.data.username
        this.loaded = true
        return true
      } catch {
        this.logout()
        this.loaded = true
        return false
      }
    },
  },
})
