const { randomUUID, createHash } = require('node:crypto');

const MAX_PRESETS = 50;
// A v1 custom automatic setting may need one extra row during migration.
const MAX_STORED_PRESETS = MAX_PRESETS + 1;
const DEFAULT_SUB_RULES = Object.freeze({
  guobiao: 'guobiao/standard',
  qingque: 'qingque/standard',
  classical: 'classical/standard',
  riichi: 'riichi/standard',
  sichuan: 'sichuan/standard',
  changsha: 'changsha/classic_double_bird',
  taiwan: 'taiwan/standard',
});
const GUOBIAO_SUB_RULES = new Set([
  'guobiao/standard', 'guobiao/xiaolin', 'guobiao/kshen', 'guobiao/lanshi',
]);
const COMMON_CONFIG_KEYS = [
  'room_name', 'sub_rule', 'game_round', 'round_timer', 'step_timer',
  'tips', 'tourist_limit', 'allow_spectator',
];
const GUOBIAO_CONFIG_KEYS = [
  ...COMMON_CONFIG_KEYS, 'hepai_limit', 'open_cuohe', 'cuohe_type',
  'tactical_call', 'claim_protection',
];
const has = (value, key) => Object.prototype.hasOwnProperty.call(value, key);
const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const isRevision = (value) => Number.isSafeInteger(value) && value >= 0;
const isId = (value) => typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$/.test(value);

class SettingsError extends Error {
  constructor(statusCode, message, data) {
    super(message);
    this.name = 'SettingsError';
    this.statusCode = statusCode;
    if (data !== undefined) this.data = data;
  }
}

function defaultConfig(rule = 'guobiao') {
  const common = {
    room_name: '',
    sub_rule: DEFAULT_SUB_RULES[rule],
    game_round: 4,
    round_timer: 20,
    step_timer: 5,
    tips: false,
    tourist_limit: false,
    allow_spectator: true,
  };
  if (rule !== 'guobiao') return common;
  return {
    ...common,
    hepai_limit: 8,
    open_cuohe: false,
    cuohe_type: 0,
    tactical_call: true,
    claim_protection: true,
  };
}

function normalizeRule(rule) {
  if (typeof rule !== 'string' || !has(DEFAULT_SUB_RULES, rule.trim())) {
    throw new SettingsError(400, '不支持的对局规则');
  }
  return rule.trim();
}

function integerField(config, key, min, max, label) {
  const value = config[key];
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    throw new SettingsError(400, `${label}必须是 ${min}–${max} 之间的整数`);
  }
}

/** Persist only settings understood by the current room editor and game server. */
function normalizeRoomConfig(rule, raw) {
  if (!isObject(raw)) throw new SettingsError(400, '对局设置必须是对象');
  const allowed = rule === 'guobiao' ? GUOBIAO_CONFIG_KEYS : COMMON_CONFIG_KEYS;
  for (const key of Object.keys(raw)) {
    if (!allowed.includes(key)) throw new SettingsError(400, `不支持的对局设置字段：${key}`);
  }
  const config = { ...defaultConfig(rule), ...raw };
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) {
    throw new SettingsError(400, '房间名必须是最多 128 字的文本');
  }
  config.room_name = config.room_name.trim();
  const validSubRule = rule === 'guobiao'
    ? GUOBIAO_SUB_RULES.has(config.sub_rule)
    : config.sub_rule === DEFAULT_SUB_RULES[rule];
  if (!validSubRule) throw new SettingsError(400, '不支持的对局子规则');
  integerField(config, 'game_round', 1, 4, '圈数');
  if (rule === 'changsha' && ![1, 2, 4].includes(config.game_round)) {
    throw new SettingsError(400, '长沙麻将圈数必须为 1、2 或 4');
  }
  integerField(config, 'round_timer', 0, 1000, '局时');
  integerField(config, 'step_timer', 0, 100, '步时');
  const booleanKeys = ['tips', 'tourist_limit', 'allow_spectator'];
  if (rule === 'guobiao') {
    integerField(config, 'hepai_limit', 1, 64, '起和番');
    integerField(config, 'cuohe_type', 0, 1, '错和形式');
    booleanKeys.push('open_cuohe', 'tactical_call', 'claim_protection');
  }
  for (const key of booleanKeys) {
    if (typeof config[key] !== 'boolean') throw new SettingsError(400, `${key}必须是布尔值`);
  }
  if (config.tips && config.open_cuohe) throw new SettingsError(400, '提示与错和不能同时开启');
  if (rule === 'guobiao' && !config.open_cuohe) config.cuohe_type = 0;
  return config;
}

