import test from 'node:test'
import assert from 'node:assert/strict'
import {
  mmcrFaceId,
  salasasaFaceId,
  tileFaceAssetUrl,
} from '../src/game2d/lib/tileFaceAsset.ts'

test('face files use salasasa numeric ids', () => {
  assert.equal(salasasaFaceId(15), 15)
  assert.equal(salasasaFaceId(25), 25)
  assert.equal(salasasaFaceId(35), 35)
  assert.equal(salasasaFaceId(41), 41)
  assert.equal(salasasaFaceId(45), 45)
  assert.equal(salasasaFaceId(46), 46)
  assert.equal(salasasaFaceId(47), 47)
  assert.equal(salasasaFaceId(51), 51)
  assert.equal(salasasaFaceId(105), 105)
})

test('mmcr ids convert to the same numeric files', () => {
  assert.equal(mmcrFaceId(0x45), 15)
  assert.equal(mmcrFaceId(0x61), 21)
  assert.equal(mmcrFaceId(0xc9), 39)
  assert.equal(mmcrFaceId(0xa1), 41)
  assert.equal(mmcrFaceId(0xa5), 45)
  assert.equal(mmcrFaceId(0xa6), 46)
  assert.equal(mmcrFaceId(0xa7), 47)
  assert.equal(mmcrFaceId(0xe1), 51)
})

test('white dragon url is 46.svg, not z6 or Bai', () => {
  const url = tileFaceAssetUrl(salasasaFaceId(46), { baseUrl: '/' })
  assert.equal(url, '/game2d-assets/textures/riichi-mahjong-tiles/Regular/46.svg')
})

test('unity flowers use numeric png names', () => {
  const url = tileFaceAssetUrl(52, { baseUrl: '/', unityFlower: true })
  assert.equal(url, '/game2d-assets/textures/riichi-mahjong-tiles/Unity/52.png')
})
