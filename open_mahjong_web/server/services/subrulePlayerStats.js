const { BASE_KEYS, DETAIL_KEYS, RIICHI_STATS_VERSION } = require('./riichiStats');
const { statisticsSelection, normalizeMatchType } = require('./statisticsCatalog');

function aggregatePlayerRows(rows, definition) {
  const out = Object.fromEntries(BASE_KEYS.map(key => [key, 0]));
  out.rule = definition.key;
  out.sub_rule = definition.sub_rule;
  out.total_games = rows.length;
  out.analyzed_games = rows.filter(row => row.metrics && (definition.rule !== 'riichi' || row.summary)).length;
  out.details_available = out.analyzed_games === rows.length;
  out.supports_fan_stats = definition.fan_stats;
  if (definition.fan_stats) out.fan_stats = {};
  if (definition.rule === 'riichi') out.riichi_details = Object.fromEntries(DETAIL_KEYS.map(key => [key, 0]));
  let netScore = 0;
  let netScoreAvailable = true;
  for (const row of rows) {
    const data = definition.rule === 'riichi' ? row.summary : row.metrics;
    // Riichi settlement scores include starting points; only its saved net summary is valid.
    // Other rules retain their settlement totals even when behavior metrics are unavailable.
    const score = definition.rule !== 'riichi' && row.has_settlement_score
      ? row.settlement_score : data?.total_round_score;
    if (score == null || !Number.isFinite(Number(score))) netScoreAvailable = false;
    else netScore += Number(score);
    const rankKey = ['first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count'][row.rank - 1];
    if (rankKey && row.rank <= definition.player_count) out[rankKey] += 1;
    if (!data) continue;
    for (const key of BASE_KEYS) {
      if (key !== 'total_games' && !key.endsWith('_place_count')) out[key] += Number(data[key]) || 0;
    }
    for (const group of ['fan_stats', 'riichi_details']) {
      if (!out[group]) continue;
      const source = group === 'fan_stats' ? row.metrics?.fan_stats : data.riichi_details;
      for (const [key, value] of Object.entries(source || {})) out[group][key] = (out[group][key] || 0) + Number(value);
    }
  }
  if (!out.details_available) {
    for (const key of BASE_KEYS) if (!['total_games', 'total_round_score'].includes(key) && !key.endsWith('_place_count')) out[key] = null;
    if (out.fan_stats) out.fan_stats = null;
    if (out.riichi_details) out.riichi_details = null;
  }
  out.total_round_score = netScoreAvailable ? netScore : null;
  return out;
}

async function fetchMetricRows(db, conditions, params) {
  const result = await db.query(`SELECT gpr.match_type AS mode,gpr.room_type,gpr.rank,
      gpm.sub_rule,to_jsonb(gpm) AS metrics,s.stats AS summary,
      to_jsonb(gpr)->'score' AS settlement_score,to_jsonb(gpr) ? 'score' AS has_settlement_score
    FROM game_player_records gpr JOIN game_records gr USING(game_id)
    LEFT JOIN LATERAL (SELECT * FROM game_player_metrics m
      WHERE m.game_id=gpr.game_id AND m.user_id=gpr.user_id ORDER BY m.id DESC LIMIT 1) gpm ON TRUE
    LEFT JOIN riichi_player_game_stats s ON gpr.rule='riichi' AND s.game_id=gpr.game_id
      AND s.user_id=gpr.user_id AND s.version=${RIICHI_STATS_VERSION}
    WHERE ${conditions.join(' AND ')} ORDER BY gpr.match_type,gpr.game_id`, params);
  return result.rows;
}

async function allPlayerRuleStats(db, userId) {
  const rows = await fetchMetricRows(db, ['gpr.user_id=$1', 'gpm.sub_rule IS NOT NULL'], [userId]);
  const groups = new Map();
  for (const row of rows) {
    const def = statisticsSelection(row.sub_rule);
    if (!def) continue;
    row.mode = normalizeMatchType(row.mode, def.sub_rule);
    const key = `${def.key}:${row.room_type}:${row.mode}`;
    if (!groups.has(key)) groups.set(key, { def, rows: [] });
    groups.get(key).rows.push(row);
  }
  const out = {};
  for (const { def, rows: items } of groups.values()) {
    (out[def.key] ||= []).push({ ...aggregatePlayerRows(items, def), mode: items[0].mode, room_type: items[0].room_type });
  }
  return out;
}

module.exports = { aggregatePlayerRows, fetchMetricRows, allPlayerRuleStats };