function defaultSnapshot() {
  return { room_rule: 'guobiao', room_config: defaultConfig(), preset_id: null };
}

function normalizeSnapshot(source, fallback = defaultSnapshot()) {
  if (!isObject(source)) throw new SettingsError(400, '对局设置必须是对象');
  const rule = normalizeRule(has(source, 'room_rule') ? source.room_rule : fallback.room_rule);
  const config = has(source, 'room_config')
    ? normalizeRoomConfig(rule, source.room_config)
    : normalizeRoomConfig(rule, rule === fallback.room_rule ? fallback.room_config : {});
  const reference = has(source, 'preset_id') ? source.preset_id : fallback.preset_id;
  if (reference != null && reference !== '' && !isId(reference)) {
    throw new SettingsError(400, '预设 ID 无效');
  }
  return { room_rule: rule, room_config: config, preset_id: reference || null };
}

function normalizeName(name) {
  if (typeof name !== 'string') throw new SettingsError(400, '请填写预设名称');
  const normalized = name.trim();
  if (!normalized || normalized.length > 40 || /[\u0000-\u001f\u007f]/.test(normalized)) {
    throw new SettingsError(400, '预设名称须为 1–40 字，不能包含换行或控制字符');
  }
  return normalized;
}

function readTimestamp(value) {
  if (typeof value !== 'string' || !value.trim()) return '';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '' : date.toISOString();
}

function sameConfig(left, right) {
  return left.room_rule === right.room_rule && JSON.stringify(left.room_config) === JSON.stringify(right.room_config);
}

function configFields(snapshot) {
  return { room_rule: snapshot.room_rule, room_config: { ...snapshot.room_config } };
}

function selectedSnapshot(settings, presetId) {
  if (presetId == null || presetId === '') return { ...configFields(settings.manual), preset_id: null, preset_name: '默认配置' };
  if (!isId(presetId)) throw new SettingsError(400, '预设 ID 无效');
  const preset = settings.presets.find((item) => item.preset_id === presetId);
  if (!preset) throw new SettingsError(400, '所选预设不存在，请重新选择');
  return { ...configFields(preset), preset_id: preset.preset_id, preset_name: preset.name };
}

function preserveLegacyAuto(settings, snapshot) {
  if (sameConfig(settings.manual, snapshot)) return null;
  const matching = settings.presets.find((preset) => preset.preset_id === snapshot.preset_id && sameConfig(preset, snapshot))
    || settings.presets.find((preset) => sameConfig(preset, snapshot));
  if (matching) return matching.preset_id;
  const digest = createHash('sha256').update(JSON.stringify(configFields(snapshot))).digest('hex').slice(0, 20);
  const baseId = `legacy-auto-${digest}`;
  let presetId = baseId;
  let suffix = 2;
  while (settings.presets.some((item) => item.preset_id === presetId)) presetId = `${baseId}-${suffix++}`;
  let name = '原自动匹配设置';
  suffix = 2;
  while (settings.presets.some((item) => item.name.toLowerCase() === name.toLowerCase())) name = `原自动匹配设置 ${suffix++}`;
  settings.presets.push({
    preset_id: presetId, name, ...configFields(snapshot), created_at: '', updated_at: '',
  });
  return presetId;
}

