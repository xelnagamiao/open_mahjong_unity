const OPTIONS = ['three_hun_self_draw', 'single_call', 'dingzhuang', 'four_hun_win', 'multi_ron', 'fan_zimo', 'fan_dealer', 'fan_repeat', 'fan_all_pungs', 'fan_all_waits', 'fan_kong', 'ron_all_pay', 'half_hun', 'pao_kong', 'pao_four_calls'];
const FIXED_FALSE = ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeBeijingRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('北京麻将对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'beijing/chipengti', game_round: 4, starting_score: 0,
    round_timer: 20, step_timer: 5, tips: true, count_tips: false, pointer_tips: true,
    tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: {},
  };
  for (const key of Object.keys(raw)) {
    if (!Object.hasOwn(config, key) && key !== 'duplicate_key') invalid(`不支持的北京麻将设置：${key}`);
  }
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('北京麻将不支持复式牌山');
  for (const key of Object.keys(config)) if (Object.hasOwn(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'beijing/chipengti') invalid('北京麻将只支持吃碰提规则');
  if (config.starting_score !== 0) invalid('北京麻将起始分为0');
  for (const [key, low, high] of [['game_round', 1, 4], ['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('北京麻将不支持此选项');
  const detail = { rule_version: 'beijing-v1', hun_mode: 'up', ...Object.fromEntries(OPTIONS.map(key => [key, false])) };
  if (!object(config.detailed_config) || Object.keys(config.detailed_config).some(key => !Object.hasOwn(detail, key))) invalid('不支持的北京麻将变体');
  Object.assign(detail, config.detailed_config);
  if (detail.rule_version !== 'beijing-v1' || !['up', 'down', 'self'].includes(detail.hun_mode)) invalid('北京麻将规则版本或混儿方式无效');
  for (const key of OPTIONS) if (typeof detail[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  config.detailed_config = detail;
  return config;
}

module.exports = { normalizeBeijingRoomConfig };
