/** MIL Changchun records retain physical special melds and private bao revisions. */
export const CHANGCHUN_EVENT_NAMES = Object.freeze({ special: '特殊杠', added_offer: '追加特殊杠', added_commit: '加杠成立', added_robbed: '加杠被抢', rob_claim: '抢碰杠牌', kong_score: '杠分', bao_reveal: '翻宝', bao_change: '换宝', bao_seen: '看宝', bao_exhausted: '无宝', tail_pass: '末四张过', round_reveal: '亮牌', settlement: '和牌结算' })
export const CHANGCHUN_FANS = Object.freeze({ 门清: 1, '夹/单吊/单和': 1, 飘和: 2, 七对: 3, 豪华七对: 4, 冲宝: 2, 摸宝: 1 })
export function isChangchunRecord(detail) { return detail?.rule === 'changchun' || detail?.record?.game_title?.rule === 'changchun' }
export function changchunInfoAt(round, node, seat) {
  const info = { tile: null, revision: 0, seen: [-1, -1, -1, -1], exhausted: false, revealed: false, ledger: [], payments: [], bao: '', tailTiles: [] }
  const tail = new Map()
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick?.[0] !== 'cc' || !tick[1] || typeof tick[1] !== 'object') continue
    const e = tick[1]
    if (['bao_reveal', 'bao_change'].includes(e.kind)) {
      info.tile = e.tile ?? null; info.revision = e.revision
      if (Number.isInteger(e.player)) info.seen[e.player] = e.revision
    } else if (e.kind === 'bao_seen') info.seen[e.player] = e.revision
    else if (e.kind === 'bao_exhausted') { info.tile = null; info.exhausted = true }
    else if (e.kind === 'tail_pass') tail.set(e.player, e.tile || 0)
    else if (e.kind === 'round_reveal') { info.tile = e.bao_tile; info.revealed = true; for (const item of e.tail_tiles || []) tail.set(item.player, item.tile) }
    else if (e.kind === 'kong_score') info.ledger.push({ player: e.player, kind: e.kong_kind, delta: e.delta })
    else if (e.kind === 'settlement') { info.payments = e.payments || []; info.bao = e.bao || '' }
  }
  info.visible = info.revealed || (Number.isInteger(seat) && info.seen[seat] === info.revision && info.revision > 0)
  if (!info.visible) info.tile = null
  info.tailTiles = [...tail].map(([player, tile]) => ({ player, tile: info.revealed || player === seat ? tile : 0 }))
  return info
}

/** Returns the exact original wall positions, including returned old indicators. */
export function changchunWallAt(round, node) {
  const tiles = round?.tiles_list || [], ids = tiles.map((_, i) => i)
  let indicator = null, tailRemaining = null
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    const [action, e] = tick
    if (action === 'cc' && ['bao_reveal', 'bao_change', 'bao_exhausted'].includes(e?.kind)) {
      if (indicator !== null) { ids.push(indicator); ids.sort((a, b) => a - b); indicator = null }
      if (Number.isInteger(e.slot)) {
        const pos = ids.indexOf(e.slot)
        if (pos >= 0) { indicator = e.slot; ids.splice(pos, 1) }
      }
    } else if (action === 'd' && ids.length) {
      if (tailRemaining === null && Math.floor(ids.length / 2) <= 9) tailRemaining = 4
      ids.shift()
      if (tailRemaining !== null) tailRemaining--
    } else if (action === 'gd' && ids.length) ids.splice(ids.length - (ids.length % 2 ? 1 : 2), 1)
  }
  const remaining = new Set(ids)
  return { wall: tiles.map((tile, i) => ({ tile, consumed: !remaining.has(i) })), remaining: ids.map(i => tiles[i]), playable: tailRemaining ?? Math.max(0, ids.length - 14 - ids.length % 2) }
}

/** Mutates only the explicitly recorded action. Scoring is applied by RecordReplay. */
export function applyChangchunPhysical(states, e, convert) {
  const state = states[e.player]
  const take = tile => {
    if (state.drawn === tile) state.drawn = null
    else { const i = state.hand.indexOf(tile); if (i >= 0) state.hand.splice(i, 1) }
  }
  const special = code => {
    const [kind, physical, logical] = code.split(':')
    return { tile: convert(Number(physical.split(',')[0])), type: 'kong', chow_mode: 0, meld_from_rel: 0,
      special_kind: kind.slice(1), physical_tiles: physical.split(',').map(t => convert(Number(t))), logical_tiles: logical.split(',').map(t => convert(Number(t))) }
  }
  if (e.kind === 'special' && state) { e.physical.forEach(take); state.melds.push(special(e.code)) }
  else if (e.kind === 'added_offer' && state) {
    take(e.tile)
    if (state.drawn !== null) { state.hand.push(state.drawn); state.drawn = null }
  } else if (e.kind === 'added_commit' && state) state.melds[e.position] = special(e.code)
  else if (e.kind === 'added_robbed' && state && !e.special) {
    const meld = state.melds.find(m => m.type === 'kong' && !m.special_kind && m.meld_from_rel >= 4 && m.tile === convert(e.tile))
    if (meld) { meld.type = 'triplet'; meld.meld_from_rel -= 4 }
  } else if (e.kind === 'rob_claim' && state) { state.river.push(e.tile); state.riverDrawn.push(false) }
  else if (e.kind === 'tail_pass' && state) { if (Number.isInteger(e.tile)) take(e.tile); else state.drawn = null }
  else if (e.kind === 'settlement' && e.source === 'bao_indicator' && states[e.winner]) states[e.winner].drawn = e.tile
  else if (e.kind === 'round_reveal') for (const player of states) for (const meld of player.melds) if (meld.concealed) meld.concealed_face_down = [false, false, false, false]
}

export function changchunTileName(tile) {
  if (!tile) return '未可见'
  if (tile >= 41 && tile <= 47) return ['东', '南', '西', '北', '中', '白', '发'][tile - 41]
  return `${tile % 10}${({ 1: '万', 2: '筒', 3: '条' })[Math.floor(tile / 10)] || ''}`
}
