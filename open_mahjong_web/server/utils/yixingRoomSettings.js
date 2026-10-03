const FIXED_FALSE = ['open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeYixingRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('宜兴对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'yixing/standard', game_round: 4, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), use_flowers: true, detailed_config: { seven_pairs: false },
  };
  for (const key of Object.keys(raw)) if (!(key in config) && key !== 'duplicate_key') invalid(`不支持的宜兴设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('复式密钥仅支持国标麻将');
  for (const key of Object.keys(config)) if (Object.prototype.hasOwnProperty.call(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'yixing/standard') invalid('宜兴麻将仅支持 标准规则');
  for (const [key, low, high] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('宜兴标准规则不支持此选项');
  if (config.use_flowers !== true) invalid('宜兴麻将固定使用八张花牌');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => key !== 'seven_pairs') || ('seven_pairs' in detail && typeof detail.seven_pairs !== 'boolean')) invalid('七小对必须是布尔值，不支持其他宜兴馆规');
  config.detailed_config = { seven_pairs: detail.seven_pairs ?? false };
  return config;
}

module.exports = { normalizeYixingRoomConfig };
