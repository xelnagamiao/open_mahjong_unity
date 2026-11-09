import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import fr from '../src/i18n/locales/fr.js'
import de from '../src/i18n/locales/de.js'
import ru from '../src/i18n/locales/ru.js'
import { frenchFanNames } from '../src/i18n/fr.js'
import { tr, setLocale, locale } from '../src/i18n/index.js'
import { normalizeLocale } from '../src/i18n/locale-utils.js'
import { translateFanName, formatFanCount, translateRuleText } from '../src/i18n/fanNames.js'
import { guessFanName } from '../src/i18n/guessFanNames.js'
import { calculatorScoreText, scorePointUnit } from '../src/utils/calculatorScoreText.js'
import { suggestFans } from '../src/constants/guessFanCatalog.js'

const catalogs = { fr, de, ru }
const tokens = text => [...text.matchAll(/\{(?:[A-Za-z_][^{}\r\n]*|\d+(?::[^{}\r\n]+)?)\}/g)].map(m => m[0]).sort()
const root = new URL('../../../open_mahjong_unity/Assets/Resources/Localization/', import.meta.url)
const original = JSON.parse(fs.readFileSync(new URL('PlatformUI.json', root), 'utf8'))

test('all three catalogs cover the existing Unity source and keep every placeholder', () => {
  for (const [lang, catalog] of Object.entries(catalogs)) {
    for (const entry of original.entries) {
      assert.equal(typeof catalog.messages[entry.key], 'string', `${lang}: ${entry.key}`)
      assert.ok(catalog.messages[entry.key].trim(), `${lang}: ${entry.key}`)
      assert.deepEqual(tokens(catalog.messages[entry.key]), tokens(entry.zh || entry.key), `${lang}: ${entry.key}`)
    }
    for (const [key, value] of Object.entries(catalog.messages)) assert.ok(value.trim(), `${lang}: empty ${key}`)
    assert.deepEqual(Object.keys(catalog.messages), Object.keys(fr.messages), `${lang}: shared coverage`)
  }
})

test('Unity and Web ship identical translations, rule contexts and quantity forms', () => {
  for (const [lang, catalog] of Object.entries(catalogs)) {
    const unity = JSON.parse(fs.readFileSync(new URL(`PlatformUI.${lang}.json`, root), 'utf8'))
    const entries = list => Object.fromEntries(list.map(x => [x.key, x.text]))
    assert.deepEqual(entries(unity.entries), catalog.messages, lang)
    assert.deepEqual(Object.fromEntries(unity.contexts.map(x => [x.name, entries(x.entries)])), catalog.contexts, lang)
    assert.deepEqual(Object.fromEntries(unity.units.map(({ key, ...forms }) => [key, forms])), catalog.units, lang)
  }
})

test('every supplied French MCR name is exact; Riichi Pinfu has a separate context', () => {
  for (const [key, value] of Object.entries(frenchFanNames)) assert.equal(fr.messages[key], value, key)
  assert.equal(translateFanName('平和', 'fr', 'guobiao'), frenchFanNames['平和'])
  assert.equal(translateFanName('平和', 'fr', 'riichi'), 'Pinfu')
  assert.equal(translateFanName('平和', 'de', 'riichi'), 'Pinfu')
  assert.equal(translateFanName('平和（1 番）', 'ru', 'riichi'), 'Пинфу (1 хан)')
  assert.equal(guessFanName('平和', ['立直'], 'ru'), 'Пинфу')
  assert.equal(guessFanName('平和', '国标', 'fr'), frenchFanNames['平和'])
})

test('Russian plural quantities distinguish fan and han, including fractions', () => {
  for (const [value, expected] of [[1,'1 фан'],[2,'2 фана'],[11,'11 фанов'],[21,'21 фан'],[1.5,'1.5 фана']])
    assert.equal(formatFanCount(value, 'ru'), expected)
  assert.equal(formatFanCount(2, 'ru', 'riichi/standard'), '2 хана')
  assert.equal(formatFanCount(8, 'ru', 'guobiao/standard'), '8 очков')
  assert.equal(formatFanCount(21, 'ru', 'guobiao'), '21 очко')
  assert.equal(formatFanCount(1.5, 'ru', 'guobiao'), '1.5 очка')
  assert.equal(tr('12枚', {}, 'ru'), '12 костей')
  assert.equal(translateRuleText('2番', 'ru', 'riichi'), '2 хана')
  assert.equal(translateRuleText('番', 'ru', 'riichi', 5), 'ханов')
  assert.equal(translateRuleText('30副', 'de', 'classical'), '30 Fu')
})

