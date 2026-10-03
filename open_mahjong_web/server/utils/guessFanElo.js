const INITIAL_RATING = 1500
const EXPECTATION_SCALE = 2000
const K = 32
const MIGRATION_ID = 'elo_1500_2000_v1'

function expectedScore(rating, opponentRating) {
  return 1 / (1 + 10 ** Math.max(-16, Math.min(16, (opponentRating - rating) / EXPECTATION_SCALE)))
}

// Rebase the old 1000-point pool and express historical deviations on the new scale.
function convertLegacyRating(rating) {
  return INITIAL_RATING + (rating - 1000) * (EXPECTATION_SCALE / 400)
}

module.exports = { INITIAL_RATING, EXPECTATION_SCALE, K, MIGRATION_ID, expectedScore, convertLegacyRating }
