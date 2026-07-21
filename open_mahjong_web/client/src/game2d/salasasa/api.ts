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
