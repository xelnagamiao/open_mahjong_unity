/**
 * 牌谱客户端分析器：从 record JSON 重建目标玩家的统计指标。
 * 与服务端 backfill_history_stats / round_score_utils 逻辑一致，
 * 用于「每日分析」对未预存（如场次等级 初级/中级/高级/mcrpl）数据的本地计算。
 */

const HU_ACTIONS = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third']);
const RON_ACTIONS = new Set(['hu_first', 'hu_second', 'hu_third']);
// 明副露 tick 码（不含 ag 暗杠）；cl/cm/cr=吃 p=碰 g=明杠 jg=加杠
const VISIBLE_FULU_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g', 'jg']);
const DRAW_CODES = new Set(['d', 'bd', 'gd', 'mo']);
// 鸣牌 tick 码：tick[2] 为鸣牌者 seat
const CLAIM_CODES = new Set(['cl', 'cm', 'cr', 'p', 'g']);

/**
 * 从一局 action_ticks 推理每位 seat 的和巡总和。
 * 国标 xunmu 初值 1，seat 0 每次切牌后 +1；和牌时 win_turn += xunmu（错和不计）。
 * 牌谱未存巡目，按 tick 序列模拟 seat 流转重建。
 */
function reconstructRoundWinTurns(rd) {
  const ticks = rd?.action_ticks;
  if (!Array.isArray(ticks)) return {};
  const start = (typeof rd.start_player_index === 'number') ? rd.start_player_index
    : (typeof rd.dealer_index === 'number' ? rd.dealer_index : 0);
  let currentSeat = ((start % 4) + 4) % 4;
  let xunmu = 1;
  const bySeat = {};
  for (const tick of ticks) {
    if (!Array.isArray(tick) || tick.length === 0) continue;
    const code = tick[0];
    if (DRAW_CODES.has(code)) continue;
    if (code === 'c') {
      if (currentSeat === 0) xunmu += 1;
      currentSeat = (currentSeat + 1) % 4;
    } else if (CLAIM_CODES.has(code)) {
      if (typeof tick[2] === 'number') currentSeat = ((tick[2] % 4) + 4) % 4;
    } else if (code === 'ca') {
      if (typeof tick[1] === 'number') currentSeat = ((tick[1] % 4) + 4) % 4;
    } else if (HU_ACTIONS.has(code)) {
      if (typeof tick[1] === 'number' && tick.length >= 5) {
        const huFan = tick[3] || [];
        const isCuohe = Array.isArray(huFan) && huFan.some((f) => String(f).includes('错和'));
        if (!isCuohe) {
          const seat = ((tick[1] % 4) + 4) % 4;
          bySeat[seat] = (bySeat[seat] || 0) + xunmu;
        }
      }
    } else if (code === 'end') {
      break;
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

function seatForOriginal(seats, originalIndex) {
  if (!Array.isArray(seats) || originalIndex < 0 || originalIndex >= seats.length) {
    return originalIndex;
  }
  return Number(seats[originalIndex]);
}

function findOriginalIndex(record, userId) {
  const title = record?.game_title;
  if (!title) return -1;
  for (let i = 0; i < 4; i++) {
    if (Number(title[`p${i}_uid`]) === Number(userId)) return i;
  }
  return -1;
}

function isCuohe(huFan) {
  return Array.isArray(huFan) && huFan.some((f) => String(f).includes('错和'));
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
    const seats = rd.seats || [0, 1, 2, 3];
    const mySeat = seatForOriginal(seats, originalIndex);
    const ticks = rd.action_ticks || [];
    let hadFulu = false;

    for (const tick of ticks) {
      if (!Array.isArray(tick) || tick.length === 0) continue;
      const code = tick[0];

      if (VISIBLE_FULU_CODES.has(code) && tick.length >= 3 && tick[2] === mySeat) {
        hadFulu = true;
      }

      if (!HU_ACTIONS.has(code) || tick.length < 5) continue;
      const sc = toScoreChanges(tick[4]);
      if (!sc || mySeat < 0 || mySeat >= sc.length) continue;

      const huFan = tick[3] || [];
      const huScore = Number(tick[2]) || 0;
      const myDelta = sc[mySeat];

      if (isCuohe(huFan)) {
        if (myDelta < 0) acc.cuohe_count += 1;
        finalScore += myDelta;
        continue;
      }

      finalScore += myDelta;

      if (myDelta > 0) {
        if (code === 'hu_self') acc.self_draw_count += 1;
        else acc.deal_in_win_count += 1; // 荣和计数（并入 win_count）
        acc.total_fan_score += huScore;
      } else if (RON_ACTIONS.has(code) && myDelta < 0) {
        // 放铳：取本局负分最小者为放铳方
        const neg = sc.filter((x) => x < 0);
        if (neg.length && myDelta === Math.min(...neg)) {
          acc.deal_in_count += 1;
          acc.total_fangchong_score += huScore;
        }
      }
    }

    if (hadFulu) acc.fulu_round_count += 1;

    // 和巡推理：本局该 seat 的和巡总和
    acc.total_win_turn += (reconstructRoundWinTurns(rd)[mySeat] || 0);
  }

  acc._finalScores.push({ idx: originalIndex, score: finalScore });
}

/**
 * 计算一组牌谱对目标玩家的统计行（与 buildStatsRows 输入结构一致）。
 */
export function analyzeRecords(records, userId) {
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
    _finalScores: [],
  };

  // 每局最终排名：按 finalScore 降序排在该局 4 人中的名次
  for (const record of records) {
    const beforeGames = acc.total_games;
    analyzeOneRecord(record, userId, acc);
    if (acc.total_games === beforeGames) continue;

    // 该局 4 人最终分（从同一条 record 重新取，确定名次）
    const scores = [];
    const title = record?.game_title;
    for (let i = 0; i < 4; i++) {
      scores.push({ idx: i, uid: Number(title?.[`p${i}_uid`]) });
    }
    // 用 acc 里刚推入的 finalScore；按记录内每位玩家重算 finalScore
    const localFinal = [];
    for (let i = 0; i < 4; i++) localFinal.push({ idx: i, score: computeFinalScore(record, i) });
    localFinal.sort((a, b) => b.score - a.score || a.idx - b.idx);
    const myRank = localFinal.findIndex((e) => e.idx === findOriginalIndex(record, userId)) + 1;
    if (myRank === 1) acc.first_place_count += 1;
    else if (myRank === 2) acc.second_place_count += 1;
    else if (myRank === 3) acc.third_place_count += 1;
    else if (myRank === 4) acc.fourth_place_count += 1;

    acc.total_round_score += computeFinalScore(record, findOriginalIndex(record, userId));
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
    const seats = rd.seats || [0, 1, 2, 3];
    const mySeat = seatForOriginal(seats, originalIndex);
    for (const tick of rd.action_ticks || []) {
      if (!Array.isArray(tick) || !HU_ACTIONS.has(tick[0]) || tick.length < 5) continue;
      const sc = toScoreChanges(tick[4]);
      if (!sc || mySeat < 0 || mySeat >= sc.length) continue;
      total += sc[mySeat];
    }
  }
  return total;
}
