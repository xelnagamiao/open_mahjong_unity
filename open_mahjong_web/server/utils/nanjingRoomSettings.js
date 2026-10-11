const VERSION = 'mil-nanjing-2024-v1';
const FIXED_FALSE = ['tactical_call', 'open_cuohe', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function normalizeNanjingRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('南京对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'nanjing/mil2024', game_round: 4, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true, use_flowers: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { rule_version: VERSION },
  };
  for (const key of Object.keys(raw)) if (!(key in config) && key !== 'duplicate_key') invalid(`不支持的南京设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('复式密钥仅支持国标麻将');
  for (const key of Object.keys(config)) if (Object.prototype.hasOwnProperty.call(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'nanjing/mil2024') invalid('南京麻将仅支持 MIL 2024');
  for (const [key, low, high] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE, 'use_flowers']) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('南京 MIL 2024 不支持此选项');
  if (!config.use_flowers) invalid('南京麻将固定使用中发白及八花');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => key !== 'rule_version') || ('rule_version' in detail && detail.rule_version !== VERSION)) invalid('南京麻将仅支持 MIL 2024 规则版本');
  config.detailed_config = { rule_version: VERSION };
  return config;
}
module.exports = { normalizeNanjingRoomConfig };
