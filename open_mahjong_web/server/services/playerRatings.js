const { getScoreBounds, getPromotionProgress } = require('../utils/rankNames');

const RATED_RULES = ['guobiao', 'riichi', 'qingque', 'sichuan'];
const GRADE_RULES = new Set(['guobiao', 'riichi']);

function numberOrNull(value) {
  if (value == null || (typeof value === 'string' && value.trim() === '') || !['number', 'string'].includes(typeof value)) return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

async function queryPlayerRatings(db, userId, nationalRank) {
  let rows;
  try {
    ({ rows } = await db.query(
      'SELECT rule, rank_name, rank_score, elo, games, updated_at FROM rule_ratings WHERE user_id = $1',
      [userId],
    ));
  } catch (error) {
    if (error.code !== '42P01') throw error;
    rows = []; // Compatible with a server whose additive rating migration is not installed yet.
  }
  return Object.fromEntries(RATED_RULES.map((rule) => {
    const row = rows.find((r) => r.rule === rule) || {};
    const graded = GRADE_RULES.has(rule);
    // rank_data remains authoritative for national grades, including administrator adjustments.
    const rankName = rule === 'guobiao' ? nationalRank.guobiao_rank : (row.rank_name || '10级');
    const score = rule === 'guobiao' ? nationalRank.guobiao_score : (numberOrNull(row.rank_score) ?? 0);
    return [rule, {
      rule,
      system: graded ? 'grade' : 'elo',
      rank_name: graded ? rankName : '',
      rank_score: graded ? score : 0,
      elo: numberOrNull(row.elo) ?? 1500,
      games: numberOrNull(row.games) ?? 0,
      updated_at: rule === 'guobiao' ? (nationalRank.updated_at || null) : (row.updated_at || null),
      bounds: graded ? getScoreBounds(rankName) : null,
      progress: graded ? getPromotionProgress(rankName, score) : null,
    }];
  }));
}

const METRIC_FIELDS = [
  'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
  'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
  'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score',
];

async function queryRankedStats(db, userId) {
  const result = Object.fromEntries(RATED_RULES.map(rule => [rule, []]));
  let rows;
  try {
    ({ rows } = await db.query(`
      SELECT rule, match_type AS mode, COUNT(*)::int AS total_games,
        ${METRIC_FIELDS.map(field => `COALESCE(SUM(${field}), 0) AS ${field}`).join(', ')}
      FROM game_player_metrics
      WHERE user_id = $1 AND room_type = 'match' AND rule = ANY($2::text[])
      GROUP BY rule, match_type ORDER BY rule, match_type
    `, [userId, RATED_RULES]));
  } catch (error) {
    if (error.code !== '42P01') throw error;
    rows = [];
  }
  for (const row of rows) {
    if (!result[row.rule]) continue;
    result[row.rule].push({
      rule: row.rule, mode: row.mode, total_games: Number(row.total_games),
      ...Object.fromEntries(METRIC_FIELDS.map(field => [field, numberOrNull(row[field]) ?? 0])),
    });
  }
  return result;
}

async function queryRecordRatings(db, playerRows) {
  const gameIds = [...new Set(playerRows.filter(row => row.room_type === 'match' && RATED_RULES.includes(row.rule)).map(row => row.game_id))];
  if (!gameIds.length) return new Map();
  // Read only the saved settlement fragment for this page, never return replay JSON or wall data.
  // rating_settlements.game_id is a runtime game ID, not the random cloud replay ID.
  const { rows } = await db.query(`
    SELECT game_id, record #> '{game_title,rating_result}' AS rating_result
    FROM game_records WHERE game_id = ANY($1::varchar[])
  `, [gameIds]);
  return new Map(rows.map(row => [row.game_id, row.rating_result]));
}

function recordRatingFields(row, results) {
  const graded = GRADE_RULES.has(row.rule);
  const savedPt = numberOrNull(row.pt_change);
  const payload = results?.[String(row.user_id)];
  const system = graded ? 'grade' : 'elo';
  const rated = row.room_type === 'match' && RATED_RULES.includes(row.rule);
  const valid = rated && payload && typeof payload === 'object' && !Array.isArray(payload)
    && payload.rating_rule === row.rule && payload.rating_system === system;
  const pt = graded && rated ? (savedPt ?? (valid ? numberOrNull(payload.rating_pt) : null)) : null;
  return {
    pt_change: rated && !graded ? null : (savedPt ?? pt),
    rating_rule: valid || pt != null ? row.rule : null,
    rating_system: valid || pt != null ? system : null,
    rating_pt: pt,
    rank_before: valid && graded && typeof payload.rank_before === 'string' ? payload.rank_before : null,
    rank_after: valid && graded && typeof payload.rank_after === 'string' ? payload.rank_after : null,
    score_before: valid && graded ? numberOrNull(payload.score_before) : null,
    score_after: valid && graded ? numberOrNull(payload.score_after) : null,
    elo_before: valid ? numberOrNull(payload.elo_before) : null,
    elo_after: valid ? numberOrNull(payload.elo_after) : null,
    elo_delta: valid ? numberOrNull(payload.elo_delta) : null,
    rating_games: valid ? numberOrNull(payload.rating_games) : null,
  };
}

module.exports = { queryPlayerRatings, queryRankedStats, queryRecordRatings, recordRatingFields };
