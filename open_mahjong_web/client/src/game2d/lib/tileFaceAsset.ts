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
  105, 205, 305,
]

export function isFlowerFaceId(id: number): boolean {
  return id >= 51 && id <= 58
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
  options: { baseUrl: string, black?: boolean, unityFlower?: boolean },
): string {
  const root = `${options.baseUrl}game2d-assets/textures/riichi-mahjong-tiles`
  if (!faceId) {
    const folder = options.black ? 'Black' : 'Regular'
    return `${root}/${folder}/Back.svg`
  }
  if (isFlowerFaceId(faceId) && options.unityFlower) {
    return `${root}/Unity/${faceId}.svg`
  }
  const folder = options.black && !isFlowerFaceId(faceId) ? 'Black' : 'Regular'
  return `${root}/${folder}/${faceId}.svg`
}
