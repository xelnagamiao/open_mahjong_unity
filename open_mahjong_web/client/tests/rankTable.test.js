import test from 'node:test'
import assert from 'node:assert/strict'
import * as clientRanks from '../src/constants/rankTable.js'
import serverRanks from '../../server/utils/rankNames.js'

test('admin and public rank tables share the complete rank order through the tenth dan', () => {
  assert.deepEqual(clientRanks.RANK_TABLE, serverRanks.RANK_TABLE)
  assert.deepEqual(clientRanks.RANK_NAMES.slice(-2), ['九段', '十段'])
  assert.ok(serverRanks.isValidRankName('十段'))
  assert.ok(serverRanks.RANK_NAME_TO_INDEX['十段'] > serverRanks.RANK_NAME_TO_INDEX['九段'])
})

for (const [name, rules] of [['client', clientRanks], ['server', serverRanks]]) {
  test(`${name}: ninth dan progresses to 7000 PT and can retain points below its starting score`, () => {
    assert.deepEqual(rules.getScoreBounds('九段'), {
      startScore: 3200, promoteScore: 7000, canDemote: true,
      minScore: 0, maxScore: 6999.99, isTopRank: false,
    })
    assert.deepEqual(rules.getPromotionProgress('九段', 6999), {
      current: 6999, target: 7000, percent: 100, remaining: 1, isMaxRank: false,
    })
    for (const score of [0, 1, 3199.99, 3200, 6999.99]) {
      assert.equal(rules.validateRankScore('九段', score).valid, true, `valid ninth dan score ${score}`)
    }
    for (const score of [-0.01, 7000, 7001]) {
      assert.equal(rules.validateRankScore('九段', score).valid, false, `invalid ninth dan score ${score}`)
    }
    assert.equal(rules.getScoreBounds('初段').minScore, 0)
    assert.equal(rules.validateRankScore('初段', 50).valid, true)
  })

  test(`${name}: tenth dan has a fixed 100/100 score and cannot demote`, () => {
    assert.equal(rules.TOP_RANK_NAME, '十段')
    assert.deepEqual(rules.getScoreBounds('十段'), {
      startScore: 100, promoteScore: 100, canDemote: false,
      minScore: 100, maxScore: 100, isTopRank: true,
    })
    for (const score of [-100, 0, 100, 3200, 7000]) {
      assert.deepEqual(rules.getPromotionProgress('十段', score), {
        current: 100, target: 100, percent: 100, remaining: 0, isMaxRank: true,
      })
      assert.equal(rules.clampScoreToRank('十段', score), 100)
      assert.equal(rules.validateRankScore('十段', score).valid, score === 100)
    }
  })

  test(`${name}: non-finite scores cannot bypass rank validation`, () => {
    for (const rank of ['九段', '十段']) {
      for (const score of [NaN, Infinity, -Infinity, 'Infinity']) {
        assert.equal(rules.validateRankScore(rank, score).valid, false)
      }
    }
  })
}
