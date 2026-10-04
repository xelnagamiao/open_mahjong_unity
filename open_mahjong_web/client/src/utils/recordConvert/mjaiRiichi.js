/**
 * MJAI ↔ salasasa（日麻）
 * 事件对齐 gimite MJAI / Mortal 常用子集。
 */
import {
  salasasaToMjai,
  mjaiToSalasasa,
  discarderFromHuClass,
  parseJsonInput
} from './tiles.js'

function ensureRiichi(title) {
  if (title?.rule && title.rule !== 'riichi') {
    throw new Error(`MJAI 互转仅支持日麻，当前 rule=${title.rule}`)
  }
}

function tileToMjai(tile) {
  return tile == null || tile === '?' ? '?' : salasasaToMjai(tile)
}

function normalTile(tile) {
  return [105, 205, 305].includes(tile) ? Math.floor(tile / 100) * 10 + 5 : tile
}

function roundSeats(round) {
  const seats = round.seats
  return Array.isArray(seats) && seats.length === 4 && new Set(seats).size === 4 &&
    seats.every(seat => Number.isInteger(seat) && seat >= 0 && seat < 4) ? seats : [0, 1, 2, 3]
}

function mapEventPlayers(event, playerMap, arrayOrder) {
  const mapped = { ...event }
  for (const key of ['actor', 'target', 'oya']) {
    if (Number.isInteger(event[key])) mapped[key] = playerMap[event[key]]
  }
  for (const key of ['scores', 'deltas', 'tehais', 'tenpais']) {
    if (Array.isArray(event[key]) && event[key].length === 4) mapped[key] = arrayOrder.map(index => event[key][index])
  }
  return mapped
}

function bakazeFromRound(currentRound, maxRound = 2) {
  // current_round: 1..4 东, 5..8 南 ...
  const idx = Math.floor(((currentRound || 1) - 1) / 4) % 4
  return ['E', 'S', 'W', 'N'][idx]
}

function kyokuIndex(currentRound) {
  return ((currentRound || 1) - 1) % 4 + 1
}

/** salasasa → MJAI 事件数组（整场） */
export function salasasaToMjaiRecord(input) {
  const data = typeof input === 'string' ? parseJsonInput(input) : input
  if (!data?.game_title || !data?.game_round) throw new Error('需要 salasasa 牌谱')
  ensureRiichi(data.game_title)

  const title = data.game_title
  const events = []
  events.push({
    type: 'start_game',
    names: [0, 1, 2, 3].map((i) => title[`p${i}_name`] || `P${i}`),
    kyoku_first: 0,
    aka_flag: Boolean(title.red_dora)
  })

  const rounds = Object.keys(data.game_round)
    .filter((k) => k.startsWith('round_index_'))
    .sort((a, b) => Number(a.split('_').pop()) - Number(b.split('_').pop()))

  let scores = [0, 1, 2, 3].map(() => Number(title.starting_score || 25000))
  if (Array.isArray(title.starting_scores) && title.starting_scores.length === 4) {
    scores = title.starting_scores.map(Number)
  }

  for (const key of rounds) {
    const round = data.game_round[key]
    const seats = roundSeats(round)
    const originalBySeat = [0, 1, 2, 3].map(seat => seats.indexOf(seat))
    const roundScores = Array.isArray(round.scores) && round.scores.length === 4
      ? round.scores.map(Number)
      : scores
    events.push(...convertRoundToMjai(round, title, originalBySeat.map(original => roundScores[original]))
      .map(event => mapEventPlayers(event, originalBySeat, seats)))
    events.push({ type: 'end_kyoku' })
    // 用最后 hu/ryuukyoku 的 deltas 更新 scores
    for (let i = events.length - 1; i >= 0; i--) {
      const e = events[i]
      if (e.type === 'hora' || e.type === 'ryukyoku' || e.type === 'reach_accepted') {
        if (Array.isArray(e.scores)) scores = [...e.scores]
        break
      }
      if (e.type === 'start_kyoku') break
    }
  }

  if (Array.isArray(title.riichi_final_scores) && title.riichi_final_scores.length === 4) {
    scores = title.riichi_final_scores.map(Number)
  }
  events.push({ type: 'end_game', scores })
  return {
    format: 'mjai',
    events,
    ndjson: events.map((e) => JSON.stringify(e)).join('\n')
  }
}

