/**
 * 国标牌谱高级分析：含标准分析全部指标，以及听牌/流局/被摸/点炮听牌、和张听张、主番与凑番和、副露、点炮、分巡场得、幸运值与狗力值。
 * 听牌判定复用客户端 gbTingpai；向听复用国标牌理计算器默认选项；明副露不含暗杠，加杠并入原碰不另计一次。
 */

import {
  GUOBIAO_FAN_DICT,
  GUOBIAO_FAN_VALUES,
  listGuobiaoFanEntries,
  formatGuobiaoFanComposition,
  parseGuobiaoFanLabel,
} from '../constants/guobiaoFanDict.js'
import {
  analyzeRecords,
  asTileId,
  findOriginalIndex,
  isCuohe,
  listSortedRoundEntries,
  parseHuTick,
  resolveRoundSeats,
  seatForOriginal,
} from './recordAnalyzer.js'

export const COUFAN_KEY = '__coufan__'
const COUFAN_LABEL = '凑番和'
export const MENDUANPING_KEY = '__menduanping__'
const MENDUANPING_LABEL = '门断平'

const MEN_NAMES = new Set(['门前清'])
const DUAN_NAMES = new Set(['断幺', '断幺九'])
const PING_NAMES = new Set(['平和'])

const HU_ACTIONS = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third'])
const RON_ACTIONS = new Set(['hu_first', 'hu_second', 'hu_third'])
const CLAIM_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g'])
const FLOWER_MIN = 51
const FLOWER_MAX = 58

function isFlowerTile(tile) {
  const id = normTile(tile)
  return id >= FLOWER_MIN && id <= FLOWER_MAX
}

const TILE_SUIT_ROWS = [
  [11, 12, 13, 14, 15, 16, 17, 18, 19],
  [21, 22, 23, 24, 25, 26, 27, 28, 29],
  [31, 32, 33, 34, 35, 36, 37, 38, 39],
  [41, 42, 43, 44, 45, 46, 47],
]

export const FAN_SEARCH_OPTIONS = [
  { key: COUFAN_KEY, label: '凑番和（全部为 1–2 番，不含门断平）', value: 0 },
  { key: MENDUANPING_KEY, label: '门断平（门前清+断幺+平和）', value: 0 },
  ...listGuobiaoFanEntries().map((item) => ({
    key: item.key,
    label: item.label,
    name: item.name,
    value: item.value,
  })),
]

function tickInt(tick, index, fallback = null) {
  if (!Array.isArray(tick) || index >= tick.length) return fallback
  const value = tick[index]
  if (typeof value === 'number' && Number.isFinite(value)) return value
  if (typeof value === 'string' && /^-?\d+$/.test(value)) return Number(value)
  return fallback
}

function normTile(tile) {
  const id = Number(tile)
  if (!Number.isFinite(id)) return 0
  return id >= 100 ? id % 100 : Math.trunc(id)
}

function removeExactOrNormalized(tiles, tile) {
  const wanted = Number(tile)
  let index = tiles.indexOf(wanted)
  if (index < 0) {
    const n = normTile(wanted)
    index = tiles.findIndex((item) => normTile(item) === n)
  }
  if (index < 0) return null
  return tiles.splice(index, 1)[0] ?? null
}

const GUOBIAO_FAN_VALUE_BY_NAME = Object.fromEntries(
  Object.entries(GUOBIAO_FAN_DICT).map(([key, name]) => [name, GUOBIAO_FAN_VALUES[key] ?? 0]),
)

function takeForClaim(tick, action, claimedTile, hand) {
  const count = action === 'g' ? 3 : 2
  const explicitTiles = tick.slice(3, 3 + count)
    .map((value) => (typeof value === 'number' ? value : Number(value)))
    .filter((value) => Number.isFinite(value) && value > 0)
  if (explicitTiles.length === count) {
    return explicitTiles.map((tile) => removeExactOrNormalized(hand, tile) ?? tile)
  }
  const n = normTile(claimedTile)
  let wanted
  if (action === 'cl') wanted = [n - 2, n - 1]
  else if (action === 'cm') wanted = [n - 1, n + 1]
  else if (action === 'cr') wanted = [n + 1, n + 2]
  else wanted = Array(count).fill(n)
  return wanted.map((tile) => removeExactOrNormalized(hand, tile) ?? tile)
}

