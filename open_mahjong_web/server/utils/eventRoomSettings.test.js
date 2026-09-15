const assert = require('node:assert/strict');
const test = require('node:test');
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection, normalizeSeatUserIds, SettingsError } = require('./eventRoomSettings');

const NOW = '2026-09-10T04:00:00.000Z';
function change(doc, kind, body = {}, args = {}, options = {}) {
  return applyRoomSettingsChange(doc, {
    kind,
    body: { revision: doc.revision, ...body },
    userId: 10001,
    eventStatus: 'active',
    ...args,
  }, { now: NOW, ...options });
}
function createPreset(doc = readRoomSettings({}), id = 'preset-1', body = {}) {
  return change(doc, 'preset-create', { name: '标准比赛', ...body }, {}, { id });
}
function errorStatus(status, pattern) {
  return (error) => {
    assert.ok(error instanceof SettingsError);
    assert.equal(error.statusCode, status);
    if (pattern) assert.match(error.message, pattern);
    return true;
  };
}
function deepFreeze(value) {
  Object.freeze(value);
  for (const child of Object.values(value)) {
    if (child && typeof child === 'object' && !Object.isFrozen(child)) deepFreeze(child);
  }
  return value;
}

test('empty and invalid stored rows return fresh independent defaults matching the Guobiao editor', () => {
  const first = readRoomSettings({});
  assert.equal(first.version, 2);
  assert.equal(first.revision, 0);
  assert.deepEqual(first.manual.room_config, {
    room_name: '', sub_rule: 'guobiao/standard', game_round: 4, round_timer: 20,
    step_timer: 5, tips: false, tourist_limit: false, allow_spectator: true,
    hepai_limit: 8, open_cuohe: false, cuohe_type: 0, tactical_call: true, claim_protection: true,
  });
  assert.deepEqual(first.auto_match, { enabled: false, ...first.manual, updated_by: null });
  assert.deepEqual(readRoomSettings('not-json'), first);
  assert.deepEqual(readRoomSettings(null), first);
  first.manual.room_config.step_timer = 80;
  assert.equal(first.auto_match.room_config.step_timer, 5);
  assert.equal(readRoomSettings({}).manual.room_config.step_timer, 5);
});

test('the default is shared with automatic matching until a named preset is selected, without input mutation', () => {
  const { settings: initial } = createPreset();
  deepFreeze(initial);
  const { settings: manual } = change(initial, 'manual', {
    room_config: { step_timer: 0, round_timer: 0, tactical_call: false },
    password: 'must-not-be-saved', reason: 'not-a-setting',
  });
  assert.equal(initial.manual.room_config.step_timer, 5);
  assert.equal(manual.manual.room_config.step_timer, 0);
  assert.equal(manual.manual.room_config.round_timer, 0);
  assert.equal(manual.manual.room_config.tactical_call, false);
  assert.equal(manual.auto_match.room_config.step_timer, 0);
  assert.equal(manual.presets[0].room_config.step_timer, 5);
  const { settings: automatic } = change(manual, 'auto', {
    enabled: true, preset_id: 'preset-1',
  });
  assert.equal(automatic.auto_match.enabled, true);
  assert.equal(automatic.auto_match.updated_by, 10001);
  assert.equal(automatic.auto_match.room_config.game_round, 4);
  assert.equal(automatic.manual.room_config.step_timer, 0);
  assert.equal(automatic.presets[0].room_config.game_round, 4);
  assert.equal(JSON.stringify(automatic).includes('must-not-be-saved'), false);
  automatic.manual.room_config.step_timer = 30;
  assert.equal(manual.manual.room_config.step_timer, 0);
  assert.equal(automatic.auto_match.room_config.step_timer, 5);
});

