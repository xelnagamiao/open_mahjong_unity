import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { newChineseRulebooks, NEW_CHINESE_DESCRIPTION } from '../src/constants/newChineseRulebooks.js'

const require = createRequire(import.meta.url)
const { normalizeNewChineseRoomConfig } = require('../../server/utils/newChineseRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error {}

for (const rounds of [4,8,12,16]) {
  for (const exchange of [true,false]) {
    test(`新中式 ${rounds} 圈与换牌设置 ${exchange} 可往返赛事手动、预设和自动房间`, () => {
      const form = createEventRoomForm({ room_rule:'new_chinese', room_config: {
        room_name:'新中式验收桌', game_round:rounds, detailed_config:{exchange_three:exchange} } })
      const payload = buildEventRoomSettings(form)
      assert.equal(eventRoomSettingsSummary(payload, {new_chinese:'自定义规则'}), `新中式麻将 · ${rounds}圈 · ${exchange ? '换三张' : '不换牌'}`)
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
  assert.equal(config.detailed_config.exchange_three,false)
  assert.throws(()=>buildEventRoomSettings({...form,game_round:7}))
  assert.throws(()=>normalizeNewChineseRoomConfig({...config,game_round:7},SettingsError))
})

test('规则书网页、原件、牌型与支付示例随项目提供', () => {
  assert.match(NEW_CHINESE_DESCRIPTION,/微信15513635592$/)
  const contents = newChineseRulebooks.map(x=>readFileSync(new URL('../public'+x.url,import.meta.url)))
  assert.equal(contents[1].readUInt16LE(),0x4b50)
  const html=contents[0].toString()
  for(const name of ['一色四同顺','准听九','和家收 34 分','自摸结算','点黑全包']) assert.ok(html.includes(name),name)
})
