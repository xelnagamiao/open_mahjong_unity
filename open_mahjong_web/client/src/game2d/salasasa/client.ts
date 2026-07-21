import type {
  ConnectionStatus,
  SalasasaLoginInfo,
  SalasasaRankData,
  SalasasaResponse,
  StoredCredentials,
} from './types'

const CREDENTIALS_KEY = 'salasasa.2d.credentials'

type MessageListener = (message: SalasasaResponse) => void
type StateListener = () => void

function makeConnectionId(): string {
  return crypto.randomUUID()
}

function buildSocketUrl(connectionId: string): string {
  const origin = window.location.origin.replace(/^http/, 'ws')
  return new URL(`/2d/ws/${connectionId}`, origin).toString()
}

function loadCredentials(): StoredCredentials | null {
  const raw = sessionStorage.getItem(CREDENTIALS_KEY)
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw) as StoredCredentials
    return parsed.username && parsed.password ? parsed : null
  } catch {
    sessionStorage.removeItem(CREDENTIALS_KEY)
    return null
  }
}

class SalasasaClient {
  private socket: WebSocket | null = null
  private statusValue: ConnectionStatus = 'idle'
  private loginValue: SalasasaLoginInfo | null = null
  private rankValue: SalasasaRankData | null = null
  private lastGameStartValue: SalasasaResponse | null = null
  private credentials: StoredCredentials | null = loadCredentials()
  private listeners = new Set<MessageListener>()
  private stateListeners = new Set<StateListener>()
  private connectPromise: Promise<SalasasaLoginInfo> | null = null
  private reconnectTimer: number | null = null
  private heartbeatTimer: number | null = null
  private intentionalClose = false

  get status(): ConnectionStatus { return this.statusValue }
  get loginInfo(): SalasasaLoginInfo | null { return this.loginValue }
  get rankData(): SalasasaRankData | null { return this.rankValue }
  get lastGameStart(): SalasasaResponse | null { return this.lastGameStartValue }
  get isLoggedIn(): boolean { return this.loginValue !== null && this.statusValue === 'online' }

  subscribe(listener: MessageListener): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  subscribeState(listener: StateListener): () => void {
    this.stateListeners.add(listener)
    return () => this.stateListeners.delete(listener)
  }

  private emitState(): void {
    for (const listener of this.stateListeners) listener()
  }

  private setStatus(status: ConnectionStatus): void {
    this.statusValue = status
    this.emitState()
  }

  async restore(): Promise<SalasasaLoginInfo | null> {
    if (!this.credentials) return null
    try {
      return await this.connect(this.credentials.username, this.credentials.password)
    } catch {
      this.logout()
      return null
    }
  }

  connect(username: string, password: string): Promise<SalasasaLoginInfo> {
    const cleanUsername = username.trim()
    if (!cleanUsername || !password) return Promise.reject(new Error('请输入用户名和密码'))
    if (this.connectPromise) return this.connectPromise

    this.intentionalClose = false
    this.credentials = { username: cleanUsername, password }
    this.setStatus('connecting')

    this.connectPromise = new Promise<SalasasaLoginInfo>((resolve, reject) => {
      const socket = new WebSocket(buildSocketUrl(makeConnectionId()))
      let authenticationFailed = false
      this.socket = socket
      const timeout = window.setTimeout(() => {
        reject(new Error('连接服务器超时'))
        socket.close()
      }, 12_000)

      socket.onopen = () => {
        socket.send(JSON.stringify({
          type: 'login',
          username: cleanUsername,
          password,
          is_tourist: false,
        }))
      }

      socket.onmessage = (event) => {
        let message: SalasasaResponse
        try { message = JSON.parse(event.data) as SalasasaResponse }
        catch { return }

        if (message.type === 'login') {
          window.clearTimeout(timeout)
          if (message.success && message.login_info) {
            this.loginValue = message.login_info
            this.rankValue = message.rank_data ?? null
            sessionStorage.setItem(CREDENTIALS_KEY, JSON.stringify(this.credentials))
            this.setStatus('online')
            this.startHeartbeat()
            resolve(message.login_info)
          } else {
            authenticationFailed = true
            this.credentials = null
            sessionStorage.removeItem(CREDENTIALS_KEY)
            reject(new Error(message.message || '登录失败'))
            socket.close()
          }
        }

        if (message.type === 'message' && message.message === 'reconnect_ask') {
          this.send({ type: 'reconnect_response', reconnect: true })
        }
        if (message.type === 'gamestate/guobiao/game_start' && message.game_info) {
          this.lastGameStartValue = message
        }
        if (message.type === 'gamestate/guobiao/game_end') {
          this.lastGameStartValue = null
        }
        for (const listener of this.listeners) listener(message)
      }

      socket.onerror = () => {
        window.clearTimeout(timeout)
        reject(new Error('无法连接游戏服务器'))
      }

      socket.onclose = () => {
        window.clearTimeout(timeout)
        this.stopHeartbeat()
        if (this.socket === socket) this.socket = null
        this.connectPromise = null
        if (!authenticationFailed && !this.intentionalClose && this.credentials) {
          this.loginValue = null
          this.setStatus('offline')
          this.scheduleReconnect()
        } else {
          this.setStatus('idle')
        }
      }
    }).finally(() => {
      this.connectPromise = null
    })

    return this.connectPromise
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer !== null || !this.credentials) return
    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = null
      if (!this.credentials || this.intentionalClose) return
      void this.connect(this.credentials.username, this.credentials.password).catch(() => {
        this.scheduleReconnect()
      })
    }, 2_000)
  }

  private startHeartbeat(): void {
    this.stopHeartbeat()
    this.heartbeatTimer = window.setInterval(() => {
      this.send({ type: 'ping', client_ts: Date.now() })
    }, 5_000)
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer !== null) window.clearInterval(this.heartbeatTimer)
    this.heartbeatTimer = null
  }

  send(message: Record<string, unknown>): boolean {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) return false
    this.socket.send(JSON.stringify(message))
    return true
  }

  logout(): void {
    this.intentionalClose = true
    if (this.reconnectTimer !== null) window.clearTimeout(this.reconnectTimer)
    this.reconnectTimer = null
    this.stopHeartbeat()
    this.credentials = null
    this.loginValue = null
    this.rankValue = null
    this.lastGameStartValue = null
    sessionStorage.removeItem(CREDENTIALS_KEY)
    this.socket?.close(1000, 'logout')
    this.socket = null
    this.setStatus('idle')
  }
}

export const salasasaClient = new SalasasaClient()