test('all mutations reject stale, missing and noninteger revisions with the latest document', () => {
  const doc = createPreset().settings;
  for (const kind of ['manual', 'auto', 'preset-create', 'preset-update', 'preset-delete']) {
    for (const revision of [undefined, -1, '1', 0, 1.5, NaN, Infinity]) {
      assert.throws(() => applyRoomSettingsChange(doc, { kind, body: { revision } }), (error) => {
        errorStatus(409)(error);
        assert.deepEqual(error.data, doc);
        assert.notStrictEqual(error.data, doc);
        return true;
      });
    }
  }
  assert.equal(doc.revision, 1);
});

test('editing the selected preset updates future automatic tables; deletion requires changing the selection first', () => {
  let { settings, savedPresetId } = createPreset(undefined, 'preset-1', { name: '  快速赛  ', room_config: { step_timer: 3 } });
  assert.equal(savedPresetId, 'preset-1');
  assert.equal(settings.presets[0].name, '快速赛');
  assert.equal(settings.presets[0].created_at, NOW);
  settings = change(settings, 'manual', { room_config: { step_timer: 2 } }).settings;
  settings = change(settings, 'auto', { enabled: true, preset_id: 'preset-1' }).settings;
  const previousTable = resolveRoomSelection(settings, { revision: settings.revision, preset_id: 'preset-1' });
  const later = '2026-09-10T05:00:00.000Z';
  settings = change(settings, 'preset-update', { name: '慢速赛', room_config: { step_timer: 50 } },
    { presetId: 'preset-1' }, { now: () => new Date(later) }).settings;
  assert.equal(settings.presets[0].preset_id, 'preset-1');
  assert.equal(settings.presets[0].created_at, NOW);
  assert.equal(settings.presets[0].updated_at, later);
  assert.equal(settings.manual.room_config.step_timer, 2);
  assert.equal(settings.auto_match.room_config.step_timer, 50);
  assert.equal(previousTable.room_config.step_timer, 3);
  assert.throws(() => change(settings, 'preset-delete', {}, { presetId: 'preset-1' }), errorStatus(400, /先切换/));
  settings = change(settings, 'auto', { enabled: false }).settings;
  assert.throws(() => change(settings, 'preset-delete', {}, { presetId: 'preset-1' }), errorStatus(400, /先切换/));
  settings = change(settings, 'auto', { preset_id: null }).settings;
  const snapshots = structuredClone({ manual: settings.manual, auto_match: settings.auto_match });
  settings = change(settings, 'preset-delete', {}, { presetId: 'preset-1' }).settings;
  snapshots.manual.preset_id = null;
  snapshots.auto_match.preset_id = null;
  assert.deepEqual(settings.manual, snapshots.manual);
  assert.deepEqual(settings.auto_match, snapshots.auto_match);
  assert.deepEqual(settings.presets, []);
  assert.equal(settings.revision, 7);
});

test('automatic selection requires a stored preset and cannot carry a detached custom snapshot', () => {
  const settings = createPreset().settings;
  assert.throws(() => change(settings, 'auto', { preset_id: 'missing' }), errorStatus(400, /不存在/));
  assert.throws(() => change(settings, 'auto', { preset_id: 1 }), errorStatus(400, /ID/));
  assert.throws(() => change(settings, 'auto', { preset_id: 'preset-1', room_config: { step_timer: 17 } }), errorStatus(400, /请选择/));
  assert.throws(() => change(settings, 'auto', { room_rule: 'riichi' }), errorStatus(400, /请选择/));
  assert.throws(() => change(settings, 'manual', { preset_id: 'preset-1' }), errorStatus(400, /默认配置/));
  const selected = change(settings, 'auto', { preset_id: 'preset-1' }).settings;
  assert.equal(selected.auto_match.room_config.step_timer, 5);
  assert.equal(selected.manual.preset_id, null);
});

