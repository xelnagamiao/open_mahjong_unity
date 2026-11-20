const DAY = 86400000
const iso = date => date.toISOString().slice(0, 10)

export function activityGrid(year, days, today) {
  const counts = new Map(days.map(day => [day.date, Number(day.games) || 0]))
  const start = new Date(Date.UTC(year, 0, 1)), end = new Date(Date.UTC(year, 11, 31))
  const cursor = new Date(start.getTime() - ((start.getUTCDay() + 6) % 7) * DAY)
  const weeks = []
  while (cursor <= end) {
    const week = []
    for (let i = 0; i < 7; i++) {
      const date = iso(cursor), outside = cursor < start || cursor > end
      const games = counts.get(date) || 0
      week.push({ date, games, outside, future: date > today,
        level: games === 0 ? 0 : games <= 2 ? 1 : games <= 5 ? 2 : games <= 9 ? 3 : 4,
        month: cursor.getUTCDate() <= 7 ? cursor.getUTCMonth() + 1 : null })
      cursor.setUTCDate(cursor.getUTCDate() + 1)
    }
    weeks.push(week)
  }
  return weeks
}

export function longestStreak(days) {
  const dates = [...new Set(days.filter(day => day.games > 0).map(day => day.date))].sort()
  let best = 0, run = 0, previous = null
  for (const day of dates) {
    const time = Date.parse(`${day}T00:00:00Z`)
    run = previous != null && time - previous === DAY ? run + 1 : 1
    best = Math.max(best, run); previous = time
  }
  return best
}

export function safeMailAction(path) {
  return typeof path === 'string' && /^\/account(?:\?(?:manage=[A-Za-z0-9_-]+|application=\d+))?#sec-manage-(?:event|base)$/.test(path) ? path : ''
}
