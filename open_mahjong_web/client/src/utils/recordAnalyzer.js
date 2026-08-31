/**
 * 牌谱客户端分析器：从 record JSON 重建目标玩家的统计指标。
 * 与服务端 backfill_history_stats / round_score_utils 逻辑一致，
 * 用于牌谱分析页对已下载到本地的牌谱做常规统计。
 */

import {
  GUOBIAO_FAN_KEY_BY_NAME,
  GUOBIAO_STACKABLE_FANS,
  parseGuobiaoFanLabel,
} from '../constants/guobiaoFanDict.js'

const HU_ACTIONS = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third']);
const RON_ACTIONS = new Set(['hu_first', 'hu_second', 'hu_third']);
// 明副露 tick 码（不含 ag 暗杠）；cl/cm/cr=吃 p=碰 g=明杠 jg=加杠
const VISIBLE_FULU_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g', 'jg']);
const CLAIM_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g']);

function tickInt(tick, index, fallback = null) {
  if (!Array.isArray(tick) || index >= tick.length) return fallback;
  const value = tick[index];
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string' && /^-?\d+$/.test(value)) return Number(value);
  return fallback;
}

/** seats[original] = 当局 player_index。对齐 next_game_round_guobiao_switchseat。 */
export function inferGuobiaoSeats(currentRound) {
  let seats = [0, 1, 2, 3];
  const round = Number(currentRound);
  if (!Number.isInteger(round) || round <= 1) return seats;
  for (let cr = 2; cr <= round; cr += 1) {
    seats = seats.map((s) => (s === 0 ? 3 : s - 1));
    if (cr === 5) seats = [1, 0, 3, 2];
    else if (cr === 9) seats = [3, 2, 0, 1];
    else if (cr === 13) seats = [2, 3, 1, 0];
  }
  return seats;
}

export function resolveRoundSeats(rd) {
  const raw = rd?.seats;
  if (Array.isArray(raw) && raw.length === 4) {
    const parsed = raw.map((s) => ((Number(s) % 4) + 4) % 4);
    if (parsed.every((s) => Number.isInteger(s)) && new Set(parsed).size === 4) {
      return parsed;
    }
  }
  const currentRound = Number.isInteger(rd?.current_round) ? rd.current_round : rd?.round_index;
  return inferGuobiaoSeats(currentRound);
}

/**
 * 从一局 action_ticks 按 player_index_go_to 语义推理每位 seat 的和巡总和。
 * 指针跨过东家且庄家已切过牌才 +1；reset/bh/bd/鸣牌显式 go_to，d 视为下一家。
 */
function reconstructRoundWinTurns(rd) {
  const ticks = rd?.action_ticks;
  if (!Array.isArray(ticks)) return {};
  const dealer = ((typeof rd.start_player_index === 'number' ? rd.start_player_index
    : (typeof rd.dealer_index === 'number' ? rd.dealer_index : 0)) % 4 + 4) % 4;
  let currentSeat = dealer;
  const history = [];
  let xunmu = 1;
  let dealerDiscarded = false;
  const bySeat = {};

  const goTo = (seat) => {
    const next = ((seat % 4) + 4) % 4;
    if (history.length && next !== history[history.length - 1] && next < history[history.length - 1] && dealerDiscarded) {
      xunmu += 1;
    }
    history.push(next);
    currentSeat = next;
  };

  for (const tick of ticks) {
    if (!Array.isArray(tick) || tick.length === 0) continue;
    const code = tick[0];
    if (code === 'end') break;
    if (code === 'reset') {
      const seat = tickInt(tick, 1, currentSeat);
      if (seat != null) goTo(seat);
      continue;
    }
    if (code === 'bh' || code === 'bd') {
      const seat = tickInt(tick, 2, currentSeat);
      if (seat != null) goTo(seat);
      continue;
    }
    if (code === 'd' || code === 'mo') {
      const explicit = tickInt(tick, 2);
      if (explicit != null && explicit >= 0 && explicit <= 3) goTo(explicit);
      else goTo(currentSeat === 3 ? 0 : currentSeat + 1);
      continue;
    }
    if (code === 'gd') {
      const explicit = tickInt(tick, 2);
      if (explicit != null && explicit >= 0 && explicit <= 3) goTo(explicit);
      continue;
    }
    if (code === 'c') {
      if (currentSeat === dealer) dealerDiscarded = true;
      continue;
    }
    if (CLAIM_CODES.has(code)) {
      const seat = tickInt(tick, 2);
      if (seat != null) goTo(seat);
      continue;
    }
    const hu = parseHuTick(tick);
    if (hu && !isCuohe(hu.yaku)) {
      bySeat[hu.winnerSeat] = (bySeat[hu.winnerSeat] || 0) + xunmu;
    }
  }
  return bySeat;
}

function toScoreChanges(raw) {
  if (!Array.isArray(raw)) return null;
  try {
    return raw.map((x) => Number(x));
  } catch (_) {
    return null;
  }
}

