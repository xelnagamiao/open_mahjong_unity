/** 日麻摘要使用与对局列表相同的筛选条件，绝不混用全历史分子。 */
const RIICHI_STATS_VERSION = 3;
const BASE_KEYS = [
  'total_games', 'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
  'total_fan_score', 'total_win_turn', 'total_fangchong_score', 'fulu_round_count',
  'cuohe_count', 'total_round_score', 'first_place_count', 'second_place_count',
  'third_place_count', 'fourth_place_count',
];
const DETAIL_KEYS = [
  'riichi_round_count', 'total_riichi_turn', 'riichi_win_count', 'riichi_deal_in_count',
  'fulu_win_count', 'damaten_win_count', 'draw_round_count', 'exhaustive_draw_count',
  'tenpai_draw_count', 'tsumo_loss_count', 'total_win_points', 'total_deal_in_points',
  'final_score_total', 'final_score_count', 'busted_count', 'net_score_count',
];

function aggregateRiichiStats(rows) {
  const total = Object.fromEntries(BASE_KEYS.map(key => [key, 0]));
  total.rule = 'riichi';
  total.riichi_details = Object.fromEntries(DETAIL_KEYS.map(key => [key, 0]));
  total.fan_stats = {};
  total.details_available = rows.every(row => row.stats != null);
  total.analyzed_games = rows.filter(row => row.stats != null).length;
  if (!total.details_available) {
    total.riichi_details = null;
    total.fan_stats = null;
    return total;
  }
  for (const { stats } of rows) {
    for (const key of BASE_KEYS) total[key] += Number(stats[key]) || 0;
    for (const group of ['riichi_details', 'fan_stats']) {
      for (const [key, value] of Object.entries(stats[group] || {})) {
        total[group][key] = (total[group][key] || 0) + (Number(value) || 0);
      }
    }
  }
  return total;
}

async function fetchRiichiRows(db, conditions, params) {
  const result = await db.query(`
    SELECT gpr.match_type AS mode, s.stats
    FROM game_player_records gpr JOIN game_records gr ON gr.game_id=gpr.game_id
    LEFT JOIN riichi_player_game_stats s ON s.game_id=gpr.game_id AND s.user_id=gpr.user_id AND s.version=${RIICHI_STATS_VERSION}
    WHERE ${conditions.join(' AND ')}
  `, params);
  return result.rows;
}

module.exports = { aggregateRiichiStats, fetchRiichiRows, RIICHI_STATS_VERSION, BASE_KEYS, DETAIL_KEYS };
