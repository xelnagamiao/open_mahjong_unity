const { rules: STATISTICS_RULES } = require('../../shared/statisticsRules.json');
const BY_KEY = new Map(STATISTICS_RULES.map(row => [row.key, row]));
const BY_SUB_RULE = new Map(STATISTICS_RULES.map(row => [row.sub_rule, row]));
const quote = value => `'${value.replaceAll("'", "''")}'`;

function statisticsSelection(key) {
  const row = BY_KEY.get(key) || BY_SUB_RULE.get(key);
  return row?.basic_stats ? row : null;
}

// 与 Python 回填使用同一配表；历史别名只还原为一个实际子规则。
function subRuleSql(p = 'p', g = 'g') {
  const raw = `COALESCE(NULLIF(${p}.sub_rule,''),NULLIF(${g}.record->'game_title'->>'sub_rule',''))`;
  const defaults = new Map();
  for (const row of STATISTICS_RULES) if (!defaults.has(row.rule)) defaults.set(row.rule, row.sub_rule);
  defaults.set('guangdong', 'guangdong/tuidao_mil2024');
  return `(CASE WHEN ${p}.rule IN ('jiandan','nanque') OR ${raw} IN ('jiandan/standard','nanque/standard') THEN 'zhongyong/nanque'
    WHEN ${raw}='hongkong/new13' THEN CASE WHEN ${g}.record->'game_title'->'detailed_config'->>'new13_version'='lianhuise' THEN 'hongkong/new13_lianhuise' ELSE 'hongkong/new13_gametower' END
    WHEN ${raw} IS NOT NULL AND ${raw}<>${p}.rule THEN ${raw}
    WHEN ${p}.rule IN ('riichi','guobiao') AND (COALESCE(${p}.match_type,'') ~ '/3(_|$)' OR COALESCE(${p}.match_type,'') LIKE '%_sanma%') THEN ${p}.rule||'/sanma'
    ELSE CASE ${p}.rule ${[...defaults].map(([rule, sub]) => `WHEN ${quote(rule)} THEN ${quote(sub)}`).join(' ')} END END)`;
}

function statsKeySql(subExpression) {
  return `(CASE ${subExpression} ${STATISTICS_RULES.filter(r => r.basic_stats).map(r => `WHEN ${quote(r.sub_rule)} THEN ${quote(r.key)}`).join(' ')} END)`;
}

function normalizeMatchType(mode, subRule) {
  const match = /^(\d+)\/(3|4)(?:_sanma)?(_rank)?$/.exec(mode || '');
  if (!match) return mode;
  const sanma = ['riichi/sanma','guobiao/sanma'].includes(subRule) || mode.includes('_sanma') || match[2] === '3';
  return `${match[1]}/${sanma ? 3 : 4}${match[3] || ''}`;
}

module.exports = { STATISTICS_RULES, statisticsSelection, subRuleSql, statsKeySql, normalizeMatchType };