function comboForClaim(action, claimedTile) {
  const n = normTile(claimedTile)
  if (action === 'cl') return `s${n - 1}`
  if (action === 'cm') return `s${n}`
  if (action === 'cr') return `s${n + 1}`
  if (action === 'p') return `k${n}`
  if (action === 'g') return `g${n}`
  return null
}

function closedHandIds(hand) {
  return hand
    .map(normTile)
    .filter((id) => id > 0 && (id < FLOWER_MIN || id > FLOWER_MAX))
    .sort((a, b) => a - b)
}

function recordGameId(item, record) {
  return String(item?.game_id || record?.game_id || record?.game_title?.game_id || '')
}

/**
 * 主番 = 单番种番数 ≥ 4。
 * 凑番和 = 全部番种都是 1 或 2 番，且不是门断平（门前清+断幺+平和）。
 * 门断平单独统计，不计入凑番和。
 */
export function classifyWinYaku(yaku) {
  const parsed = (Array.isArray(yaku) ? yaku : [])
    .map((raw) => parseGuobiaoFanLabel(raw))
    .filter((item) => item.name && !String(item.name).includes('错和'))
  const names = parsed.map((item) => item.name)
  const mainFans = parsed.filter((item) => item.value >= 4)
  const allOneOrTwo = parsed.length > 0
    && parsed.every((item) => item.value === 1 || item.value === 2)
  const isMenduanping = allOneOrTwo
    && names.some((name) => MEN_NAMES.has(name))
    && names.some((name) => DUAN_NAMES.has(name))
    && names.some((name) => PING_NAMES.has(name))
  const isCoufan = allOneOrTwo && !isMenduanping
  return { parsed, mainFans, isCoufan, isMenduanping }
}

function winMatchesFan(win, fanKey) {
  if (!win || !fanKey) return false
  if (fanKey === COUFAN_KEY) return !!win.isCoufan
  if (fanKey === MENDUANPING_KEY) return !!win.isMenduanping
  const name = GUOBIAO_FAN_DICT[fanKey]
  if (!name) return false
  return (win.parsed || []).some((item) => item.name === name)
}

/** 小局结束巡分段：≤5、6–10、11–15、≥16。 */
export const XUN_END_SCORE_BUCKETS = [
  { key: 'le5', label: '5巡内结束场次平均场得' },
  { key: 'le10', label: '5–10巡结束场次平均场得' },
  { key: 'le15', label: '10–15巡结束场次平均场得' },
  { key: 'gt15', label: '15巡后结束场次平均场得' },
]

export function xunEndBucket(xunmu) {
  const x = Number(xunmu)
  if (!Number.isFinite(x) || x <= 5) return 'le5'
  if (x <= 10) return 'le10'
  if (x <= 15) return 'le15'
  return 'gt15'
}

function emptyXunEndMap() {
  return { le5: 0, le10: 0, le15: 0, gt15: 0 }
}

function drawScoreChanges(tick) {
  if (!Array.isArray(tick) || tick.length < 2) return null
  const code = tick[0]
  const candidates = code === 'ryuukyoku' ? [tick[2]] : tick.slice(1)
  for (const raw of candidates) {
    if (!Array.isArray(raw) || raw.length < 4) continue
    const nums = raw.slice(0, 4).map((value) => Number(value))
    if (nums.every((value) => Number.isFinite(value))) return nums
  }
  return null
}

const SHARE_SILENT_ACTIONS = new Set(['reset', 'ask_hand', 'ask_other', 'ca'])

function isShareSilentTick(tick) {
  const action = String(tick?.[0] ?? '')
  if (!action || SHARE_SILENT_ACTIONS.has(action)) return true
  if (!String(action).startsWith('hu_')) return false
  const hu = parseHuTick(tick)
  return !!(hu && isCuohe(hu.yaku))
}

