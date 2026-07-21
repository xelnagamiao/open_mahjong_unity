import { Assets, Texture } from 'pixi.js'
import { tileIdToAlias, TILE_TEXTURE_PATHS } from './constants'

let loaded = false
let loadPromise: Promise<void> | null = null

/** Ensure all tile textures are loaded. Safe to call multiple times. */
export function ensureTexturesLoaded(): Promise<void> {
  if (loaded) return Promise.resolve()
  if (loadPromise) return loadPromise
  loadPromise = Assets.load(TILE_TEXTURE_PATHS).then(() => { loaded = true })
  return loadPromise
}

/** Get a cached tile texture by numeric tile id. */
export function getTexture(tid: number): Texture {
  if (tid <= 0) {
    return (Assets.get('Back') as Texture | undefined) ?? Texture.WHITE
  }
  const alias = tileIdToAlias(tid)
  const texture = Assets.get(alias) as Texture | undefined
  if (texture) return texture
  // Fallback: try with path
  const fallback = Assets.get(`${import.meta.env.BASE_URL}textures/riichi-mahjong-tiles/Regular/${alias}.svg`) as Texture | undefined
  return fallback ?? Texture.WHITE
}