/** Return a fresh canonical document; incomplete legacy rows retain safe defaults. */
function readRoomSettings(raw) {
  let source = raw;
  if (typeof source === 'string') {
    try { source = JSON.parse(source); } catch (_) { source = {}; }
  }
  if (!isObject(source)) source = {};
  const settings = {
    version: 2,
    revision: isRevision(source.revision) ? source.revision : 0,
    manual: defaultSnapshot(),
    auto_match: { enabled: false, ...defaultSnapshot(), updated_by: null },
    presets: [],
  };
  const ids = new Set();
  const names = new Set();
  for (const item of Array.isArray(source.presets) ? source.presets : []) {
    if (settings.presets.length === (source.version === 2 ? MAX_STORED_PRESETS : MAX_PRESETS)) break;
    if (!isObject(item) || !isId(item.preset_id) || ids.has(item.preset_id)) continue;
    try {
      const name = normalizeName(item.name);
      if (names.has(name.toLowerCase())) continue;
      const snapshot = normalizeSnapshot({ room_rule: item.room_rule, room_config: item.room_config });
      settings.presets.push({
        preset_id: item.preset_id,
        name,
        room_rule: snapshot.room_rule,
        room_config: snapshot.room_config,
        created_at: readTimestamp(item.created_at),
        updated_at: readTimestamp(item.updated_at),
      });
      ids.add(item.preset_id);
      names.add(name.toLowerCase());
    } catch (_) { /* Discard unusable stored presets without exposing arbitrary fields. */ }
  }
  try { settings.manual = normalizeSnapshot({ ...(source.manual || {}), preset_id: null }); } catch (_) { /* Keep defaults. */ }
  try {
    const auto = source.auto_match || {};
    let presetId = auto.preset_id || null;
    if (source.version !== 2) {
      // v1 allowed a detached auto snapshot. Make it a visible shared preset without changing its rules.
      presetId = isObject(source.auto_match) ? preserveLegacyAuto(settings, normalizeSnapshot(auto)) : null;
    }
    const snapshot = selectedSnapshot(settings, presetId);
    settings.auto_match = {
      enabled: auto.enabled === true,
      ...configFields(snapshot),
      preset_id: snapshot.preset_id,
      updated_by: Number.isSafeInteger(auto.updated_by) && auto.updated_by > 0 ? auto.updated_by : null,
    };
  } catch (_) {
    settings.auto_match = { enabled: false, ...configFields(settings.manual), preset_id: null, updated_by: null };
  }
  return settings;
}

function assertRevision(settings, revision) {
  if (!isRevision(revision) || revision !== settings.revision) {
    throw new SettingsError(409, '对局设置已更新，请确认最新设置后重试', settings);
  }
}

/** Resolve a confirmed table selection without changing the venue's default settings. */
function resolveRoomSelection(doc, { revision, preset_id = null } = {}) {
  const settings = readRoomSettings(doc);
  assertRevision(settings, revision);
  return { revision: settings.revision, ...selectedSnapshot(settings, preset_id) };
}

function normalizeSeatUserIds(userIds) {
  if (!Array.isArray(userIds) || userIds.length !== 4
    || userIds.some((id) => !Number.isSafeInteger(id) || id <= 0) || new Set(userIds).size !== 4) {
    throw new SettingsError(400, '请恰好选择 4 名不同的准备中玩家');
  }
  return [...userIds];
}

function updateAutoSnapshot(settings, userId) {
  const snapshot = selectedSnapshot(settings, settings.auto_match.preset_id);
  if (!sameConfig(snapshot, settings.auto_match)) {
    if (!Number.isSafeInteger(userId) || userId <= 0) throw new SettingsError(400, '缺少有效的操作管理员');
    settings.auto_match = { ...settings.auto_match, ...configFields(snapshot), updated_by: userId };
  }
}

function assertUniqueName(settings, name, exceptId) {
  if (settings.presets.some((preset) => preset.preset_id !== exceptId && preset.name.toLowerCase() === name.toLowerCase())) {
    throw new SettingsError(400, '预设名称已存在');
  }
}

function changeTimestamp(options) {
  const value = typeof options.now === 'function' ? options.now() : options.now;
  return new Date(value === undefined ? Date.now() : value).toISOString();
}