/** 分享链接落到和牌前最后一个可见动作（出牌/摸牌/加杠），跳过鸣牌询问等静默 tick。 */
export function shareNodeBeforeWin(ticks, huNode) {
  const list = Array.isArray(ticks) ? ticks : []
  const hu = Math.max(0, Number(huNode) || 0)
  const end = Math.min(hu, list.length)
  for (let index = end - 1; index >= 0; index -= 1) {
    if (!isShareSilentTick(list[index])) return index
  }
  return Math.max(0, hu - 1)
}

function bump(map, key, n = 1) {
  if (key == null || key === '') return
  map[key] = (map[key] || 0) + n
}

function emptyAcc() {
  return {
    total_games: 0,
    total_rounds: 0,
    win_count: 0,
    self_draw_count: 0,
    ron_win_count: 0,
    liuju_count: 0,
    tenpai_round_count: 0,
    total_first_tenpai_turn: 0,
    total_win_score: 0,
    total_win_fan: 0,
    closed_win_count: 0,
    deal_in_count: 0,
    deal_in_tenpai_count: 0,
    tsumo_against_count: 0,
    total_deal_in_fan: 0,
    total_deal_in_score: 0,
    total_deal_in_turn: 0,
    coufan_count: 0,
    menduanping_count: 0,
    fulu_at_win: [0, 0, 0, 0, 0],
    hu_tile_counts: {},
    wait_tile_counts: {},
    main_fan_counts: {},
    wins: [],
    deal_ins: [],
    xun_end_score: emptyXunEndMap(),
    xun_end_count: emptyXunEndMap(),
    deal_draw_tile_count: 0,
    flower_tile_count: 0,
    opening_shanten_total: 0,
    opening_shanten_count: 0,
  }
}

/**
 * 重建一局目标座位的手牌，并收集听牌/和牌/点炮事件。
 * @param {boolean} tingpai 为 false 时跳过听牌判定（按番查谱用）
 */
