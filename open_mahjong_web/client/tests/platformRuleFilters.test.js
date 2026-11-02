import test from 'node:test';
import assert from 'node:assert/strict';
import displayCatalog from '../../shared/ruleDisplayCatalog.json' with { type: 'json' };
import { STATISTICS_RULES } from '../src/utils/statisticsCatalog.js';
import { PLATFORM_RULE_GROUPS, platformSubRuleOptions } from '../src/utils/platformRuleFilters.js';

test('platform filters retain workbook names and order and cover every statistics rule once', () => {
  const enabled = new Set(STATISTICS_RULES.map(rule => rule.sub_rule));
  const expected = displayCatalog.rules.map(group => ({
    value: group.rule,
    label: group.label,
    subRules: group.subRules.filter(sub => enabled.has(sub.sub_rule)),
  })).filter(group => group.subRules.length);
  assert.deepEqual(PLATFORM_RULE_GROUPS.map(group => ({
    value: group.value,
    label: group.label,
    subRules: group.subRules.map(({ sub_rule, label }) => ({ sub_rule, label })),
  })), expected);
  const keys = PLATFORM_RULE_GROUPS.flatMap(group => group.subRules.map(sub => sub.key));
  assert.deepEqual(keys.toSorted(), STATISTICS_RULES.map(rule => rule.key).toSorted());
  assert.equal(new Set(keys).size, keys.length);
  assert.equal(PLATFORM_RULE_GROUPS.some(group => group.value === 'free'), false);
});

test('subrule order follows the Unity create-room panel, including variants formerly appended last', () => {
  assert.deepEqual(platformSubRuleOptions('guobiao').map(rule => rule.sub_rule.split('/')[1]), [
    'standard', 'xiaolin', 'kshen', 'lanshi', 'blood_battle', 'sanma',
    'transparent', 'transparent_realtime', 'huahun', 'four_jokers', 'sixteen',
  ]);
  assert.deepEqual(platformSubRuleOptions('riichi').map(rule => rule.sub_rule.split('/')[1]), [
    'standard', 'langyong', 'sanma', 'washizu', 'washizu_realtime', 'washizu_doubles',
  ]);
  assert.deepEqual(platformSubRuleOptions('guangdong').map(rule => rule.sub_rule), [
    'guangdong/tuidao_mil2024', 'guangdong/mil2023',
  ]);
  assert.deepEqual(platformSubRuleOptions('hongque').map(rule => rule.label), ['虹雀²', '虹雀 W']);
  assert.equal(platformSubRuleOptions('guobiao')[0].label, '标准规(新编MCR)');
  assert.equal(PLATFORM_RULE_GROUPS.find(rule => rule.value === 'singapore').label, '新加坡四人麻将');
  assert.equal(PLATFORM_RULE_GROUPS.find(rule => rule.value === 'beijing').label, '北京');
  assert.deepEqual(platformSubRuleOptions('unknown'), []);
});
