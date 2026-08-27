import { isMobile } from 'pixi.js'
import { STANDARD_FACE_IDS, isFlowerFaceId, mmcrFaceId } from '../../lib/tileFaceAsset'

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
const ASSET_ROOT = import.meta.env.BASE_URL
const TILE_ROOT = `${ASSET_ROOT}game2d-assets/textures/riichi-mahjong-tiles`
const FLOWER_IDS = STANDARD_FACE_IDS.filter(isFlowerFaceId)
const TABLE_FACE_IDS = STANDARD_FACE_IDS.filter((id) => !isFlowerFaceId(id))

export const TILE_TEXTURE_PATHS: { alias: string; src: string }[] = [
  { alias: 'regular-Back', src: `${TILE_ROOT}/Regular/Back.svg` },
  { alias: 'black-Back', src: `${TILE_ROOT}/Black/Back.svg` },
  ...STANDARD_FACE_IDS.map((id) => ({
    alias: `regular-${id}`,
    src: `${TILE_ROOT}/Regular/${id}.svg`,
  })),
  ...TABLE_FACE_IDS.map((id) => ({
    alias: `black-${id}`,
    src: `${TILE_ROOT}/Black/${id}.svg`,
  })),
  ...FLOWER_IDS.map((id) => ({
    alias: `unity-${id}`,
    src: `${TILE_ROOT}/Unity/${id}.svg`,
  })),
]

/** MMCR tile id → 国标数字文件名（11.svg / 46.svg …）。 */
export function tileIdToAlias(tid: number): string {
  return String(mmcrFaceId(tid))
}

export { isFlowerFaceId }

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
