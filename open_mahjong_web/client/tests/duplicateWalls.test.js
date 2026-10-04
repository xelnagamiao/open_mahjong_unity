import test from 'node:test'
import assert from 'node:assert/strict'
import { DUPLICATE_ROUND_COUNTS, duplicateRoundSeats, duplicateRoundLabel, createDuplicateDraft, duplicateRuleWithFlowers, duplicateZoneLimit, flattenDuplicateDraft, moveDuplicateTile, validateDuplicateDraft, duplicateRoomLabel, isDuplicateGameRecord, isDuplicateWallRule, splitDuplicateWall, tileCounts, validateDuplicateSeed, validateManualWall } from '../src/utils/duplicateWalls.js'
import { saveLocalGameRecord } from '../src/utils/localGameRecordStore.js'
import { duplicateReplayWall } from '../src/utils/duplicateReplayWall.js'
import { buildEventRoomSettings, clearUnsupportedDuplicateRoom, createEventRoomForm, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

const catalog = { tile_count: 8, tiles: [{ tile: 11, count: 4 }, { tile: 12, count: 4 }] }

test('series hand limits follow Guobiao dealer rotation and original player numbers', () => {
  assert.deepEqual(DUPLICATE_ROUND_COUNTS, [1, 4, 8, 12, 16])
  const dealers = [0, 1, 2, 3, 1, 0, 3, 2, 2, 3, 1, 0, 3, 2, 0, 1]
  for (let round = 1; round <= 16; round++) {
    const seats = duplicateRoundSeats(round)
    assert.equal(seats.indexOf(0), dealers[round - 1])
    for (const count of [136, 144]) {
      for (let seat = 0; seat < 4; seat++) {
        const hand = duplicateZoneLimit(count, seat, 'hand', seats.indexOf(0))
        assert.equal(hand, seat === dealers[round - 1] ? 14 : 13)
        assert.equal(hand + duplicateZoneLimit(count, seat, 'wall', seats.indexOf(0)), count / 4)
      }
    }
  }
  assert.equal(duplicateRoundLabel(16), '北四局')
})

test('public room descriptions use type and count without disclosing keys', () => {
  for (const duplicate_key of [undefined, 'dup_secret']) {
    const settings = { room_rule: 'guobiao', room_config: { is_duplicate: true, duplicate_wall_type: 'key', duplicate_round_count: 12, duplicate_key } }
    const rows = eventRoomSettingsRows(settings)
    assert.equal(rows.find(row => row.label === '圈数').value, '12 局')
    assert.equal(rows.find(row => row.label === '复式').value, '密钥牌山')
    assert.equal(JSON.stringify(rows).includes('dup_secret'), false)
  }
})

test('manual wall preserves ordered four seat piles and enforces the full legal multiset', () => {
  const wall = [11, 12, 11, 11, 12, 12, 11, 12]
  assert.equal(validateManualWall(wall, catalog), '')
  assert.deepEqual(splitDuplicateWall(wall, 8), [[11, 12], [11, 11], [12, 12], [11, 12]])
  assert.deepEqual(tileCounts(wall), { 11: 4, 12: 4 })
  assert.match(validateManualWall(wall.slice(1), catalog), /补齐/)
  assert.match(validateManualWall([11, 11, 11, 11, 11, 12, 12, 12], catalog), /超过/)
  assert.match(validateManualWall([11, 12, 11, 12, 11, 12, 11, 99], catalog), /不支持/)
})

test('text seeds are accepted without narrowing to numeric seeds or losing precision', () => {
  for (const seed of ['1', '0x1', '复式 第一轮', '1234567890123456789012345678901234567890', 'x'.repeat(128)]) assert.equal(validateDuplicateSeed(seed), '')
  for (const seed of ['', '   ', 'x'.repeat(129)]) assert.match(validateDuplicateSeed(seed), /1–128/)
})

test('Guobiao event rooms retain duplicate keys through editing and submission', () => {
  for (const sub_rule of ['guobiao/standard', 'guobiao/xiaolin', 'guobiao/kshen', 'guobiao/lanshi']) {
    const form = createEventRoomForm({ room_rule: 'guobiao', room_config: { sub_rule, duplicate_key: '  DU-review-key  ' } })
    const result = buildEventRoomSettings(form)
    assert.equal(result.room_config.duplicate_key, 'DU-review-key')
    assert.equal(createEventRoomForm(result).duplicate_key, 'DU-review-key')
    assert.equal(eventRoomSettingsRows(result).some(row => row.label === '复式密钥'), false)
    assert.equal(eventRoomSettingsRows(result).find(row => row.label === '圈数').value, '跟随密钥设置')
    assert.equal(result.room_config.sub_rule, sub_rule)
    form.duplicate_key = ''
    assert.equal('duplicate_key' in buildEventRoomSettings(form).room_config, false)
  }
})

test('duplicate wall catalog only allows Guobiao and its no-flower wall variant', () => {
  assert.equal(isDuplicateWallRule('guobiao'), true)
  assert.equal(isDuplicateWallRule('guobiao/lanshi'), true)
  for (const rule of ['riichi', 'qingque', 'classical', 'sichuan', 'changsha', 'taiwan', 'jiandan', 'hongque', 'guobiao/unsupported', '', undefined]) {
    assert.equal(isDuplicateWallRule(rule), false)
  }
})

test('non-Guobiao saved presets discard legacy duplicate keys and preserve ordinary rule settings', () => {
  for (const room_rule of ['riichi', 'qingque', 'classical', 'sichuan', 'changsha', 'taiwan', 'jiandan', 'hongque']) {
    const settings = { room_rule, room_config: { duplicate_key: 'dup_old', starting_score: 30000 } }
    const form = createEventRoomForm(settings)
    assert.equal(form.duplicate_key, '')
    const result = buildEventRoomSettings(form)
    assert.equal('duplicate_key' in result.room_config, false)
    assert.equal('red_dora' in result.room_config, false)
    assert.equal(eventRoomSettingsRows(result).some(row => row.label === '复式密钥'), false)
    assert.equal(settings.room_config.duplicate_key, 'dup_old', 'loading must not mutate the original saved preset')
    if (room_rule === 'riichi') assert.equal(result.room_config.starting_score, 30000)
  }
})

test('switching away from Guobiao clears keys; stale injected keys cannot enter other rule payloads', () => {
  for (const room_rule of ['riichi', 'qingque', 'classical', 'sichuan', 'changsha', 'taiwan', 'jiandan', 'hongque']) {
    const form = createEventRoomForm({ room_rule: 'guobiao', room_config: { duplicate_key: 'dup_test' } })
    clearUnsupportedDuplicateRoom(form)
    assert.equal(form.duplicate_key, 'dup_test')
    form.room_rule = room_rule
    assert.throws(() => buildEventRoomSettings(form), /仅支持国标/)
    clearUnsupportedDuplicateRoom(form)
    assert.equal(form.duplicate_key, '')
    assert.equal('duplicate_key' in buildEventRoomSettings(form).room_config, false)
    form.room_rule = 'guobiao'
    clearUnsupportedDuplicateRoom(form)
    assert.equal(form.duplicate_key, '', 'switching back must not restore a removed key')
  }
})

test('rooms distinguish three duplicate wall types from legacy scenario reproduction', () => {
  assert.equal(duplicateRoomLabel({ duplicate_wall_type: 'manual' }), '手动牌山')
  assert.equal(duplicateRoomLabel({ duplicate_wall_type: 'seed' }), '复现牌山')
  assert.equal(duplicateRoomLabel({ duplicate_wall_type: 'key' }), '密钥牌山')
  assert.equal(duplicateRoomLabel({ is_player_set_random_seed: true }), '场景复现')
  assert.equal(duplicateRoomLabel({}), '普通对局')
})

test('duplicate game records are never written to the browser database', async () => {
  let opened = 0
  globalThis.indexedDB = { open() { opened++; throw new Error('should never write duplicate records') } }
  try {
    for (const marker of [{ duplicate_key: 'dup_test' }, { duplicate_wall_type: 'manual' }, { is_duplicate: true }, { duplicate_wall_id: 123 }]) {
      const detail = { game_id: 'duplicate-test', rule: 'riichi', record: { game_title: { ...marker, rule: 'riichi' }, game_round: {} } }
      assert.equal(isDuplicateGameRecord(detail), true)
      await saveLocalGameRecord(detail)
    }
    assert.equal(opened, 0)
    assert.equal(isDuplicateGameRecord({ record: { game_title: { is_player_set_random_seed: true } } }), false)
  } finally { delete globalThis.indexedDB }
})

test('duplicate replay consumes each original player’s own wall for draws, flowers, and kongs', () => {
  const round = { start_player_index: 0, seats: [0, 1, 2, 3], duplicate_walls: [[11, 12], [21, 22], [31, 32], [41, 42]], action_ticks: [
    ['bd', 41, 3], ['reset', 0], ['c', 19, false], ['d', 21], ['c', 21, true], ['p', 21, 3], ['gd', 42], ['reset', 2], ['d', 31],
  ] }
  const view = duplicateReplayWall(round, 9)
  assert.deepEqual(view.filter(tile => tile.consumed).map(tile => [tile.originalPlayer, tile.tile]), [[1, 21], [2, 31], [3, 41], [3, 42]])
  assert.equal(duplicateReplayWall(round, 0).some(tile => tile.consumed), false)
  assert.deepEqual(round.duplicate_walls, [[11, 12], [21, 22], [31, 32], [41, 42]])
  assert.equal(duplicateReplayWall({ tiles_list: [11, 12] }, 2), null)
})

test('duplicate replay maps current seats back to original seats', () => {
  const round = { start_player_index: 2, seats: [2, 0, 3, 1], duplicate_walls: [[11], [21], [31], [41]], action_ticks: [['d', 11], ['c', 11, true], ['d', 31]] }
  assert.deepEqual(duplicateReplayWall(round, 3).filter(tile => tile.consumed).map(tile => tile.originalPlayer), [0, 2])
})

test('Guobiao flower settings preserve false, fix Lanshi to false and defer entirely to duplicate walls', () => {
  const normal = createEventRoomForm()
  assert.equal(buildEventRoomSettings(normal).room_config.use_flowers, true)
  normal.use_flowers = false
  assert.equal(buildEventRoomSettings(normal).room_config.use_flowers, false)
  assert.equal(createEventRoomForm(buildEventRoomSettings(normal)).use_flowers, false)
  normal.sub_rule = 'guobiao/lanshi'
  normal.use_flowers = true
  assert.equal(buildEventRoomSettings(normal).room_config.use_flowers, false)
  normal.duplicate_key = 'dup_server_owns_flowers'
  assert.equal('use_flowers' in buildEventRoomSettings(normal).room_config, false)
  assert.equal(eventRoomSettingsRows(buildEventRoomSettings(normal)).find(row => row.label === '花牌').value, '跟随复式设置')
})

const fullCatalog = { rule: 'guobiao', tiles: [
  ...[1, 2, 3].flatMap(suit => Array.from({ length: 9 }, (_, i) => ({ tile: suit * 10 + i + 1, count: 4 }))),
  ...Array.from({ length: 7 }, (_, i) => ({ tile: 41 + i, count: 4 })),
  ...Array.from({ length: 8 }, (_, i) => ({ tile: 51 + i, count: 1 })),
] }

test('manual editor requires all four independent hands and walls before flattening into exact seat partitions', () => {
  for (const useFlowers of [true, false]) {
    const rule = duplicateRuleWithFlowers(fullCatalog, useFlowers)
    assert.equal(rule.tile_count, useFlowers ? 144 : 136)
    const all = rule.tiles.flatMap(tile => Array(tile.count).fill(tile.tile))
    const draft = createDuplicateDraft()
    let cursor = 0
    for (let seat = 0; seat < 4; seat++) {
      for (const zone of ['hand', 'wall']) {
        const length = duplicateZoneLimit(rule.tile_count, seat, zone)
        draft[seat][zone] = all.slice(cursor, cursor + length)
        cursor += length
      }
    }
    assert.deepEqual(draft.map(seat => seat.hand.length), [14, 13, 13, 13])
    assert.deepEqual(draft.map(seat => seat.wall.length), useFlowers ? [22, 23, 23, 23] : [20, 21, 21, 21])
    assert.equal(validateDuplicateDraft(draft, rule), '')
    assert.deepEqual(flattenDuplicateDraft(draft), all)
    assert.equal(moveDuplicateTile(draft, { seat: 0, zone: 'hand', index: 0 }, { seat: 1, zone: 'hand' }, rule.tile_count), false)
    draft[2].hand.pop()
    assert.match(validateDuplicateDraft(draft, rule), /3 号玩家手牌/)
  }
  assert.equal(duplicateRuleWithFlowers({ ...fullCatalog, rule: 'guobiao/lanshi' }, true).use_flowers, false)
})

test('manual tile moves preserve every unrelated seat and reorder precisely within the selected zone', () => {
  const draft = createDuplicateDraft()
  draft[0].hand.push(11, 12, 13)
  draft[2].wall.push(21, 22)
  assert.equal(moveDuplicateTile(draft, { seat: 0, zone: 'hand', index: 1 }, { seat: 1, zone: 'wall' }, 144), true)
  assert.deepEqual(draft[0].hand, [11, 13])
  assert.deepEqual(draft[1].wall, [12])
  assert.deepEqual(draft[2].wall, [21, 22])
  assert.equal(moveDuplicateTile(draft, { seat: 2, zone: 'wall', index: 0 }, { seat: 2, zone: 'wall', index: 1 }, 144), true)
  assert.deepEqual(draft[2].wall, [22, 21])
  assert.equal(moveDuplicateTile(draft, { seat: 3, zone: 'hand', index: 0 }, { seat: 1, zone: 'wall' }, 144), false)
})

test('v2 replacement draws use independent back 2/1 cursors; ordinary head draws and one-tile fallback remain correct', () => {
  const round = { start_player_index: 0, seats: [0, 1, 2, 3], duplicate_walls: [[11, 12, 13, 14, 15, 16], [21, 22, 23, 24], [31], []], action_ticks: [
    ['bd', 15, 0], ['bd', 23, 1], ['reset', 0], ['gd', 16], ['d', 11], ['gd', 13], ['gd', 14], ['gd', 12], ['reset', 1], ['gd', 24], ['bd', 31, 2],
  ] }
  const consumedAt = node => duplicateReplayWall(round, node, 2).filter(tile => tile.consumed).map(tile => tile.tile)
  assert.deepEqual(consumedAt(1), [15])
  assert.deepEqual(consumedAt(2), [15, 23])
  assert.deepEqual(consumedAt(4), [15, 16, 23])
  assert.deepEqual(consumedAt(8), [11, 12, 13, 14, 15, 16, 23])
  assert.deepEqual(consumedAt(11), [11, 12, 13, 14, 15, 16, 23, 24, 31])
  assert.deepEqual(duplicateReplayWall(round, 1).filter(tile => tile.consumed).map(tile => tile.tile), [11], 'historical records keep their original head replacement rule')
})
