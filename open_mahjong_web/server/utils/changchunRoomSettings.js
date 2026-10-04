const CHANGCHUN_SUB_RULE = 'changchun/mil2024';
const CHANGCHUN_CONFIG = Object.freeze({ edition: 'mil-changchun-2024-om1', fan_cap: 6, special_initial_replacement: 0, ordinary_jokers: false });
const FIXED_FALSE = ['open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he', 'use_flowers'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeChangchunRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('长春对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: CHANGCHUN_SUB_RULE, game_round: 4, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { ...CHANGCHUN_CONFIG },
  };
  for (const key of Object.keys(raw)) if (!Object.hasOwn(config, key) && key !== 'duplicate_key') invalid(`不支持的长春设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('复式密钥仅支持国标麻将');
  Object.assign(config, raw);
  delete config.duplicate_key;
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== CHANGCHUN_SUB_RULE) invalid('长春麻将仅支持 MIL 2024');
  for (const [key, low, high] of [['game_round', 1, 4], ['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('MIL 长春标准规则不支持此选项');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => !Object.hasOwn(CHANGCHUN_CONFIG, key) || detail[key] !== CHANGCHUN_CONFIG[key])) invalid('长春麻将只支持 MIL 2024 标准配置');
  config.detailed_config = { ...CHANGCHUN_CONFIG };
  return config;
}

module.exports = { CHANGCHUN_SUB_RULE, CHANGCHUN_CONFIG, normalizeChangchunRoomConfig };
