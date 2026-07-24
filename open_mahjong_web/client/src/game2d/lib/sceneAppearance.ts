export type FlowerAreaDisplay = 'always' | 'when-present' | 'never'
export type TileFaceTheme = 'regular' | 'black'
export type FlowerFaceTheme = 'flat' | 'unity'
/** Unity MoqieShortcutMode: 0 双击 1 右键 2 无 */
export type MoqieShortcutMode = 0 | 1 | 2
/** Unity AskOtherPassShortcutMode: 0 右键 1 双击 2 无 */
export type PassShortcutMode = 0 | 1 | 2
/** 牌背覆盖色轮换 */
export type TileCoverRotateMode = 'cycle' | 'random' | 'random-no-repeat'

export type SceneAppearanceSettings = {
  backgroundColorTable: string
  backgroundColorOutside: string
  backgroundImageEnabled: boolean
  backgroundImageAlpha: number
  tileCoverColors: string[]
  tileCoverRotateMode: TileCoverRotateMode
  /** Persisted so random-no-repeat can avoid the previous round's color across reloads. */
  lastTileCoverIndex: number
  flowerAreaDisplay: FlowerAreaDisplay
  flowerAreaColor: string
  flowerAreaAlpha: number
  tileFaceTheme: TileFaceTheme
  flowerFaceTheme: FlowerFaceTheme
  moqieShortcutMode: MoqieShortcutMode
  passShortcutMode: PassShortcutMode
}

const HEX_COLOR_PATTERN = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i
export const MAX_TILE_COVER_COLORS = 8

export const DEFAULT_SCENE_APPEARANCE: SceneAppearanceSettings = {
  backgroundColorTable: '#999999',
  backgroundColorOutside: '#666666',
  backgroundImageEnabled: false,
  backgroundImageAlpha: 0.35,
  tileCoverColors: ['#f6bc1e'],
  tileCoverRotateMode: 'cycle',
  lastTileCoverIndex: 0,
  flowerAreaDisplay: 'always',
  flowerAreaColor: '#f7f7f0',
  flowerAreaAlpha: 0.82,
  tileFaceTheme: 'regular',
  flowerFaceTheme: 'unity',
  moqieShortcutMode: 1,
  passShortcutMode: 0,
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
    .slice(0, MAX_TILE_COVER_COLORS)

  return normalized.length > 0 ? normalized : [...DEFAULT_SCENE_APPEARANCE.tileCoverColors]
}

function normalizeRotateMode(value: unknown): TileCoverRotateMode {
  if (value === 'random' || value === 'random-no-repeat') return value
  return 'cycle'
}

function normalizeShortcutMode(value: unknown, fallback: 0 | 1 | 2): 0 | 1 | 2 {
  const parsed = Number(value)
  if (parsed === 0 || parsed === 1 || parsed === 2) return parsed
  return fallback
}

export function normalizeSceneAppearanceSettings(
  value: Partial<SceneAppearanceSettings> | null | undefined,
): SceneAppearanceSettings {
  const tileCoverColors = normalizeTileCoverColors(value?.tileCoverColors)
  const lastRaw = Number(value?.lastTileCoverIndex ?? 0)
  const lastTileCoverIndex = Number.isInteger(lastRaw)
    ? Math.max(0, Math.min(tileCoverColors.length - 1, lastRaw))
    : 0
  return {
    backgroundColorTable: normalizeHexColor(value?.backgroundColorTable, DEFAULT_SCENE_APPEARANCE.backgroundColorTable),
    backgroundColorOutside: normalizeHexColor(value?.backgroundColorOutside, DEFAULT_SCENE_APPEARANCE.backgroundColorOutside),
    backgroundImageEnabled: normalizeBoolean(value?.backgroundImageEnabled, DEFAULT_SCENE_APPEARANCE.backgroundImageEnabled),
    backgroundImageAlpha: clampUnitInterval(value?.backgroundImageAlpha, DEFAULT_SCENE_APPEARANCE.backgroundImageAlpha),
    tileCoverColors,
    tileCoverRotateMode: normalizeRotateMode(value?.tileCoverRotateMode),
    lastTileCoverIndex,
    flowerAreaDisplay: value?.flowerAreaDisplay === 'when-present' || value?.flowerAreaDisplay === 'never'
      ? value.flowerAreaDisplay
      : 'always',
    flowerAreaColor: normalizeHexColor(value?.flowerAreaColor, DEFAULT_SCENE_APPEARANCE.flowerAreaColor),
    flowerAreaAlpha: clampUnitInterval(value?.flowerAreaAlpha, DEFAULT_SCENE_APPEARANCE.flowerAreaAlpha),
    tileFaceTheme: value?.tileFaceTheme === 'black' ? 'black' : 'regular',
    flowerFaceTheme: value?.flowerFaceTheme === 'flat' ? 'flat' : 'unity',
    moqieShortcutMode: normalizeShortcutMode(value?.moqieShortcutMode, DEFAULT_SCENE_APPEARANCE.moqieShortcutMode),
    passShortcutMode: normalizeShortcutMode(value?.passShortcutMode, DEFAULT_SCENE_APPEARANCE.passShortcutMode),
  }
}

/**
 * Pick the tile-cover palette index for a round.
 * Returns both the color and the chosen index (for persisting lastTileCoverIndex).
 */
export function pickTileCoverColor(
  appearance: SceneAppearanceSettings,
  roundCounter: number,
): { color: string; index: number } {
  const palette = appearance.tileCoverColors.length
    ? appearance.tileCoverColors
    : DEFAULT_SCENE_APPEARANCE.tileCoverColors
  if (palette.length === 1) {
    return { color: palette[0], index: 0 }
  }

  if (appearance.tileCoverRotateMode === 'cycle') {
    const index = Math.max(roundCounter - 1, 0) % palette.length
    return { color: palette[index], index }
  }

  if (appearance.tileCoverRotateMode === 'random-no-repeat') {
    const previous = Math.max(0, Math.min(palette.length - 1, appearance.lastTileCoverIndex))
    let index = Math.floor(Math.random() * palette.length)
    if (palette.length > 1) {
      let guard = 0
      while (index === previous && guard < 8) {
        index = Math.floor(Math.random() * palette.length)
        guard += 1
      }
      if (index === previous) index = (previous + 1) % palette.length
    }
    return { color: palette[index], index }
  }

  // random
  const index = Math.floor(Math.random() * palette.length)
  return { color: palette[index], index }
}

export function hexColorToNumber(color: string): number {
  return Number.parseInt(color.slice(1), 16)
}