function walkRound(rd, mySeat, { tingpaiCheck = null, shantenOf = null } = {}) {
  const ticks = Array.isArray(rd?.action_ticks) ? rd.action_ticks : []
  const dealer = ((typeof rd.start_player_index === 'number' ? rd.start_player_index
    : (typeof rd.dealer_index === 'number' ? rd.dealer_index : 0)) % 4 + 4) % 4
  let currentSeat = dealer
  const history = []
  let xunmu = 1
  let dealerDiscarded = false
  let seenReset = false

  const goTo = (seat) => {
    const next = ((Number(seat) % 4) + 4) % 4
    if (history.length && next !== history[history.length - 1]
      && next < history[history.length - 1] && dealerDiscarded) {
      xunmu += 1
    }
    history.push(next)
    currentSeat = next
  }

  const initial = Array.isArray(rd[`p${mySeat}_tiles`]) ? [...rd[`p${mySeat}_tiles`]] : []
  const startHasDrawn = initial.length % 3 === 2
  const state = {
    hand: startHasDrawn ? initial.slice(0, -1) : [...initial],
    drawn: startHasDrawn ? initial.at(-1) ?? null : null,
    combos: [],
    visibleFulu: 0,
    discarded: false,
  }
  let lastCutTile = null
  let lastCutSeat = null
  let myLastDraw = startHasDrawn ? state.drawn : null
  let firstTenpaiXunmu = null
  let firstTenpaiWaits = []
  let liuju = false
  let dealtThisRound = false
  let tsumoAgainst = false
  let endXunmu = null
  let roundScore = 0
  let dealDrawTiles = initial.length
  let flowerTiles = initial.filter(isFlowerTile).length
  let openingShanten = null
  const wins = []
  const dealIns = []

  const snapshotOpeningShanten = () => {
    if (openingShanten != null || !shantenOf) return
    const tiles = closedHandIds([
      ...state.hand,
      ...(state.drawn != null ? [state.drawn] : []),
    ])
    if (tiles.length !== 13 && tiles.length !== 14) return
    try {
      const value = Number(shantenOf(tiles, []))
      if (Number.isFinite(value)) openingShanten = value
    } catch (_) { /* 手牌不合法时跳过该局狗力值 */ }
  }

  const readWaits = () => {
    if (!tingpaiCheck) return []
    if (state.drawn != null) return []
    const hand = closedHandIds(state.hand)
    if (hand.length % 3 !== 1) return []
    try {
      return (tingpaiCheck(hand, [...state.combos]) || [])
        .map(normTile)
        .filter((id) => asTileId(id))
    } catch (_) {
      return []
    }
  }

  const checkTenpai = () => {
    if (firstTenpaiXunmu != null) return
    const waits = readWaits()
    if (waits.length) {
      firstTenpaiXunmu = xunmu
      firstTenpaiWaits = waits
    }
  }

  const applyDraw = (tile, action) => {
    if (state.drawn != null) state.hand.push(state.drawn)
    if (action === 'bd' && !seenReset && mySeat !== dealer) {
      state.hand.push(tile)
      state.drawn = null
    } else {
      state.drawn = tile
    }
    myLastDraw = tile
  }

  for (let node = 0; node < ticks.length; node += 1) {
    const tick = ticks[node]
    if (!Array.isArray(tick) || tick.length === 0) continue
    const code = tick[0]
    if (code === 'end') break
    if (code === 'ask_hand' || code === 'ask_other' || code === 'ca') continue
    if (code === 'reset') {
      const seat = tickInt(tick, 1, currentSeat)
      if (seat != null) goTo(seat)
      seenReset = true
      // 狗力值只在补花轮结束的 reset 取样。线上国标小局已全部回填 reset，不再兼容无 reset 的旧谱。
      snapshotOpeningShanten()
      continue
    }
    if (code === 'liuju' || code === 'ryuukyoku') {
      liuju = true
      if (endXunmu == null) endXunmu = xunmu
      const drawScores = drawScoreChanges(tick)
      if (drawScores && mySeat >= 0 && mySeat < drawScores.length) {
        roundScore += drawScores[mySeat]
      }
      continue
    }

    if (code === 'bh' || code === 'bd') {
      const seat = tickInt(tick, 2, currentSeat)
      if (seat != null) goTo(seat)
    } else if (code === 'd' || code === 'mo') {
      const explicit = tickInt(tick, 2)
      if (explicit != null && explicit >= 0 && explicit <= 3) goTo(explicit)
      else goTo(currentSeat === 3 ? 0 : currentSeat + 1)
    } else if (code === 'gd') {
      const explicit = tickInt(tick, 2)
      if (explicit != null && explicit >= 0 && explicit <= 3) goTo(explicit)
    } else if (CLAIM_CODES.has(code)) {
      const seat = tickInt(tick, 2)
      if (seat != null) goTo(seat)
    }

    let actor = currentSeat
    if (['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g'].includes(code)) {
      actor = tickInt(tick, 2, currentSeat)
    }
    if (HU_ACTIONS.has(code) || code === 'hu_riichi') {
      const hu = parseHuTick(tick)
      actor = hu ? hu.winnerSeat : tickInt(tick, 1, actor)
    }

    if (['d', 'gd', 'bd'].includes(code) && actor === mySeat) {
      const tile = tickInt(tick, 1, 0)
      dealDrawTiles += 1
      if (isFlowerTile(tile)) flowerTiles += 1
      applyDraw(tile, code)
    } else if (code === 'c') {
      const tile = tickInt(tick, 1, 0)
      lastCutTile = tile
      lastCutSeat = actor
      if (actor === dealer) dealerDiscarded = true
      if (actor === mySeat) {
        const fromDraw = tick[2] === true || tick[2] === 'T' || tick[2] === 1
        const drawnMatches = state.drawn != null && normTile(state.drawn) === normTile(tile)
        if (fromDraw && drawnMatches) state.drawn = null
        else {
          const removed = removeExactOrNormalized(state.hand, tile)
          if (removed == null && drawnMatches) state.drawn = null
          else if (state.drawn != null) {
            state.hand.push(state.drawn)
            state.drawn = null
          }
        }
        state.discarded = true
        checkTenpai()
      }
    } else if (code === 'bh' && actor === mySeat) {
      const tile = tickInt(tick, 1, 0)
      const fromDraw = tick.length >= 4 && (tick[3] === true || tick[3] === 'T' || tick[3] === 1)
      const drawnMatches = state.drawn != null && normTile(state.drawn) === normTile(tile)
      if (fromDraw && drawnMatches) state.drawn = null
      else {
        const removed = removeExactOrNormalized(state.hand, tile)
        if (removed == null && drawnMatches) state.drawn = null
      }
    } else if (CLAIM_CODES.has(code) && actor === mySeat) {
      const tile = tickInt(tick, 1, 0)
      takeForClaim(tick, code, tile, state.hand)
      if (state.drawn != null) {
        state.hand.push(state.drawn)
        state.drawn = null
      }
      const combo = comboForClaim(code, tile)
      if (combo) state.combos.push(combo)
      if (code !== 'jg') state.visibleFulu += 1
    } else if (code === 'ag' && actor === mySeat) {
      const tile = tickInt(tick, 1, 0)
      const explicit = tick.slice(3, 7).map((value) => Number(value)).filter((value) => value > 0)
      const removed = explicit.length === 4 ? explicit : Array(4).fill(tile)
      for (const item of removed) {
        if (state.drawn != null && normTile(state.drawn) === normTile(item)) state.drawn = null
        else removeExactOrNormalized(state.hand, item)
      }
      state.combos.push(`G${normTile(tile)}`)
    } else if (code === 'jg' && actor === mySeat) {
      const tile = tickInt(tick, 1, 0)
      if (state.drawn != null && normTile(state.drawn) === normTile(tile)) state.drawn = null
      else removeExactOrNormalized(state.hand, tile)
      const n = normTile(tile)
      const idx = state.combos.indexOf(`k${n}`)
      if (idx >= 0) state.combos[idx] = `g${n}`
      else state.combos.push(`g${n}`)
    }

    const hu = parseHuTick(tick)
    if (hu?.scoreChanges && mySeat >= 0 && mySeat < hu.scoreChanges.length) {
      roundScore += hu.scoreChanges[mySeat]
      if (!isCuohe(hu.yaku) && endXunmu == null) endXunmu = xunmu
    }
    if (!hu || isCuohe(hu.yaku)) continue
    const sc = hu.scoreChanges
    if (!sc || mySeat < 0 || mySeat >= sc.length) continue
    const myDelta = sc[mySeat]
    const classified = classifyWinYaku(hu.yaku)
    const winTile = asTileId(hu.hepaiTile)
      || (hu.huClass === 'hu_self'
        ? asTileId(state.drawn) || asTileId(myLastDraw)
        : asTileId(lastCutTile))

    if (myDelta > 0) {
      if (firstTenpaiXunmu == null) {
        firstTenpaiXunmu = xunmu
        firstTenpaiWaits = winTile ? [winTile] : []
      }
      wins.push({
        winnerSeat: hu.winnerSeat,
        huClass: hu.huClass,
        fanScore: hu.fanScore,
        yaku: hu.yaku || [],
        parsed: classified.parsed,
        mainFans: classified.mainFans,
        isCoufan: classified.isCoufan,
        isMenduanping: classified.isMenduanping,
        score: myDelta,
        xunmu,
        node,
        shareNode: shareNodeBeforeWin(ticks, node),
        visibleFulu: Math.min(4, Math.max(0, state.visibleFulu)),
        winTile,
        waitTiles: firstTenpaiWaits,
        isSelfDraw: hu.huClass === 'hu_self',
      })
    } else if (!dealtThisRound && RON_ACTIONS.has(hu.huClass) && myDelta < 0) {
      const neg = sc.filter((x) => x < 0)
      if (neg.length && myDelta === Math.min(...neg)) {
        dealtThisRound = true
        const discardedByMe = lastCutSeat === mySeat
        dealIns.push({
          fanScore: hu.fanScore,
          score: Math.abs(myDelta),
          xunmu,
          node,
          yaku: hu.yaku || [],
          winTile: asTileId(hu.hepaiTile) || asTileId(lastCutTile),
          discarderWasMe: discardedByMe,
          tenpai: discardedByMe && readWaits().length > 0,
        })
      }
    } else if (hu.huClass === 'hu_self' && myDelta < 0) {
      tsumoAgainst = true
    }
  }

  return {
    firstTenpaiXunmu,
    firstTenpaiWaits,
    liuju: liuju && wins.length === 0,
    visibleFulu: state.visibleFulu,
    tsumoAgainst,
    endXunmu: endXunmu ?? xunmu,
    roundScore,
    dealDrawTiles,
    flowerTiles,
    openingShanten,
    wins,
    dealIns,
  }
}

