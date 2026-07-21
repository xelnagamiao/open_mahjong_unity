/**
 * 赛事玩家顺位统计：字段与数据站 GET /api/player/rank-stats 一致
 *（total_games + 1～4 位次数，来自 game_player_records）
 */
const pool = require('../config/database');

const GAME_TYPE_MATCH_TYPES = {
  dongfeng: ['1/4', '1/4_rank'],
  banzhuang: ['2/4', '2/4_rank'],
  xifeng: ['3/4'],
  quanzhuang: ['4/4', '4/4_rank'],
};

const EMPTY_RANK = {
  total_games: 0,
  first_place_count: 0,
  second_place_count: 0,
  third_place_count: 0,
  fourth_place_count: 0,
};

function pushParam(params, value) {
  params.push(value);
  return `$${params.length}`;
}

/** 构建赛事维度筛选（固定 room_type=events + event_id） */
function buildEventStatFilters(eventId, query, params) {
  const conditions = [
    `gpr.event_id = ${pushParam(params, String(eventId).trim())}`,
    `gpr.room_type = ${pushParam(params, 'events')}`,
  ];

  if (query.rule) {
    conditions.push(`gpr.rule = ${pushParam(params, query.rule)}`);
  }
  if (query.sub_rule) {
    conditions.push(`gpr.sub_rule = ${pushParam(params, query.sub_rule)}`);
  }
  if (query.game_type) {
    const mts = GAME_TYPE_MATCH_TYPES[query.game_type];
    if (mts && mts.length) {
      conditions.push(`gpr.match_type = ANY(${pushParam(params, mts)}::varchar[])`);
    }
  }
  if (query.date_from) {
    conditions.push(`gr.created_at >= ${pushParam(params, query.date_from)}`);
  }
  if (query.date_to) {
    conditions.push(`gr.created_at < ${pushParam(params, query.date_to)}`);
  }
  return conditions;
}

function mapRankRow(row) {
  return {
    total_games: Number(row.total_games) || 0,
    first_place_count: Number(row.first_place_count) || 0,
    second_place_count: Number(row.second_place_count) || 0,
    third_place_count: Number(row.third_place_count) || 0,
    fourth_place_count: Number(row.fourth_place_count) || 0,
  };
}

/**
 * 赛事总计 + 各玩家顺位统计（与数据站 rank-stats 口径一致）
 * @param {string} eventId
 * @param {{ rule?: string, sub_rule?: string, game_type?: string, date_from?: string, date_to?: string, q?: string }} query
 */
async function fetchEventPlayerStats(eventId, query = {}) {
  const params = [];
  const conditions = buildEventStatFilters(eventId, query, params);
  const whereSql = conditions.join(' AND ');

  // 可选玩家关键字：用户 ID 精确 或 用户名模糊
  const q = String(query.q || '').trim();
  let playerFilterSql = '';
  if (q) {
    if (/^\d+$/.test(q)) {
      playerFilterSql = ` AND gpr.user_id = ${pushParam(params, parseInt(q, 10))}`;
    } else {
      playerFilterSql = ` AND gpr.username ILIKE ${pushParam(params, `%${q}%`)}`;
    }
  }

  const [totalsRes, playersRes, filtersRes] = await Promise.all([
    pool.query(
      `SELECT
         COUNT(DISTINCT gpr.game_id)::int AS total_games,
         COUNT(DISTINCT gpr.user_id)::int AS player_count,
         COUNT(*) FILTER (WHERE gpr.rank = 1)::int AS first_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 2)::int AS second_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 3)::int AS third_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 4)::int AS fourth_place_count
       FROM game_player_records gpr
       JOIN game_records gr ON gr.game_id = gpr.game_id
       WHERE ${whereSql}${playerFilterSql}`,
      params
    ),
    pool.query(
      `SELECT
         gpr.user_id,
         MAX(gpr.username) AS username,
         COUNT(*)::int AS total_games,
         COUNT(*) FILTER (WHERE gpr.rank = 1)::int AS first_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 2)::int AS second_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 3)::int AS third_place_count,
         COUNT(*) FILTER (WHERE gpr.rank = 4)::int AS fourth_place_count,
         COALESCE(SUM(gpr.score), 0)::bigint AS total_score
       FROM game_player_records gpr
       JOIN game_records gr ON gr.game_id = gpr.game_id
       WHERE ${whereSql}${playerFilterSql}
       GROUP BY gpr.user_id
       ORDER BY first_place_count DESC, total_games DESC, gpr.user_id ASC`,
      params
    ),
    // 筛选项：不带 rule/game_type/q，仅当前赛事有数据的维度
    (async () => {
      const filterParams = [];
      const filterConds = buildEventStatFilters(eventId, {}, filterParams);
      return pool.query(
        `SELECT
           ARRAY_AGG(DISTINCT gpr.rule ORDER BY gpr.rule)
             FILTER (WHERE gpr.rule IS NOT NULL AND gpr.rule <> '') AS rules,
           ARRAY_AGG(DISTINCT gpr.match_type ORDER BY gpr.match_type)
             FILTER (WHERE gpr.match_type IS NOT NULL AND gpr.match_type <> '') AS match_types
         FROM game_player_records gpr
         JOIN game_records gr ON gr.game_id = gpr.game_id
         WHERE ${filterConds.join(' AND ')}`,
        filterParams
      );
    })(),
  ]);

  const totalsRow = totalsRes.rows[0] || {};
  const totals = {
    ...mapRankRow(totalsRow),
    player_count: Number(totalsRow.player_count) || 0,
  };

  const players = playersRes.rows.map((row) => ({
    user_id: Number(row.user_id),
    username: row.username || String(row.user_id),
    ...mapRankRow(row),
    total_score: Number(row.total_score) || 0,
  }));

  const filterRow = filtersRes.rows[0] || {};
  return {
    totals,
    players,
    filters: {
      rules: filterRow.rules || [],
      match_types: filterRow.match_types || [],
    },
  };
}

module.exports = {
  GAME_TYPE_MATCH_TYPES,
  fetchEventPlayerStats,
  EMPTY_RANK,
};