test('language detection and dynamic text preserve names, links and counts', () => {
  for (const lang of Object.keys(catalogs)) {
    assert.equal(normalizeLocale(`${lang}_${lang.toUpperCase()}`), lang)
    const name = '取消・东风・Play'
    const room = tr('{name}的房间', { name }, lang)
    assert.ok(room.includes(name))
    assert.doesNotMatch(room.replace(name,''), /[\u3400-\u9fff]/u)
    const url = 'https://example.test/2d/replay?a=1&b=2'
    assert.ok(tr('复制失败，请手动复制：{url}', { url }, lang).includes(url))
    assert.ok(tr('第12巡', {}, lang).includes('12'))
    assert.doesNotMatch(tr('第12巡', {}, lang), /[\u3400-\u9fff]/u)
    assert.ok(tr('玩家0: {2} (原始 ID:123)', {}, lang).includes('{2}'))
    assert.doesNotMatch(tr('牌面（添加到副露 #1，仅高亮牌可组成合法牌组）', {}, lang), /[\u3400-\u9fff]/u)
    assert.doesNotMatch(tr('牌 1万 已达 4 张上限', {}, lang), /[\u3400-\u9fff]/u)
  }
  assert.equal(tr('{count} 秒', {count:1}, 'de'), '1 Sekunde')
  assert.equal(tr('{count} 局', {count:1}, 'de'), '1 Hand')
  assert.equal(tr('{count} 圈', {count:1}, 'de'), '1 Runde')
})

test('translated guess search resolves the original scoring ID without changing catalog data', () => {
  const suggestions = suggestFans('pinfu', ['guobiao', 'riichi'], 20, fan => fan.names.map(name => translateFanName(name, 'fr', fan.rules[0])))
  assert.ok(suggestions.some(fan => fan.id === 'riichi:pinfu'))
  assert.equal(suggestions.find(fan => fan.id === 'riichi:pinfu').names[0], '平和')
  const russian = suggestFans('пинфу', ['riichi'], 20, fan => fan.names.map(name => translateFanName(name, 'ru', fan.rules[0])))
  assert.equal(russian[0].id, 'riichi:pinfu')
  assert.deepEqual(suggestFans('pinfu', ['guobiao'], 20, fan => fan.names.map(name => translateFanName(name, 'fr', fan.rules[0]))), [])
})

test('calculator changes score language reactively and keeps Riichi han/fu', () => {
  const previous = locale.value
  try {
    setLocale('ru')
    assert.equal(calculatorScoreText({ rule:'riichi', canWin:true, points:2000, fan:2, fu:30 }), '2000 очков · 2 хана 30 фу')
    assert.equal(scorePointUnit(3900), 'очков')
    assert.equal(tr('放铳者支付 3900 点'), 'Снёсший платит 3900 очков')
    assert.equal(tr('每家 2000 点'), 'По 2000 очков с каждого')
    assert.equal(tr('庄家 2000 点，闲家各 1000 点'), 'Дилер — 2000 очков, остальные — по 1000 очков')
    setLocale('de')
    assert.equal(scorePointUnit(3900), 'Punkte')
    assert.equal(tr('放铳者支付 3900 点'), 'Der Einwerfer zahlt 3900 Punkte')
    setLocale('fr')
    assert.match(calculatorScoreText({ rule:'riichi', canWin:true, points:2000, fan:2, fu:30 }), /2 han/)
    assert.equal(tr('每家 2000 点'), '2000 points par joueur')
  } finally { setLocale(previous) }
})

test('account regulations preserve clauses, links and project identifiers', () => {
  const base = new URL('../src/content/', import.meta.url)
  const source = fs.readFileSync(new URL('accountRegulations.txt', base), 'utf8')
  const clauses = value => [...value.matchAll(/^([1-4])\./gm)].map(m => m[1])
  for (const lang of Object.keys(catalogs)) {
    const text = fs.readFileSync(new URL(`accountRegulations.${lang}.txt`, base), 'utf8')
    assert.deepEqual(clauses(text), clauses(source), lang)
    for (const literal of ['open_mahjong_unity', 'Salasasa Studio', 'salasasa.cn', 'https://github.com/xelnagamiao/open_mahjong_unity']) assert.ok(text.includes(literal), `${lang}: ${literal}`)
  }
})
