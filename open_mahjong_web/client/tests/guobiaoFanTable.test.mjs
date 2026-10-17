import { test } from 'node:test'
import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { fanExampleFaceDown, parseFanExample } from '../src/utils/guobiaoFanExample.js'
import { guobiaoFanTileAssets } from '../src/utils/guobiaoFanTileAssets.js'

const catalog = JSON.parse(readFileSync(new URL('../src/constants/guobiaoFanTable.json', import.meta.url), 'utf8'))

test('规则阅读页与 Unity 共用正文，且每条保留可追溯来源', () => {
  const guide = JSON.parse(readFileSync(new URL('../src/constants/guobiaoRuleGuide.json', import.meta.url), 'utf8'))
  const unity = JSON.parse(readFileSync(new URL('../../../open_mahjong_unity/Assets/Resources/GuobiaoRuleGuide.json', import.meta.url), 'utf8'))
  assert.deepEqual(guide, unity)
  assert.deepEqual(guide.pages.map(page => page.title), ['游戏规则', '计分原则'])
  for (const page of guide.pages) {
    assert.equal(new Set(page.sections.map(section => section.id)).size, page.sections.length)
    for (const section of page.sections) {
      assert.ok(section.title && section.body && section.sources.length)
      for (const source of section.sources) {
        assert.ok(existsSync(new URL('../../../' + source.split('#')[0], import.meta.url)), source)
      }
    }
  }
  const scoring = guide.pages.find(page => page.id === 'scoring')
  for (const section of scoring.sections) assert.ok(section.sourcePages.length > 0, section.id)
  assert.ok(scoring.sections.some(section => section.title.includes('套算一次')))
})

test('Web 番表和 Unity 的 82 个番种、说明及牌例一致', () => {
  const unity = JSON.parse(readFileSync(new URL('../../../open_mahjong_unity/Assets/Resources/GuobiaoFanCatalog.json', import.meta.url), 'utf8'))
  assert.deepEqual(catalog, unity)
  assert.equal(catalog.entries.length, 82)
  assert.deepEqual([...new Set(catalog.entries.map((entry) => entry.fan))].sort((a, b) => a - b),
    [1, 2, 4, 5, 6, 8, 12, 16, 24, 32, 48, 64, 88])
})

test('明杠全亮、暗杠两端背面，混合杠不改变牌例牌号', () => {
  const orientation = (name, groupIndex) => {
    const entry = catalog.entries.find((entry) => entry.name === name)
    const groups = parseFanExample(entry.example)
    return groups[groupIndex].map((tile, index) => fanExampleFaceDown(entry, groups, groupIndex, index))
  }
  assert.deepEqual(orientation('明杠', 0), [false, false, false, false])
  assert.deepEqual(orientation('暗杠', 0), [true, false, false, true])
  assert.deepEqual(orientation('明暗杠', 0), [false, false, false, false])
  assert.deepEqual(orientation('明暗杠', 1), [true, false, false, true])
  assert.deepEqual(orientation('双暗杠', 0), [true, false, false, true])
  assert.deepEqual(orientation('双暗杠', 1), [true, false, false, true])
})

test('牌例使用中发白字序，且所有牌张有 Web 资源', () => {
  assert.deepEqual(parseFanExample('555z 666z 777z'), [[45, 45, 45], [47, 47, 47], [46, 46, 46]])
  for (const entry of catalog.entries) {
    const tiles = parseFanExample(entry.example).flat()
    assert.ok(tiles.length >= 8, entry.name)
    for (const tile of tiles) {
      const count = tiles.filter((id) => id === tile).length
      assert.ok(count <= (tile >= 51 ? 1 : 4), entry.name + ': ' + tile)
      const assets = guobiaoFanTileAssets(tile)
      for (const url of Object.values(assets)) {
        assert.ok(existsSync(new URL('../public' + url, import.meta.url)), entry.name + ': ' + tile)
      }
    }
  }
  assert.throws(() => parseFanExample('8z'))
  assert.throws(() => parseFanExample('9f'))
})

test('番表使用与 Unity 完全相同的原始高清手牌、背景和牌背', () => {
  const root = '/game2d-assets/textures/guobiao-mahjong-tiles'
  const tileIds = [...new Set(catalog.entries.flatMap(entry => parseFanExample(entry.example).flat()))]
  assert.equal(tileIds.length, 42)
  const pairs = tileIds.map(id => [`${root}/hand/${id}.png`, `Faces/official/hand/${id}.png`])
  pairs.push([`${root}/surfaces/front.png`, 'Surfaces/backgrounds/hand-default.png'],
    [`${root}/surfaces/back.png`, 'Surfaces/backs/hand-default.png'])
  for (const [webPath, unityPath] of pairs) {
    const bytes = readFileSync(new URL('../public' + webPath, import.meta.url))
    const original = readFileSync(new URL('../../../open_mahjong_unity/Assets/Resources/image/Cards/' + unityPath, import.meta.url))
    assert.deepEqual(bytes, original, webPath)
    assert.ok(bytes.readUInt32BE(16) >= 271, `${webPath}: full resolution width`)
    assert.equal(bytes.readUInt32BE(20), 389, `${webPath}: original canvas height`)
  }
})

test('暗杠背面不叠加牌面，且支持非根路径部署', () => {
  for (const entry of catalog.entries) {
    const groups = parseFanExample(entry.example)
    groups.forEach((group, groupIndex) => group.forEach((tile, tileIndex) => {
      const hidden = fanExampleFaceDown(entry, groups, groupIndex, tileIndex)
      const assets = guobiaoFanTileAssets(tile, hidden, '/app/')
      assert.equal(assets.background, `/app/game2d-assets/textures/guobiao-mahjong-tiles/surfaces/${hidden ? 'back' : 'front'}.png`)
      assert.equal(assets.face, hidden ? null : `/app/game2d-assets/textures/guobiao-mahjong-tiles/hand/${tile}.png`)
    }))
  }
})
