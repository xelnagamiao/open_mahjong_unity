const MATCH_OPENING_COUNTDOWN_MS = 3000
const ROUND_RESULT_WAIT_MS = 6000
const CUSTOM_TIME_LIMIT_OPTIONS = Object.freeze([40, 60, 80, 100])
const CUSTOM_MAX_GUESSES_OPTIONS = Object.freeze([6, 8, 10, 12])

function normalizeCustomTimeLimit(value) {
  if (value == null || value === '') return 60
  const seconds = Number(value)
  if (!CUSTOM_TIME_LIMIT_OPTIONS.includes(seconds)) {
    throw new Error('限时只能设置为 40、60、80 或 100 秒')
  }
  return seconds
}

function normalizeCustomMaxGuesses(value) {
  if (value == null || value === '') return 8
  const maxGuesses = Number(value)
  if (!CUSTOM_MAX_GUESSES_OPTIONS.includes(maxGuesses)) {
    throw new Error('猜测次数只能设置为 6、8、10 或 12 次')
  }
  return maxGuesses
}

function shouldUseMatchOpeningCountdown(room) {
  return !room?.openingCountdownUsed && Number(room?.round || 0) === 0
}

module.exports = {
  MATCH_OPENING_COUNTDOWN_MS,
  ROUND_RESULT_WAIT_MS,
  CUSTOM_TIME_LIMIT_OPTIONS,
  CUSTOM_MAX_GUESSES_OPTIONS,
  normalizeCustomTimeLimit,
  normalizeCustomMaxGuesses,
  shouldUseMatchOpeningCountdown,
}
