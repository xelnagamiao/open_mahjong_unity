export type SceneAppearanceSettings = {
  backgroundColorTable: string
  backgroundColorOutside: string
  backgroundImageEnabled: boolean
  backgroundImageAlpha: number
  tileCoverColors: string[]
}

const HEX_COLOR_PATTERN = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i

export const DEFAULT_SCENE_APPEARANCE: SceneAppearanceSettings = {
  backgroundColorTable: '#999999',
  backgroundColorOutside: '#666666',
  backgroundImageEnabled: false,
  backgroundImageAlpha: 0.35,
  tileCoverColors: ['#f6bc1e'],
}

function normalizeHexColor(value: unknown, fallback: string): string {
  if (typeof value !== 'string') {
    return fallback
  }

  const trimmed = value.trim()
  if (!HEX_COLOR_PATTERN.test(trimmed)) {
    return fallback
  }

  if (trimmed.length === 4) {
    return `#${trimmed[1]}${trimmed[1]}${trimmed[2]}${trimmed[2]}${trimmed[3]}${trimmed[3]}`.toLowerCase()
  }

  return trimmed.toLowerCase()
}

function normalizeBoolean(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback
}

function clampUnitInterval(value: unknown, fallback: number): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(parsed)) {
    return fallback
  }
  return Math.min(1, Math.max(0, parsed))
}

function normalizeTileCoverColors(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [...DEFAULT_SCENE_APPEARANCE.tileCoverColors]
  }

  const normalized = value
    .map((entry) => normalizeHexColor(entry, ''))
    .filter((entry) => entry.length > 0)

  return normalized.length > 0 ? normalized : [...DEFAULT_SCENE_APPEARANCE.tileCoverColors]
}

export function normalizeSceneAppearanceSettings(
  value: Partial<SceneAppearanceSettings> | null | undefined,
): SceneAppearanceSettings {
  return {
    backgroundColorTable: normalizeHexColor(value?.backgroundColorTable, DEFAULT_SCENE_APPEARANCE.backgroundColorTable),
    backgroundColorOutside: normalizeHexColor(value?.backgroundColorOutside, DEFAULT_SCENE_APPEARANCE.backgroundColorOutside),
    backgroundImageEnabled: normalizeBoolean(value?.backgroundImageEnabled, DEFAULT_SCENE_APPEARANCE.backgroundImageEnabled),
    backgroundImageAlpha: clampUnitInterval(value?.backgroundImageAlpha, DEFAULT_SCENE_APPEARANCE.backgroundImageAlpha),
    tileCoverColors: normalizeTileCoverColors(value?.tileCoverColors),
  }
}

export function hexColorToNumber(color: string): number {
  return Number.parseInt(color.slice(1), 16)
}