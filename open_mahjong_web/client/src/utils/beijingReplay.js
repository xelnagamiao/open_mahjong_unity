export function parseBeijingFan(label) {
  const parts = String(label ?? '').split('|')
  return parts.length >= 4 && parts[0] === 'BJ' ? { name: parts[3], value: parts[1] + parts[2] } : null
}

export function beijingInfoAt(round, node) {
  let info = round?.beijing ?? null
  for (const tick of (round?.action_ticks ?? []).slice(0, Math.max(0, node))) {
    if (tick[0] !== 'beijing') continue
    if (tick[1] === 'state') info = tick[2]
    if (tick[1] === 'win_source') info = tick[6] ?? info
  }
  return info
}

export function applyBeijingAction(states, event) {
  const actor = Number(event.player), state = states[actor]
  if (!state) return {}
  if (event.action === 'declare_kan') return { actor, robTile: event.tile }
  if (event.action === 'rob_kan') {
    if (state.drawn === event.tile) state.drawn = null
    else {
      const index = state.hand.indexOf(event.tile)
      if (index >= 0) state.hand.splice(index, 1)
    }
    if (state.drawn != null) { state.hand.push(state.drawn); state.drawn = null }
    return { actor, robTile: event.tile }
  }
  return event.action === 'tianting' ? { actor } : {}
}
