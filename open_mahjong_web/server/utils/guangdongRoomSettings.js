const TUIDAO = 'guangdong/tuidao_mil2024';
const MIL2023 = 'guangdong/mil2023';
const has = (value, key) => Object.prototype.hasOwnProperty.call(value, key);

function normalizeGuangdongDetails(subRule, detail = {}, SettingsError = Error) {
  const fail = message => { throw new SettingsError(400, message); };
  if (!detail || typeof detail !== 'object' || Array.isArray(detail)) fail('广东麻将详细设置必须是对象');
  const fixed = subRule === TUIDAO
    ? { edition: 'mil-tuidao-2024-om1', fan_cap: 32, wildcards: false }
    : subRule === MIL2023
      ? { edition: 'mil-guangdong-2023-om1', wildcards: true, minimum_fan: 2, minimum_score: 4, horse_count: 4 }
      : null;
  if (!fixed) fail('不支持的广东麻将子规则');
  for (const [key, value] of Object.entries(detail)) {
    if (subRule === MIL2023 && key === 'require_minimum_score') {
      if (typeof value !== 'boolean') fail('4分起和开关必须是布尔值');
    } else if (!has(fixed, key) || value !== fixed[key]) {
      fail('广东麻将固定规则项不能覆盖，请选择对应子规则');
    }
  }
  return subRule === MIL2023 ? { ...fixed, require_minimum_score: detail.require_minimum_score ?? true } : fixed;
}

module.exports = { normalizeGuangdongDetails, GUANGDONG_SUB_RULES: Object.freeze([TUIDAO, MIL2023]) };
