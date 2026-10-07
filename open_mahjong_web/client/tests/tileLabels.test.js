import test from 'node:test'
import assert from 'node:assert/strict'
import { normalizeTileLabelMode, shouldShowTileLabels, tileFaceLabel, tileFaceLabelColor } from '../src/game2d/lib/tileLabels.ts'
import { mmcrFaceId, salasasaFaceId } from '../src/game2d/lib/tileFaceAsset.ts'

test('language defaults include both traditional variants and Japanese', () => {
  for (const language of ['zh-CN', 'zh-TW', 'zh-HK', 'zh-Hant', 'ja', 'ja-JP']) {
    assert.equal(shouldShowTileLabels('auto', language), false, language)
  }
  for (const language of ['en', 'en-US', 'fr', 'fr-FR', 'de']) {
    assert.equal(shouldShowTileLabels('auto', language), true, language)
  }
})

test('old preferences follow language while explicit choices survive a language switch', () => {
  assert.equal(normalizeTileLabelMode(undefined), 'auto')
  assert.equal(normalizeTileLabelMode('unknown'), 'auto')
  for (const language of ['zh-CN', 'ja', 'fr', 'en']) {
    assert.equal(shouldShowTileLabels(normalizeTileLabelMode('on'), language), true)
    assert.equal(shouldShowTileLabels(normalizeTileLabelMode('off'), language), false)
  }
})

test('table and settlement IDs use matching ranks without changing red fives', () => {
  for (const [tableId, serverId] of [[0x41, 11], [0x69, 29], [0xc5, 35]]) {
    assert.equal(tileFaceLabel(mmcrFaceId(tableId)), tileFaceLabel(salasasaFaceId(serverId)))
  }
  for (const redFive of [105, 205, 305]) {
    assert.equal(tileFaceLabel(salasasaFaceId(redFive)), '5')
    assert.equal(salasasaFaceId(redFive), redFive)
  }
})

test('honors use international wind and dragon letters; unknown and back tiles have no label', () => {
  assert.deepEqual([41, 42, 43, 44, 45, 46, 47].map(tileFaceLabel), ['E', 'S', 'W', 'N', 'R', 'Wh', 'G'])
  for (const id of [0, -1, 101, 102, 40, 48, 59, 41.5]) {
    assert.equal(tileFaceLabel(id), '')
  }
})

test('numbers and green dragon have red labels; red and white dragons have black labels', () => {
  for (const id of [11, 29, 35, 105, 205, 305, 41, 42, 43, 44, 47, 51, 58]) {
    assert.equal(tileFaceLabelColor(id), '#d00000')
  }
  assert.equal(tileFaceLabelColor(45), '#151515')
  assert.equal(tileFaceLabelColor(46), '#151515')
})
