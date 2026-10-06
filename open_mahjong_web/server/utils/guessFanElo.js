const INITIAL_RATING = 1500
const EXPECTATION_SCALE = 2000
const K = 32

function expectedScore(rating, opponentRating) {
  return 1 / (1 + 10 ** Math.max(-16, Math.min(16, (opponentRating - rating) / EXPECTATION_SCALE)))
}

module.exports = { INITIAL_RATING, EXPECTATION_SCALE, K, expectedScore }