test('closed venues can disable automatic matching but cannot enable it', () => {
  const defaults = readRoomSettings({});
  for (const eventStatus of ['registered', 'closed', undefined]) {
    assert.throws(() => change(defaults, 'auto', { enabled: true }, { eventStatus }), errorStatus(400, /已开启/));
  }
  const configured = change(defaults, 'manual', { room_config: { step_timer: 2 } }).settings;
  const enabled = change(configured, 'auto', { enabled: true }).settings;
  const disabled = change(enabled, 'auto', { enabled: false }, { eventStatus: 'closed', userId: 10002 }).settings;
  assert.equal(disabled.auto_match.enabled, false);
  assert.equal(disabled.auto_match.room_config.step_timer, 2);
  assert.equal(disabled.auto_match.updated_by, 10002);
  assert.throws(() => change(defaults, 'auto', { enabled: 'false' }), errorStatus(400, /布尔/));
  assert.throws(() => change(defaults, 'auto', { enabled: true }, { userId: null }), errorStatus(400, /管理员/));
});

test('numbers and booleans are strict, including boundaries and mutual exclusions', () => {
  const doc = readRoomSettings({});
  const invalid = [
    { game_round: 0 }, { game_round: 5 }, { game_round: 1.1 }, { game_round: '2' },
    { round_timer: -1 }, { round_timer: 1001 }, { round_timer: false },
    { step_timer: 101 }, { step_timer: NaN }, { step_timer: null },
    { hepai_limit: 0 }, { hepai_limit: 65 }, { cuohe_type: 2 },
    { tactical_call: 'false' }, { claim_protection: 0 }, { allow_spectator: null },
    { tips: true, open_cuohe: true }, { sub_rule: 'riichi/standard' },
  ];
  for (const room_config of invalid) {
    assert.throws(() => change(doc, 'manual', { room_config }), errorStatus(400));
  }
  const valid = change(doc, 'manual', { room_config: {
    game_round: 4, round_timer: 1000, step_timer: 100, hepai_limit: 64,
    open_cuohe: true, cuohe_type: 1, tactical_call: false,
  } }).settings.manual.room_config;
  assert.equal(valid.step_timer, 100);
  assert.equal(valid.tactical_call, false);
  assert.equal(valid.cuohe_type, 1);
});

test('unknown config fields and nested secrets cannot be stored by any settings editor', () => {
  const doc = createPreset().settings;
  for (const key of ['password', 'random_seed', 'reason', 'admin', 'created_by', 'room_type', '__proto__']) {
    const room_config = JSON.parse(`{"${key}":"private"}`);
    for (const kind of ['manual', 'auto', 'preset-create', 'preset-update']) {
      assert.throws(() => change(doc, kind, { room_config, name: '新设置' }, { presetId: 'preset-1' }), errorStatus(400));
    }
  }
  assert.throws(() => change(doc, 'manual', { room_config: [] }), errorStatus(400));
  assert.throws(() => change(doc, 'manual', { room_config: null }), errorStatus(400));
});

test('all seven supported rules have the correct common defaults and rule changes reset irrelevant config', () => {
  const doc = readRoomSettings({});
  const rules = ['guobiao', 'qingque', 'classical', 'riichi', 'sichuan', 'changsha', 'taiwan'];
  for (const room_rule of rules) {
    const settings = change(doc, 'manual', { room_rule, room_config: { room_name: ' 测试桌 ' } }).settings;
    assert.equal(settings.manual.room_rule, room_rule);
    assert.equal(settings.manual.room_config.room_name, '测试桌');
    assert.equal(settings.manual.room_config.round_timer, 20);
    if (room_rule !== 'guobiao') assert.equal('hepai_limit' in settings.manual.room_config, false);
  }
  const riichi = change(doc, 'manual', { room_rule: 'riichi' }).settings;
  assert.equal(riichi.manual.room_config.sub_rule, 'riichi/standard');
  assert.equal('tactical_call' in riichi.manual.room_config, false);
  assert.throws(() => change(doc, 'manual', { room_rule: 'unknown' }), errorStatus(400));
  assert.throws(() => change(doc, 'manual', { room_rule: 'changsha', room_config: { game_round: 3 } }), errorStatus(400));
});