function formatYakuText(yaku) {
  const list = Array.isArray(yaku) ? [...yaku] : []
  list.sort((a, b) => {
    const pa = parseGuobiaoFanLabel(a)
    const pb = parseGuobiaoFanLabel(b)
    return (pb.value - pa.value) || pa.name.localeCompare(pb.name, 'zh-CN')
  })
  return list.map((raw) => formatGuobiaoFanComposition(raw)).join('、')
}

function attachWinMeta(win, { gameId, round, currentRound, roundIndexKey, createdAt }) {
  let mainFanText = '—'
  if (win.isCoufan) mainFanText = COUFAN_LABEL
  else if (win.isMenduanping) mainFanText = MENDUANPING_LABEL
  else if (win.mainFans?.length) {
    mainFanText = [...win.mainFans]
      .sort((a, b) => (b.value - a.value) || a.name.localeCompare(b.name, 'zh-CN'))
      .map((item) => item.name)
      .join('、')
  }
  return {
    ...win,
    game_id: gameId,
    round,
    current_round: currentRound,
    round_index_key: roundIndexKey,
    created_at: createdAt || null,
    yakuText: formatYakuText(win.yaku),
    mainFanText,
  }
}

function analyzeOne(record, userId, acc, { tingpaiCheck = null, shantenOf = null, gameId = '', createdAt = null } = {}) {
  const originalIndex = findOriginalIndex(record, userId)
  if (originalIndex < 0) return
  const entries = listSortedRoundEntries(record)
  if (!entries.length) return
  acc.total_games += 1
  acc.total_rounds += entries.length
  const gid = gameId || recordGameId(null, record)

  for (let round = 0; round < entries.length; round += 1) {
    const [key, rd] = entries[round]
    const seats = resolveRoundSeats(rd || {})
    const mySeat = seatForOriginal(seats, originalIndex)
    let walked
    try {
      walked = walkRound(rd || {}, mySeat, { tingpaiCheck, shantenOf })
    } catch (_) {
      continue
    }
    if (walked.firstTenpaiXunmu != null) {
      acc.tenpai_round_count += 1
      acc.total_first_tenpai_turn += walked.firstTenpaiXunmu
      for (const tile of walked.firstTenpaiWaits) bump(acc.wait_tile_counts, tile)
    }
    if (walked.liuju) acc.liuju_count += 1
    if (walked.tsumoAgainst) acc.tsumo_against_count += 1
    const bucket = xunEndBucket(walked.endXunmu)
    acc.xun_end_score[bucket] += walked.roundScore || 0
    acc.xun_end_count[bucket] += 1
    acc.deal_draw_tile_count += walked.dealDrawTiles || 0
    acc.flower_tile_count += walked.flowerTiles || 0
    if (walked.openingShanten != null) {
      acc.opening_shanten_total += walked.openingShanten
      acc.opening_shanten_count += 1
    }

    const currentRound = Number(rd?.current_round || rd?.round_index || round + 1)
    for (const win of walked.wins) {
      const row = attachWinMeta(win, {
        gameId: gid,
        round: round + 1,
        currentRound,
        roundIndexKey: key,
        createdAt,
      })
      acc.wins.push(row)
      acc.win_count += 1
      acc.total_win_score += win.score || 0
      acc.total_win_fan += win.fanScore || 0
      if (win.isSelfDraw) acc.self_draw_count += 1
      else acc.ron_win_count += 1
      if (win.visibleFulu === 0) acc.closed_win_count += 1
      const fulu = Math.min(4, Math.max(0, win.visibleFulu || 0))
      acc.fulu_at_win[fulu] += 1
      if (win.winTile) bump(acc.hu_tile_counts, win.winTile)
      if (win.isCoufan) {
        acc.coufan_count += 1
        bump(acc.main_fan_counts, COUFAN_KEY)
      } else if (win.isMenduanping) {
        acc.menduanping_count += 1
        bump(acc.main_fan_counts, MENDUANPING_KEY)
      } else {
        for (const fan of win.mainFans) bump(acc.main_fan_counts, fan.name)
      }
    }
    for (const deal of walked.dealIns) {
      acc.deal_ins.push({ ...deal, game_id: gid, round: round + 1, node: deal.node })
      acc.deal_in_count += 1
      if (deal.tenpai) acc.deal_in_tenpai_count += 1
      acc.total_deal_in_fan += deal.fanScore || 0
      acc.total_deal_in_score += deal.score || 0
      acc.total_deal_in_turn += deal.xunmu || 0
    }
  }
}

