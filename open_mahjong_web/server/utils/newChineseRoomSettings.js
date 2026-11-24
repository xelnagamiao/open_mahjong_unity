const FIXED_FALSE = ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeNewChineseRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('新中式麻将对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'new_chinese/standard', game_round: 4, starting_score: 0, hepai_limit: 0,
    round_timer: 20, step_timer: 5, tips: true, count_tips: false, pointer_tips: true,
    tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: {},
  };
  for (const key of Object.keys(raw)) {
    if (!Object.hasOwn(config, key) && key !== 'duplicate_key') invalid(`不支持的新中式麻将设置：${key}`);
  }
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('新中式麻将不支持复式牌山');
  for (const key of Object.keys(config)) if (Object.hasOwn(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  const sanhua = config.sub_rule === 'new_chinese/sanhua';
  if (!sanhua && config.sub_rule !== 'new_chinese/standard') invalid('新中式麻将只支持标准规则');
  if (config.starting_score !== 0) invalid('新中式麻将起始分为0');
  for (const [key, low, high] of [['round_timer', 0, 1000], ['step_timer', 0, 100], ['hepai_limit', 0, 2147483647]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('新中式麻将不支持此选项');
  if (![4, 8, 12, 16].includes(config.game_round)) invalid('圈数须为4、8、12或16');
  if (sanhua) {
    if (Object.hasOwn(raw, 'hepai_limit') && raw.hepai_limit !== 4) invalid('散花天才麻将固定四番起和');
    const detail = config.detailed_config;
    if (!object(detail) || Object.keys(detail).some(k => k !== 'rule_version') ||
        (detail.rule_version !== undefined && detail.rule_version !== 'sanhua-2026-11')) invalid('无效的散花天才麻将配置');
    return { ...config, hepai_limit: 4, detailed_config: { rule_version: 'sanhua-2026-11' } };
  }
  const detail = { rule_version: 'new-chinese-2026-11', exchange_three: false, showdown: false };
  if (!object(config.detailed_config) || Object.keys(config.detailed_config).some(key => !Object.hasOwn(detail, key))) invalid('不支持的新中式麻将选项');
  Object.assign(detail, config.detailed_config);
  if (detail.rule_version !== 'new-chinese-2026-11' || typeof detail.exchange_three !== 'boolean' || typeof detail.showdown !== 'boolean') invalid('换三张、摊牌设置或规则版本无效');
  if (detail.exchange_three) config.hepai_limit = 15;
  config.detailed_config = detail;
  return config;
}

module.exports = { normalizeNewChineseRoomConfig };
