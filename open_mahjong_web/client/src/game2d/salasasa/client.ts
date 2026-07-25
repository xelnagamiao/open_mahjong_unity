import type {
  ConnectionStatus,
  SalasasaLoginInfo,
  SalasasaRankData,
  SalasasaResponse,
  StoredCredentials,
} from './types'

type MessageListener = (message: SalasasaResponse) => void
type StateListener = () => void

function makeConnectionId(): string {
  return crypto.randomUUID()
}

function buildSocketUrl(connectionId: string): string {
  const origin = window.location.origin.replace(/^http/, 'ws')
  return new URL(`/2d/ws/${connectionId}`, origin).toString()
}

class SalasasaClient {
  private socket: WebSocket | null = null
  private statusValue: ConnectionStatus = 'idle'
  private loginValue: SalasasaLoginInfo | null = null
  private rankValue: SalasasaRankData | null = null
  private lastGameStartValue: SalasasaResponse | null = null
  /**
   * Ordered guobiao messages since the latest game_start, kept only until the
   * Game page drains them. Survives Lobby→Game navigation where Lobby
   * unsubscribes before Game subscribes (first hand ask is otherwise lost when
   * nobody needs opening flowers).
   */
  private guobiaoBuffer: SalasasaResponse[] = []
  private guobiaoBufferActive = false
  private credentials: StoredCredentials | null = null
  private listeners = new Set<MessageListener>()
  private stateListeners = new Set<StateListener>()
  private connectPromise: Promise<SalasasaLoginInfo> | null = null
  private reconnectTimer: number | null = null
  private heartbeatTimer: number | null = null
  private restoreBlockedToken: string | null = null
  private intentionalClose = false

  get status(): ConnectionStatus { return this.statusValue }
  get loginInfo(): SalasasaLoginInfo | null { return this.loginValue }
  get rankData(): SalasasaRankData | null { return this.rankValue }
  get lastGameStart(): SalasasaResponse | null { return this.lastGameStartValue }
  get isLoggedIn(): boolean { return this.loginValue !== null && this.statusValue === 'online' }

  /**
   * Take the buffered game_start…ask stream for the Game page, then stop
   * buffering until the next game_start (live subscribe owns the rest).
   */
  drainGuobiaoBuffer(): SalasasaResponse[] {
    const buffered = this.guobiaoBuffer
    this.guobiaoBuffer = []
    this.guobiaoBufferActive = false
    return buffered
  }

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

  async restore(preferredToken?: string | null): Promise<SalasasaLoginInfo | null> {
    const token = (preferredToken || '').trim()
    if (!token) return null
    if (token === this.restoreBlockedToken) return null
    try {
      return await this.connectWithToken(token)
    } catch {
      this.logout()
      return null
    }
  }

  connectWithToken(token: string): Promise<SalasasaLoginInfo> {
    const cleanToken = token.trim()
    this.restoreBlockedToken = null
    if (!cleanToken) return Promise.reject(new Error('缺少网站登录凭证'))
    return this.openConnection({ mode: 'token', token: cleanToken })
  }

  connect(username: string, password: string): Promise<SalasasaLoginInfo> {
    const cleanUsername = username.trim()
    if (!cleanUsername || !password) return Promise.reject(new Error('请输入用户名和密码'))
    return this.openConnection({ mode: 'password', username: cleanUsername, password })
  }

  private openConnection(credentials: StoredCredentials): Promise<SalasasaLoginInfo> {
    if (this.connectPromise) return this.connectPromise

    this.intentionalClose = false
    this.credentials = credentials
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
        if (credentials.mode === 'token') {
          socket.send(JSON.stringify({
            type: 'login',
            token: credentials.token,
            is_tourist: false,
          }))
        } else {
          socket.send(JSON.stringify({
            type: 'login',
            username: credentials.username,
            password: credentials.password,
            is_tourist: false,
          }))
        }
      }

      socket.onmessage = (event) => {
        let message: SalasasaResponse
        try { message = JSON.parse(event.data) as SalasasaResponse }
        catch { return }
        const loginKickedOut = message.type === 'message' && message.message === 'login_kickout'

        if (message.type === 'login') {
          window.clearTimeout(timeout)
          if (message.success && message.login_info) {
            this.loginValue = message.login_info
            this.rankValue = message.rank_data ?? null
            this.setStatus('online')
            this.startHeartbeat()
            resolve(message.login_info)
          } else {
            authenticationFailed = true
            this.credentials = null
            reject(new Error(message.message || '登录失败'))
            socket.close()
          }
        }

        if (
          !this.loginValue
          && this.statusValue === 'connecting'
          && message.type !== 'login'
          && message.success === false
          && ['tips', 'error_message', 'message'].includes(message.type)
        ) {
          window.clearTimeout(timeout)
          authenticationFailed = true
          this.credentials = null
          reject(new Error(message.message || '登录失败'))
          socket.close()
        }

        if (message.type === 'message' && message.message === 'reconnect_ask') {
          this.send({ type: 'reconnect_response', reconnect: true })
        }
        if (message.type === 'gamestate/guobiao/game_start' && message.game_info) {
          this.lastGameStartValue = message
          this.guobiaoBuffer = [message]
          this.guobiaoBufferActive = true
        } else if (
          this.guobiaoBufferActive
          && typeof message.type === 'string'
          && message.type.startsWith('gamestate/guobiao/')
          && message.type !== 'gamestate/guobiao/game_end'
        ) {
          this.guobiaoBuffer.push(message)
        }
        if (message.type === 'gamestate/guobiao/game_end') {
          this.lastGameStartValue = null
          this.guobiaoBuffer = []
          this.guobiaoBufferActive = false
        }
        for (const listener of this.listeners) listener(message)
        if (loginKickedOut) {
          // A newer 2D/3D login owns the account now. Keep the website token,
          // but terminate this game connection and suppress automatic reconnect.
          const kickedToken = this.credentials?.mode === 'token' ? this.credentials.token : null
          this.logout()
          this.restoreBlockedToken = kickedToken
        }
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
      const creds = this.credentials
      const retry = creds.mode === 'token'
        ? this.connectWithToken(creds.token)
        : this.connect(creds.username, creds.password)
      void retry.catch(() => {
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
    this.guobiaoBuffer = []
    this.guobiaoBufferActive = false
    this.socket?.close(1000, 'logout')
    this.socket = null
    this.setStatus('idle')
  }
}

export const salasasaClient = new SalasasaClient()
