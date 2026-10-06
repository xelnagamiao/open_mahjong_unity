const VERSION = 'mil-guizhou-2023-om1';
const FIXED_FALSE = ['use_flowers', 'open_cuohe', 'claim_protection', 'tian_di_ren_he'];
const SWITCHES = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator', 'tactical_call'];
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

function normalizeGuizhouRoomConfig(raw, SettingsError) {
  const invalid = message => { throw new SettingsError(400, message); };
  if (!object(raw)) invalid('贵州对局设置必须是对象');
  const config = {
    room_name: '', sub_rule: 'guizhou/standard', game_round: 4, round_timer: 20, step_timer: 5,
    tips: true, count_tips: false, pointer_tips: true, tourist_limit: false, allow_spectator: true, tactical_call: true,
    ...Object.fromEntries(FIXED_FALSE.map(key => [key, false])), detailed_config: { rule_version: VERSION },
  };
  for (const key of Object.keys(raw)) if (!(key in config) && key !== 'duplicate_key') invalid(`不支持的贵州设置：${key}`);
  if ('duplicate_key' in raw && (typeof raw.duplicate_key !== 'string' || raw.duplicate_key.trim())) invalid('复式密钥仅支持国标麻将');
  for (const key of Object.keys(config)) if (Object.prototype.hasOwnProperty.call(raw, key)) config[key] = raw[key];
  if (typeof config.room_name !== 'string' || config.room_name.trim().length > 128) invalid('房间名必须是最多128字的文本');
  config.room_name = config.room_name.trim();
  if (config.sub_rule !== 'guizhou/standard') invalid('贵州麻将仅支持 MIL 2023 标准规');
  for (const [key, low, high] of [['game_round',1,4],['round_timer',0,1000],['step_timer',0,100]]) {
    if (!Number.isSafeInteger(config[key]) || config[key] < low || config[key] > high) invalid(`${key}须为 ${low}–${high} 的整数`);
  }
  for (const key of [...SWITCHES, ...FIXED_FALSE]) if (typeof config[key] !== 'boolean') invalid(`${key}必须是布尔值`);
  for (const key of FIXED_FALSE) if (config[key]) invalid('贵州 MIL 标准规不支持此选项');
  const detail = config.detailed_config;
  if (!object(detail) || Object.keys(detail).some(key => key !== 'rule_version') || ('rule_version' in detail && detail.rule_version !== VERSION)) invalid('贵州规则版本或馆规无效');
  config.detailed_config = { rule_version: VERSION };
  return config;
}

module.exports = { normalizeGuizhouRoomConfig };
