import { defineStore } from 'pinia'
import playerApi, { getPlayerToken, setPlayerToken } from '@/api/playerClient'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import { setEventAdminToken } from '@/api/eventAdminClient'
import { useGame2dSessionStore } from '@/stores/game2dSession'

function applyEventAdminSession(data) {
  const eventAuth = useEventAdminAuthStore()
  if (data?.event_admin_token) {
    setEventAdminToken(data.event_admin_token)
    eventAuth.userId = Number(data.user_id) || data.user_id
    eventAuth.username = data.username || ''
    eventAuth.events = data.events || []
    eventAuth.loaded = true
    return true
  }
  eventAuth.logout()
  return false
}

export const usePlayerAuthStore = defineStore('playerAuth', {
  state: () => ({
    token: getPlayerToken() || '',
    userId: null,
    username: '',
    email: '',
    emailVerified: false,
    isEventAdmin: false,
    loaded: false,
  }),
  getters: {
    isLoggedIn: (s) => !!s.token && s.userId != null && s.userId !== '',
  },
  actions: {
    _setSession({ token, userId, username, email, emailVerified, isEventAdmin = false }) {
      if (token) {
        setPlayerToken(token)
        this.token = token
      }
      if (userId != null) this.userId = Number(userId) || userId
      if (username != null) this.username = username
      if (email !== undefined) this.email = email || ''
      if (emailVerified !== undefined) this.emailVerified = !!emailVerified
      this.isEventAdmin = !!isEventAdmin
      this.loaded = true
    },
    async login(username, password) {
      const res = await playerApi.post('/auth/login', { username, password })
      const data = res.data.data
      this._setSession({
        token: data.token,
        userId: data.user_id,
        username: data.username,
        email: data.email,
        emailVerified: data.email_verified,
        isEventAdmin: !!data.is_event_admin,
      })
      applyEventAdminSession(data)
    },
    logout() {
      setPlayerToken(null)
      this.token = ''
      this.userId = null
      this.username = ''
      this.email = ''
      this.emailVerified = false
      this.isEventAdmin = false
      this.loaded = true
      try {
        useEventAdminAuthStore().logout()
      } catch {
        /* ignore */
      }
      try {
        useGame2dSessionStore().logout()
      } catch {
        /* ignore */
      }
    },
    async fetchMe() {
      if (!getPlayerToken()) {
        this.token = ''
        this.userId = null
        this.username = ''
        this.email = ''
        this.emailVerified = false
        this.isEventAdmin = false
        this.loaded = true
        try {
          useEventAdminAuthStore().logout()
        } catch {
          /* ignore */
        }
        return false
      }
      this.token = getPlayerToken() || ''
      try {
        const res = await playerApi.get('/auth/me')
        const data = res.data.data
        this._setSession({
          token: this.token,
          userId: data.user_id,
          username: data.username,
          email: data.email,
          emailVerified: data.email_verified,
          isEventAdmin: !!data.is_event_admin,
        })
        applyEventAdminSession({ ...data, user_id: data.user_id })
        return true
      } catch (e) {
        if (e.response?.status === 401) {
          this.logout()
          return false
        }
        this.loaded = true
        return !!this.token
      }
    },
    async changePassword(oldPassword, newPassword) {
      await playerApi.post('/auth/change-password', {
        old_password: oldPassword,
        new_password: newPassword,
      })
    },
  },
})
