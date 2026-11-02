const options = require('../../shared/hongqueRoomOptions.json');
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeHongqueRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('虹雀对局设置必须是对象');
  const subRule = raw.sub_rule ?? options.defaults.sub_rule;
  if (!options.profiles.some(profile => profile.value === subRule)) invalid('不支持的虹雀子规则');
  const w = subRule === 'hongque/w-v1.0';
  const config = { ...options.defaults, ...(w ? options.wDefaults : {}), sub_rule: subRule };
  for (const key of Object.keys(raw)) {
    if (!Object.hasOwn(config, key) && key !== 'duplicate_key') invalid(`不支持的虹雀设置：${key}`);
  }
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('虹雀不支持复式牌山');
  for (const key of Object.keys(config)) if (Object.hasOwn(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (![4, 8, 12, 16].includes(config.game_round)) invalid('局数须为4、8、12或16');
  if (config.starting_score !== 0 || config.open_cuohe !== false || config.tactical_call !== true) invalid('虹雀起始分为0，不支持此变更');
  if (!['multi_ron', 'head_bump'].includes(config.hepai_way)) invalid('不支持的点和方式');
  for (const [key, max] of [['round_timer', 1000], ['step_timer', 100], ...(w ? [['hongque_min_fan', 100], ['hongque_min_points', 10000]] : [])]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < 0 || config[key] > max) invalid(`${key}须为0–${max}的整数`);
  }
  for (const key of ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator', ...(w ? ['hongque_colourless'] : [])]) {
    if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  }
  return config;
}

module.exports = { normalizeHongqueRoomConfig };
