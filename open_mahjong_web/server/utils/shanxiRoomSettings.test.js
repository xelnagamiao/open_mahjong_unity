const test = require('node:test');
const assert = require('node:assert/strict');
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('./eventRoomSettings');

function change(settings, kind, body, options) {
  return applyRoomSettingsChange(settings, { kind, userId: 101, eventStatus: 'active',
    body: { revision: settings.revision, ...body } }, options).settings;
}

test('Shanxi room defaults store a 20 second hand bank and a 5 second step', () => {
  const saved = change(readRoomSettings({}), 'manual', { room_rule: 'shanxi', room_config: {} });
  assert.equal(saved.manual.room_config.round_timer, 20);
  assert.equal(saved.manual.room_config.step_timer, 5);
  assert.equal(saved.manual.room_config.sub_rule, 'shanxi/mil2023');
});

for (const [bank, step] of [[20, 5], [11, 9], [0, 9], [11, 0], [0, 0]]) {
  test(`Shanxi ${bank}+${step} survives save, reload, preset selection and automatic matching`, () => {
    const config = { round_timer: bank, step_timer: step };
    let saved = change(readRoomSettings({}), 'manual', { room_rule: 'shanxi', room_config: config });
    saved = readRoomSettings(JSON.parse(JSON.stringify(saved)));
    assert.equal(saved.manual.room_config.round_timer, bank);
    assert.equal(saved.manual.room_config.step_timer, step);
    saved = change(saved, 'preset-create', { name: '山西计时', room_rule: 'shanxi', room_config: config },
      { id: 'shanxi-timing', now: '2026-10-05T00:00:00Z' });
    saved = change(saved, 'auto', { enabled: true, preset_id: 'shanxi-timing' });
    for (const selected of [saved.auto_match, resolveRoomSelection(saved, {
      revision: saved.revision, preset_id: 'shanxi-timing',
    })]) {
      assert.equal(selected.room_rule, 'shanxi');
      assert.equal(selected.room_config.round_timer, bank);
      assert.equal(selected.room_config.step_timer, step);
    }
  });
}
