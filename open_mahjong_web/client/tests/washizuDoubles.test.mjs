import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createEventRoomForm, buildEventRoomSettings } from '../src/utils/eventRoomSettings.js'
import { changeRiichiMode, applyRiichiPreset } from '../src/utils/riichiRoomConfig.js'
import { washizuDoublesSummary, captainResultReason } from '../src/utils/washizuDoubles.js'
import captain from '../../shared/washizuDoubles.json' with { type: 'json' }

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')

test('captain mode normalizes settings through browser, Node storage and reopen', () => {
  const form = createEventRoomForm({ room_rule: 'riichi', room_config: { room_name: '主将赛', round_timer: 30 } })
  changeRiichiMode(form, captain.sub_rule)
  assert.equal(form.game_round, 2)
  assert.equal(form.starting_score, 25000)
  form.red_dora = true; form.open_tobi = false; form.riichi_detailed_config.pao = true
  const payload = buildEventRoomSettings(form)
  const stored = applyRoomSettingsChange(readRoomSettings({}), { kind: 'manual', body: { ...payload, revision: 0 }, userId: 10001, eventStatus: 'active' }).settings.manual
  for (const [key, value] of Object.entries(captain.room)) assert.equal(stored.room_config[key], value)
  for (const [key, value] of Object.entries(captain.detailed_config)) assert.equal(stored.room_config.detailed_config[key], value)
  assert.deepEqual(buildEventRoomSettings(createEventRoomForm(stored)).room_config, stored.room_config)
  assert.equal(stored.room_config.room_name, '主将赛')
  assert.equal(stored.room_config.round_timer, 30)
})

test('Node enforces fixed captain rules independently of browser', () => {
  const stored = applyRoomSettingsChange(readRoomSettings({}), { kind: 'manual', body: {
    room_rule: 'riichi', room_config: { sub_rule: captain.sub_rule, red_dora: true, open_tobi: false,
      detailed_config: { rank_points: '10_30', pao: true, tenpai_renchan: false } }, revision: 0,
  }, userId: 10001, eventStatus: 'active' }).settings.manual.room_config
  assert.equal(stored.hepai_way, 'head_bump')
  assert.equal(stored.detailed_config.rank_points, 'none')
  assert.equal(stored.detailed_config.pao, false)
  assert.equal(stored.detailed_config.tenpai_renchan, true)
  assert.equal(stored.open_tobi, true)
})

test('switching away restores standard/sanma defaults and keeps independent room fields', () => {
  const form = createEventRoomForm({ room_rule: 'riichi', room_config: { room_name: '主将赛', sub_rule: captain.sub_rule } })
  changeRiichiMode(form, 'riichi/sanma')
  assert.equal(form.starting_score, 35000)
  assert.equal(form.red_dora, true)
  assert.doesNotThrow(() => buildEventRoomSettings(form))
  applyRiichiPreset(form, 'majsoul')
  assert.equal(form.sub_rule, 'riichi/standard')
  assert.equal(form.room_name, '主将赛')
})

test('replay identities use original seats, retain duplicate usernames and expose captain result', () => {
  const result = { reason: 'captain_score', winner_team_id: 1, scores: { 0: 24000, 1: 26000 }, match_points: { 0: 0, 1: 1 } }
  const detail = { record: { game_title: { sub_rule: captain.sub_rule, team_by_player_index: [1, 1, 0, 0], team_role_by_player_index: ['assistant', 'captain', 'assistant', 'captain'], p0_name: '机器人', p1_name: '机器人' }, doubles_result: result } }
  const summary = washizuDoublesSummary(detail)
  assert.deepEqual(summary.players.map(p => p.role), ['B队助手', 'B队主将', 'A队助手', 'A队主将'])
  assert.equal(summary.players[0].name, summary.players[1].name)
  assert.equal(summary.result, result)
  assert.equal(captainResultReason(result), '按主将最终点数判定')
  assert.match(captainResultReason({ reason: 'captain_bust', winner_team_id: null }), /同时击飞/)
  detail.record.game_title.team_role_by_player_index[0] = 'captain'
  assert.equal(washizuDoublesSummary(detail), null)
  detail.record.game_title.sub_rule = 'riichi/washizu'
  assert.equal(washizuDoublesSummary(detail), null)
})
