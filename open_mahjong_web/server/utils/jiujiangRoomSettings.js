const JIUJIANG_SUB_RULE = 'jiujiang/standard';
const JIUJIANG_CONFIG = Object.freeze({ rule_version: 'jiujiang-om1' });
const FIXED_FALSE = ['open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeJiujiangRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('九江对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: JIUJIANG_SUB_RULE, game_round: 4, round_timer: 20, step_timer: 5,
    use_flowers: true, hepai_limit: 1, tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { ...JIUJIANG_CONFIG },
  };
  for (const key of Object.keys(raw)) if (!Object.hasOwn(config, key) && key !== 'duplicate_key') invalid(`不支持的九江设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('复式密钥仅支持国标麻将');
  Object.assign(config, raw);
  delete config.duplicate_key;
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== JIUJIANG_SUB_RULE) invalid('九江麻将仅支持 标准规则');
  for (const [key, low, high] of [['game_round', 1, 4], ['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('九江标准规则不支持此选项');
  if (config.use_flowers !== true || config.hepai_limit !== 1) invalid("九江固定使用发财补牌规则，1分起胡");
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => !Object.hasOwn(JIUJIANG_CONFIG, key) || detail[key] !== JIUJIANG_CONFIG[key])) invalid('九江麻将只支持 标准规则 标准配置');
  config.detailed_config = { ...JIUJIANG_CONFIG };
  return config;
}

module.exports = { JIUJIANG_SUB_RULE, JIUJIANG_CONFIG, normalizeJiujiangRoomConfig };
