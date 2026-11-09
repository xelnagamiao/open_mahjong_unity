const taiwan = require('../../shared/taiwanRoomOptions.json');

function normalizeSceneSeed(value, SettingsError) {
  if (value == null || value === 0 || value === '' || value === '0') return '';
  if (typeof value !== 'string') throw new SettingsError(400, '随机种子须为64位十六进制字符串');
  const seed = value.trim().replace(/^0x/i, '').toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(seed) || /^0+$/.test(seed)) throw new SettingsError(400, '随机种子须为64位非零十六进制字符串');
  return seed;
}

function normalizeTaiwanDetails(raw, SettingsError) {
  const fail = message => { throw new SettingsError(400, message); };
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) fail('台湾馆规必须是对象');
  const allowed = new Set([...taiwan.options.map(o => o.key), 'fan_tai_overrides']);
  if (Object.keys(raw).some(key => !allowed.has(key))) fail('未知台湾麻将馆规字段');
  const result = {};
  for (const option of taiwan.options) {
    const value = Object.hasOwn(raw, option.key) ? raw[option.key] : option.default;
    if (!option.values.includes(value)) fail(`无效的台湾馆规：${option.label}`);
    result[option.key] = value;
  }
  const overrides = raw.fan_tai_overrides ?? {};
  if (!overrides || typeof overrides !== 'object' || Array.isArray(overrides)) fail('自定义台表必须是对象');
  result.fan_tai_overrides = {};
  for (const [key, value] of Object.entries(overrides)) {
    if (!taiwan.fans.some(f => f.key === key) || !Number.isSafeInteger(value) || value < 1 || value > 64) fail('自定义台值须为1–64的整数');
    result.fan_tai_overrides[key] = value;
  }
  return result;
}

function normalizeShangraoRoomConfig(raw, SettingsError) {
  const fail = message => { throw new SettingsError(400, message); };
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) fail('上饶对局设置必须是对象');
  const config = { room_name: '', sub_rule:'shangrao/standard', game_round:4, round_timer:20, step_timer:5,
    tips:true, count_tips:false, pointer_tips:true, tourist_limit:false, allow_spectator:true,
    use_flowers:false, open_cuohe:false, tactical_call:false, claim_protection:false, tian_di_ren_he:false,
    detailed_config:{ rule_version:'shangrao-v0.2' } };
  for (const key of Object.keys(raw)) if (!Object.hasOwn(config, key) && key !== 'duplicate_key') fail(`不支持的上饶设置：${key}`);
  if (Object.hasOwn(raw, 'duplicate_key') && typeof raw.duplicate_key !== 'string') fail('复式密钥必须为文本');
  if (raw.duplicate_key?.trim()) fail('上饶不支持复式密钥');
  Object.assign(config, raw); delete config.duplicate_key;
  if (config.sub_rule !== 'shangrao/standard') fail('不支持的上饶子规则');
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) fail('房间名须为最多128字的文本');
  config.room_name = config.room_name.trim();
  for (const [key,min,max] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) if (!Number.isSafeInteger(config[key]) || config[key]<min || config[key]>max) fail(`无效的${key}`);
  for (const key of ['tips','count_tips','pointer_tips','tourist_limit','allow_spectator','use_flowers','open_cuohe','tactical_call','claim_protection','tian_di_ren_he']) {
    if (typeof config[key] !== 'boolean') fail(`${key}必须为布尔值`);
    if (['use_flowers','open_cuohe','tactical_call','claim_protection','tian_di_ren_he'].includes(key) && config[key]) fail('上饶规则不支持此选项');
  }
  const detail = config.detailed_config;
  const switches = ['qingyise','ziyise','allow_special_bao','four_bao_reward','flying_seven_stars'];
  if (!detail || typeof detail !== 'object' || Array.isArray(detail) || Object.keys(detail).some(k=>!['rule_version',...switches].includes(k))) fail('未知上饶馆规');
  if (detail.rule_version != null && detail.rule_version !== 'shangrao-v0.2') fail('上饶规则版本无效');
  config.detailed_config = { rule_version:'shangrao-v0.2' };
  for (const key of switches) { if (detail[key] != null && typeof detail[key] !== 'boolean') fail('馆规须为布尔值'); config.detailed_config[key] = detail[key] ?? false; }
  return config;
}

module.exports = { normalizeSceneSeed, normalizeTaiwanDetails, normalizeShangraoRoomConfig };
