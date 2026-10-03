const assert = require('node:assert/strict');
const test = require('node:test');
const {
  addDaysYmd,
  currentStatDate,
  lastCompleteStatDate,
  weekRange,
  nextShanghaiRefreshAt,
  cacheMatchesWeek,
} = require('./funStatsTime');

test('04:00 Shanghai is the start of a new statistical day', () => {
  const before = new Date('2026-09-18T03:59:00+08:00');
  const after = new Date('2026-09-18T04:00:00+08:00');
  assert.equal(currentStatDate(before), '2026-09-17');
  assert.equal(currentStatDate(after), '2026-09-18');
  assert.equal(lastCompleteStatDate(before), '2026-09-16');
  assert.equal(lastCompleteStatDate(after), '2026-09-17');
});

test('weekly window is the last 7 completed statistical days', () => {
  const now = new Date('2026-09-18T14:49:00+08:00');
  assert.deepEqual(weekRange(now), {
    date_from: '2026-09-11',
    date_to: '2026-09-17',
  });
  assert.equal(addDaysYmd('2026-09-01', -1), '2026-08-31');
});

test('next 04:00 refresh is later the same morning or the next day', () => {
  const before = new Date('2026-09-18T03:00:00+08:00');
  const after = new Date('2026-09-18T04:00:01+08:00');
  assert.equal(nextShanghaiRefreshAt(4, before).toISOString(), '2026-09-17T20:00:00.000Z');
  assert.equal(nextShanghaiRefreshAt(4, after).toISOString(), '2026-09-18T20:00:00.000Z');
});

test('cached week snapshot is reused until the next 04:00 cut', () => {
  const now = new Date('2026-09-18T14:49:00+08:00');
  assert.equal(cacheMatchesWeek({ date_from: '2026-09-11', date_to: '2026-09-17' }, now), true);
  assert.equal(cacheMatchesWeek({ date_from: '2026-09-10', date_to: '2026-09-16' }, now), false);
});