function convertRoundToMjai(round, title, scoresIn) {
  const events = []
  const honba = round.riichi?.honba ?? 0
  const kyotaku = round.riichi?.riichi_sticks ?? 0
  const bakaze = bakazeFromRound(round.current_round, title.max_round)
  const kyoku = kyokuIndex(round.current_round)
  const oya = round.dealer_index ?? 0
  const startPlayer = round.start_player_index ?? oya

  // 初始手牌按 player_index；MJAI tehais 按 actor 0-3
  const tehais = [0, 1, 2, 3].map((p) => {
    const tiles = [...(round[`p${p}_tiles`] || [])]
    // 庄家示例里可能 14 张；MJAI start 通常 13，庄多的一张用随后 tsumo
    if (p === oya && tiles.length > 13) return tiles.slice(0, 13).map(salasasaToMjai)
    return tiles.slice(0, 13).map(salasasaToMjai)
  })

  // 首张宝牌指示牌通常保存在牌山死墙（倒数第 6 张），后续 dora tick 仅表示杠后新增指示牌。
  // 从 MJAI 导入的牌谱会在 riichi.dora_marker 写入首张指示牌；旧谱缺失时保留 1m 作为兜底。
  let doraMarker = '1m'
  const storedDoraMarker = round.riichi?.dora_marker
  if (storedDoraMarker != null) doraMarker = tileToMjai(storedDoraMarker)
  else if (Array.isArray(round.tiles_list) && round.tiles_list.length >= 6) {
    doraMarker = tileToMjai(round.tiles_list[round.tiles_list.length - 6])
  }
  const firstDora = (round.action_ticks || []).find((t) => t[0] === 'dora')
  if (storedDoraMarker == null && (!Array.isArray(round.tiles_list) || round.tiles_list.length < 6) && firstDora) {
    doraMarker = tileToMjai(firstDora[1])
  }

  events.push({
    type: 'start_kyoku',
    bakaze,
    dora_marker: doraMarker,
    kyoku,
    honba,
    kyotaku,
    oya,
    scores: [...scoresIn],
    tehais
  })

  // 庄家第 14 张
  const oyaTiles = round[`p${oya}_tiles`] || []
  if (oyaTiles.length >= 14) {
    events.push({ type: 'tsumo', actor: oya, pai: tileToMjai(oyaTiles[13]) })
  }

  const ticks = round.action_ticks || []
  let scores = [...scoresIn]
  let lastDiscardActor = null
  let lastWinnableTile = null
  let reachPending = null
  const declaredReach = new Set()
  const acceptedReach = new Set()
  const acceptReach = (actor) => {
    if (acceptedReach.has(actor)) return
    const deltas = [0, 0, 0, 0]
    deltas[actor] = -1000
    scores = scores.map((score, index) => score + deltas[index])
    events.push({ type: 'reach_accepted', actor, deltas, scores: [...scores] })
    acceptedReach.add(actor)
  }
  // d/c tick 不带座位。按日麻回合推进推断：普通弃牌后轮到下家，鸣牌后由鸣牌者先打，杠后摸岭上牌。
  let turnActor = startPlayer
  let meldActor = null
  const ponTiles = new Map()

  for (let i = 0; i < ticks.length; i++) {
    const t = ticks[i]
    const code = t[0]
    if (code === 'd') {
      lastWinnableTile = t[1]
      const actor = meldActor ?? turnActor
      // 杠后的摸牌仍由杠牌者执行；本次摸牌后等待其切牌。
      meldActor = null
      events.push({ type: 'tsumo', actor, pai: tileToMjai(t[1]) })
      continue
    }
    if (code === 'gd') {
      lastWinnableTile = t[1]
      const actor = meldActor ?? turnActor
      meldActor = null
      events.push({ type: 'tsumo', actor, pai: tileToMjai(t[1]) })
      continue
    }
    if (code === 'c') {
      lastWinnableTile = t[1]
      const actor = meldActor ?? turnActor
      const tsumogiri = t[2] === 'T'
      if ((reachPending === actor || t[3] === 'H') && !declaredReach.has(actor)) {
        events.push({ type: 'reach', actor })
        declaredReach.add(actor)
      }
      events.push({
        type: 'dahai',
        actor,
        pai: tileToMjai(t[1]),
        tsumogiri
      })
      if (reachPending === actor) {
        acceptReach(actor)
        reachPending = null
      }
      lastDiscardActor = actor
      turnActor = (actor + 1) % 4
      meldActor = null
      continue
    }
    if (code === 'riichi') {
      const actor = Number(t[1])
      if (declaredReach.has(actor)) acceptReach(actor)
      else reachPending = actor // 兼容旧导入谱中 riichi 在 c 之前的顺序。
      continue
    }
    if (code === 'dora') {
      events.push({ type: 'dora', dora_marker: tileToMjai(t[1]) })
      continue
    }
    if (code === 'cl' || code === 'cm' || code === 'cr') {
      const actor = t[2]
      const pai = tileToMjai(t[1])
      const consumed = [tileToMjai(t[3]), tileToMjai(t[4])]
      events.push({
        type: 'chi',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai,
        consumed
      })
      lastDiscardActor = actor
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'p') {
      const actor = t[2]
      ponTiles.set(`${actor}/${normalTile(t[1])}`, [t[1], t[3], t[4]])
      events.push({
        type: 'pon',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai: tileToMjai(t[1]),
        consumed: [tileToMjai(t[3]), tileToMjai(t[4])]
      })
      lastDiscardActor = actor
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'g') {
      const actor = t[2]
      events.push({
        type: 'daiminkan',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai: tileToMjai(t[1]),
        consumed: [t[3], t[4], t[5]].map(tileToMjai)
      })
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'ag') {
      const actor = meldActor ?? turnActor
      const tile = tileToMjai(t[1])
      const consumed =
        t.length >= 7
          ? [t[3], t[4], t[5], t[6]].map(tileToMjai)
          : [tile, tile, tile, tile]
      events.push({ type: 'ankan', actor, consumed })
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'jg') {
      const actor = meldActor ?? turnActor
      const actualTile = Number.isInteger(t[3]) && t[3] >= 11 ? t[3] : t[1]
      const normal = normalTile(t[1])
      const consumed = ponTiles.get(`${actor}/${normal}`) || [normal, normal, normal]
      events.push({
        type: 'kakan',
        actor,
        pai: tileToMjai(actualTile),
        consumed: consumed.map(tileToMjai)
      })
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'rk') {
      const actor = Number(t[1])
      const tile = tileToMjai(t[2])
      const kind = t[4] === 'angang' ? 'ankan' : 'kakan'
      const consumed = Array.isArray(t[5]) ? t[5].map(tileToMjai) : Array(kind === 'ankan' ? 4 : 3).fill(tile)
      events.push(kind === 'ankan' ? { type: kind, actor, consumed } : { type: kind, actor, pai: tile, consumed })
      lastDiscardActor = actor
      lastWinnableTile = t[2]
      turnActor = actor
      meldActor = actor
      continue
    }
    if (code === 'hu_riichi') {
      const actor = t[1]
      const huClass = t[2]
      const han = t[3]
      const fu = t[4]
      const yakuNames = t[5] || []
      const deltas = [...(t[6] || [0, 0, 0, 0])]
      const ura = (t[8] || []).map(tileToMjai)
      scores = scores.map((s, idx) => s + (deltas[idx] || 0))
      const target =
        huClass === 'hu_self' ? actor : discarderFromHuClass(actor, huClass) ?? lastDiscardActor
      events.push({
        type: 'hora',
        actor,
        target,
        pai: lastWinnableTile == null ? '?' : tileToMjai(lastWinnableTile),
        uradora_markers: ura,
        hora_points: Number(t[12]) > 0 ? Number(t[12]) : Math.max(...deltas.map(Math.abs)),
        yakus: yakuNames.map((name) => [name, 0]),
        fan: han,
        fu,
        deltas,
        scores: [...scores]
      })
      continue
    }
    if (code === 'ryuukyoku') {
      const deltas = [...(t[2] || [0, 0, 0, 0])]
      scores = scores.map((s, idx) => s + (deltas[idx] || 0))
      events.push({
        type: 'ryukyoku',
        reason: t[3] || 'fanpai',
        tenpais: t[1] || [0, 0, 0, 0],
        deltas,
        scores: [...scores]
      })
      continue
    }
  }

  return events
}