export function seatForOriginal(seats, originalIndex) {
  if (!Array.isArray(seats) || originalIndex < 0 || originalIndex >= seats.length) {
    return originalIndex;
  }
  return Number(seats[originalIndex]);
}

export function findOriginalIndex(record, userId) {
  const title = record?.game_title;
  if (!title) return -1;
  for (let i = 0; i < 4; i++) {
    if (Number(title[`p${i}_uid`]) === Number(userId)) return i;
  }
  return -1;
}

export function isCuohe(huFan) {
  return Array.isArray(huFan) && huFan.some((f) => String(f).includes('错和'));
}

export function asTileId(value) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return null;
  const t = value >= 100 ? value % 100 : Math.trunc(value);
  if ((t >= 11 && t <= 19) || (t >= 21 && t <= 29) || (t >= 31 && t <= 39)
    || (t >= 41 && t <= 47) || (t >= 51 && t <= 58)) {
    return t;
  }
  return null;
}

function extractHepaiTile(tick) {
  if (!Array.isArray(tick) || tick.length < 6) return null;
  // 国标新谱：[hu, seat, score, yaku, changes, hepai_tile]
  // 带副底：[hu, seat, score, yaku, changes, base_fu, fu_fan_list, hepai_tile]
  if (Array.isArray(tick[6])) return asTileId(tick[7]);
  return asTileId(tick[5]);
}

/**
 * 与 2D 回放相同的小局排序：round_index，缺省则取键名末尾数字。
 * 分享链接 round 参数 = 本数组下标 + 1。
 */
export function listSortedRoundEntries(record) {
  const gameRound = record?.game_round;
  if (!gameRound || typeof gameRound !== 'object') return [];
  return Object.entries(gameRound).sort((left, right) => {
    const li = Number(left[1]?.round_index ?? left[0].match(/\d+$/)?.[0]) || 0;
    const ri = Number(right[1]?.round_index ?? right[0].match(/\d+$/)?.[0]) || 0;
    return li - ri;
  });
}

/**
 * 统一解析和牌 tick（国标 hu_* 与日麻 hu_riichi）。
 * 日麻: [hu_riichi, seat, hu_class, han, fu, yaku[], score_changes[], ...]
 * 其他: [hu_class, seat, fanScore, yaku[], score_changes[], ...]
 */
export function parseHuTick(tick) {
  if (!Array.isArray(tick) || tick.length === 0) return null;
  const code = tick[0];
  if (code === 'hu_riichi' && tick.length >= 7) {
    const huClass = tick[2];
    if (typeof huClass !== 'string' || !HU_ACTIONS.has(huClass)) return null;
    const seat = tickInt(tick, 1);
    if (seat == null) return null;
    return {
      huClass,
      winnerSeat: ((seat % 4) + 4) % 4,
      fanScore: Number(tick[3]) || 0,
      yaku: tick[5] || [],
      scoreChanges: toScoreChanges(tick[6]),
      hepaiTile: asTileId(tick[10]) || asTileId(tick[11]) || null,
    };
  }
  if (HU_ACTIONS.has(code) && tick.length >= 5) {
    const seat = tickInt(tick, 1);
    if (seat == null) return null;
    return {
      huClass: code,
      winnerSeat: ((seat % 4) + 4) % 4,
      fanScore: Number(tick[2]) || 0,
      yaku: tick[3] || [],
      scoreChanges: toScoreChanges(tick[4]),
      hepaiTile: extractHepaiTile(tick),
    };
  }
  return null;
}

/**
 * 分析单条牌谱，累加到 acc。
 * @param {object} record 牌谱 JSON
 * @param {number} userId 目标玩家 user_id
 * @param {object} acc 累加器
 */
function analyzeOneRecord(record, userId, acc) {
  const originalIndex = findOriginalIndex(record, userId);
  if (originalIndex < 0) return;

  const gameRound = record?.game_round;
  if (!gameRound || typeof gameRound !== 'object') return;

  const roundKeys = Object.keys(gameRound).filter((k) => k.startsWith('round_index_'));
  acc.total_games += 1;
  acc.total_rounds += roundKeys.length;

  let finalScore = 0;

  for (const key of roundKeys) {
    const rd = gameRound[key] || {};
    const seats = resolveRoundSeats(rd);
    const mySeat = seatForOriginal(seats, originalIndex);
    const ticks = rd.action_ticks || [];
    let hadFulu = false;

    for (const tick of ticks) {
      if (!Array.isArray(tick) || tick.length === 0) continue;
      const code = tick[0];

      if (VISIBLE_FULU_CODES.has(code) && tickInt(tick, 2) === mySeat) {
        hadFulu = true;
      }

      const hu = parseHuTick(tick);
      if (!hu) continue;
      const sc = hu.scoreChanges;
      if (!sc || mySeat < 0 || mySeat >= sc.length) continue;

      const myDelta = sc[mySeat];

      if (isCuohe(hu.yaku)) {
        if (myDelta < 0) acc.cuohe_count += 1;
        finalScore += myDelta;
        continue;
      }

      finalScore += myDelta;

      if (myDelta > 0) {
        if (hu.huClass === 'hu_self') acc.self_draw_count += 1;
        else acc.deal_in_win_count += 1; // 荣和计数（并入 win_count）
        acc.total_fan_score += hu.fanScore;
        addFanStats(acc, hu.yaku);
      } else if (RON_ACTIONS.has(hu.huClass) && myDelta < 0) {
        // 放铳：取本局负分最小者为放铳方
        const neg = sc.filter((x) => x < 0);
        if (neg.length && myDelta === Math.min(...neg)) {
          acc.deal_in_count += 1;
          acc.total_fangchong_score += hu.fanScore;
        }
      }
    }

    if (hadFulu) acc.fulu_round_count += 1;

    // 和巡推理：本局该 seat 的和巡总和
    acc.total_win_turn += (reconstructRoundWinTurns(rd)[mySeat] || 0);
  }

  acc._finalScores.push({ idx: originalIndex, score: finalScore });
}

