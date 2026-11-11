export function gameVenueScope(game) {
  return game.event_id ? (game.venue_kind === 'base' ? 'base' : 'event') : 'lobby'
}

export function gameVenueOptions(games, scope) {
  const venues = new Map()
  for (const game of games) {
    if (game.event_id && gameVenueScope(game) === scope) {
      venues.set(game.event_id, { event_id: game.event_id, name: game.event_name || game.event_id })
    }
  }
  return [...venues.values()].sort((a, b) => a.name.localeCompare(b.name, 'zh-CN') || a.event_id.localeCompare(b.event_id))
}

export function filterAdminGames(games, { scope = 'all', venueId = '', keyword = '', status = 'all' } = {}) {
  const query = keyword.trim().toLowerCase()
  return games.filter(game => {
    if (scope !== 'all' && gameVenueScope(game) !== scope) return false
    if (venueId && game.event_id !== venueId) return false
    const phase = game.vote_phase || 'idle'
    const paused = ['pause_pending', 'paused', 'resume_voting', 'resume_countdown'].includes(phase)
    if (status === 'paused' && !paused) return false
    if (status === 'running' && (paused || phase === 'end_countdown')) return false
    if (status === 'voting' && !['voting', 'resume_voting'].includes(phase)) return false
    if (status === 'ending' && phase !== 'end_countdown') return false
    return !query || [game.gamestate_id, game.room_id, game.event_id, game.event_name,
      ...(game.players || []).flatMap(player => [player.username, player.user_id])]
      .some(value => String(value ?? '').toLowerCase().includes(query))
  })
}