/** Apply one revision-checked mutation without sharing mutable settings with the input. */
function applyRoomSettingsChange(doc, { kind, body = {}, presetId, userId, eventStatus }, options = {}) {
  const settings = readRoomSettings(doc);
  assertRevision(settings, isObject(body) ? body.revision : undefined);
  if (settings.revision === Number.MAX_SAFE_INTEGER) throw new SettingsError(409, '对局设置版本超出范围', settings);
  let savedPresetId;
  if (kind === 'manual' || kind === 'auto') {
    if (kind === 'manual') {
      if (body.preset_id != null && body.preset_id !== '') throw new SettingsError(400, '默认配置不能引用预设，请直接修改默认配置');
      settings.manual = { ...normalizeSnapshot(body, settings.manual), preset_id: null };
    } else {
      if (has(body, 'room_rule') || has(body, 'room_config')) throw new SettingsError(400, '自动匹配请选择默认配置或已有预设');
      const current = settings.auto_match;
      const snapshot = selectedSnapshot(settings, has(body, 'preset_id') ? body.preset_id : current.preset_id);
      const enabled = has(body, 'enabled') ? body.enabled : current.enabled;
      if (typeof enabled !== 'boolean') throw new SettingsError(400, '自动匹配开关必须是布尔值');
      if (enabled && eventStatus !== 'active') throw new SettingsError(400, '仅已开启的赛事或基地可启用自动匹配');
      if (!Number.isSafeInteger(userId) || userId <= 0) throw new SettingsError(400, '缺少有效的操作管理员');
      settings.auto_match = { enabled, ...configFields(snapshot), preset_id: snapshot.preset_id, updated_by: userId };
    }
  } else if (kind === 'preset-create' || kind === 'preset-update') {
    const existing = kind === 'preset-update'
      ? settings.presets.find((preset) => preset.preset_id === presetId)
      : null;
    if (kind === 'preset-update' && !existing) throw new SettingsError(404, '预设不存在');
    if (!existing && settings.presets.length >= MAX_PRESETS) throw new SettingsError(400, '最多可创建 50 个对局设置预设');
    const name = normalizeName(has(body, 'name') ? body.name : existing?.name);
    assertUniqueName(settings, name, existing?.preset_id);
    const snapshot = normalizeSnapshot({
      ...(has(body, 'room_rule') ? { room_rule: body.room_rule } : {}),
      ...(has(body, 'room_config') ? { room_config: body.room_config } : {}),
    }, existing ? { ...existing, preset_id: null } : defaultSnapshot());
    const now = changeTimestamp(options);
    savedPresetId = existing?.preset_id || (typeof options.id === 'function' ? options.id() : options.id) || randomUUID();
    if (!isId(savedPresetId) || (!existing && settings.presets.some((preset) => preset.preset_id === savedPresetId))) {
      throw new SettingsError(409, '预设 ID 重复或无效，请重试');
    }
    const preset = {
      preset_id: savedPresetId,
      name,
      room_rule: snapshot.room_rule,
      room_config: snapshot.room_config,
      created_at: existing?.created_at || now,
      updated_at: now,
    };
    if (existing) settings.presets[settings.presets.indexOf(existing)] = preset;
    else settings.presets.push(preset);
  } else if (kind === 'preset-delete') {
    const index = settings.presets.findIndex((preset) => preset.preset_id === presetId);
    if (index === -1) throw new SettingsError(404, '预设不存在');
    if (settings.auto_match.preset_id === presetId) throw new SettingsError(400, '自动匹配正在使用该预设，请先切换到其他设置再删除');
    settings.presets.splice(index, 1);
  } else {
    throw new SettingsError(400, '不支持的对局设置操作');
  }
  updateAutoSnapshot(settings, userId);
  settings.revision += 1;
  return { settings, ...(savedPresetId ? { savedPresetId } : {}) };
}

module.exports = { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection, normalizeSeatUserIds, SettingsError };
