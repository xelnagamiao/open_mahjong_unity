import catalog from '../../../shared/statisticsRules.json' with { type: 'json' };
export const STATISTICS_RULES = catalog.rules.filter(row => row.basic_stats);
export const STATISTIC_VALUE_UNITS = catalog.stat_value_units;
const canonicalKey = key => ['jiandan', 'jiandan/standard', 'nanque/standard'].includes(key) ? 'nanque' : key;
export const statisticsSelection = key => STATISTICS_RULES.find(row => row.key === canonicalKey(key) || row.sub_rule === key);
export const statisticRuleKey = row => row.sub_rule
  ? statisticsSelection(row.sub_rule)?.key
  : statisticsSelection(row.rule)?.key;

export function statisticsGameType(key, mode) {
  const hongque = statisticsSelection(key)?.rule === 'hongque';
  const length = Number.parseInt(mode, 10);
  if (hongque) return [4,8,12,16].includes(length) ? `hands${length}` : null;
  return ({1:'dongfeng',2:'banzhuang',3:'xifeng',4:'quanzhuang'})[length] || null;
}

export function statisticsModeLabel(key, mode) {
  if (!mode) return '-';
  const type = statisticsGameType(key, mode);
  if (type?.startsWith('hands')) return `${type.slice(5)}局`;
  return ({dongfeng:'东风战',banzhuang:'半庄战',xifeng:'东西战',quanzhuang:'全庄战'})[type] || mode;
}
