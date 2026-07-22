import { Assets, Texture } from 'pixi.js'
import { tileIdToAlias, TILE_TEXTURE_PATHS } from './constants'
import type { FlowerFaceTheme, TileFaceTheme } from '../../lib/sceneAppearance'

let loaded = false
let loadPromise: Promise<void> | null = null
let tileFaceTheme: TileFaceTheme = 'regular'
let flowerFaceTheme: FlowerFaceTheme = 'flat'

export function setTileThemes(tileTheme: TileFaceTheme, flowerTheme: FlowerFaceTheme): void {
  tileFaceTheme = tileTheme
  flowerFaceTheme = flowerTheme
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
  const theme = tileFaceTheme
  if (tid <= 0) {
    return (Assets.get(`${theme}-Back`) as Texture | undefined) ?? Texture.WHITE
  }
  const alias = tileIdToAlias(tid)
  const isFlower = alias.startsWith('Flower')
  if (isFlower && flowerFaceTheme === 'unity') {
    return (Assets.get(`unity-${alias}`) as Texture | undefined)
      ?? (Assets.get(`regular-${alias}`) as Texture | undefined)
      ?? Texture.WHITE
  }
  const resolvedTheme = theme === 'black' && !isFlower ? 'black' : 'regular'
  const texture = Assets.get(`${resolvedTheme}-${alias}`) as Texture | undefined
  if (texture) return texture
  const fallback = Assets.get(`regular-${alias}`) as Texture | undefined
  return fallback ?? Texture.WHITE
}
