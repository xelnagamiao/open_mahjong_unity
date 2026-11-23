import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { customRulebooks, newChineseRulebooks, NEW_CHINESE_DESCRIPTION } from '../src/constants/newChineseRulebooks.js'

const require = createRequire(import.meta.url)
const { normalizeNewChineseRoomConfig } = require('../../server/utils/newChineseRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error {}

for (const rounds of [4,8,12,16]) {
  for (const exchange of [true,false]) {
    test(`新中式 ${rounds} 圈与换牌设置 ${exchange} 可往返赛事手动、预设和自动房间`, () => {
      const form = createEventRoomForm({ room_rule:'new_chinese', room_config: {
        room_name:'新中式验收桌', game_round:rounds, hepai_limit: 25, detailed_config:{exchange_three:exchange} } })
      const payload = buildEventRoomSettings(form)
      const limit = exchange ? 15 : 25
      assert.equal(eventRoomSettingsSummary(payload, {new_chinese:'自定义规则'}), `新中式麻将 · ${rounds}圈 · ${exchange ? '换三张' : '不换牌'} · ${limit}番起和`)
      assert.equal(eventRoomSettingsRows(payload).find(row => row.label === '起和番').value, `${limit}番`)
      let doc = readRoomSettings({})
      const apply = (kind, body) => {
        doc = applyRoomSettingsChange(doc, {kind, body:{revision:doc.revision,...body},userId:10001,eventStatus:'active'},
          {id:'new-chinese-table', now:'2026-11-19T00:00:00Z'}).settings
      }
      apply('manual', payload)
      apply('preset-create', {name:'新中式验收桌',...payload})
      apply('auto', {enabled:true,preset_id:'new-chinese-table'})
      doc = readRoomSettings(JSON.stringify(doc))
      const selected = resolveRoomSelection(doc, {revision:doc.revision,preset_id:'new-chinese-table'})
      assert.equal(selected.room_config.game_round,rounds)
      assert.equal(selected.room_config.hepai_limit,limit)
      assert.equal(selected.room_config.detailed_config.exchange_three,exchange)
      assert.deepEqual(selected.room_config,doc.auto_match.room_config)
      assert.deepEqual(buildEventRoomSettings(createEventRoomForm(selected)).room_config,payload.room_config)
    })
  }
}

test('切换规则还原默认参数，两端均拒绝错误圈数', () => {
  const form = createEventRoomForm({room_rule:'guobiao',room_config:{}})
  changeEventRoomRule(form,'new_chinese')
  const {room_config: config} = buildEventRoomSettings(form)
  assert.equal(config.game_round,4)
  assert.equal(config.starting_score,0)
  assert.equal(config.hepai_limit,0)
  assert.equal(config.detailed_config.exchange_three,false)
  assert.throws(()=>buildEventRoomSettings({...form,game_round:7}))
  assert.throws(()=>normalizeNewChineseRoomConfig({...config,game_round:7},SettingsError))
})

test('换三张锁定15番，关闭后恢复本次编辑的起和番，旧预设依换牌开关补齐起和番', () => {
  const settings = { room_rule:'new_chinese', room_config:{ hepai_limit: 1800 } }
  const form = createEventRoomForm(settings)
  form.exchange_three = true
  assert.equal(buildEventRoomSettings(form).room_config.hepai_limit, 15)
  form.exchange_three = false
  assert.equal(buildEventRoomSettings(form).room_config.hepai_limit, 1800)
  for (const exchange_three of [false, true]) {
    const legacy = { detailed_config: { exchange_three } }
    const limit = exchange_three ? 15 : 0
    assert.equal(normalizeNewChineseRoomConfig(legacy,SettingsError).hepai_limit, limit)
    assert.equal(buildEventRoomSettings(createEventRoomForm({...settings,room_config:legacy})).room_config.hepai_limit, limit)
  }
  assert.equal(normalizeNewChineseRoomConfig({hepai_limit:1800,detailed_config:{exchange_three:true}},SettingsError).hepai_limit,15)
})

test('自定义起和番接受非负整数且可保存高番门槛', () => {
  const form = createEventRoomForm({room_rule:'new_chinese'})
  for (const limit of [0, 15, 1800, 2147483647]) {
    form.hepai_limit = limit
    const payload = buildEventRoomSettings(form)
    assert.equal(normalizeNewChineseRoomConfig(payload.room_config, SettingsError).hepai_limit, limit)
  }
  for (const invalid of [-1, 1.5, '15', true, null, 2147483648]) {
    assert.throws(() => buildEventRoomSettings({...form, hepai_limit:invalid}))
    assert.throws(() => normalizeNewChineseRoomConfig({hepai_limit:invalid}, SettingsError))
  }
})

test('规则书网页、原件、牌型与支付示例随项目提供', () => {
  assert.match(NEW_CHINESE_DESCRIPTION,/微信15513635592$/)
  const contents = newChineseRulebooks.map(x=>readFileSync(new URL('../public'+x.url,import.meta.url)))
  assert.equal(contents[1].readUInt16LE(),0x4b50)
  const html=contents[0].toString()
  for(const name of ['一色四同顺','准听九','和家收 34 分','自摸结算','点黑全包']) assert.ok(html.includes(name),name)
})

test('散花建房与赛事使用副数、固定起和番，说明书保留红字裁定', () => {
  for (const rounds of [4, 8, 12, 16]) {
    const form = createEventRoomForm({room_rule:'new_chinese',room_config:{sub_rule:'new_chinese/sanhua',game_round:rounds}})
    const payload = buildEventRoomSettings(form)
    const config = normalizeNewChineseRoomConfig(payload.room_config, SettingsError)
    assert.equal(config.sub_rule, 'new_chinese/sanhua')
    assert.equal(config.hepai_limit, 4)
    assert.deepEqual(config.detailed_config, {rule_version:'sanhua-2026-11'})
    assert.equal(eventRoomSettingsRows(payload).find(row=>row.label==='副数').value, rounds+'副')
  }
  const docs = customRulebooks.find(entry=>entry.subRule==='new_chinese/sanhua').docs
  const [web,word] = docs.map(doc=>readFileSync(new URL('../public'+doc.url, import.meta.url)))
  assert.equal(word.readUInt16LE(), 0x4b50)
  for (const clause of ['单张特殊杠不算开口','第三次开口时锁定供牌者','该家共付三份','每家开口番累计最多 0.5 番']) {
    assert.ok(web.toString().split('<p class="addition">').some(p=>p.split('</p>')[0].includes(clause)), clause)
  }
})
