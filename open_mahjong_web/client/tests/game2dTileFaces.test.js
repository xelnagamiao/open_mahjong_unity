import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync, existsSync } from 'node:fs'
import path from 'node:path'
import { Assets, Texture, TextureSource } from 'pixi.js'
import { STANDARD_FACE_IDS, GUOBIAO_FACE_IDS, tileFaceAssetUrl, tileFaceAssetVariant, tileFaceDisplayScale } from '../src/game2d/lib/tileFaceAsset.ts'
import { DEFAULT_SCENE_APPEARANCE, normalizeSceneAppearanceSettings } from '../src/game2d/lib/sceneAppearance.ts'
import { loadStoredSceneAppearance, saveStoredSceneAppearance } from '../src/game2d/lib/storage.ts'
import { TILE_TEXTURE_PATHS, TILE_WIDTH, TILE_HEIGHT } from '../src/game2d/game/scene/constants.ts'
import { setTileThemes } from '../src/game2d/game/scene/textures.ts'
import { Tile } from '../src/game2d/game/scene/Tile.ts'
import { salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

test('face preferences preserve old styles and save the new style across reloads', () => {
  const previous = Object.getOwnPropertyDescriptor(globalThis, 'localStorage')
  const entries = new Map([['mmcr14.flowerFaceImageDefault', '1']])
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
    getItem: key => entries.get(key) ?? null,
    setItem: (key, value) => entries.set(key, value),
    removeItem: key => entries.delete(key),
  } })
  try {
    for (const tileFaceTheme of ['regular', 'black', 'guobiao', 'regular']) {
      const settings = { ...DEFAULT_SCENE_APPEARANCE, tileFaceTheme, tileCoverColors: ['#abcdef'], flowerFaceTheme: 'flat' }
      saveStoredSceneAppearance(settings)
      assert.deepEqual(loadStoredSceneAppearance(), settings)
    }
    assert.equal(normalizeSceneAppearanceSettings({ tileFaceTheme: 'unknown' }).tileFaceTheme, 'regular')
  } finally {
    if (previous) Object.defineProperty(globalThis, 'localStorage', previous)
    else delete globalThis.localStorage
  }
})

test('the Guobiao pack uses the calibrated table canvas and margins for every face', () => {
  assert.equal(new Set(GUOBIAO_FACE_IDS).size, 45)
  assert.ok([46, 47, 51, 58, 105, 205, 305].every(id => GUOBIAO_FACE_IDS.includes(id)))
  for (const id of GUOBIAO_FACE_IDS) {
    const bytes = readFileSync(path.join(process.cwd(), 'public', tileFaceAssetUrl(id, { baseUrl: '', guobiao: true, unityFlower: true })))
    assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', `${id}: PNG signature`)
    assert.equal(bytes.readUInt32BE(16), 400, `${id}: calibrated table width`)
    assert.equal(bytes.readUInt32BE(20), 532, `${id}: calibrated table height`)
    assert.ok(bytes.includes(Buffer.from('om-table-image-scale-v1\x000.93')), `${id}: calibrated margin metadata`)
  }
})

test('flowers, red fives, backs and regional faces retain their identities in both styles', () => {
  assert.equal(tileFaceAssetUrl(46, { baseUrl: '/app/', guobiao: true }), '/app/game2d-assets/textures/guobiao-mahjong-tiles/table/46.png')
  for (const id of [105, 205, 305]) assert.ok(tileFaceAssetUrl(id, { baseUrl: '/', guobiao: true }).endsWith(`/${id}.png`))
  assert.equal(tileFaceAssetVariant(51, { guobiao: true, unityFlower: true }), 'guobiao')
  assert.equal(tileFaceAssetVariant(51, { guobiao: true, unityFlower: false }), 'regular')
  assert.equal(tileFaceAssetVariant(51, { black: true, unityFlower: true }), 'unity')
  for (const id of [0, 59, 61, 62, 63, 64, 71, 72, 73, 74, 81]) {
    assert.equal(tileFaceAssetVariant(id, { guobiao: true }), 'regular')
  }
  assert.equal(tileFaceAssetVariant(0, { black: true }), 'black')
})