function yieldToUi() {
  return new Promise((resolve) => setTimeout(resolve, 0))
}

/**
 * @param {Array} items IndexedDB 行 { game_id, record } 或裸牌谱
 * @param {number} userId
 * @param {{ tingpai?: boolean, onProgress?: function }} [options]
 */
export async function analyzeRecordsAdvanced(items, userId, options = {}) {
  const wantTingpai = options.tingpai !== false
  let tingpaiCheck = options.tingpaiCheck || null
  if (!tingpaiCheck && wantTingpai) {
    const mod = await import('../game2d/calc/guobiao/gbTingpai')
    tingpaiCheck = mod.tingpaiCheck
  }
  let shantenOf = typeof options.shantenOf === 'function' ? options.shantenOf : null
  if (!shantenOf && options.tingpai !== false) {
    try {
      const mod = await import('./pailiCalculator')
      shantenOf = (handTiles, combinations = []) => mod.calculatePailiShanten(handTiles, combinations)
    } catch (_) {
      shantenOf = null
    }
  }
  const acc = emptyAcc()
  const list = items || []
  for (let i = 0; i < list.length; i += 1) {
    const item = list[i]
    const record = item?.record ?? item
    analyzeOne(record, userId, acc, {
      tingpaiCheck,
      shantenOf,
      gameId: recordGameId(item, record),
      createdAt: item?.created_at
        || record?.created_at
        || record?.game_title?.created_at
        || null,
    })
    if (i % 2 === 1 || i === list.length - 1) {
      options.onProgress?.(i + 1, list.length)
      await yieldToUi()
    }
  }
  const basic = analyzeRecords(list, userId)
  return { ...basic, ...acc }
}