/** MJAI NDJSON / 事件数组 → salasasa */
export function mjaiToSalasasaRecord(input) {
  let events
  if (typeof input === 'string') {
    const parsed = parseJsonInput(input)
    if (Array.isArray(parsed)) events = parsed
    else if (parsed?.events) events = parsed.events
    else if (parsed?.ndjson) {
      events = parsed.ndjson
        .split(/\r?\n/)
        .filter(Boolean)
        .map((l) => JSON.parse(l))
    } else if (parsed?.type) events = [parsed]
    else throw new Error('无法识别 MJAI 输入')
  } else if (Array.isArray(input)) events = input
  else if (input?.events) events = input.events
  else throw new Error('需要 MJAI 事件数组或 NDJSON')

  const title = {
    rule: 'riichi',
    room_type: 'custom',
    sub_rule: 'riichi/standard',
    commitment_hex: '0'.repeat(64),
    salt: '0'.repeat(32),
    max_round: 2,
    hepai_limit: 1,
    open_cuohe: false,
    tips: false,
    is_player_set_random_seed: false,
    red_dora: true,
    hepai_way: 'multi_ron',
    starting_score: 25000,
    player_entry_order: [1, 2, 3, 4],
    p0_uid: 1,
    p0_name: 'P0',
    p1_uid: 2,
    p1_name: 'P1',
    p2_uid: 3,
    p2_name: 'P2',
    p3_uid: 4,
    p3_name: 'P3',
    is_external: true,
    source_format: 'mjai'
  }

  const gameRound = {}
  let roundIdx = 0
  let i = 0
  while (i < events.length) {
    const e = events[i]
    if (e.type === 'start_game') {
      if (Array.isArray(e.names)) {
        e.names.forEach((n, idx) => {
          title[`p${idx}_name`] = n
        })
      }
      if (typeof e.aka_flag === 'boolean') title.red_dora = e.aka_flag
      i++
      continue
    }
    if (e.type === 'start_kyoku') {
      if (!Array.isArray(title.starting_scores) && Array.isArray(e.scores) && e.scores.length === 4) {
        title.starting_scores = e.scores.map(Number)
        title.starting_score = title.starting_scores[0]
      }
      const { round, nextIndex } = parseKyoku(events, i)
      roundIdx++
      gameRound[`round_index_${roundIdx}`] = round
      i = nextIndex
      continue
    }
    if (e.type === 'end_game' && Array.isArray(e.scores) && e.scores.length === 4) {
      title.riichi_final_scores = e.scores.map(Number)
    }
    i++
  }

  if (!roundIdx) throw new Error('未找到 start_kyoku')
  return { game_title: title, game_round: gameRound }
}

