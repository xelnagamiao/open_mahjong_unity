import displayCatalog from '../../../shared/ruleDisplayCatalog.json' with { type: 'json' };
import { STATISTICS_RULES } from './statisticsCatalog.js';

const definitions = new Map(STATISTICS_RULES.map(rule => [rule.sub_rule, rule]));

// Names and order come from the support workbook; statistics keys stay stable.
export const PLATFORM_RULE_GROUPS = displayCatalog.rules.map(group => ({
  value: group.rule,
  label: group.label,
  subRules: group.subRules.flatMap(sub => {
    const definition = definitions.get(sub.sub_rule);
    return definition ? [{ ...definition, label: sub.label }] : [];
  }),
})).filter(group => group.subRules.length);

export const platformSubRuleOptions = rule =>
  PLATFORM_RULE_GROUPS.find(group => group.value === rule)?.subRules ?? [];
