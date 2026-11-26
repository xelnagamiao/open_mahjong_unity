import test from 'node:test'
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { tr } from '../src/i18n/index.js'
import { messages } from '../src/i18n/messages.js'
import { menchinEnglish, createMenchinPatterns } from '../src/i18n/menchin-messages.js'
import { specialDrawCaption } from '../src/utils/specialDrawCaptions.js'
import { eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { localizeRoomSummary } from '../src/i18n/room-summary.js'

const languages = ['en', 'ja', 'fr', 'de', 'ru', 'es']
const chinese = /[\u3400-\u9fff]/u
const tokens = value => [...value.matchAll(/\{\w+\}/g)].map(m => m[0]).sort()

test('shared Web catalogs are regenerated from the current Unity resources', () => {
  execFileSync(process.execPath, ['scripts/sync-platform-localization.mjs', '--check'], {
    cwd: new URL('../', import.meta.url), stdio: 'pipe',
  })
})

test('Menchin pages and server errors have all six translations with unchanged parameters', () => {
  for (const lang of languages) for (const source of Object.keys(menchinEnglish)) {
    const translated = messages[lang][source]
    assert.equal(typeof translated, 'string', `${lang}: ${source}`)
    assert.ok(translated.trim(), `${lang}: ${source}`)
    assert.deepEqual(tokens(translated), tokens(source), `${lang}: ${source}`)
    if (lang !== 'ja') assert.doesNotMatch(translated, chinese, `${lang}: ${source}`)
  }
})

test('Menchin dynamic scores, tile calls and results translate their fixed game values', () => {
  const examples = [
    ['第 7 / 20 局', ['7', '20']], ['我方 3 ∶ 5 对方', ['3', '5']],
    ['4 秒后开始下一局', ['4']], ['牌山 9 张', ['9']], ['对方暗手 13 张', ['13']],
    ['我的 Elo 1500', ['1500']], ['房间码 001234', ['001234']],
    ['吃 123万', ['1', '2', '3']], ['对方打出三万', ['3']],
    ['对方加杠九万', ['9']], ['暗杠 一万', ['1']], ['加杠 七万', ['7']],
    ['我方自摸 · 我方 +1 分', ['1']], ['对方诈和 · 我方 +1 分', ['1']],
    ['我方抢杠和 · 我方 +1 分', ['1']],
  ]
  for (const lang of languages) for (const [source, numbers] of examples) {
    const translated = tr(source, {}, lang)
    assert.notEqual(translated, source, `${lang}: ${source}`)
    assert.doesNotMatch(translated, /\{\w+\}/, `${lang}: ${source}`)
    if (lang !== 'ja') assert.doesNotMatch(translated, chinese, `${lang}: ${source}`)
    for (const number of numbers) assert.ok(translated.includes(number), `${lang}: ${source} -> ${translated}`)
  }
})

test('Menchin game patterns cannot consume arbitrary names or malformed room codes', () => {
  const patterns = createMenchinPatterns(value => value)
  for (const value of ['取消{2}', '对方打出玩家名', '房间码 00123', '房间码 0012345', '用户自摸 · 我方 +1 分']) {
    assert.ok(patterns.every(pattern => !pattern.match.test(value)), value)
  }
  for (const lang of languages) {
    assert.ok(tr('{name}的房间', { name: '取消・{2}・东风' }, lang).includes('取消・{2}・东风'))
    assert.ok(tr('玩家0: {2} (原始 ID:123)', {}, lang).includes('{2}'))
  }
})

test('four-player Riichi is distinct from four riichi declarations and duplicate room labels', () => {
  const abort = specialDrawCaption('four_riichi_abort', 'riichi')
  assert.equal(abort, '四家立直')
  for (const lang of languages) assert.notEqual(tr('四人立直', {}, lang), tr(abort, {}, lang), lang)
  assert.equal(tr('四人立直', {}, 'en'), 'Four-player Riichi')
  assert.equal(tr('开室', {}, 'ja'), 'オープンルーム')
  assert.equal(tr('闭室', {}, 'ja'), 'クローズドルーム')
})

test('event room summaries translate complete toggles, timers and winning thresholds', () => {
  const labels = { guobiao: '国标麻将', riichi: '立直麻将', yixing: '宜兴麻将', malaysia: '马来西亚三人麻将', singapore: '新加坡麻将' }
  for (const rule of Object.keys(labels)) for (const enabled of [false, true]) {
    const settings = { room_rule: rule, room_config: { game_round: 1, seven_pairs: enabled, multiple_max: enabled, round_timer: 20, step_timer: 5, starting_score: 25000, riichi_hepai_limit: 2 } }
    const source = eventRoomSettingsSummary(settings, labels)
    assert.equal(localizeRoomSummary(source, 'zh-CN'), source)
    for (const lang of languages) {
      const translated = localizeRoomSummary(source, lang)
      assert.notEqual(translated, source, `${lang}: ${source}`)
      if (lang !== 'ja') assert.doesNotMatch(translated, chinese, `${lang}: ${translated}`)
    }
  }
  assert.equal(localizeRoomSummary('1圈', 'en'), '1 round')
  assert.equal(localizeRoomSummary('2圈', 'en'), '2 rounds')
  assert.equal(localizeRoomSummary('七小对关闭', 'fr'), 'Sept paires désactivé')
  assert.equal(localizeRoomSummary('2 番起和（不含宝牌）', 'en'), 'Minimum 2 han (excluding dora)')
})
