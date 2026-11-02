import test from 'node:test';
import assert from 'node:assert/strict';
import { statisticsSelection, statisticsGameType, statisticsModeLabel } from '../src/utils/statisticsCatalog.js';

test('Qingque statistics retain fixed games and expose unlimited ranked matches', () => {
  assert.deepEqual(statisticsSelection('qingque').ranked_modes, ['4/4_rank', 'unlimited/4_rank']);
  assert.equal(statisticsGameType('qingque', 'unlimited/4_rank'), 'unlimited');
  assert.equal(statisticsModeLabel('qingque', 'unlimited/4_rank'), '无固定时限');
  assert.equal(statisticsModeLabel('qingque', '4/4_rank'), '全庄战');
  assert.equal(statisticsGameType('hongque', 'unlimited/4_rank'), null);
});
