import test from 'node:test'
import assert from 'node:assert/strict'
import { loadWenzhouForm, buildWenzhouRoomPayload, WENZHOU_VERSION } from '../src/utils/wenzhouRoomConfig.js'

const form = values => Object.assign(loadWenzhouForm({ room_rule: 'wenzhou' }), values)
test('Wenzhou defaults and all room switches round trip', () => {
  const standard = buildWenzhouRoomPayload(form({ room_name: ' 温州 ', password: ' test ' }))
  assert.equal(standard.room_rule, 'wenzhou')
  assert.equal(standard.room_config.detailed_config.edition, WENZHOU_VERSION)
  assert.equal(standard.room_config.room_name, '温州')
  assert.equal(standard.password, 'test')
  assert.equal(standard.room_config.use_flowers, false)
  for (const key of ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator']) {
    for (const value of [true, false]) {
      const config = buildWenzhouRoomPayload(form({ [key]: value })).room_config
      assert.equal(loadWenzhouForm({}, config)[key], value)
    }
    assert.throws(() => buildWenzhouRoomPayload(form({ [key]: 1 })))
  }
  for (const [key, lo, hi] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    for (const value of [lo, hi]) assert.equal(buildWenzhouRoomPayload(form({ [key]: value })).room_config[key], value)
    for (const value of [lo-1, hi+1, 1.5, true, '1', null]) assert.throws(() => buildWenzhouRoomPayload(form({ [key]: value })))
  }
})
test('invalid profile and duplicate wall cannot silently become ordinary Wenzhou', () => {
  assert.throws(() => buildWenzhouRoomPayload(form({ sub_rule: 'wenzhou/folk' })))
  assert.throws(() => buildWenzhouRoomPayload(form({ duplicate_key: 'dup_x' })))
  const empty = buildWenzhouRoomPayload(form())
  assert.equal(empty.password, '')
  assert.equal('room_name' in empty.room_config, false)
})
