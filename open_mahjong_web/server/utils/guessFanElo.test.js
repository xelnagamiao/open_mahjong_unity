const test = require('node:test')
const assert = require('node:assert/strict')
const { expectedScore, convertLegacyRating, INITIAL_RATING, EXPECTATION_SCALE, K } = require('./guessFanElo')

test('1500 baseline and 2000-point expectation curve', () => {
  assert.equal(INITIAL_RATING, 1500)
  assert.equal(EXPECTATION_SCALE, 2000)
  assert.equal(K, 32)
  assert.equal(expectedScore(1500,1500), .5)
  assert.ok(Math.abs(expectedScore(2100,1500) - .666139424583122) < 1e-12)
  assert.ok(Math.abs(expectedScore(2100,1500) + expectedScore(1500,2100) - 1) < 1e-12)
  assert.equal(Math.round(K * (1-expectedScore(2100,1500))), 11)
  assert.equal(Math.round(K * (0-expectedScore(2100,1500))), -21)
  assert.ok(Number.isFinite(expectedScore(-1e300,1e300)))
})

test('legacy deviations scale by five around the rebased starting rating', () => {
  assert.deepEqual([900,1000,1100,1300].map(convertLegacyRating), [1000,1500,2000,3000])
  for (const [a,b] of [[900,1100],[1100,1300],[1000,1000]]) {
    const oldExpected=1/(1+10**((b-a)/400))
    assert.ok(Math.abs(expectedScore(convertLegacyRating(a),convertLegacyRating(b))-oldExpected)<1e-12)
  }
})
