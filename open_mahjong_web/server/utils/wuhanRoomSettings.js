const VERSION = 'mil2025';
const FIXED_FALSE = ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const isObject = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const has = (value, key) => Object.prototype.hasOwnProperty.call(value, key);

function normalizeWuhanRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!isObject(raw)) invalid('武汉对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'wuhan/mil2025', game_round: 4, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { rule_version: VERSION },
  };
  for (const key of Object.keys(raw)) {
    if (!has(config, key) && key !== 'duplicate_key') invalid(`不支持的武汉设置：${key}`);
  }
  if (has(raw, 'duplicate_key') && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) {
    invalid('复式密钥仅支持国标麻将');
  }
  for (const key of Object.keys(config)) if (has(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'wuhan/mil2025') invalid('武汉麻将仅支持 MIL 2025');
  for (const [key, low, high] of [['game_round', 1, 4], ['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('武汉 MIL 2025 不支持此选项');
  const detail = config.detailed_config;
  if (!isObject(detail) || Object.keys(detail).some(key => key !== 'rule_version')
      || (has(detail, 'rule_version') && detail.rule_version !== VERSION)) invalid('武汉麻将仅支持 MIL 2025 规则版本');
  config.detailed_config = { rule_version: VERSION };
  return config;
}

module.exports = { normalizeWuhanRoomConfig };