test('preset names and IDs reject duplicates and the 50-preset limit still permits editing and deletion', () => {
  let settings = createPreset(undefined, 'preset-0', { name: '比赛 0' }).settings;
  assert.throws(() => createPreset(settings, 'preset-other', { name: ' 比赛 0 ' }), errorStatus(400, /已存在/));
  assert.throws(() => createPreset(settings, 'preset-0', { name: '第二预设' }), errorStatus(409, /ID/));
  for (const name of ['', '  ', 'a'.repeat(41), '换\n行', 123]) {
    assert.throws(() => createPreset(settings, 'preset-other', { name }), errorStatus(400));
  }
  settings = createPreset(settings, 'preset-1', { name: 'a'.repeat(40) }).settings;
  assert.throws(() => change(settings, 'preset-update', { name: '比赛 0' }, { presetId: 'preset-1' }), errorStatus(400, /已存在/));
  for (let index = 2; index < 50; index += 1) {
    settings = createPreset(settings, `preset-${index}`, { name: `比赛 ${index}` }).settings;
  }
  assert.equal(settings.presets.length, 50);
  assert.throws(() => createPreset(settings, 'preset-50', { name: '比赛 50' }), errorStatus(400, /50/));
  settings = change(settings, 'preset-update', { name: '更新比赛' }, { presetId: 'preset-0' }).settings;
  assert.equal(settings.presets.length, 50);
  settings = change(settings, 'preset-delete', {}, { presetId: 'preset-0' }).settings;
  assert.equal(createPreset(settings, 'preset-50', { name: '比赛 50' }).settings.presets.length, 50);
});

test('missing preset updates and deletions do not change the current document', () => {
  const doc = deepFreeze(createPreset().settings);
  for (const kind of ['preset-update', 'preset-delete']) {
    assert.throws(() => change(doc, kind, { name: '更新比赛' }, { presetId: 'missing' }), errorStatus(404));
  }
  assert.equal(doc.revision, 1);
  assert.equal(doc.presets[0].name, '标准比赛');
});

test('stored data is sanitized and an invalid v2 automatic preset reference fails closed', () => {
  const raw = createPreset().settings;
  raw.manual.preset_id = 'deleted-preset';
  raw.auto_match = { enabled: true, preset_id: 'missing', room_rule: 'guobiao', room_config: { password: 'secret' }, updated_by: 10001 };
  raw.presets.push({ ...raw.presets[0], name: '重复 ID' });
  raw.presets.push({ ...raw.presets[0], preset_id: 'invalid-config', room_config: { step_timer: '5' } });
  raw.password = 'secret';
  const normalized = readRoomSettings(JSON.stringify(raw));
  assert.equal(normalized.manual.preset_id, null);
  assert.equal(normalized.auto_match.enabled, false);
  assert.equal(normalized.presets.length, 1);
  assert.equal(JSON.stringify(normalized).includes('secret'), false);
});

test('per-table selections resolve the confirmed revision without changing the shared default', () => {
  const doc = createPreset(undefined, 'fast', { name: '快速赛', room_config: { step_timer: 2 } }).settings;
  deepFreeze(doc);
  const table = resolveRoomSelection(doc, { revision: 1, preset_id: 'fast' });
  assert.equal(table.preset_name, '快速赛');
  assert.equal(table.room_config.step_timer, 2);
  assert.equal(doc.manual.room_config.step_timer, 5);
  assert.equal(doc.auto_match.room_config.step_timer, 5);
  assert.equal(doc.revision, 1);
  assert.equal(resolveRoomSelection(doc, { revision: 1, preset_id: null }).preset_name, '默认配置');
  table.room_config.step_timer = 90;
  assert.equal(doc.presets[0].room_config.step_timer, 2);
  assert.throws(() => resolveRoomSelection(doc, { revision: 0, preset_id: 'fast' }), errorStatus(409));
  assert.throws(() => resolveRoomSelection(doc, { revision: 1, preset_id: 'gone' }), errorStatus(400));
});

