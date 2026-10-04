const test = require('node:test');
const assert = require('node:assert/strict');
const { readRoomSettings, applyRoomSettingsChange } = require('./eventRoomSettings');

function save(room_config) {
  const doc = readRoomSettings({});
  return applyRoomSettingsChange(doc, { kind: 'manual', userId: 101, eventStatus: 'active', body: {
    revision: doc.revision, room_rule: 'guangdong', room_config,
  } }).settings.manual;
}

test('Guangdong exposes only the MIL 2024 no-wildcard Tuidao edition', () => {
  const result = save({});
  assert.equal(result.room_rule, 'guangdong');
  assert.equal(result.room_config.sub_rule, 'guangdong/tuidao_mil2024');
  assert.deepEqual(result.room_config.detailed_config, {
    edition: 'mil-tuidao-2024-om1', fan_cap: 32, wildcards: false,
  });
  for (const sub_rule of ['guangdong/standard', 'tuidao/mil2024', 'guangdong/wildcards']) {
    assert.throws(() => save({ sub_rule }));
  }
});

test('fixed rules reject unknown keys, unsupported versions and type coercion', () => {
  for (const detailed_config of [null, [], { wildcards: true }, { wildcards: 0 }, { fan_cap: 64 },
    { fan_cap: '32' }, { edition: 'next' }, { flowers: false }]) {
    assert.throws(() => save({ detailed_config }));
  }
  const raw = { wildcards: false };
  assert.equal(save({ detailed_config: raw }).room_config.detailed_config.fan_cap, 32);
  assert.deepEqual(raw, { wildcards: false });
});

test('hand count, clocks and all room toggles survive canonical storage', () => {
  for (const game_round of [1, 2, 3, 4]) for (let bits=0; bits<8; bits++) {
    const raw = { game_round, round_timer: 45, step_timer: 8,
      tips: !!(bits & 1), tourist_limit: !!(bits & 2), allow_spectator: !!(bits & 4) };
    const saved = save(raw).room_config;
    for (const [key, value] of Object.entries(raw)) assert.equal(saved[key], value);
  }
});
