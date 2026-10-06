const test = require('node:test');
const assert = require('node:assert/strict');
const { readRoomSettings, applyRoomSettingsChange } = require('./eventRoomSettings');

function save(room_config, kind = 'manual') {
  const doc = readRoomSettings({});
  return applyRoomSettingsChange(doc, { kind, userId: 10001, eventStatus: 'active', body: {
    revision: doc.revision, room_rule: 'guizhou', room_config, name: '贵州',
  } }).settings;
}

test('Guizhou manual settings and presets preserve all 256 round/switch combinations', () => {
  for (let game_round = 1; game_round <= 4; game_round++) for (let bits = 0; bits < 64; bits++) {
    const options = { game_round };
    for (const [i, key] of ['tips','count_tips','pointer_tips','tourist_limit','allow_spectator','tactical_call'].entries()) options[key] = Boolean(bits & (1 << i));
    for (const kind of ['manual','preset-create']) {
      const doc = save(options, kind);
      const entry = kind === 'manual' ? doc.manual : doc.presets[0];
      for (const [key, value] of Object.entries(options)) assert.equal(entry.room_config[key], value);
      assert.equal(entry.room_config.detailed_config.rule_version, 'mil-guizhou-2023-om1');
      assert.deepEqual(readRoomSettings(doc), doc);
    }
  }
});

test('Guizhou rejects malformed timers, flags, house rules and unsupported families', () => {
  const inputs = [null, [], { sub_rule: 'guizhou/joker' }, { duplicate_key: 'anything' },
    { game_round: true }, { game_round: 0 }, { game_round: 5 }, { game_round: 1.5 },
    { round_timer: '20' }, { step_timer: 101 }, { room_name: 'x'.repeat(129) },
    { detailed_config: null }, { detailed_config: { rule_version: 'other' } }, { detailed_config: { chicken: false } }, { extra: true }];
  for (const key of ['tips','count_tips','pointer_tips','tourist_limit','allow_spectator','tactical_call']) inputs.push({ [key]: 'false' });
  for (const key of ['use_flowers','open_cuohe','claim_protection','tian_di_ren_he']) inputs.push({ [key]: true });
  for (const input of inputs) assert.throws(() => save(input), { statusCode: 400 });
});

test('Guizhou canonical defaults and boundary times agree with the room server', () => {
  const defaults = save({}).manual.room_config;
  assert.equal(defaults.round_timer, 20); assert.equal(defaults.step_timer, 5); assert.equal(defaults.tactical_call, true);
  for (const round_timer of [0,1000]) for (const step_timer of [0,100]) {
    const config = save({ round_timer, step_timer }).manual.room_config;
    assert.equal(config.round_timer, round_timer); assert.equal(config.step_timer, step_timer);
    assert.equal(config.sub_rule, 'guizhou/standard');
    assert.equal(config.use_flowers, false); assert.equal(config.claim_protection, false);
  }
});
