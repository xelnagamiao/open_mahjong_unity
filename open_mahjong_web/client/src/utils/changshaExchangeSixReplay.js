export const isExchangeSix = title => title?.sub_rule === 'changsha/exchange_six'
export const exchangeSixActionNames = {
  changsha_deal_dice: '抓牌骰子', changsha_exchange_start: '选择六张', changsha_exchange_roll: '换牌骰子',
  changsha_exchange_six: '交换六张', changsha_declaration: '报听或明牌', changsha_four_joy: '四喜',
  changsha_actual_draw: '取牌', changsha_lock_patterns: '锁定听牌', changsha_hidden_draw: '闷杠摸牌',
  changsha_hidden_reveal: '闷杠看牌', changsha_open_kong: '公开杠牌', changsha_batch_discard: '打出杠牌',
  changsha_kong_continue: '续杠', changsha_sea_draw: '海底摸牌', changsha_sea_release: '海底放出',
  changsha_exchange_six_win: '多人结算', changsha_exchange_six_draw_scores: '流局分数',
}
const remove = (list, tile) => { const i = list.lastIndexOf(tile); if (i >= 0) list.splice(i, 1); return i }
const held = state => { if (state.drawn != null) { state.hand.push(state.drawn); state.drawn = null } }

export function exchangeSixInfoAt(round, node) {
  const info = { ready: [], open: [], disqualified: [], forbidden: null, dealDice: [], dice: [], offset: 0, round: 0, locks: {}, settlements: [], birds: [], fourJoy: [] }
  for (const t of (round?.action_ticks || []).slice(0, node)) {
    if (t[0] === 'changsha_deal_dice') { info.dealDice = t[1]; info.forbidden = t[2] }
    if (t[0] === 'changsha_exchange_start') info.round = t[1]
    if (t[0] === 'changsha_exchange_roll') { info.dice = t[2]; info.offset = t[3] }
    if (t[0] === 'changsha_exchange_six') { info.dice = t[1]; info.offset = t[2]; info.disqualified = t[4]; info.round = t[5] }
    if (t[0] === 'changsha_declaration') {
      if (t[2] === 'changsha_report_ready') info.ready.push(t[1])
      if (t[2] === 'changsha_open_hand') info.open.push(t[1])
    }
    if (t[0] === 'changsha_lock_patterns') info.locks[t[1]] = t[2]
    if (t[0] === 'changsha_four_joy') info.fourJoy.push({ seat: t[1], tile: t[2], dice: t[3] })
    if (t[0] === 'changsha_exchange_six_win') { info.settlements = t[1]; info.birds = t[2] }
  }
  return info
}

// Apply only this subrule's physical events. Ordinary cuts/melds still use the shared replay.
export function applyExchangeSixTick(states, t, context) {
  const action = t[0], seat = Number(t[1]), state = states[seat]
  if (action === 'changsha_exchange_six') {
    states.forEach(held)
    states.forEach((s, i) => { for (const tile of t[3][i]) remove(s.hand, tile) })
    // Offset identifies each recipient's source seat.
    states.forEach((s, i) => s.hand.push(...t[3][(i + Number(t[2])) % 4]))
    states.forEach(s => s.hand.sort((a, b) => a - b))
  } else if (action === 'changsha_hidden_draw') {
    context.hidden = { seat, tile: t[3] }
  } else if (action === 'changsha_hidden_reveal') {
    if (context.hidden) { held(state); state.drawn = context.hidden.tile; context.hidden = null }
  } else if (action === 'changsha_open_kong') {
    held(state); state.publicKong = [...t[4]]
  } else if (action === 'changsha_batch_discard') {
    state.river.push(...t[2]); state.riverDrawn.push(...t[2].map(() => true)); state.publicKong = []
    return { actor: seat, next: (seat + 1) % 4, discarder: seat }
  } else if (action === 'changsha_kong_continue') {
    const i = remove(state.river, t[3]); if (i >= 0) state.riverDrawn.splice(i, 1)
    held(state); state.drawn = t[3]
  } else if (action === 'changsha_sea_draw') {
    held(state); state.drawn = t[2]
  } else if (action === 'changsha_sea_release') {
    if (state.drawn === t[2]) state.drawn = null
    else remove(state.hand, t[2])
  } else if (action === 'changsha_exchange_six_win') {
    context.settled = true
    for (const win of t[1]) {
      const group = win.groups[0], winner = states[win.winner], tile = group.tiles[0]
      winner.hand = [...group.hand]; remove(winner.hand, tile); winner.drawn = tile
    }
    states.forEach(s => { s.publicKong = [] })
  }
  if (['changsha_actual_draw', 'changsha_hidden_draw', 'changsha_hidden_reveal', 'changsha_open_kong', 'changsha_kong_continue', 'changsha_sea_draw', 'changsha_sea_release'].includes(action)) return { actor: seat, next: seat }
  return {}
}

// Terminal hu stores the whole-round delta; intermediate four-joy payments are not additional wins.
export function exchangeSixScoreChanges(round, node, seats) {
  let changes = [0, 0, 0, 0]
  for (const t of (round?.action_ticks || []).slice(0, node)) {
    if (t[0] === 'changsha_four_joy') changes = changes.map((n, original) => n + Number(t[4][original] || 0))
    else if (t[0] === 'changsha_exchange_six_win') {
      for (const win of t[1]) for (const payment of win.payer_details) {
        changes[seats.indexOf(win.winner)] += payment.payment
        changes[seats.indexOf(payment.payer)] -= payment.payment
      }
    } else if (t[0].startsWith('hu_') || t[0] === 'changsha_exchange_six_draw_scores') {
      const scores = t[0].startsWith('hu_') ? t[4] : t[1]
      const values = typeof scores === 'string' ? JSON.parse(scores) : scores
      changes = seats.map(seat => Number(values[seat]) || 0)
    }
  }
  return changes
}

export function exchangeSixStatisticsRound(round) {
  const source = round.action_ticks || [], grouped = source.some(t => t?.[0] === 'changsha_exchange_six_win'), ticks = []
  for (const t of source) {
    if (t[0] === 'changsha_exchange_six_win') {
      for (const win of t[1]) {
        const changes = [0, 0, 0, 0]
        for (const p of win.payer_details) { changes[win.winner] += p.payment; changes[p.payer] -= p.payment }
        const distance = (win.winner - win.payer_details[0].payer + 4) % 4
        const code = win.payer_details.length === 3 ? 'hu_self' : ['hu_first', 'hu_second', 'hu_third'][distance - 1]
        ticks.push([code, win.winner, changes[win.winner], win.groups.flatMap(g => g.fans), changes, win.groups[0].tiles[0]])
      }
    } else if (!(grouped && t[0].startsWith('hu_'))) {
      ticks.push(t)
      if (['changsha_actual_draw', 'changsha_batch_discard', 'changsha_kong_continue'].includes(t[0])) ticks.push(['reset', t[1]])
    }
  }
  return { ...round, action_ticks: ticks }
}

export function exchangeSixResultTick(tick, settlements, selectedSeat) {
  if (!tick?.[0]?.startsWith('hu_') || !settlements.length) return tick
  const win = settlements.find(w => w.winner === selectedSeat) || settlements[0]
  const distance = (win.winner - win.payer_details[0].payer + 4) % 4
  const code = win.payer_details.length === 3 ? 'hu_self' : ['hu_first', 'hu_second', 'hu_third'][distance - 1]
  return [code, win.winner, win.payment, win.groups.flatMap(g => g.fans), tick[4], win.groups[0].tiles[0]]
}
