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

function bakazeFromRound(currentRound, maxRound = 2) {
  // current_round: 1..4 东, 5..8 南 ...
  const idx = Math.floor(((currentRound || 1) - 1) / 4) % 4
  return ['E', 'S', 'W', 'N'][idx]
}

function kyokuIndex(currentRound) {
  return ((currentRound || 1) - 1) % 4
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
    events.push(...convertRoundToMjai(round, title, scores))
    // 用最后 hu/ryuukyoku 的 deltas 更新 scores
    for (let i = events.length - 1; i >= 0; i--) {
      const e = events[i]
      if (e.type === 'hora' || e.type === 'ryukyoku') {
        if (Array.isArray(e.scores)) scores = [...e.scores]
        break
      }
      if (e.type === 'start_kyoku') break
    }
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

  // 初始手牌按 player_index；MJAI tehais 按 actor 0-3
  const tehais = [0, 1, 2, 3].map((p) => {
    const tiles = [...(round[`p${p}_tiles`] || [])]
    // 庄家示例里可能 14 张；MJAI start 通常 13，庄多的一张用随后 tsumo
    if (p === oya && tiles.length > 13) return tiles.slice(0, 13).map(salasasaToMjai)
    return tiles.slice(0, 13).map(salasasaToMjai)
  })

  // 宝牌指示：从后续 dora tick 或死墙推断；先占位
  let doraMarker = '1m'
  const firstDora = (round.action_ticks || []).find((t) => t[0] === 'dora')
  if (firstDora) doraMarker = salasasaToMjai(firstDora[1])

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
    events.push({ type: 'tsumo', actor: oya, pai: salasasaToMjai(oyaTiles[13]) })
  }

  const ticks = round.action_ticks || []
  let scores = [...scoresIn]
  let lastDiscardActor = null
  let reachPending = null

  for (let i = 0; i < ticks.length; i++) {
    const t = ticks[i]
    const code = t[0]
    if (code === 'd') {
      const actor = guessActor(ticks, i, lastDiscardActor, oya)
      events.push({ type: 'tsumo', actor, pai: salasasaToMjai(t[1]) })
      continue
    }
    if (code === 'gd') {
      const actor = guessActor(ticks, i, lastDiscardActor, oya)
      events.push({ type: 'tsumo', actor, pai: salasasaToMjai(t[1]) })
      continue
    }
    if (code === 'c') {
      const actor = guessCutActor(ticks, i, lastDiscardActor, oya)
      const tsumogiri = t[2] === 'T'
      if (reachPending === actor) {
        events.push({ type: 'reach', actor })
      }
      events.push({
        type: 'dahai',
        actor,
        pai: salasasaToMjai(t[1]),
        tsumogiri
      })
      if (reachPending === actor) {
        // reach_accepted 在打出后
        const deltas = [0, 0, 0, 0]
        deltas[actor] = -1000
        scores = scores.map((s, idx) => s + deltas[idx])
        events.push({
          type: 'reach_accepted',
          actor,
          deltas,
          scores: [...scores]
        })
        reachPending = null
      }
      lastDiscardActor = actor
      continue
    }
    if (code === 'riichi') {
      reachPending = t[1]
      continue
    }
    if (code === 'dora') {
      events.push({ type: 'dora', dora_marker: salasasaToMjai(t[1]) })
      continue
    }
    if (code === 'cl' || code === 'cm' || code === 'cr') {
      const actor = t[2]
      const pai = salasasaToMjai(t[1])
      const consumed = [salasasaToMjai(t[3]), salasasaToMjai(t[4])]
      events.push({
        type: 'chi',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai,
        consumed
      })
      lastDiscardActor = actor
      continue
    }
    if (code === 'p') {
      const actor = t[2]
      events.push({
        type: 'pon',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai: salasasaToMjai(t[1]),
        consumed: [salasasaToMjai(t[3]), salasasaToMjai(t[4])]
      })
      lastDiscardActor = actor
      continue
    }
    if (code === 'g') {
      const actor = t[2]
      events.push({
        type: 'daiminkan',
        actor,
        target: lastDiscardActor ?? (actor + 3) % 4,
        pai: salasasaToMjai(t[1]),
        consumed: [t[3], t[4], t[5]].map(salasasaToMjai)
      })
      continue
    }
    if (code === 'ag') {
      const actor = guessCutActor(ticks, i, lastDiscardActor, oya)
      const tile = salasasaToMjai(t[1])
      const consumed =
        t.length >= 7
          ? [t[3], t[4], t[5], t[6]].map(salasasaToMjai)
          : [tile, tile, tile, tile]
      events.push({ type: 'ankan', actor, consumed })
      continue
    }
    if (code === 'jg') {
      const actor = guessCutActor(ticks, i, lastDiscardActor, oya)
      events.push({
        type: 'kakan',
        actor,
        pai: salasasaToMjai(t[1]),
        consumed: [salasasaToMjai(t[1]), salasasaToMjai(t[1]), salasasaToMjai(t[1])]
      })
      continue
    }
    if (code === 'hu_riichi') {
      const actor = t[1]
      const huClass = t[2]
      const han = t[3]
      const fu = t[4]
      const yakuNames = t[5] || []
      const deltas = [...(t[6] || [0, 0, 0, 0])]
      const doraInd = (t[7] || []).map(salasasaToMjai)
      const ura = (t[8] || []).map(salasasaToMjai)
      scores = scores.map((s, idx) => s + (deltas[idx] || 0))
      const target =
        huClass === 'hu_self' ? actor : discarderFromHuClass(actor, huClass) ?? lastDiscardActor
      events.push({
        type: 'hora',
        actor,
        target,
        pai: '?', // 牌谱未单独存和了哪张时占位
        ura_markers: ura,
        hora_points: Math.max(...deltas.map(Math.abs)),
        yakus: yakuNames.map((name) => [name, 0]),
        fan: han,
        fu,
        deltas,
        scores: [...scores]
      })
      if (doraInd.length && events[0]) {
        /* already have dora events */
      }
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

function guessActor(ticks, idx, lastDiscardActor, oya) {
  for (let j = idx + 1; j < ticks.length; j++) {
    const t = ticks[j]
    if (t[0] === 'c') return guessCutActor(ticks, j, lastDiscardActor, oya)
    if (['cl', 'cm', 'cr', 'p', 'g'].includes(t[0])) return t[2]
    if (t[0] === 'riichi') return t[1]
    if (t[0] === 'hu_riichi') return t[1]
  }
  return lastDiscardActor == null ? oya : (lastDiscardActor + 1) % 4
}

function guessCutActor(ticks, idx, lastDiscardActor, oya) {
  for (let j = idx - 1; j >= 0; j--) {
    const t = ticks[j]
    if (['cl', 'cm', 'cr', 'p', 'g'].includes(t[0])) return t[2]
    if (t[0] === 'riichi') return t[1]
    if (t[0] === 'c') return (guessCutActor(ticks, j, lastDiscardActor, oya) + 1) % 4
    if (t[0] === 'd' || t[0] === 'gd') continue
  }
  return lastDiscardActor == null ? oya : (lastDiscardActor + 1) % 4
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
      const { round, nextIndex } = parseKyoku(events, i)
      roundIdx++
      gameRound[`round_index_${roundIdx}`] = round
      i = nextIndex
      continue
    }
    i++
  }

  if (!roundIdx) throw new Error('未找到 start_kyoku')
  return { game_title: title, game_round: gameRound }
}

