import {
  DEFAULT_SCENE_APPEARANCE,
  normalizeSceneAppearanceSettings,
  type SceneAppearanceSettings,
} from './sceneAppearance'

const SCENE_APPEARANCE_STORAGE_KEY = 'mmcr14.sceneAppearance'
const SCENE_VOLUME_STORAGE_KEY = 'mmcr14.sceneVolume'

export function loadStoredVolume(): number {
  const raw = localStorage.getItem(SCENE_VOLUME_STORAGE_KEY)
  if (!raw) return 0.5
  const value = Number(raw)
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0.5
}

export function saveStoredVolume(volume: number): void {
  localStorage.setItem(SCENE_VOLUME_STORAGE_KEY, String(Math.max(0, Math.min(1, volume))))
}

export function loadStoredSceneAppearance(): SceneAppearanceSettings {
  const raw = localStorage.getItem(SCENE_APPEARANCE_STORAGE_KEY)
  if (!raw) return DEFAULT_SCENE_APPEARANCE
  try {
    const stored = JSON.parse(raw) as Partial<SceneAppearanceSettings>
    // Migrate the former untouched flower-area default while preserving
    // players who had already chosen a custom background.
    if (
      stored.flowerAreaLabelColor === undefined
      && stored.flowerAreaLabelScale === undefined
      && stored.flowerAreaColor?.toLowerCase() === '#f7f7f0'
      && Number(stored.flowerAreaAlpha) === 0.82
    ) {
      stored.flowerAreaColor = DEFAULT_SCENE_APPEARANCE.flowerAreaColor
      stored.flowerAreaAlpha = DEFAULT_SCENE_APPEARANCE.flowerAreaAlpha
    }
    return normalizeSceneAppearanceSettings(stored)
  } catch {
    localStorage.removeItem(SCENE_APPEARANCE_STORAGE_KEY)
    return DEFAULT_SCENE_APPEARANCE
  }
}

export function saveStoredSceneAppearance(appearance: SceneAppearanceSettings): void {
  localStorage.setItem(
    SCENE_APPEARANCE_STORAGE_KEY,
    JSON.stringify(normalizeSceneAppearanceSettings(appearance)),
  )
}

export function resetStoredSceneAppearance(): void {
  localStorage.removeItem(SCENE_APPEARANCE_STORAGE_KEY)
}
