import { isMobile } from 'pixi.js'

// ── Geometry ──────────────────────────────────────────────────────────
export const TILE_WIDTH = 360
export const TILE_HEIGHT = 480
export const TILE_RADIUS = 40
export const LINE_WIDTH = 15
export const WAIT_SEP = 60
export const TILE_SEP = 80
export const SCALE_FACTOR = 8070
export const ANIMATION_TIME = 110
export const FRONT_COLOR = 0xf7f7f0
export const BACK_COLOR = 0xf6bc1e
export const BORDER_COLOR = 0x606060
export const WINDOW_SCALE = 0.95
export const DUANG_CUTOFF = 32
export const MELD_OPT_SCALE = 1.23
export const SELF_HAND_SCALE = 1.23
export const SUIT_HONOR = 0b10100000

// ── Device ────────────────────────────────────────────────────────────
export const IS_MOBILE_PHONE = isMobile.any
export const IS_MOBILE_ANY = isMobile.any

// ── Textures ──────────────────────────────────────────────────────────
const STANDARD_TILE_NAMES: string[] = []
for (let i = 1; i <= 9; i += 1) STANDARD_TILE_NAMES.push(`Man${i}`)
for (let i = 1; i <= 9; i += 1) STANDARD_TILE_NAMES.push(`Pin${i}`)
for (let i = 1; i <= 9; i += 1) STANDARD_TILE_NAMES.push(`Sou${i}`)
for (let i = 1; i <= 7; i += 1) STANDARD_TILE_NAMES.push(`z${i}`)
const TILE_NAMES = [...STANDARD_TILE_NAMES]
for (let i = 1; i <= 8; i += 1) TILE_NAMES.push(`Flower${i}`)

const ASSET_ROOT = import.meta.env.BASE_URL

export const TILE_TEXTURE_PATHS: { alias: string; src: string }[] = [
  { alias: 'regular-Back', src: `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles/Regular/Back.svg` },
  ...TILE_NAMES.map((name) => ({
    alias: `regular-${name}`,
    src: `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles/Regular/${name}.svg`,
  })),
  { alias: 'black-Back', src: `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles/Black/Back.svg` },
  ...STANDARD_TILE_NAMES.map((name) => ({
    alias: `black-${name}`,
    src: `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles/Black/${name}.svg`,
  })),
  ...Array.from({ length: 8 }, (_, index) => ({
    alias: `unity-Flower${index + 1}`,
    src: `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles/Unity/Flower${index + 1}.png`,
  })),
]

/** Map tile id → texture alias (e.g. 0b01000001 → "Man1") */
export function tileIdToAlias(tid: number): string {
  const suit = tid & 0b11100000
  const num = tid & 0b00001111
  if (suit === 0b01000000) return `Man${num}`
  if (suit === 0b01100000) return `Pin${num}`
  if (suit === 0b11000000) return `Sou${num}`
  if (suit === 0b10100000) return `z${num}`
  if (suit === 0b11100000) return `Flower${num}`
  return 'z7'
}

// ── Sound aliases ─────────────────────────────────────────────────────
export const SOUND_FILES = [
  '01-start.wav', '03-cd.wav', '05-draw.wav', '06-discard.wav',
  '08-inquire.wav', '09-cpk.wav', '25-xchg.wav',
] as const

// ── Wind labels ───────────────────────────────────────────────────────
export const WIND_LABELS = ['東', '南', '西', '北'] as const

// ── Tile sort helper ──────────────────────────────────────────────────
export function tileSortKey(tid: number): number {
  return tid + ((tid & 0b11100000) === SUIT_HONOR ? 1000 : 0)
}