function parseKyoku(events, start) {
  const sk = events[start]
  const bakazeMap = { E: 0, S: 1, W: 2, N: 3 }
  const windBase = (bakazeMap[sk.bakaze] || 0) * 4
  const currentRound = windBase + (sk.kyoku || 0) + 1
  const oya = sk.oya ?? 0

  const pTiles = [[], [], [], []]
  ;(sk.tehais || []).forEach((hand, p) => {
    pTiles[p] = (hand || []).map(mjaiToSalasasa).filter((x) => x != null)
  })

  const ticks = []
  if (sk.dora_marker) {
    // 开局宝牌在 salasasa 常写在后续 dora tick；这里先记
  }
  ticks.push(['dora', mjaiToSalasasa(sk.dora_marker) || 11])

  let i = start + 1
  let lastDahai = null
  while (i < events.length) {
    const e = events[i]
    if (e.type === 'start_kyoku' || e.type === 'end_game') break
    if (e.type === 'tsumo') {
      ticks.push(['d', mjaiToSalasasa(e.pai)])
      // 若是庄家开局多摸，已含在 ticks；手牌 14 张时补进 pTiles
      if (e.actor === oya && pTiles[oya].length === 13 && e.pai && e.pai !== '?') {
        pTiles[oya].push(mjaiToSalasasa(e.pai))
      }
      i++
      continue
    }
    if (e.type === 'dahai') {
      const flag = e.tsumogiri ? 'T' : 'F'
      if (e.reach_flag || false) ticks.push(['c', mjaiToSalasasa(e.pai), flag, 'H'])
      else ticks.push(['c', mjaiToSalasasa(e.pai), flag])
      lastDahai = e.actor
      i++
      continue
    }
    if (e.type === 'reach') {
      ticks.push(['riichi', e.actor, 0])
      i++
      continue
    }
    if (e.type === 'reach_accepted') {
      i++
      continue
    }
    if (e.type === 'dora') {
      ticks.push(['dora', mjaiToSalasasa(e.dora_marker)])
      i++
      continue
    }
    if (e.type === 'chi') {
      const called = mjaiToSalasasa(e.pai)
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      const code = chiCode(called, consumed)
      ticks.push([code, called, e.actor, consumed[0], consumed[1]])
      i++
      continue
    }
    if (e.type === 'pon') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      ticks.push(['p', mjaiToSalasasa(e.pai), e.actor, consumed[0], consumed[1]])
      i++
      continue
    }
    if (e.type === 'daiminkan') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      ticks.push(['g', mjaiToSalasasa(e.pai), e.actor, ...consumed.slice(0, 3)])
      i++
      continue
    }
    if (e.type === 'ankan') {
      const consumed = (e.consumed || []).map(mjaiToSalasasa)
      ticks.push(['ag', consumed[0], 'F', ...consumed])
      i++
      continue
    }
    if (e.type === 'kakan') {
      ticks.push(['jg', mjaiToSalasasa(e.pai), 'F'])
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
      ticks.push([
        'hu_riichi',
        actor,
        huClass,
        e.fan || 0,
        e.fu || 0,
        yaku,
        e.deltas || [0, 0, 0, 0],
        [],
        (e.ura_markers || []).map(mjaiToSalasasa).filter((x) => x != null),
        0,
        sk.honba || 0,
        0
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
    seats: [0, 1, 2, 3],
    dealer_index: oya,
    start_player_index: oya,
    riichi: { honba: sk.honba || 0, riichi_sticks: sk.kyotaku || 0 },
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
