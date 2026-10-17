/**
 * 2D 牌面按国标数字 ID 取图：11-19万 21-29饼 31-39条 41-47字 51-58花。
 * 文件名即 ID（46.svg=白，47.svg=发），不再使用 Man/z/Zhong 等别名。
 */

export const STANDARD_FACE_IDS: number[] = [
  ...Array.from({ length: 9 }, (_, i) => 11 + i),
  ...Array.from({ length: 9 }, (_, i) => 21 + i),
  ...Array.from({ length: 9 }, (_, i) => 31 + i),
  ...Array.from({ length: 7 }, (_, i) => 41 + i),
  ...Array.from({ length: 8 }, (_, i) => 51 + i),
  59, 105, 205, 305, 61,62,63,64,71,72,73,74,81,
]

export function isFlowerFaceId(id: number): boolean {
  return id >= 51 && id <= 58
}

/** Existing Unity/Snow table artwork, including the three red fives. */
export const GUOBIAO_FACE_IDS = STANDARD_FACE_IDS.filter(id =>
  (id >= 11 && id <= 47) || isFlowerFaceId(id) || [105, 205, 305].includes(id))

// The table pack already contains the calibrated suit layouts and optical margins.
// Match its om-table-image-scale-v1 metadata instead of padding the hand pack again.
export const GUOBIAO_FACE_SCALE = 0.93
const GUOBIAO_FACE_ROOT = 'game2d-assets/textures/guobiao-mahjong-tiles/table'

/** DOM faces share the Pixi display area; other packs keep their existing scale. */
export function tileFaceDisplayScale(src: string, fallbackScale: number): number {
  return src.includes(`${GUOBIAO_FACE_ROOT}/`) ? GUOBIAO_FACE_SCALE : fallbackScale
}

type FaceStyleOptions = { black?: boolean, guobiao?: boolean, unityFlower?: boolean }

/** Shared by DOM images and Pixi so every view selects the same artwork. */
export function tileFaceAssetVariant(faceId: number, options: FaceStyleOptions): 'guobiao' | 'unity' | 'black' | 'regular' {
  const flower = isFlowerFaceId(faceId)
  if (options.guobiao && GUOBIAO_FACE_IDS.includes(faceId) && (!flower || options.unityFlower)) return 'guobiao'
  if (flower && options.unityFlower) return 'unity'
  if (options.black && !flower && ![61, 62, 63, 64, 71, 72, 73, 74, 81].includes(faceId)) return 'black'
  return 'regular'
}

/** 牌谱/结算用的服务端 ID。赤宝 105/205/305 保留原值。 */
export function salasasaFaceId(tile: number): number {
  const value = Number(tile)
  if (!value || value < 0) return 0
  if (value === 105 || value === 205 || value === 305) return value
  return value >= 100 ? value % 100 : value
}

/** Pixi 桌面用的 MMCR ID → 同一套数字文件。 */
export function mmcrFaceId(tid: number): number {
  const value = Number(tid)
  if (!value) return 0
  if ([61,62,63,64,71,72,73,74,81].includes(value - 0x100)) return value - 0x100
  const suit = value & 0xe0
  const rank = value & 0x0f
  if (suit === 0x40) return 10 + rank
  if (suit === 0x60) return 20 + rank
  if (suit === 0xc0) return 30 + rank
  if (suit === 0xa0) return 40 + rank
  if (suit === 0xe0) return 50 + rank
  return 0
}

export function tileFaceAssetUrl(
  faceId: number,
  options: { baseUrl: string } & FaceStyleOptions,
): string {
  const variant = tileFaceAssetVariant(faceId, options)
  if (variant === 'guobiao') return `${options.baseUrl}${GUOBIAO_FACE_ROOT}/${faceId}.png`
  const root = `${options.baseUrl}game2d-assets/textures/riichi-mahjong-tiles`
  const folder = variant === 'unity' ? 'Unity' : variant === 'black' ? 'Black' : 'Regular'
  return `${root}/${folder}/${faceId || 'Back'}.svg`
}
