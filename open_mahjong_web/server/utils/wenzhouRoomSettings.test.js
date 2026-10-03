const test = require('node:test');
const assert = require('node:assert/strict');
const { normalizeWenzhouRoomConfig: normalize } = require('./wenzhouRoomSettings');
class SettingsError extends Error { constructor(status, message) { super(message); this.status = status; } }
const valid = (raw = {}) => normalize(raw, SettingsError);

test('canonical edition, all ordinary switches and numeric endpoints', () => {
  assert.equal(valid().detailed_config.edition, 'mil-wenzhou-2024-om1');
  assert.equal(valid({ room_name: ' 温州 ' }).room_name, '温州');
  for (const key of ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator']) {
    for (const value of [true, false]) assert.equal(valid({ [key]: value })[key], value);
    assert.throws(() => valid({ [key]: 'false' }), SettingsError);
  }
  for (const [key, lo, hi] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    for (const value of [lo, hi]) assert.equal(valid({ [key]: value })[key], value);
    for (const value of [lo-1, hi+1, 1.5, true, '1', null]) assert.throws(() => valid({ [key]: value }), SettingsError);
  }
  assert.equal(valid({ duplicate_key: ' ', detailed_config: {} }).sub_rule, 'wenzhou/mil2024');
});
test('rejects unknown rules, ambiguous coercions and unpublished variants', () => {
  for (const value of [null, [], false, '']) assert.throws(() => valid(value), SettingsError);
  for (const config of [{ unused: true }, { duplicate_key: 3 }, { duplicate_key: 'dup_x' },
    { room_name: 3 }, { room_name: 'x'.repeat(129) }, { sub_rule: 'wenzhou/folk' },
    { detailed_config: null }, { detailed_config: [] }, { detailed_config: { edition: 'future' } },
    { detailed_config: { wildcards: false } }]) assert.throws(() => valid(config), SettingsError);
  for (const key of ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he']) {
    assert.equal(valid({ [key]: false })[key], false);
    for (const value of [true, 'false', 0]) assert.throws(() => valid({ [key]: value }), SettingsError);
  }
});
