import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createDuplicateDraft, duplicateRoundLabel, duplicateRoundSeats, duplicateDealerSeat, duplicateWallLayout, duplicateRuleWithFlowers, duplicateZoneLimit, flattenDuplicateDraft, splitDuplicateWall, validateDuplicateDraft, moveDuplicateTile, isDuplicateWallRule } from '../src/utils/duplicateWalls.js'
import { createEventRoomCreationForm, buildEventRoomCreation } from '../src/utils/eventRoomCreation.js'
const require = createRequire(import.meta.url)
const { catalog, normalizeInput, fullWall, wallDto } = require('../../server/services/duplicateWalls.js')
const rule = catalog().rules.find(item => item.rule === 'sichuan/sbr')

for (const [ruleId, layout, wallCount] of [['sichuan/sbr', 'sbr', 14], ['sichuan/xueliu', 'xueliu', 8]]) {
  test(`${ruleId} never falls back to Guobiao hands and rejects honors, flowers and 14-tile presets`, () => {
    const spec = catalog().rules.find(item => item.rule === ruleId)
    const withoutLayout = { ...spec, wall_layout: undefined }
    assert.equal(duplicateWallLayout(withoutLayout), layout)
    assert.equal(duplicateRuleWithFlowers(withoutLayout, true).use_flowers, false)
    assert.equal(duplicateRuleWithFlowers(withoutLayout).wall_layout, layout)
    assert.equal(spec.tile_count, 108)
    assert.equal(spec.tiles.length, 27)
    assert.ok(spec.tiles.every(({ tile, count }) => tile >= 11 && tile <= 39 && count === 4))
    const parts = splitDuplicateWall(fullWall(ruleId), 108, layout)
    const draft = parts.map((tiles, seat) => ({ hand: seat < 4 ? tiles.slice(0, 13) : [], wall: seat < 4 ? tiles.slice(13) : tiles }))
    for (let round = 1; round <= 16; round++) {
      const dealer = duplicateDealerSeat(round, layout)
      assert.equal(dealer, (round - 1) % 4)
      assert.equal(validateDuplicateDraft(draft, withoutLayout, dealer), '')
      assert.ok(draft.slice(0, 4).every(seat => seat.hand.length === 13 && seat.wall.length === wallCount))
      const badDealer = structuredClone(draft)
      badDealer[dealer].hand.push(badDealer[dealer].wall.shift())
      assert.match(validateDuplicateDraft(badDealer, spec, dealer), /13/)
    }
    for (const tile of [41, 47, 51, '11', true]) {
      const invalid = structuredClone(draft)
      invalid[0].hand[0] = tile
      assert.notEqual(validateDuplicateDraft(invalid, spec), '')
    }
  })
}

test('SBR key editor uses fixed seats and 13+14 partitions in every board', () => {
  assert.ok(isDuplicateWallRule(rule.rule))
  assert.equal(rule.wall_layout, 'sbr')
  assert.deepEqual(rule.scopes, ['personal', 'event'])
  const tiles = fullWall(rule.rule)
  const draft = createDuplicateDraft(rule.wall_layout)
  splitDuplicateWall(tiles, rule.tile_count, rule.wall_layout).forEach((part, seat) => {
    draft[seat] = { hand: part.slice(0, 13), wall: part.slice(13) }
  })
  for (let round = 1; round <= 16; round++) {
    assert.deepEqual(duplicateRoundSeats(round, rule.wall_layout), [0, 1, 2, 3])
    assert.equal(duplicateRoundLabel(round, rule.wall_layout), `第 ${round} 副`)
    assert.equal(validateDuplicateDraft(draft, rule, (round - 1) % 4), '')
    for (let seat = 0; seat < 4; seat++) {
      assert.equal(duplicateZoneLimit(108, seat, 'hand', (round - 1) % 4, 'sbr'), 13)
      assert.equal(duplicateZoneLimit(108, seat, 'wall', (round - 1) % 4, 'sbr'), 14)
    }
  }
  assert.deepEqual(flattenDuplicateDraft(draft), tiles)
  const legacyDealer = structuredClone(draft)
  legacyDealer[0].hand.push(legacyDealer[0].wall.shift())
  assert.match(validateDuplicateDraft(legacyDealer, rule), /13/)
  assert.equal(moveDuplicateTile(draft, { seat: 0, zone: 'wall', index: 0 }, { seat: 1, zone: 'wall' }, 108, 0, 'sbr'), false)
})

for (const wall_type of ['manual', 'seed', 'key']) {
  test(`SBR ${wall_type} keys preserve legal walls and use the same room input`, () => {
    const input = { scope: 'event', event_id: 'venue-A', rule: rule.rule, wall_type, round_count: 8,
      seed: 'SBR-key-test', round_tiles: Array.from({ length: 8 }, () => fullWall(rule.rule)) }
    const normalized = normalizeInput(input)
    assert.equal(normalized.round_tiles.length, 8)
    assert.equal(normalized.use_flowers, false)
    for (const wall of normalized.round_tiles) assert.deepEqual([...wall].sort(), fullWall(rule.rule))
    assert.equal(wallDto(normalized, true).wall_layout, 'sbr')
    if (wall_type === 'key') {
      assert.equal(wallDto(normalized, true).tiles, undefined)
      assert.equal(wallDto(normalized, true).seed, undefined)
    } else if (wall_type === 'seed') assert.deepEqual(normalizeInput(input).round_tiles, normalized.round_tiles)
    else assert.deepEqual(normalized.round_tiles, input.round_tiles)
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: {
      sub_rule: 'sichuan/doubles_duplicate', duplicate_key: 'dup_' + 'a'.repeat(32),
    } })
    assert.equal(form.duplicate_key_source, 'existing')
    assert.equal(buildEventRoomCreation(form).room_config.duplicate_key, form.duplicate_key)
  })
}

test('both paired rules default automatic and validate switching to existing keys', () => {
  for (const sub_rule of ['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate']) {
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule } })
    assert.equal(form.duplicate_key_source, 'auto')
    assert.equal(form.duplicate_round_count, sub_rule === 'sichuan/doubles_duplicate' ? 8 : 16)
    assert.equal(buildEventRoomCreation(form).room_config.duplicate_key, undefined)
    form.duplicate_key_source = 'existing'
    assert.throws(() => buildEventRoomCreation(form), /请输入复式密钥/)
    form.duplicate_key = 'dup_' + 'b'.repeat(32)
    assert.equal(buildEventRoomCreation(form).room_config.duplicate_key, form.duplicate_key)
    form.room_count = 3
    assert.throws(() => buildEventRoomCreation(form), /一组/)
    form.duplicate_key_source = 'auto'
    assert.equal(buildEventRoomCreation(form).room_config.duplicate_key, undefined, 'automatic never sends a stale key')
    assert.equal(buildEventRoomCreation(form).room_count, 3)
  }
})
