/** Replay piles are the four remaining walls after the initial hands were dealt. */
export function duplicateReplayWall(round, requestedNode, rulesVersion = round?.duplicate_rules_version) {
  if (!Array.isArray(round?.duplicate_walls) || round.duplicate_walls.length !== 4 || !round.duplicate_walls.every(Array.isArray)) return null
  const piles = round.duplicate_walls.map((tiles, originalPlayer) => tiles.map(tile => ({ tile, originalPlayer, consumed: false })))
  const bloodflow = round.duplicate_wall_layout === 'xueliu'
  if (bloodflow) piles.push((round.duplicate_common_wall || []).map(tile => ({ tile, originalPlayer: -1, consumed: false })))
  const useSecondFromBack = [true, true, true, true]
  const seats = Array.isArray(round.seats) && round.seats.length === 4 ? round.seats : [0, 1, 2, 3]
  let currentPlayer = Number(round.start_player_index) || 0
  for (const tick of (round.action_ticks || []).slice(0, Math.max(0, requestedNode))) {
    const action = String(tick?.[0] || '')
    if (action === 'reset') { currentPlayer = Number(tick[1]); continue }
    if (['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g'].includes(action) && tick.length >= 3) currentPlayer = Number(tick[2])
    if (['d', 'gd', 'bd'].includes(action)) {
      const sichuanDraw = tick[2] === 'dw' && tick.length >= 6
      if (sichuanDraw) currentPlayer = Number(tick[5])
      const originalPlayer = sichuanDraw ? Number(tick[3]) : seats.findIndex(seat => Number(seat) === currentPlayer)
      if (originalPlayer < (bloodflow ? -1 : 0) || originalPlayer > 3) continue
      const remaining = piles[originalPlayer === -1 ? 4 : originalPlayer].filter(tile => !tile.consumed)
      const replacement = !sichuanDraw && Number(rulesVersion) >= 2 && action !== 'd'
      const index = replacement
        ? remaining.length - (useSecondFromBack[originalPlayer] && remaining.length > 1 ? 2 : 1)
        : 0
      const tile = remaining[index]
      if (tile) {
        tile.consumed = true
        if (replacement) useSecondFromBack[originalPlayer] = !useSecondFromBack[originalPlayer]
      }
    } else if (action === 'c') currentPlayer = (currentPlayer + 1) % 4
  }
  if (bloodflow) {
    const commonActive = piles.slice(0, 4).some(pile => pile.every(tile => tile.consumed))
    return piles.flat().map(tile => ({ ...tile, commonActive, shared: tile.originalPlayer === -1 || (commonActive && !tile.consumed) }))
  }
  return piles.flat()
}