function resolveMyRank(record, userId, serverRank) {
  const r = Number(serverRank);
  if (r >= 1 && r <= 4) return r;
  const originalIndex = findOriginalIndex(record, userId);
  if (originalIndex < 0) return 0;
  const localFinal = [];
  for (let i = 0; i < 4; i++) localFinal.push({ idx: i, score: computeFinalScore(record, i) });
  localFinal.sort((a, b) => b.score - a.score || a.idx - b.idx);
  return localFinal.findIndex((e) => e.idx === originalIndex) + 1;
}

/**
 * 计算一组牌谱对目标玩家的统计行（与 buildStatsRows 输入结构一致）。
 * @param {Array<object|object>} items 牌谱 JSON，或 { record, rank }（rank 来自服务端）
 */
export function analyzeRecords(items, userId) {
  const acc = {
    total_games: 0,
    total_rounds: 0,
    self_draw_count: 0,
    deal_in_win_count: 0, // 荣和次数
    deal_in_count: 0, // 放铳次数
    total_fan_score: 0,
    total_fangchong_score: 0,
    fulu_round_count: 0,
    cuohe_count: 0,
    total_round_score: 0,
    total_win_turn: 0, // 由 action_ticks 推理 seat 流转重建（reconstructRoundWinTurns）
    first_place_count: 0,
    second_place_count: 0,
    third_place_count: 0,
    fourth_place_count: 0,
    fan_stats: {},
    _finalScores: [],
  };

  for (const item of items) {
    const record = item?.record ?? item;
    const beforeGames = acc.total_games;
    analyzeOneRecord(record, userId, acc);
    if (acc.total_games === beforeGames) continue;

    const originalIndex = findOriginalIndex(record, userId);
    const myRank = resolveMyRank(record, userId, item?.rank);
    if (myRank === 1) acc.first_place_count += 1;
    else if (myRank === 2) acc.second_place_count += 1;
    else if (myRank === 3) acc.third_place_count += 1;
    else if (myRank === 4) acc.fourth_place_count += 1;

    acc.total_round_score += computeFinalScore(record, originalIndex);
  }

  acc.win_count = acc.self_draw_count + acc.deal_in_win_count;
  delete acc._finalScores;
  delete acc.deal_in_win_count;
  return acc;
}

// 单条牌谱某玩家最终分（所有小局 hu tick 净得分之和）
function computeFinalScore(record, originalIndex) {
  if (originalIndex < 0) return 0;
  const gameRound = record?.game_round;
  if (!gameRound || typeof gameRound !== 'object') return 0;
  let total = 0;
  for (const key of Object.keys(gameRound)) {
    if (!key.startsWith('round_index_')) continue;
    const rd = gameRound[key] || {};
    const seats = resolveRoundSeats(rd);
    const mySeat = seatForOriginal(seats, originalIndex);
    for (const tick of rd.action_ticks || []) {
      const hu = parseHuTick(tick);
      if (!hu) continue;
      const sc = hu.scoreChanges;
      if (!sc || mySeat < 0 || mySeat >= sc.length) continue;
      total += sc[mySeat];
    }
  }
  return total;
}

function addFanStats(acc, yaku) {
  if (!Array.isArray(yaku)) return
  for (const raw of yaku) {
    const source = String(raw || '')
    const parsed = parseGuobiaoFanLabel(source)
    if (!parsed.name || parsed.name.includes('错和')) continue
    const key = GUOBIAO_FAN_KEY_BY_NAME[parsed.name]
    if (!key) continue
    const starred = /\*\d+$/.test(source)
    if (starred && !GUOBIAO_STACKABLE_FANS.has(parsed.name)) continue
    const n = starred ? (Number(parsed.count) || 1) : 1
    acc.fan_stats[key] = (acc.fan_stats[key] || 0) + n
  }
}
