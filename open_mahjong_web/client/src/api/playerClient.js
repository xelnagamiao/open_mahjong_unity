import axios from 'axios'

const TOKEN_KEY = 'player_token'

export function getPlayerToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function setPlayerToken(token) {
  if (token) {
    localStorage.setItem(TOKEN_KEY, token)
  } else {
    localStorage.removeItem(TOKEN_KEY)
  }
}

const playerApi = axios.create({
  baseURL: '/api/player',
  timeout: 30000,
})

playerApi.interceptors.request.use((config) => {
  const token = getPlayerToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

playerApi.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401) {
      setPlayerToken(null)
      // 同步清掉 pinia 会话，避免 header 仍显示已登录
      import('@/stores/playerAuth').then(({ usePlayerAuthStore }) => {
        try {
          const auth = usePlayerAuthStore()
          if (auth.token || auth.userId != null) {
            auth.logout()
          }
        } catch {
          /* pinia 未就绪时忽略 */
        }
      })
    }
    return Promise.reject(err)
  }
)

export default playerApi
