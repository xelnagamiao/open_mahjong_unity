import { Assets, Texture } from 'pixi.js'
import { isFlowerFaceId, tileIdToAlias, TILE_TEXTURE_PATHS } from './constants'
import type { FlowerFaceTheme, TileFaceTheme } from '../../lib/sceneAppearance'
import { tileFaceAssetVariant } from '../../lib/tileFaceAsset'
import { shouldShowTileLabels, tileFaceLabel, tileFaceLabelColor, type TileLabelMode } from '../../lib/tileLabels'
import { locale } from '../../../i18n'

let loaded = false
let loadPromise: Promise<void> | null = null
let tileFaceTheme: TileFaceTheme = 'regular'
let flowerFaceTheme: FlowerFaceTheme = 'unity'
let tileLabelMode: TileLabelMode = 'auto'

export function setTileThemes(tileTheme: TileFaceTheme, flowerTheme: FlowerFaceTheme, labelMode: TileLabelMode): void {
  tileFaceTheme = tileTheme
  flowerFaceTheme = flowerTheme
  tileLabelMode = labelMode
}

export function getTileFaceLabel(tid: number): string {
  return shouldShowTileLabels(tileLabelMode, locale.value)
    ? tileFaceLabel(Number(tileIdToAlias(tid)))
    : ''
}

export function getTileFaceLabelColor(tid: number): string {
  return tileFaceLabelColor(Number(tileIdToAlias(tid)))
}

export function isBlackTileFaceTheme(): boolean {
  return tileFaceTheme === 'black'
}

export function isUnityFlowerFace(tid: number): boolean {
  return flowerFaceTheme === 'unity' && isFlowerFaceId(Number(tileIdToAlias(tid)))
}

function faceVariant(faceId: number) {
  return tileFaceAssetVariant(faceId, {
    black: tileFaceTheme === 'black', guobiao: tileFaceTheme === 'guobiao', unityFlower: flowerFaceTheme === 'unity',
  })
}

export function isGuobiaoTileFace(tid: number): boolean {
  return faceVariant(Number(tileIdToAlias(tid))) === 'guobiao'
}

/** Ensure all tile textures are loaded. Safe to call multiple times. */
export function ensureTexturesLoaded(): Promise<void> {
  if (loaded) return Promise.resolve()
  if (loadPromise) return loadPromise
  loadPromise = Assets.load(TILE_TEXTURE_PATHS).then(() => { loaded = true })
  return loadPromise
}

/** Get a cached tile texture by numeric tile id. */
export function getTexture(tid: number): Texture {
  const faceId = tid > 0 ? Number(tileIdToAlias(tid)) : 0
  const alias = faceId || 'Back'
  const texture = Assets.get(`${faceVariant(faceId)}-${alias}`) as Texture | undefined
  if (texture) return texture
  const fallback = Assets.get(`regular-${alias}`) as Texture | undefined
  return fallback ?? Texture.WHITE
}
