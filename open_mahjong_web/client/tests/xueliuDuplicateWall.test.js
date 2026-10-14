import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createDuplicateDraft, duplicateZoneLimit, flattenDuplicateDraft, validateDuplicateDraft, moveDuplicateTile, splitDuplicateWall, duplicateRoundSeats } from '../src/utils/duplicateWalls.js'
import { duplicateReplayWall } from '../src/utils/duplicateReplayWall.js'
import { buildEventRoomCreation, createEventRoomCreationForm } from '../src/utils/eventRoomCreation.js'
const require = createRequire(import.meta.url)
const { catalog, normalizeInput, fullWall, wallDto } = require('../../server/services/duplicateWalls.js')
const rule = catalog().rules.find(rule => rule.rule === 'sichuan/xueliu')

test('manual bloodflow layout has four 13+8 partitions and one 24-tile shared wall', () => {
  const tiles = fullWall(rule.rule)
  const partitions = splitDuplicateWall(tiles, 108, 'xueliu')
  const draft = createDuplicateDraft('xueliu')
  assert.deepEqual(partitions.map(p => p.length), [21,21,21,21,24])
  partitions.forEach((p, seat) => { draft[seat] = { hand: seat === 4 ? [] : p.slice(0,13), wall: seat === 4 ? p : p.slice(13) } })
  for (let dealer = 0; dealer < 4; dealer++) {
    assert.equal(validateDuplicateDraft(draft, rule, dealer), '')
    assert.equal(duplicateZoneLimit(108, dealer, 'wall', dealer, 'xueliu'), 8)
  }
  assert.deepEqual(flattenDuplicateDraft(draft), tiles)
  assert.equal(moveDuplicateTile(draft, { seat: 0, zone: 'wall', index: 0 }, { seat: 4, zone: 'wall' }, 108, 0, 'xueliu'), false)
  draft[4].wall.pop()
  assert.match(validateDuplicateDraft(draft, rule), /公共牌墙/)
  assert.equal(moveDuplicateTile(draft, { seat: 0, zone: 'wall', index: 0 }, { seat: 4, zone: 'wall' }, 108, 0, 'xueliu'), true)
  assert.equal(draft[0].wall.length, 7)
  assert.equal(draft[4].wall.length, 24)
  for (let round = 1; round <= 16; round++) assert.deepEqual(duplicateRoundSeats(round, 'xueliu'), [0,1,2,3])
})

test('seed, random key and manual walls conserve exactly 108 tiles and keep key contents private', () => {
  for (const wall_type of ['seed', 'key', 'manual']) {
    const input = { rule: rule.rule, wall_type, seed: 'JJ seed', tiles: fullWall(rule.rule) }
    const normalized = normalizeInput(input)
    assert.equal(normalized.tiles.length, 108)
    assert.equal(normalized.use_flowers, false)
    assert.deepEqual([...normalized.tiles].sort(), fullWall(rule.rule))
    assert.equal(wallDto(normalized, true).wall_layout, 'xueliu')
    if (wall_type === 'key') {
      assert.equal(wallDto(normalized, true).tiles, undefined)
      assert.equal(wallDto(normalized, true).seed, undefined)
    } else if (wall_type === 'seed') assert.deepEqual(normalizeInput(input).tiles, normalized.tiles)
    else assert.deepEqual(normalized.tiles, input.tiles)
  }
  assert.throws(() => normalizeInput({ rule: rule.rule, wall_type: 'manual', tiles: fullWall('guobiao') }), /108/)
})

test('bloodflow replay consumes original fixed leftovers and then common source -1 exactly once', () => {
  const round = { duplicate_wall_layout: 'xueliu', duplicate_walls: [[11],[12,13],[14,15],[16]], duplicate_common_wall: [21,22], action_ticks: [
    ['d',11,'dw',0,7,0], ['gd',12,'dw',1,6,3], ['d',13,'dw',1,7,2], ['d',14,'dw',2,6,1], ['d',15,'dw',2,7,0],
    ['d',16,'dw',3,7,3], ['d',21,'dw',-1,0,2], ['d',22,'dw',-1,1,1],
  ] }
  for (let node = 0; node <= 8; node++) {
    const tiles = duplicateReplayWall(round, node)
    assert.equal(tiles.filter(t => t.consumed).length, node)
    assert.equal(tiles.filter(t => !t.consumed).length, 8-node)
    assert.equal(tiles[0].commonActive, node >= 1)
    if (node >= 1) assert.ok(tiles.filter(t => !t.consumed).every(t => t.shared))
  }
  const legacy = duplicateReplayWall({ duplicate_walls: [[11],[12],[13],[14]], action_ticks: [['d',11,'dw',0,0,0]] }, 1)
  assert.equal(legacy.length,4)
  assert.equal(legacy[0].consumed,true)
  assert.equal(legacy[1].shared,undefined)
})

test('ordinary and paired room payloads preserve bloodflow keys with their own creation scope', () => {
  for (const sub_rule of ['sichuan/xueliu', 'sichuan/xueliu_exchange', 'sichuan/xueliu_doubles_duplicate']) {
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule, duplicate_key: 'dup_'+'a'.repeat(32) } })
    const payload = buildEventRoomCreation(form)
    assert.equal(payload.room_config.sub_rule, sub_rule)
    assert.equal(payload.room_config.duplicate_key, form.duplicate_key)
    if (sub_rule.endsWith('_duplicate')) {
      assert.equal(payload.duplicate_enabled, undefined)
      form.room_count = 3
      assert.throws(() => buildEventRoomCreation(form), /一组/)
    } else assert.equal(payload.duplicate_enabled, true)
  }
})