test('all DOM face choices exist and use exactly the assets preloaded for Pixi', () => {
  const registered = new Map(TILE_TEXTURE_PATHS.map(item => [item.alias, item.src]))
  assert.equal(registered.size, TILE_TEXTURE_PATHS.length, 'texture aliases must be unique')
  for (const theme of ['guobiao', 'regular', 'black']) for (const unityFlower of [false, true]) {
    const options = { baseUrl: '/', guobiao: theme === 'guobiao', black: theme === 'black', unityFlower }
    for (const id of [0, ...STANDARD_FACE_IDS]) {
      const url = tileFaceAssetUrl(id, options)
      assert.ok(existsSync(path.join(process.cwd(), 'public', url.slice(1))), url)
      assert.equal(registered.get(`${tileFaceAssetVariant(id, options)}-${id || 'Back'}`), url)
    }
  }
})

test('DOM previews, walls and settlements do not shrink the calibrated pack a second time', () => {
  for (const baseUrl of ['', '/', '/app/', 'https://example.test/app/']) {
    for (const id of GUOBIAO_FACE_IDS) for (const fallback of [0.84, 0.86, 1]) {
      const url = tileFaceAssetUrl(id, { baseUrl, guobiao: true, unityFlower: true })
      assert.equal(tileFaceDisplayScale(url, fallback), 0.93)
    }
  }
  for (const id of [11, 46, 51, 61]) for (const fallback of [0.84, 0.86, 1]) {
    for (const black of [false, true]) {
      assert.equal(tileFaceDisplayScale(tileFaceAssetUrl(id, { baseUrl: '/', black }), fallback), fallback)
    }
  }
})

test('changing live tile artwork preserves placement and concealed tiles without stretching the Guobiao art', (t) => {
  const ids = [11, 21, 31, 41, 46, 47, 51]
  const textures = new Map()
  for (const variant of ['guobiao', 'regular', 'black', 'unity']) for (const id of ids) {
    const texture = new Texture({ source: new TextureSource({ width: variant === 'guobiao' ? 400 : 300, height: variant === 'guobiao' ? 532 : 400 }) })
    const alias = `${variant}-${id}`
    textures.set(alias, texture)
    Assets.cache.set(alias, texture)
  }
  setTileThemes('regular', 'unity', 'off')
  const tiles = ids.map((id, index) => {
    const tile = new Tile(salasasaTileToMmcr(id))
    tile.position.set(index * 30, index * 12)
    tile.rotation = index === 1 ? Math.PI / 2 : 0
    return tile
  })
  tiles[3].setConcealedFaceDown(true, false)
  const positions = tiles.map(tile => [tile.tid, tile.x, tile.y, tile.rotation, tile.shown])
  t.after(() => {
    tiles.forEach(tile => tile.destroy({ children: true }))
    for (const [alias, texture] of textures) { Assets.cache.remove(alias); texture.destroy(true) }
    setTileThemes('regular', 'unity', 'auto')
  })
  for (const theme of ['guobiao', 'black', 'regular', 'guobiao']) {
    setTileThemes(theme, 'unity', 'off')
    tiles.forEach(tile => tile.refreshTexture())
    assert.deepEqual(tiles.map(tile => [tile.tid, tile.x, tile.y, tile.rotation, tile.shown]), positions)
    assert.equal(tiles[3].cover.visible, true, 'concealed kong remains face down')
    assert.equal(tiles[3].sprite.visible, false)
    tiles.forEach((tile, index) => {
      const variant = tileFaceAssetVariant(ids[index], { guobiao: theme === 'guobiao', black: theme === 'black', unityFlower: true })
      assert.equal(tile.sprite.texture, textures.get(`${variant}-${ids[index]}`))
      if (theme === 'guobiao') {
        assert.ok(Math.abs(tile.sprite.width / tile.sprite.height - 400 / 532) < 1e-8)
        assert.ok(tile.sprite.width > TILE_WIDTH * 0.9, 'artwork fills the face without double padding')
        assert.ok(tile.sprite.height > TILE_HEIGHT * 0.9)
        assert.ok(tile.sprite.width < TILE_WIDTH * 0.94, 'retain a safe border')
        assert.ok(tile.sprite.height < TILE_HEIGHT * 0.94)
      }
    })
  }
})
