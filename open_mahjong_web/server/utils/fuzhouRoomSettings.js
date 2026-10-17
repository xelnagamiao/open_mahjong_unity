const VERSION = 'fuzhou-user-20261015-v2';
const FIXED_FALSE = ['tactical_call', 'open_cuohe', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function normalizeFuzhouRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('福州对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'fuzhou/standard', game_round: 1, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true, use_flowers: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { rule_version: VERSION, base_score: 15 },
  };
  for (const key of Object.keys(raw)) if (!(key in config) && key !== 'duplicate_key') invalid(`不支持的福州设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('福州不支持复式牌墙');
  for (const key of Object.keys(config)) if (Object.prototype.hasOwnProperty.call(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'fuzhou/standard') invalid('未开放的福州子规则');
  for (const [key, low, high] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE, 'use_flowers']) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  if (![1,2,4].includes(config.game_round)) invalid('福州时长只能为一圈、半庄或一雀');
  for (const key of FIXED_FALSE) if (config[key]) invalid('福州规则不支持此选项');
  if (!config.use_flowers) invalid('福州固定使用全部字牌及八花');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => !['rule_version','base_score'].includes(key)) || ('rule_version' in detail && detail.rule_version !== VERSION)) invalid('未知福州馆规设置');
  const base = detail.base_score ?? 15;
  if (![10,15,20].includes(base)) invalid('福州底分只能为10、15或20');
  config.detailed_config = { rule_version: VERSION, base_score: base };
  return config;
}
module.exports = { normalizeFuzhouRoomConfig };
