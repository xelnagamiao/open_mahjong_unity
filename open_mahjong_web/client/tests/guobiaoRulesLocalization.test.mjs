import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { computed } from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { tr, setLocale, SUPPORTED_LOCALES } from '../src/i18n/index.js'
import { guobiaoRulesMessages } from '../src/i18n/guobiao-rules-messages.js'
import { translateFanName } from '../src/i18n/fanNames.js'
import { TILE_NAME } from '../src/composables/useMahjongTiles.js'

const readSource = file => readFileSync(new URL(`../src/${file}`, import.meta.url), 'utf8')
const catalog = JSON.parse(readSource('constants/guobiaoFanTable.json'))
const guide = JSON.parse(readSource('constants/guobiaoRuleGuide.json'))
const sections = guide.pages.flatMap(page => page.sections)
const controlLabels = [...readSource('components/GuobiaoFanTable.vue').matchAll(/tr\('([^']+)'\)/g)].map(match => match[1])
const content = [
  ...controlLabels, '番种表', '牌背', '关闭此对话框',
  ...guide.pages.map(page => page.title),
  ...sections.flatMap(section => [section.title, section.body]),
  ...catalog.entries.flatMap(entry => [entry.description, entry.note]).filter(Boolean),
]

test('all 82 fan descriptions, guide sections and reader controls are localized', () => {
  assert.equal(catalog.entries.length, 82)
  assert.ok(sections.length > 0)
  for (const language of ['en', 'ja', 'fr']) {
    for (const source of content) {
      assert.ok(Object.hasOwn(guobiaoRulesMessages[language], source), `${language}: missing ${source}`)
      assert.ok(tr(source, {}, language).trim(), `${language}: empty ${source}`)
      if (source.length > 20) assert.notEqual(tr(source, {}, language), source, `${language}: untranslated ${source}`)
    }
  }
  for (const language of ['en', 'fr']) {
    for (const source of content) assert.doesNotMatch(tr(source, {}, language), /[\u3400-\u9fff]/u, `${language}: ${source}`)
  }
  assert.equal(tr('关闭窗口', {}, 'en'), 'Close')
  assert.equal(tr('关闭窗口', {}, 'ja'), '閉じる')
  assert.equal(tr('关闭窗口', {}, 'fr'), 'Fermer')
})

test('fan names and all 42 example tile labels work in every supported locale', () => {
  const tileLabels = Object.entries(TILE_NAME).filter(([id]) => Number(id) <= 58).map(([, label]) => label)
  assert.equal(tileLabels.length, 42)
  for (const language of SUPPORTED_LOCALES) {
    const labels = [
      ...tileLabels.map(label => tr(label, {}, language)),
      ...catalog.entries.map(entry => translateFanName(entry.name, language)),
    ]
    for (const label of labels) {
      assert.ok(label.trim(), language)
      if (['en', 'fr'].includes(language)) assert.doesNotMatch(label, /[\u3400-\u9fff]/u, `${language}: ${label}`)
    }
  }
  assert.equal(translateFanName('单吊将', 'en'), 'Single Waiting')
  assert.equal(translateFanName('单吊将', 'ja'), '単釣将')
})

test('Traditional Chinese covers the rules vocabulary without corrupting quantifiers', () => {
  for (const language of ['zh-TW', 'zh-HK']) {
    assert.equal(tr('查看规则', {}, language), '檢視規則')
    assert.equal(tr('牌墙中的若干饼牌', {}, language), '牌牆中的若干餅牌')
    assert.equal(tr('抢杠和、错和或诈和，确认状态，不可复计', {}, language), '搶槓和、錯和或詐和，確認狀態，不可複計')
    for (const source of content) assert.doesNotMatch(tr(source, {}, language), /[墙饼抢诈杠状确]|若乾/u)
  }
})

test('changing locale updates already-created reader labels and descriptions', () => {
  const source = catalog.entries[0].description
  const labels = computed(() => [tr('查看规则'), tr(source), tr(sections[1].body), translateFanName('单吊将')])
  try {
    for (const language of SUPPORTED_LOCALES) {
      setLocale(language)
      assert.deepEqual(labels.value, [tr('查看规则', {}, language), tr(source, {}, language), tr(sections[1].body, {}, language), translateFanName('单吊将', language)])
    }
  } finally {
    setLocale('zh-CN')
  }
})

test('localized rules reader and theme binding compile in the 2D table', () => {
  for (const file of ['components/GuobiaoFanTable.vue', 'components/GuobiaoFanTile.vue', 'views/game2d/Game.vue']) {
    const { descriptor, errors } = parse(readSource(file), { filename: file })
    assert.deepEqual(errors, [], file)
    const script = compileScript(descriptor, { id: file })
    const result = compileTemplate({ source: descriptor.template.content, filename: file, id: file, compilerOptions: { bindingMetadata: script.bindings } })
    assert.deepEqual(result.errors, [], file)
  }
})
