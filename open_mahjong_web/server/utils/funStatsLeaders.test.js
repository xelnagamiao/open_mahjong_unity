const assert = require('node:assert/strict');
const test = require('node:test');
const { pickPtLeaders } = require('./funStatsLeaders');

test('weekly leaders keep the top 10 gainers and losers', () => {
  const rows = [];
  for (let i = 1; i <= 12; i += 1) {
    rows.push({ user_id: 10000000 + i, username: `up${i}`, total_pt_change: i * 10, games: i });
    rows.push({ user_id: 20000000 + i, username: `down${i}`, total_pt_change: -i * 10, games: i });
  }
  rows.push({ user_id: 30000001, username: 'zero', total_pt_change: 0, games: 4 });
  const { gainers, losers } = pickPtLeaders(rows, 10);
  assert.equal(gainers.length, 10);
  assert.equal(losers.length, 10);
  assert.equal(gainers[0].username, 'up12');
  assert.equal(gainers[0].place, 1);
  assert.equal(losers[0].username, 'down12');
  assert.equal(losers[0].total_pt_change, -120);
  assert.ok(!gainers.some((row) => row.username === 'zero'));
  assert.ok(!losers.some((row) => row.username === 'zero'));
});

test('PT signs and decimal values determine the ranking independently of game scores', () => {
  const { gainers, losers } = pickPtLeaders([
    { user_id: '101', total_score: -400, total_pt_change: '11.76', games: '2' },
    { user_id: '102', total_score: 500, total_pt_change: '-5.15', games: '3' },
    { user_id: '103', total_score: 1000, total_pt_change: null, games: 0 },
    { user_id: '104', total_score: 800, total_pt_change: '0.00', games: 4 },
    { user_id: '105', total_pt_change: '11.76', games: '3' },
    { user_id: '106', total_pt_change: '11.76', games: '3' },
  ]);
  assert.deepEqual(gainers.map(row => row.user_id), [105, 106, 101]);
  assert.deepEqual(losers.map(row => row.user_id), [102]);
  assert.equal(gainers[0].total_pt_change, 11.76);
  assert.equal(losers[0].total_pt_change, -5.15);
  assert.equal(gainers[2].username, '101');
  assert.ok(!('total_score' in gainers[0]));
});
