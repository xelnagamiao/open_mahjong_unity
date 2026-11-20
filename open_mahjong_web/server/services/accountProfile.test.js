const test = require('node:test');
const assert = require('node:assert/strict');
const { getAccountProfile, activityYear, shanghaiDay } = require('./accountProfile');
const { STATISTICS_RULES } = require('./statisticsCatalog');

test('profile uses Beijing midnight and rejects invalid/future years', () => {
  assert.equal(shanghaiDay(new Date('2025-12-31T16:00:00Z')), '2026-01-01');
  assert.equal(activityYear(undefined, '2026-01-01'), 2026);
  for (const year of ['2027', '1999', '2026 OR 1=1', ['2025', '2026']]) assert.throws(() => activityYear(year, '2026-01-01'));
});
test('profile is scoped to authenticated user and uses normalized rule labels', async () => {
  const rule = STATISTICS_RULES.find(row => row.basic_stats);
  const calls = [], results = [
    [{user_id:101,username:'test'}],
    [{total_games:3,active_days:2}],
    [{date:'2026-01-01',games:1},{date:'2026-01-02',games:2}],
    [{rule:rule.key,games:3}],
  ];
  const db = { query: async (sql, args) => { calls.push({sql,args}); return {rows:results.shift()}; } };
  const profile = await getAccountProfile(db,101,2026,new Date('2026-01-02T12:00:00Z'));
  assert.ok(calls.every(call => call.args[0] === 101));
  assert.deepEqual(calls[2].args, [101,'2026-01-01','2026-01-02']);
  assert.equal(profile.activity.games,3);
  assert.equal(profile.favorite_rules[0].label,rule.stats_name);
});
