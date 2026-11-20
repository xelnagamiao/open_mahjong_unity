// Validate Sichuan's frozen wall and resolve each recorded draw to its original slot.
import { requireData } from './browserRecordJson.js'

export function origins(rd, instance, layout, board) {
  const seats = rd.seats
  requireData(Array.isArray(seats) && [...seats].sort().join() === '0,1,2,3', '座次')
  const valid = (n) => Number.isInteger(n) && n >= 0 && n < 4
  let current = rd.start_player_index
  requireData(valid(current) && valid(rd.dealer_index), '起始动作位置')
  const dealer = seats.indexOf(rd.dealer_index)
  const consumed = [0, 0, 0, 0]
  consumed[dealer] = 1
  const size = layout === 'xueliu' ? 8 : 14
  const retired = new Set(),
    used = new Set([dealer + ',0'])
  let shared = null,
    reliable = true
  const ref = ([seat, offset]) => ({
    board_instance_id: instance,
    zone: seat < 0 ? 'common_wall' : 'seat_wall',
    source_original_seat: seat < 0 ? null : seat,
    offset,
  })
  function next(player) {
    for (let i = 1; i <= 4; i++) if (!retired.has(seats.indexOf((player + i) % 4))) return (player + i) % 4
    return player
  }
  function sourceFor(actor) {
    if (shared) return shared[0]
    if (consumed[actor] < size) return [actor, consumed[actor]]
    const partner = (actor + 2) % 4
    if (layout !== 'xueliu' && retired.has(partner) && consumed[partner] < size)
      return [partner, consumed[partner]]
    return null
  }
  const action_meta = rd.action_ticks.map((tick) => {
    const [code] = tick
    let actor = seats.indexOf(current)
    const m = {
      actor_original_seat: actor,
      occurred_at_ms: null,
      wall_ref: null,
      wall_ref_basis: 'not_applicable',
    }
    if (code === 'd' || code === 'gd') {
      let source = null
      if (tick.length >= 6 && tick[2] === 'dw') {
        requireData(valid(tick[5]), '摸牌座次')
        current = tick[5]
        actor = seats.indexOf(current)
        source = [tick[3], tick[4]]
        m.wall_ref_basis = 'recorded'
      } else if (board && reliable) {
        source = sourceFor(actor)
        for (let i = 0; !source && i < 4; i++) {
          current = next(current)
          actor = seats.indexOf(current)
          source = sourceFor(actor)
        }
        m.wall_ref_basis = 'reconstructed'
      } else {
        reliable = false
        m.wall_ref_basis = 'unavailable'
      }
      m.actor_original_seat = actor
      if (source) {
        const [s, i] = source
        requireData(
          (valid(s) || (s === -1 && layout === 'xueliu')) &&
            Number.isInteger(i) &&
            i >= 0 &&
            i < (s < 0 ? 24 : size) &&
            !used.has(source.join()),
          '牌墙位置',
        )
        if (board) requireData((s < 0 ? board.common_wall : board.walls[s])[i] === tick[1], '原始牌墙与摸牌不符')
        if (reliable) {
          requireData(sourceFor(actor)?.join() === source.join(), '牌墙消耗顺序')
          if (shared) shared.shift()
          else if (++consumed[s] === size && layout === 'xueliu') {
            shared = []
            for (let seat = 0; seat < 4; seat++) for (let j = consumed[seat]; j < size; j++) shared.push([seat, j])
            for (let j = 0; j < 24; j++) shared.push([-1, j])
          }
        }
        used.add(source.join())
        m.wall_ref = ref(source)
      }
    } else if (['p', 'g', 'cl', 'cm', 'cr', 'bh'].includes(code)) {
      if (valid(tick[2])) {
        current = tick[2]
        m.actor_original_seat = seats.indexOf(current)
      }
    } else if (code === 'c') current = next(current)
    else if (code === 'reset') {
      requireData(valid(tick[1]), 'reset 座次')
      current = tick[1]
      m.actor_original_seat = null
    } else if (code.startsWith('hu_')) {
      if (valid(tick[1])) {
        m.actor_original_seat = seats.indexOf(tick[1])
        if (layout !== 'xueliu') retired.add(m.actor_original_seat)
        if (code === 'hu_self') current = next(tick[1])
        else if (retired.has(seats.indexOf(current))) current = next(current)
      }
    } else if (!['ag', 'jg', 'ca'].includes(code)) m.actor_original_seat = null
    return m
  })
  const partial = action_meta.some((m) => m.wall_ref_basis === 'unavailable')
  return {
    initial_anchors: [
      {
        kind: 'dealer_initial_draw',
        wall_ref: ref([dealer, 0]),
        actor_original_seat: dealer,
        node: 0,
        phase: 'after_draw',
      },
    ],
    action_meta,
    alignment_status: partial ? 'partial' : 'exact',
    alignment_reason: partial ? 'missing_draw_origin' : null,
  }
}

export function boardDefinition(title, rd, n) {
  const flat = title.duplicate_round_tiles?.[n - 1] ?? (n === 1 ? title.duplicate_tiles : null)
  if (!flat) return null
  const layout = rd.duplicate_wall_layout || title.duplicate_profile?.wall_layout || 'standard'
  requireData(['xueliu', 'standard'].includes(layout) && flat.length === 108, '原始牌墙布局')
  const counts = new Map()
  for (const tile of flat) {
    requireData(Number.isInteger(tile) && tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9, '原始牌种')
    counts.set(tile, (counts.get(tile) || 0) + 1)
  }
  requireData(counts.size === 27 && [...counts.values()].every((n) => n === 4), '原始牌数')
  const size = layout === 'xueliu' ? 21 : 27
  const board = { number: n, dealer: (n - 1) % 4, hands: [], walls: [] }
  for (let i = 0; i < 4; i++) {
    board.hands.push(flat.slice(i * size, i * size + 13))
    board.walls.push(flat.slice(i * size + 13, (i + 1) * size))
  }
  if (layout === 'xueliu') Object.assign(board, { wall_layout: layout, common_wall: flat.slice(84) })
  return board
}
