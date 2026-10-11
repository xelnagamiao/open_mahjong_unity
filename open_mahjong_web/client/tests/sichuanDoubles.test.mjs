import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { formatSubRule } from '../src/utils/gameMeta.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
import { sichuanDoublesSummary, isSichuanBloodBattle } from '../src/utils/sichuanReplay.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
function normalizeRoomConfig(rule, config) {
  const doc = readRoomSettings({})
  return applyRoomSettingsChange(doc, { kind: 'manual', userId: 101, eventStatus: 'active',
    body: { revision: doc.revision, room_rule: rule, room_config: config },
  }).settings.manual.room_config
}

test('doubles preset round trips through editor and server', () => {
  const settings = { room_rule: 'sichuan', room_config: { sub_rule: 'sichuan/doubles', game_round: 2 } }
  const form = createEventRoomForm(settings)
  const payload = buildEventRoomSettings(form)
  const normalized = normalizeRoomConfig(payload.room_rule, payload.room_config)
  assert.equal(normalized.sub_rule, 'sichuan/doubles')
  assert.equal(normalized.game_round, 2)
  assert.equal(formatSubRule(normalized.sub_rule), '血战双打·2V2')
  assert.ok(eventRoomSettingsRows(settings).some(row => row.value === '血战双打·2V2'))
})

test('team metadata preserves same-name players and tie result', () => {
  const result = { scores: { 0: 0, 1: 0 }, winner_team_id: null, match_points: { 0: .5, 1: .5 }, reason: 'draw' }
  const detail = { record: { game_title: { rule: 'sichuan', sub_rule: 'sichuan/doubles', team_by_player_index: [0,1,0,1], p0_name: '机器人', p1_name: '机器人', p2_name: '机器人', p3_name: '机器人' }, doubles_result: result } }
  assert.ok(isSichuanBloodBattle(detail))
  assert.deepEqual(sichuanDoublesSummary(detail).teams.map(t => t.players.length), [2,2])
  assert.deepEqual(sichuanDoublesSummary(detail).result, result)
  detail.record.game_title.team_by_player_index = [0,0,0,1]
  assert.equal(sichuanDoublesSummary(detail), null)
})

test('ordinary presets stay standard and unsupported modes fail', () => {
  const payload = buildEventRoomSettings(createEventRoomForm({room_rule:'sichuan'}))
  assert.equal(normalizeRoomConfig('sichuan', payload.room_config).sub_rule, 'sichuan/standard')
  assert.throws(() => normalizeRoomConfig('sichuan', {sub_rule:'sichuan/duplicate'}))
})
