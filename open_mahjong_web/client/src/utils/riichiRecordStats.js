import { RIICHI_FAN_NAMES } from '../constants/riichiFanDict.js';

const CALLS = new Set(['cl', 'cm', 'cr', 'p', 'g']);
const HU = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third']);
const YAKU_KEYS = Object.fromEntries(Object.entries(RIICHI_FAN_NAMES).map(([key, name]) => [name, key]));
const DETAIL_KEYS = ['riichi_round_count', 'total_riichi_turn', 'riichi_win_count', 'riichi_deal_in_count',
  'fulu_win_count', 'damaten_win_count', 'draw_round_count', 'exhaustive_draw_count', 'tenpai_draw_count',
  'tsumo_loss_count', 'total_win_points', 'total_deal_in_points', 'final_score_total', 'final_score_count', 'busted_count', 'net_score_count'];
const BASE_KEYS = ['total_games', 'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
  'total_fan_score', 'total_win_turn', 'total_fangchong_score', 'fulu_round_count', 'cuohe_count',
  'total_round_score', 'first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count'];
const playerCount = (title) => title.sub_rule === 'riichi/sanma' ? 3 : 4;

/** 本地牌谱分析与 Python 日麻摘要使用同一统计口径。 */
export function analyzeRiichiRecord(record, originalIndex, finalScore = null, rank = null) {
  const title = record?.game_title || {};
  const count = playerCount(title);
  if (title.rule !== 'riichi' || !record.game_round || originalIndex < 0 || originalIndex >= count) return null;
  const out = Object.fromEntries(BASE_KEYS.map(key => [key, 0]));
  out.rule = 'riichi';
  out.riichi_details = Object.fromEntries(DETAIL_KEYS.map(key => [key, 0]));
  out.fan_stats = {};
  const d = out.riichi_details;
  out.total_games = 1;
  for (const [key, rd] of Object.entries(record.game_round)) {
    const ticks = rd?.action_ticks;
    if (!key.startsWith('round_index_') || !Array.isArray(ticks) || !ticks.some(t => Array.isArray(t) && t[0] === 'end')) continue;
    const seats = rd.seats || Array.from({ length: count }, (_, i) => i);
    if (seats.length !== count || seats.some(s => !Number.isInteger(s) || s < 0 || s >= count) || new Set(seats).size !== count) continue;
    const seat = seats[originalIndex];
    let current = rd.start_player_index ?? rd.dealer_index ?? 0;
    let opening = true, won = false, dealt = false, drawn = false, tsumoLost = false, net = 0;
    const cuts = Array(count).fill(0), declarations = new Set(), called = new Set();
    out.total_rounds += 1;
    for (const tick of ticks) {
      if (!Array.isArray(tick) || !tick.length) continue;
      const code = tick[0];
      if (code === 'end') break;
      if (code === 'reset' && tick.length > 1) current = Number(tick[1]) % count;
      else if (code === 'd' || code === 'mo') {
        if (Number.isInteger(tick[2])) current = tick[2] % count;
        else if (!opening) current = (current + 1) % count;
        opening = false;
      } else if (code === 'c') { cuts[current] += 1; opening = false; }
      else if (CALLS.has(code) && tick.length > 2) { current = Number(tick[2]) % count; called.add(current); opening = false; }
      else if (code === 'jg') called.add(current);
      else if (code === 'nuki' && tick.length > 1) { current = Number(tick[1]) % count; opening = false; }
      else if (code === 'riichi' && tick.length > 1) {
        const declarer = Number(tick[1]) % count;
        if (!declarations.has(declarer)) {
          declarations.add(declarer);
          if (declarer === seat) { d.riichi_round_count += 1; d.total_riichi_turn += Math.max(1, cuts[seat]); net -= 1000; }
        }
      } else if (code === 'ryuukyoku' && tick.length >= 3) {
        if (Array.isArray(tick[2]) && tick[2].length === count) net += Number(tick[2][seat]);
        if (!drawn) {
          d.draw_round_count += 1;
          if (tick.length < 4 || tick[3] === 'exhaustive') {
            d.exhaustive_draw_count += 1;
            if (Array.isArray(tick[1]) && tick[1].length === count && tick[1][seat]) d.tenpai_draw_count += 1;
          }
          drawn = true;
        }
      } else if (code === 'hu_riichi' && tick.length >= 7 && HU.has(tick[2])) {
        const winner = tick[1], hu = tick[2], han = tick[3], yaku = tick[5], sc = tick[6];
        if (!Array.isArray(sc) || sc.length !== count) continue;
        net += Number(sc[seat]);
        if (yaku.some(y => String(y).includes('错和'))) {
          if (winner === seat) out.cuohe_count += 1;
          if (declarations.has(seat)) net += 1000;
          declarations.clear();
          continue;
        }
        if (winner === seat && !won) {
          won = true; out.win_count += 1; out.self_draw_count += Number(hu === 'hu_self');
          out.total_fan_score += han; out.total_win_turn += cuts[seat] + 1;
          d.total_win_points += Math.max(0, sc[seat]);
          if (declarations.has(seat)) d.riichi_win_count += 1;
          else if (!called.has(seat)) d.damaten_win_count += 1;
          if (called.has(seat)) d.fulu_win_count += 1;
          for (const label of yaku) {
            const [name, count] = String(label).split('*');
            const field = YAKU_KEYS[name.trim()];
            if (field && (count === undefined || ['宝牌', '里宝牌', '赤宝牌'].includes(name.trim()))) {
              out.fan_stats[field] = (out.fan_stats[field] || 0) + (count === undefined ? 1 : Number(count) || 0);
            }
          }
        } else if (hu !== 'hu_self' && current === seat) {
          dealt = true; out.total_fangchong_score += han; d.total_deal_in_points += Math.max(0, -sc[seat]);
        } else if (hu === 'hu_self' && sc[seat] < 0) tsumoLost = true;
      }
    }
    out.fulu_round_count += Number(called.has(seat)); out.deal_in_count += Number(dealt);
    d.riichi_deal_in_count += Number(dealt && declarations.has(seat));
    d.tsumo_loss_count += Number(tsumoLost); out.total_round_score += net;
  }
  const starts = title.starting_scores;
  const start = Array.isArray(starts) && starts.length === count ? starts[originalIndex] : title.starting_score;
  if (finalScore != null) {
    d.final_score_total = finalScore; d.final_score_count = 1; d.busted_count = Number(finalScore < 0);
    if (start != null) { out.total_round_score = finalScore - start; d.net_score_count = 1; }
  }
  if (Number.isInteger(rank) && rank >= 1 && rank <= count) out[['first', 'second', 'third', 'fourth'][rank - 1] + '_place_count'] = 1;
  return out;
}