function parseKyoku(events, start) {
  const originalHeader = events[start]
  const originalDealer = originalHeader.oya ?? 0
  const seats = [0, 1, 2, 3].map(original => (original - originalDealer + 4) % 4)
  const originalBySeat = [0, 1, 2, 3].map(seat => seats.indexOf(seat))
  const toSeatEvent = event => mapEventPlayers(event, seats, originalBySeat)
  const sk = toSeatEvent(originalHeader)
  const bakazeMap = { E: 0, S: 1, W: 2, N: 3 }
  const windBase = (bakazeMap[sk.bakaze] || 0) * 4
  const kyoku = Math.max(1, Math.min(4, Number(sk.kyoku) || 1))
  const currentRound = windBase + kyoku
  const oya = sk.oya ?? 0

  const pTiles = [[], [], [], []]
  ;(sk.tehais || []).forEach((hand, p) => {
    pTiles[p] = (hand || []).map(mjaiToSalasasa).filter((x) => x != null)
  })

  const ticks = []
  // 首张宝牌指示牌属于局头元数据，不写入 dora tick；dora tick 只表示杠后新增指示牌。
  const doraMarker = sk.dora_marker ? mjaiToSalasasa(sk.dora_marker) : null

  let i = start + 1
  let pendingReachActor = null
  const drawnTiles = [null, null, null, null]
  while (i < events.length) {
    const e = toSeatEvent(events[i])
    if (e.type === 'start_kyoku' || e.type === 'end_kyoku' || e.type === 'end_game') break
    if (e.type === 'tsumo') {
      // salasasa 局头固定保存发牌后的 13 张；庄家第 14 张也只记录为 d tick。
      // 若把它同时追加到 pTiles，反向转换会重复输出一条 tsumo。
      const tile = mjaiToSalasasa(e.pai)
      if (tile != null) ticks.push(['d', tile])
      drawnTiles[e.actor] = tile
      i++
      continue
    }
    if (e.type === 'dahai') {
      const flag = e.tsumogiri ? 'T' : 'F'
      // MJAI 通常用 reach → dahai → reach_accepted，dahai 本身未必带 reach_flag。
      const reachDiscard = e.reach_flag || pendingReachActor === e.actor
      const tile = mjaiToSalasasa(e.pai)
      if (tile != null) {
        if (reachDiscard) ticks.push(['c', tile, flag, 'H'])
        else ticks.push(['c', tile, flag])
      }
      if (pendingReachActor === e.actor) pendingReachActor = null
      drawnTiles[e.actor] = null
      i++
      continue
    }
    if (e.type === 'reach') {
      pendingReachActor = e.actor
      i++
      continue
    }
    if (e.type === 'reach_accepted') {
      ticks.push(['riichi', e.actor, 0])
      i++
      continue
    }
    if (e.type === 'dora') {
      const tile = mjaiToSalasasa(e.dora_marker)
      if (tile != null) ticks.push(['dora', tile])
      i++
      continue
    }
    if (e.type === 'chi') {
      const called = mjaiToSalasasa(e.pai)
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      if (called == null || consumed.some((tile) => tile == null)) {
        i++
        continue
      }
      const code = chiCode(called, consumed)
      ticks.push([code, called, e.actor, consumed[0], consumed[1]])
      i++
      continue
    }
    if (e.type === 'pon') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      if (consumed.some((tile) => tile == null)) {
        i++
        continue
      }
      ticks.push(['p', mjaiToSalasasa(e.pai), e.actor, consumed[0], consumed[1]])
      i++
      continue
    }
    if (e.type === 'daiminkan') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      if (consumed.length < 3 || consumed.some((tile) => tile == null)) {
        i++
        continue
      }
      ticks.push(['g', mjaiToSalasasa(e.pai), e.actor, ...consumed.slice(0, 3)])
      i++
      continue
    }
    if (e.type === 'ankan') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      if (!consumed.length || consumed.some((tile) => tile == null)) {
        i++
        continue
      }
      const nextEvent = events.slice(i + 1).find((entry) => !['none', 'dora'].includes(entry.type))
      const next = nextEvent && toSeatEvent(nextEvent)
      const fromDraw = drawnTiles[e.actor] != null && normalTile(drawnTiles[e.actor]) === normalTile(consumed[0])
      if (next?.type === 'hora' && next.target === e.actor && next.actor !== e.actor) {
        ticks.push(['rk', e.actor, mjaiToSalasasa(next.pai) ?? consumed[0], Number(fromDraw), 'angang', consumed])
      } else ticks.push(['ag', normalTile(consumed[0]), fromDraw ? 'T' : 'F', ...consumed])
      drawnTiles[e.actor] = null
      i++
      continue
    }
    if (e.type === 'kakan') {
      const tile = mjaiToSalasasa(e.pai)
      const nextEvent = events.slice(i + 1).find((entry) => !['none', 'dora'].includes(entry.type))
      const next = nextEvent && toSeatEvent(nextEvent)
      const fromDraw = drawnTiles[e.actor] === tile
      if (tile != null) {
        if (next?.type === 'hora' && next.target === e.actor && next.actor !== e.actor) {
          ticks.push(['rk', e.actor, tile, Number(fromDraw), 'jiagang', (e.consumed || []).map(mjaiToSalasasa)])
        } else ticks.push(['jg', normalTile(tile), fromDraw ? 'T' : 'F', tile])
      }
      drawnTiles[e.actor] = null
      i++
      continue
    }
    if (e.type === 'hora') {
      const actor = e.actor
      const target = e.target
      let huClass = 'hu_self'
      if (target != null && target !== actor) {
        const delta = (target - actor + 4) % 4
        if (delta === 3) huClass = 'hu_first'
        else if (delta === 2) huClass = 'hu_second'
        else if (delta === 1) huClass = 'hu_third'
      }
      const yaku = (e.yakus || []).map((y) => (Array.isArray(y) ? y[0] : y))
      const uraMarkers = e.uradora_markers ?? e.ura_markers ?? []
      ticks.push([
        'hu_riichi',
        actor,
        huClass,
        e.fan || 0,
        e.fu || 0,
        yaku,
        e.deltas || [0, 0, 0, 0],
        [],
        uraMarkers.map(mjaiToSalasasa).filter((x) => x != null),
        0,
        sk.honba || 0,
        0,
        e.hora_points ?? null
      ])
      i++
      continue
    }
    if (e.type === 'ryukyoku') {
      ticks.push(['ryuukyoku', e.tenpais || [0, 0, 0, 0], e.deltas || [0, 0, 0, 0], e.reason || 'exhaustive'])
      i++
      continue
    }
    i++
  }
  ticks.push(['end'])

  const round = {
    round_index: currentRound,
    current_round: currentRound,
    seats,
    dealer_index: oya,
    start_player_index: oya,
    ...(Array.isArray(originalHeader.scores) && originalHeader.scores.length === 4 ? { scores: originalHeader.scores.map(Number) } : {}),
    riichi: {
      honba: sk.honba || 0,
      riichi_sticks: sk.kyotaku || 0,
      ...(doraMarker != null ? { dora_marker: doraMarker } : {})
    },
    p0_tiles: pTiles[0],
    p1_tiles: pTiles[1],
    p2_tiles: pTiles[2],
    p3_tiles: pTiles[3],
    tiles_list: [],
    action_ticks: ticks
  }
  return { round, nextIndex: i }
}

function chiCode(called, consumed) {
  const all = [called, ...consumed].map((t) => (t >= 100 ? Math.floor(t / 100) * 10 + 5 : t))
  all.sort((a, b) => a - b)
  const mid = all[1]
  if (called === mid || (called >= 100 && mid % 10 === 5)) return 'cm'
  if (called < mid) return 'cl'
  return 'cr'
}
