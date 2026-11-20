import test from 'node:test'
import assert from 'node:assert/strict'
import { activityGrid, longestStreak, safeMailAction } from '../src/utils/accountActivity.js'

test('calendar includes leap day once and uses Monday weeks with future days disabled', () => {
  const weeks = activityGrid(2024, [{ date: '2024-02-29', games: 10 }], '2024-03-01')
  assert.equal(weeks.flat().filter(day => !day.outside).length, 366)
  assert.ok(weeks.every(week => week.length === 7 && new Date(week[0].date).getUTCDay() === 1))
  assert.equal(weeks.flat().find(day => day.date === '2024-02-29').level, 4)
  assert.equal(weeks.flat().find(day => day.date === '2024-03-02').future, true)
})
test('streak ignores duplicate days and zero-game days', () => {
  assert.equal(longestStreak([{date:'2024-02-28',games:1},{date:'2024-02-29',games:2},{date:'2024-02-29',games:2},{date:'2024-03-01',games:1},{date:'2024-03-02',games:0},{date:'2024-03-04',games:1}]), 3)
  assert.equal(longestStreak([]), 0)
})
test('mail actions allow only local application destinations', () => {
  assert.equal(safeMailAction('/account?manage=base-101#sec-manage-base'), '/account?manage=base-101#sec-manage-base')
  assert.equal(safeMailAction('/account?application=19#sec-manage-base'), '/account?application=19#sec-manage-base')
  for (const path of ['https://evil.test', '//evil.test', '/account?manage=x&redirect=evil#sec-manage-base', '/admin', null]) assert.equal(safeMailAction(path), '')
})