export function analyzeRiichiRecords(items, userId) {
  let total = null;
  for (const item of items) {
    const record = item?.record ?? item;
    const title = record?.game_title || {};
    const indices = Array.from({ length: playerCount(title) }, (_, i) => i);
    const original = indices.find(i => Number(title[`p${i}_uid`]) === Number(userId));
    if (original == null) continue;
    const summaries = indices.map(i => analyzeRiichiRecord(record, i));
    if (summaries.some(s => !s)) continue;
    const scores = summaries.map((s, i) => {
      const saved = title.riichi_points?.[String(i)];
      const start = title.starting_scores?.[i] ?? title.starting_score;
      return saved ?? (start == null ? null : Number(start) + s.total_round_score);
    });
    const ranked = indices.sort((a, b) => (scores[b] ?? summaries[b].total_round_score)
      - (scores[a] ?? summaries[a].total_round_score) || a - b);
    const stats = analyzeRiichiRecord(record, original, scores[original], ranked.indexOf(original) + 1);
    if (!total) total = { ...stats, ...Object.fromEntries(BASE_KEYS.map(k => [k, 0])), riichi_details: {}, fan_stats: {} };
    for (const key of BASE_KEYS) total[key] += stats[key];
    for (const group of ['riichi_details', 'fan_stats']) {
      for (const [key, value] of Object.entries(stats[group])) total[group][key] = (total[group][key] || 0) + value;
    }
  }
  return total;
}
