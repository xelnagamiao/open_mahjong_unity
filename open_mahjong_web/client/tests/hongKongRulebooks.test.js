import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile, access } from 'node:fs/promises'
import { hkProfiles } from '../src/utils/hongKongRoomConfig.js'
import { hongKongRulebooks, hongKongRulebook } from '../src/constants/hongKongRulebooks.js'
import { getLibraryRule } from '../src/constants/libraryRules.js'

test('each playable Hong Kong profile has a unique reading source, also used by the library', async () => {
  assert.deepEqual(hongKongRulebooks.map(book => book.subRule), hkProfiles.map(profile => profile.value))
  assert.equal(new Set(hongKongRulebooks.map(book => book.url)).size, hkProfiles.length)
  assert.deepEqual(getLibraryRule('hongkong').resources.map(book => book.url), hongKongRulebooks.map(book => book.url))
  for (const profile of hkProfiles) {
    const book = hongKongRulebook({ sub_rule: profile.value })
    if (book.url.startsWith('https://')) {
      assert.equal(book.downloadable, false)
      assert.equal(book.filename, null)
      continue
    }
    assert.ok(book.filename.endsWith('.docx'))
    const original = await readFile(new URL(`../public${book.url}`, import.meta.url))
    assert.equal(original.subarray(0, 4).toString('hex'), '504b0304')
    const archived = await readFile(new URL(`../../../other/rule/hongkong/${book.url.split('/').at(-1)}`, import.meta.url))
    assert.deepEqual(original, archived)
  }
})

test('legacy rooms resolve the right book and explicit profiles override stale version fields', () => {
  for (const [version, explicit] of [['gametower', 'new13_gametower'], ['lianhuise', 'new13_lianhuise']]) {
    const book = hongKongRulebook({ sub_rule: 'hongkong/new13', hk_new13_version: version })
    assert.equal(book.subRule, `hongkong/${explicit}`)
  }
  assert.equal(hongKongRulebook({ sub_rule: 'hongkong/new13' }).subRule, 'hongkong/new13_gametower')
  for (const profile of hkProfiles) {
    assert.equal(hongKongRulebook({ sub_rule: profile.value, hk_new13_version: 'lianhuise' }).subRule, profile.value)
  }
  assert.equal(hongKongRulebook().subRule, 'hongkong/qingzhang')
  assert.equal(hongKongRulebook({ sub_rule: '<invalid>' }).subRule, 'hongkong/qingzhang')
})

test('profiles link only to original rulebooks, with no platform supplements or old HTML redirects', async () => {
  const book = hongKongRulebook({ sub_rule: 'hongkong/new13_gametower' })
  assert.equal(book.url, 'https://mahjong.wikidot.com/rules:guangdong-style-scoring')
  assert.ok(book.readHint.includes('IGS'))
  assert.ok(book.readHint.includes('Avg'))
  assert.equal(book.label, '新章十三（Wiki）')
  for (const source of hongKongRulebooks) {
    assert.equal(source.relatedLinks, undefined)
    assert.doesNotMatch(source.url, /\.html$/)
    assert.doesNotMatch(source.desc, /补则|补充条款|实现|裁定/)
  }
  for (const file of ['hongkong-new13-gametower.html', 'hongkong-new13-wiki.html', 'hongkong.html']) {
    await assert.rejects(access(new URL(`../public/rulebooks/${file}`, import.meta.url)), { code: 'ENOENT' })
  }
})
