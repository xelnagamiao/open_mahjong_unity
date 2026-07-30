const API_PREFIX = '/2d/api'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

function buildUrl(path: string): string {
  const normalized = path.startsWith('/') ? path : `/${path}`
  return new URL(`${API_PREFIX}${normalized}`, window.location.origin).toString()
}

export async function publicApiGet<T>(path: string): Promise<T> {
  const response = await fetch(buildUrl(path), {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
  })
  const payload = await response.json().catch(() => null) as {
    success?: boolean
    data?: T
    message?: string
  } | null
  if (!response.ok || !payload?.success) {
    throw new ApiError(response.status, payload?.message || '请求失败')
  }
  return payload.data as T
}

export function playerProfileUrl(key: string | number): string {
  return `/player/info/${encodeURIComponent(String(key))}`
}

export function leaderboardUrl(limit = 20): string {
  return `/player/leaderboard?limit=${Math.max(1, Math.min(20, limit))}`
}

/** Requests that belong to the 2D client but require the shared website login. */
export async function game2dPlayerApi<T>(path: string, init: RequestInit = {}): Promise<T> {
  const { getPlayerToken } = await import('@/api/playerClient')
  const token = getPlayerToken()
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const response = await fetch(buildUrl(`/player${path.startsWith('/') ? path : `/${path}`}`), {
    ...init,
    headers,
    credentials: 'same-origin',
  })
  const payload = await response.json().catch(() => null) as { success?: boolean, data?: T, message?: string } | null
  if (!response.ok || !payload?.success) throw new ApiError(response.status, payload?.message || '请求失败')
  return payload.data as T
}

export function queueStatusUrl(): string {
  return '/platform/queue-status'
}

export function publicRecordUrl(gameId: string): string {
  return `/platform/record/${encodeURIComponent(gameId)}`
}
