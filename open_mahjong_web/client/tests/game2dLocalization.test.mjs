import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import { computed } from 'vue'
import { tr, setLocale, SUPPORTED_LOCALES } from '../src/i18n/index.js'
import { game2dMessages } from '../src/i18n/game2d-messages.js'
import { messages } from '../src/i18n/messages.js'
import { translateFanName } from '../src/i18n/fanNames.js'
import { homeRuleOptions } from '../src/utils/homeStats.js'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const parameters = text => [...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort()

test('replay and room translations retain every dynamic value in all languages', () => {
  for (const key of Object.keys(game2dMessages.en)) {
    for (const lang of SUPPORTED_LOCALES) {
      const value = tr(key, {}, lang)
      assert.deepEqual(parameters(value), parameters(key), `${lang}: ${key}`)
      if (['en', 'fr'].includes(lang)) assert.doesNotMatch(value, /[\u3400-\u9fff]/u, `${lang}: ${key}`)
    }
  }
})

test('all home rule options, including variants, have concise labels without changing values', () => {
  const options = homeRuleOptions()
  assert.ok(options.length > 40)
  const originalValues = options.map(option => option.value)
  for (const lang of ['en', 'ja', 'fr']) {
    for (const option of options) {
      const label = tr(option.label, {}, lang)
      assert.ok(label && Object.hasOwn(messages[lang], option.label), `${lang}: ${option.label}`)
      if (lang !== 'ja') assert.doesNotMatch(label, /[\u3400-\u9fff]/u, `${lang}: ${option.label}`)
      assert.ok(label.length < 40, `${lang}: ${label}`)
    }
  }
  assert.deepEqual(homeRuleOptions().map(option => option.value), originalValues)
})

test('language switches update replay status, wall counts, room defaults and selected rule', () => {
  const labels = computed(() => [
    tr('本局结束'),
    tr('余 {remaining}/{total}', { remaining: 58, total: 91 }),
    tr('鸣牌保护：{state}', { state: tr('开') }),
    tr('{name}的房间', { name: 'Xelnaga' }),
    tr('国标（标准）'),
  ])
  setLocale('en')
  assert.deepEqual(labels.value, ['End of hand', 'Left 58/91', 'Call protection: On', 'Xelnaga’s room', 'MCR (standard)'])
  setLocale('ja')
  assert.deepEqual(labels.value, ['終局', '残り 58/91', '鳴き保護：オン', 'Xelnagaの部屋', '国際公式（標準）'])
  setLocale('zh-TW')
  assert.equal(labels.value[0], '本局結束')
  assert.equal(labels.value[3], 'Xelnaga的房間')
  setLocale('zh-CN')
  assert.equal(labels.value[0], '本局结束')
})

test('interpolation preserves player names, URLs, identifiers and counts', () => {
  const name = '取消・东风・Play'
  assert.equal(tr('{name}的房间', { name }, 'en'), `${name}’s room`)
  const url = 'https://example.test/2d/record/test?round=1&node=72'
  assert.ok(tr('复制失败，请手动复制：{url}', { url }, 'en').endsWith(url))
  assert.equal(tr('已复制{mode}当前位置（第{round}局 node {node}）', { mode: '3D', round: 4, node: 72 }, 'en'), '3D position copied (hand 4, node 72)')
  assert.equal(tr('8 番', {}, 'en'), '8 fan')
  assert.equal(tr('余 58 枚', {}, 'en'), '58 tiles left')
  assert.equal(translateFanName('花龙', 'en'), 'Mixed Straight')
  assert.equal(tr('国标标准', {}, 'ja'), '国際公式（標準）')
  assert.equal(tr('复式密钥', {}, 'ja'), 'デュプリケートキー')
  assert.equal(tr('复式密钥', {}, 'zh-TW'), '複式密鑰')
  assert.equal(tr('选择全部规则', {}, 'en'), 'Select rule')
  assert.equal(tr('国标（四癞子）', {}, 'zh-TW'), '國標（四癩子）')
})

test('localized Vue templates compile, including bound options and accessibility labels', () => {
  for (const file of ['views/Home.vue', 'views/game2d/Replay.vue', 'views/game2d/ReplayIndependentWall.vue', 'views/game2d/CustomRoomPanel.vue', 'components/GuobiaoEmptyRoomConfig.vue', 'components/DuplicateRoomField.vue', 'views/game2d/GameScoreboardPanel.vue']) {
    const source = fs.readFileSync(new URL(`../src/${file}`, import.meta.url), 'utf8')
    const { descriptor, errors } = parse(source, { filename: file })
    assert.deepEqual(errors, [], file)
    const script = compileScript(descriptor, { id: file })
    const result = compileTemplate({ source: descriptor.template.content, filename: file, id: file, compilerOptions: { bindingMetadata: script.bindings, expressionPlugins: ['typescript'] } })
    assert.deepEqual(result.errors, [], file)
  }
})
