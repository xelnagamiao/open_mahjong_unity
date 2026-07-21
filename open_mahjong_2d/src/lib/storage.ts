import type { AuthSession } from './types'
import {
  DEFAULT_SCENE_APPEARANCE,
  normalizeSceneAppearanceSettings,
  type SceneAppearanceSettings,
} from './sceneAppearance'

const AUTH_STORAGE_KEY = 'mmcr14.auth'
const SESSION_STORAGE_KEY = 'mmcr14.sessionId'
const SCENE_APPEARANCE_STORAGE_KEY = 'mmcr14.sceneAppearance'
const SCENE_VOLUME_STORAGE_KEY = 'mmcr14.sceneVolume'

export function loadStoredVolume(): number {
  const raw = localStorage.getItem(SCENE_VOLUME_STORAGE_KEY)
  if (!raw) return 0.5
  const v = Number(raw)
  return Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0.5
}

export function saveStoredVolume(volume: number) {
  localStorage.setItem(SCENE_VOLUME_STORAGE_KEY, String(Math.max(0, Math.min(1, volume))))
}

export function loadStoredAuth(): AuthSession | null {
  const raw = localStorage.getItem(AUTH_STORAGE_KEY)
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw) as AuthSession
  } catch {
    localStorage.removeItem(AUTH_STORAGE_KEY)
    return null
  }
}

export function saveStoredAuth(session: AuthSession) {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session))
}

export function clearStoredAuth() {
  localStorage.removeItem(AUTH_STORAGE_KEY)
}

export function loadStoredSessionId(): number | null {
  const raw = localStorage.getItem(SESSION_STORAGE_KEY)
  if (!raw) {
    return null
  }
  const parsed = Number(raw)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

export function saveStoredSessionId(sessionId: number) {
  localStorage.setItem(SESSION_STORAGE_KEY, String(sessionId))
}

export function clearStoredSessionId() {
  localStorage.removeItem(SESSION_STORAGE_KEY)
}

export function loadStoredSceneAppearance(): SceneAppearanceSettings {
  const raw = localStorage.getItem(SCENE_APPEARANCE_STORAGE_KEY)
  if (!raw) {
    return DEFAULT_SCENE_APPEARANCE
  }

  try {
    return normalizeSceneAppearanceSettings(JSON.parse(raw) as Partial<SceneAppearanceSettings>)
  } catch {
    localStorage.removeItem(SCENE_APPEARANCE_STORAGE_KEY)
    return DEFAULT_SCENE_APPEARANCE
  }
}

export function saveStoredSceneAppearance(settings: SceneAppearanceSettings) {
  localStorage.setItem(
    SCENE_APPEARANCE_STORAGE_KEY,
    JSON.stringify(normalizeSceneAppearanceSettings(settings)),
  )
}

export function resetStoredSceneAppearance() {
  localStorage.removeItem(SCENE_APPEARANCE_STORAGE_KEY)
}