test('seat IDs require four distinct positive integers instead of parseInt coercion', () => {
  const ids = [101, 102, 103, 104];
  assert.deepEqual(normalizeSeatUserIds(ids), ids);
  assert.notStrictEqual(normalizeSeatUserIds(ids), ids);
  for (const invalid of [null, [], [1, 2, 3], [1, 2, 3, 3], ['1', 2, 3, 4], ['1x', 2, 3, 4], [0, 2, 3, 4], [1.5, 2, 3, 4]]) {
    assert.throws(() => normalizeSeatUserIds(invalid), errorStatus(400));
  }
});

test('v1 detached automatic settings become a deterministic named preset without changing old rules', () => {
  const old = createPreset(undefined, 'fast', { name: '快速赛', room_config: { step_timer: 2 } }).settings;
  old.version = 1;
  old.manual.preset_id = 'fast';
  old.auto_match = { ...old.auto_match, enabled: true, preset_id: 'fast', room_config: { step_timer: 17 }, updated_by: 10002 };
  deepFreeze(old);
  const migrated = readRoomSettings(old);
  assert.equal(migrated.version, 2);
  assert.equal(migrated.revision, old.revision);
  assert.equal(migrated.manual.preset_id, null);
  assert.equal(migrated.auto_match.enabled, true);
  assert.equal(migrated.auto_match.room_config.step_timer, 17);
  assert.equal(migrated.presets[0].room_config.step_timer, 2);
  assert.equal(migrated.presets[1].name, '原自动匹配设置');
  assert.equal(migrated.auto_match.preset_id, migrated.presets[1].preset_id);
  assert.match(migrated.auto_match.preset_id, /^legacy-auto-/);
  assert.deepEqual(readRoomSettings(old), migrated);
  assert.deepEqual(readRoomSettings(migrated), migrated);
  assert.equal(old.presets.length, 1);
  const persisted = change(old, 'preset-create', { name: '其他比赛' }, {}, { id: 'other' }).settings;
  assert.equal(persisted.version, 2);
  assert.equal(persisted.revision, old.revision + 1);
  assert.equal(persisted.auto_match.preset_id, migrated.auto_match.preset_id);
});

test('v1 migration binds equal configurations to the default or an existing named preset', () => {
  const old = createPreset(undefined, 'fast', { name: '快速赛', room_config: { step_timer: 2 } }).settings;
  old.version = 1;
  assert.equal(readRoomSettings(old).auto_match.preset_id, null);
  old.auto_match.room_config = { ...old.presets[0].room_config };
  const migrated = readRoomSettings(old);
  assert.equal(migrated.auto_match.preset_id, 'fast');
  assert.equal(migrated.presets.length, 1);
  const changed = change(migrated, 'preset-update', { room_config: { step_timer: 11 } }, { presetId: 'fast', userId: 10002 }).settings;
  assert.equal(changed.auto_match.room_config.step_timer, 11);
  assert.equal(changed.auto_match.updated_by, 10002);
  assert.equal(changed.manual.room_config.step_timer, 5);
});

test('a full v1 preset list preserves all 50 rows plus its automatic custom configuration', () => {
  const old = readRoomSettings({});
  old.version = 1;
  old.presets = Array.from({ length: 50 }, (_, index) => ({
    preset_id: `old-${index}`, name: index === 0 ? '原自动匹配设置' : `旧比赛 ${index}`,
    room_rule: 'guobiao', room_config: { step_timer: 2 }, created_at: NOW, updated_at: NOW,
  }));
  old.auto_match.room_config = { step_timer: 17 };
  const migrated = readRoomSettings(old);
  assert.equal(migrated.presets.length, 51);
  assert.equal(migrated.presets[50].name, '原自动匹配设置 2');
  assert.equal(migrated.auto_match.room_config.step_timer, 17);
  assert.equal(readRoomSettings(migrated).presets.length, 51);
  assert.throws(() => createPreset(migrated, 'extra', { name: '新增' }), errorStatus(400, /50/));
  const renamed = change(migrated, 'preset-update', { name: '旧比赛重命名' }, { presetId: 'old-1' }).settings;
  assert.equal(renamed.presets.length, 51);
  assert.equal(renamed.auto_match.room_config.step_timer, 17);
});
