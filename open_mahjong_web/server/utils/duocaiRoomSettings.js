const VERSION = 'duocai-v1';
const ROUNDS = new Map([['duocai/guobiao', 4], ['duocai/riichi', 2]]);
const FIXED_FALSE = ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeDuocaiRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('夺彩对局设置必须是对象');
  const subRule = raw.sub_rule ?? 'duocai/guobiao';
  if (!ROUNDS.has(subRule)) invalid('夺彩麻将只支持国标与立直');
  const config = {
    room_name: '', sub_rule: subRule, game_round: ROUNDS.get(subRule), starting_score: 40000,
    round_timer: 20, step_timer: 5, tips: true, count_tips: false, pointer_tips: true,
    tourist_limit: false, allow_spectator: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { rule_version: VERSION },
  };
  for (const key of Object.keys(raw)) {
    if (!Object.prototype.hasOwnProperty.call(config, key) && key !== 'duplicate_key') invalid(`不支持的夺彩设置：${key}`);
  }
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('夺彩麻将不支持复式牌山');
  for (const key of Object.keys(config)) if (Object.prototype.hasOwnProperty.call(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.game_round !== ROUNDS.get(subRule)) invalid('夺彩国标采用全庄战，夺彩立直采用东南战');
  if (config.starting_score !== 40000) invalid('夺彩麻将起始点数为40000点');
  for (const [key, low, high] of [['round_timer', 0, 1000], ['step_timer', 0, 100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('夺彩麻将不支持此选项');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => key !== 'rule_version') || ('rule_version' in detail && detail.rule_version !== VERSION)) invalid('不支持的夺彩麻将规则版本或馆规');
  config.detailed_config = { rule_version: VERSION };
  return config;
}

module.exports = { normalizeDuocaiRoomConfig };