export function filterWinsByFan(wins, fanKey) {
  return (wins || []).filter((win) => winMatchesFan(win, fanKey))
}

function timeMs(value) {
  if (!value) return 0
  const t = new Date(value).getTime()
  return Number.isNaN(t) ? 0 : t
}

/** 按对局时间新→旧；同牌谱再按小局、node 降序。 */
export function sortWinsByTimeDesc(wins) {
  return [...(wins || [])].sort((a, b) => {
    const tb = timeMs(b.created_at)
    const ta = timeMs(a.created_at)
    if (tb !== ta) return tb - ta
    return (Number(b.round) || 0) - (Number(a.round) || 0)
      || (Number(b.node) || 0) - (Number(a.node) || 0)
  })
}

export function buildMainFanRows(stats) {
  const counts = stats?.main_fan_counts || {}
  const wins = Number(stats?.win_count) || 0
  const rows = Object.entries(counts).map(([key, count]) => {
    const isCoufan = key === COUFAN_KEY
    const isMenduanping = key === MENDUANPING_KEY
    return {
      key,
      label: isCoufan ? COUFAN_LABEL : isMenduanping ? MENDUANPING_LABEL : key,
      count,
      percent: wins ? (count / wins) * 100 : 0,
      isCoufan,
      isMenduanping,
      value: isCoufan || isMenduanping ? 0 : (GUOBIAO_FAN_VALUE_BY_NAME[key] || 0),
    }
  })
  rows.sort((a, b) => {
    const rank = (row) => (row.isCoufan ? 0 : row.isMenduanping ? 1 : 2)
    const ra = rank(a)
    const rb = rank(b)
    if (ra !== rb) return ra - rb
    return b.count - a.count || a.label.localeCompare(b.label, 'zh-CN')
  })
  return rows
}

export function buildTileFreqRows(countMap, denominator) {
  const den = Number(denominator) || 0
  return TILE_SUIT_ROWS.map((ids) => ids.map((id) => {
    const count = Number(countMap?.[id]) || 0
    return {
      id,
      count,
      percent: den ? (count / den) * 100 : 0,
    }
  }))
}
