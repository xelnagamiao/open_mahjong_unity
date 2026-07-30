const MATCH_OPENING_COUNTDOWN_MS = 3000
const ROUND_RESULT_WAIT_MS = 6000

function shouldUseMatchOpeningCountdown(room) {
  return !room?.openingCountdownUsed && Number(room?.round || 0) === 0
}

module.exports = {
  MATCH_OPENING_COUNTDOWN_MS,
  ROUND_RESULT_WAIT_MS,
  shouldUseMatchOpeningCountdown,
}
