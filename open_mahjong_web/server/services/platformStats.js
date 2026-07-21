const pool = require('../config/database');
const { GUOBIAO_FAN_KEYS } = require('../constants/guobiaoFanDict');

const LADDER_TIERS = ['beginner', 'intermediate', 'advanced', 'mcrpl'];
const STAT_DATE_EXPR = "((created_at AT TIME ZONE 'Asia/Shanghai') - interval '4 hours')::date";
const CURRENT_STAT_DATE_EXPR = "((NOW() AT TIME ZONE 'Asia/Shanghai') - interval '4 hours')::date";

const SCENE_METRIC_SUMS = `
  COUNT(*)::int AS total_games,
  SUM(per_game_rounds)::int AS total_rounds,
  SUM(win_count)::int AS win_count,
  SUM(self_draw_count)::int AS self_draw_count,
  SUM(deal_in_count)::int AS deal_in_count,
  SUM(total_fan_score)::int AS total_fan_score,
  SUM(total_win_turn)::int AS total_win_turn,
  SUM(total_fangchong_score)::int AS total_fangchong_score,
  SUM(first_place_count)::int AS first_place_count,
  SUM(second_place_count)::int AS second_place_count,
  SUM(third_place_count)::int AS third_place_count,
  SUM(fourth_place_count)::int AS fourth_place_count,
  SUM(fulu_round_count)::int AS fulu_round_count,
  SUM(cuohe_count)::int AS cuohe_count,
  SUM(total_round_score)::int AS total_round_score
`;

function formatStatDate(val) {
  if (val == null) return null;
  if (typeof val === 'string') return val.slice(0, 10);
  if (val instanceof Date) {
    const y = val.getFullYear();
    const m = String(val.getMonth() + 1).padStart(2, '0');
    const d = String(val.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
  return String(val).slice(0, 10);
}

function mapTotalsRow(row) {
  const out = { match_tier: row.match_tier };
  if (row.rule != null) out.rule = row.rule;
  if (row.game_type != null) out.game_type = row.game_type;
  for (const k of [
    'total_games', 'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
    'total_fan_score', 'total_win_turn', 'total_fangchong_score',
    'first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count',
    'fulu_round_count', 'cuohe_count', 'total_round_score',
  ]) {
    out[k] = Number(row[k]) || 0;
  }
  return out;
}

function buildSceneTotalsQuery(groupByTierOnly, extraWhere, params) {
  const tierPlaceholders = LADDER_TIERS.map((_, i) => `$${i + 1}`).join(', ');
  const innerWhere = [
    "room_type = 'match'",
    `match_tier IN (${tierPlaceholders})`,
    ...extraWhere,
  ];
  const inner = `
    SELECT game_id, room_type, match_tier, event_id, rule, game_type,
           MAX(total_rounds) AS per_game_rounds,
           SUM(win_count) AS win_count,
           SUM(self_draw_count) AS self_draw_count,
           SUM(deal_in_count) AS deal_in_count,
           SUM(total_fan_score) AS total_fan_score,
           SUM(total_win_turn) AS total_win_turn,
           SUM(total_fangchong_score) AS total_fangchong_score,
           SUM(first_place_count) AS first_place_count,
           SUM(second_place_count) AS second_place_count,
           SUM(third_place_count) AS third_place_count,
           SUM(fourth_place_count) AS fourth_place_count,
           SUM(fulu_round_count) AS fulu_round_count,
           SUM(cuohe_count) AS cuohe_count,
           SUM(total_round_score) AS total_round_score
    FROM game_player_metrics
    WHERE ${innerWhere.join(' AND ')}
    GROUP BY game_id, room_type, match_tier, event_id, rule, game_type
  `;
  if (groupByTierOnly) {
    return {
      sql: `
        SELECT match_tier, ${SCENE_METRIC_SUMS}
        FROM (${inner}) g
        GROUP BY match_tier
        ORDER BY array_position(ARRAY['beginner','intermediate','advanced','mcrpl']::varchar[], match_tier)
      `,
      params,
    };
  }
  return {
    sql: `
      SELECT match_tier, rule, game_type, ${SCENE_METRIC_SUMS}
      FROM (${inner}) g
      GROUP BY match_tier, rule, game_type
      ORDER BY match_tier, rule, game_type
    `,
    params,
  };
}

async function getAsOfStatDate() {
  const result = await pool.query('SELECT MAX(stat_date) AS as_of FROM daily_stats');
  return formatStatDate(result.rows[0]?.as_of);
}

async function querySceneTotals({ asOfDate, tier, gameType, rule, detail } = {}) {
  const extraWhere = [];
  const params = [...LADDER_TIERS];
  if (asOfDate) {
    params.push(asOfDate);
    extraWhere.push(`${STAT_DATE_EXPR} <= $${params.length}::date`);
  }
  if (tier && LADDER_TIERS.includes(tier)) {
    params.push(tier);
    extraWhere.push(`match_tier = $${params.length}`);
  }
  if (gameType) {
    params.push(gameType);
    extraWhere.push(`game_type = $${params.length}`);
  }
  if (rule) {
    params.push(rule);
    extraWhere.push(`rule = $${params.length}`);
  }
  const groupByTierOnly = detail !== '1';
  const { sql, params: queryParams } = buildSceneTotalsQuery(groupByTierOnly, extraWhere, params);
  const result = await pool.query(sql, queryParams);
  return result.rows.map(mapTotalsRow);
}

function fillFanByTier(rawByTier) {
  const byTier = {};
  for (const tier of LADDER_TIERS) {
    byTier[tier] = {};
    for (const key of GUOBIAO_FAN_KEYS) {
      byTier[tier][key] = Number(rawByTier[tier]?.[key]) || 0;
    }
  }
  return byTier;
}

async function querySceneTotalsFans({ asOfDate, tier, rule } = {}) {
  const where = [`match_tier IN (${LADDER_TIERS.map((_, i) => `$${i + 1}`).join(', ')})`];
  const params = [...LADDER_TIERS];
  if (asOfDate) {
    params.push(asOfDate);
    where.push(`stat_date <= $${params.length}::date`);
  }
  if (tier && LADDER_TIERS.includes(tier)) {
    params.push(tier);
    where.push(`match_tier = $${params.length}`);
  }
  if (rule) {
    params.push(rule);
    where.push(`rule = $${params.length}`);
  } else {
    where.push("rule = 'guobiao'");
  }
  const result = await pool.query(
    `SELECT match_tier, fan_field, SUM(fan_count)::int AS fan_count
     FROM scene_tier_fan_daily
     WHERE ${where.join(' AND ')}
     GROUP BY match_tier, fan_field
     ORDER BY match_tier, fan_count DESC`,
    params
  );
  const rawByTier = {};
  for (const row of result.rows) {
    if (!rawByTier[row.match_tier]) rawByTier[row.match_tier] = {};
    rawByTier[row.match_tier][row.fan_field] = Number(row.fan_count) || 0;
  }
  return fillFanByTier(rawByTier);
}

async function querySceneDailyGames({ dateFrom, dateTo, asOfDate, tier, gameType, rule } = {}) {
  const where = [
    "room_type = 'match'",
    `match_tier IN (${LADDER_TIERS.map((_, i) => `$${i + 1}`).join(', ')})`,
  ];
  const params = [...LADDER_TIERS];
  if (dateFrom) {
    params.push(dateFrom);
    where.push(`stat_date >= $${params.length}::date`);
  }
  if (dateTo) {
    params.push(dateTo);
    where.push(`stat_date <= $${params.length}::date`);
  } else if (asOfDate) {
    params.push(asOfDate);
    where.push(`stat_date <= $${params.length}::date`);
  }
  if (tier && LADDER_TIERS.includes(tier)) {
    params.push(tier);
    where.push(`match_tier = $${params.length}`);
  }
  if (gameType) {
    params.push(gameType);
    where.push(`game_type = $${params.length}`);
  }
  if (rule) {
    params.push(rule);
    where.push(`rule = $${params.length}`);
  }
  const result = await pool.query(
    `SELECT stat_date, match_tier, SUM(total_games)::int AS total_games
     FROM scene_daily_stats
     WHERE ${where.join(' AND ')}
     GROUP BY stat_date, match_tier
     ORDER BY stat_date ASC, match_tier`,
    params
  );
  return result.rows.map((row) => ({
    stat_date: formatStatDate(row.stat_date),
    match_tier: row.match_tier,
    total_games: Number(row.total_games) || 0,
  }));
}

const HISTORY_RULE_TABLES = [
  { rule: 'guobiao', table: 'guobiao_history_stats' },
  { rule: 'riichi', table: 'riichi_history_stats' },
  { rule: 'qingque', table: 'qingque_history_stats' },
  { rule: 'classical', table: 'classical_history_stats' },
  { rule: 'changsha', table: 'changsha_history_stats' },
];

const MODE_TO_GAME_TYPE = {
  '1/4': 'dongfeng',
  '1/4_rank': 'dongfeng',
  '2/4': 'banzhuang',
  '2/4_rank': 'banzhuang',
  '3/4': 'xifeng',
  '4/4': 'quanzhuang',
  '4/4_rank': 'quanzhuang',
};

const GAME_TYPE_LABEL = {
  dongfeng: '东风战',
  banzhuang: '半庄战',
  xifeng: '西入',
  quanzhuang: '全庄战',
};

function emptyMetricRow() {
  return {
    total_games: 0,
    total_rounds: 0,
    win_count: 0,
    self_draw_count: 0,
    deal_in_count: 0,
    total_fan_score: 0,
    total_win_turn: 0,
    total_fangchong_score: 0,
    first_place_count: 0,
    second_place_count: 0,
    third_place_count: 0,
    fourth_place_count: 0,
    fulu_round_count: 0,
    cuohe_count: 0,
    total_round_score: 0,
  };
}

function historyRoomType(rule, mode) {
  // 国标：mode 带 _rank 的是匹配场局制桶；其他规则目前只有自定义
  if (rule === 'guobiao') return String(mode || '').endsWith('_rank') ? 'match' : 'custom';
  return 'custom';
}

/**
 * 首页层级统计：规则 → 匹配/自定义 → 局制(mode) → 等级场(match_tier，仅匹配)
 * - 自定义 / 无等级场：来自各规则 history_stats
 * - 匹配 + 等级场：来自 game_player_metrics（天梯四档）
 */
async function queryHomeHierarchyStats() {
  const rows = [];

  for (const { rule, table } of HISTORY_RULE_TABLES) {
    let result;
    try {
      result = await pool.query(`
      SELECT mode,
             SUM(total_games)::int AS total_games,
             SUM(total_rounds)::int AS total_rounds,
             SUM(win_count)::int AS win_count,
             SUM(self_draw_count)::int AS self_draw_count,
             SUM(deal_in_count)::int AS deal_in_count,
             SUM(total_fan_score)::int AS total_fan_score,
             SUM(total_win_turn)::int AS total_win_turn,
             SUM(total_fangchong_score)::int AS total_fangchong_score,
             SUM(first_place_count)::int AS first_place_count,
             SUM(second_place_count)::int AS second_place_count,
             SUM(third_place_count)::int AS third_place_count,
             SUM(fourth_place_count)::int AS fourth_place_count,
             SUM(fulu_round_count)::int AS fulu_round_count
      FROM ${table}
      GROUP BY mode
      ORDER BY mode
    `);
    } catch (err) {
      if (err && err.code === '42P01') continue; // 表尚未创建（如新规则）
      throw err;
    }
    for (const row of result.rows) {
      const mode = row.mode;
      const roomType = historyRoomType(rule, mode);
      const totals = mapTotalsRow({ ...row, match_tier: null, cuohe_count: 0, total_round_score: 0 });
      // history 按玩家累加，平台视角对局/回合需 ÷4，与数据站 metrics 按局聚合一致
      totals.total_games = Math.round((Number(totals.total_games) || 0) / 4);
      totals.total_rounds = Math.round((Number(totals.total_rounds) || 0) / 4);
      rows.push({
        rule,
        room_type: roomType,
        mode,
        game_type: MODE_TO_GAME_TYPE[mode] || null,
        game_type_label: GAME_TYPE_LABEL[MODE_TO_GAME_TYPE[mode]] || mode,
        match_tier: null,
        source: 'history',
        ...emptyMetricRow(),
        ...totals,
      });
    }
  }

  // 川麻 / 尚无 history 的长沙 / 各规则比赛场：用牌谱对局数（按 game_id 去重，避免×4）
  const recordsOnly = await pool.query(`
    SELECT rule, room_type, match_type AS mode, COUNT(DISTINCT game_id)::int AS total_games
    FROM game_player_records
    WHERE rule IN ('sichuan', 'changsha')
       OR room_type = 'events'
    GROUP BY rule, room_type, match_type
    ORDER BY rule, room_type, match_type
  `);
  for (const row of recordsOnly.rows) {
    const mode = row.mode || '4/4';
    if (row.rule === 'changsha' && row.room_type !== 'events') {
      const hasHistory = rows.some(
        (r) => r.rule === 'changsha' && r.mode === mode && r.source === 'history'
      );
      if (hasHistory) continue;
    }
    if (row.rule === 'sichuan' && row.room_type !== 'custom' && row.room_type !== 'events') continue;
    rows.push({
      rule: row.rule,
      room_type: row.room_type,
      mode,
      game_type: MODE_TO_GAME_TYPE[mode] || null,
      game_type_label: GAME_TYPE_LABEL[MODE_TO_GAME_TYPE[mode]] || mode,
      match_tier: null,
      source: 'records',
      ...emptyMetricRow(),
      total_games: Number(row.total_games) || 0,
    });
  }

  const metrics = await pool.query(`
    SELECT rule, room_type, match_type AS mode, match_tier, game_type,
           ${SCENE_METRIC_SUMS.replace(/\n/g, '\n           ')}
    FROM (
      SELECT game_id, room_type, match_tier, rule, match_type, game_type,
             MAX(total_rounds) AS per_game_rounds,
             SUM(win_count) AS win_count,
             SUM(self_draw_count) AS self_draw_count,
             SUM(deal_in_count) AS deal_in_count,
             SUM(total_fan_score) AS total_fan_score,
             SUM(total_win_turn) AS total_win_turn,
             SUM(total_fangchong_score) AS total_fangchong_score,
             SUM(first_place_count) AS first_place_count,
             SUM(second_place_count) AS second_place_count,
             SUM(third_place_count) AS third_place_count,
             SUM(fourth_place_count) AS fourth_place_count,
             SUM(fulu_round_count) AS fulu_round_count,
             SUM(cuohe_count) AS cuohe_count,
             SUM(total_round_score) AS total_round_score
      FROM game_player_metrics
      WHERE room_type = 'match'
        AND match_tier IN ('beginner', 'intermediate', 'advanced', 'mcrpl')
      GROUP BY game_id, room_type, match_tier, rule, match_type, game_type
    ) g
    GROUP BY rule, room_type, match_type, match_tier, game_type
    ORDER BY rule, match_type, match_tier
  `);
  for (const row of metrics.rows) {
    const mode = row.mode;
    const gameType = row.game_type || MODE_TO_GAME_TYPE[mode] || null;
    rows.push({
      rule: row.rule,
      room_type: 'match',
      mode,
      game_type: gameType,
      game_type_label: GAME_TYPE_LABEL[gameType] || mode,
      match_tier: row.match_tier,
      source: 'metrics',
      ...mapTotalsRow(row),
    });
  }

  return {
    rows,
    meta: {
      hierarchy: ['rule', 'room_type', 'mode', 'match_tier'],
      note: '规则 → 全部天梯/初级/中级/高级/mcrpl/自定义/比赛场 → 局制；metrics 按局聚合与数据站一致',
    },
  };
}

/**
 * 平台最近天梯牌谱（与数据站对局记录字段对齐，含同桌玩家）
 * @param {{ matchTier?: string|null, limit?: number, offset?: number }} opts
 */
async function queryRecentLadderRecords({ matchTier = null, limit = 20, offset = 0 } = {}) {
  const lim = Math.min(50, Math.max(1, parseInt(limit, 10) || 20));
  const off = Math.max(0, parseInt(offset, 10) || 0);
  const params = [];
  const conditions = [`gpr.room_type = 'match'`];

  if (matchTier && LADDER_TIERS.includes(matchTier)) {
    params.push(matchTier);
    conditions.push(`gpr.match_tier = $${params.length}`);
  } else {
    params.push(LADDER_TIERS);
    conditions.push(`gpr.match_tier = ANY($${params.length}::varchar[])`);
  }

  const whereSql = conditions.join(' AND ');
  const limitIdx = params.length + 1;
  const offsetIdx = params.length + 2;
  params.push(lim, off);

  const listRes = await pool.query(
    `SELECT gr.game_id, gr.created_at,
            MAX(gpr.rule) AS rule,
            MAX(gpr.sub_rule) AS sub_rule,
            MAX(gpr.match_type) AS match_type,
            MAX(gpr.room_type) AS room_type,
            MAX(gpr.match_tier) AS match_tier
     FROM game_records gr
     JOIN game_player_records gpr ON gpr.game_id = gr.game_id
     WHERE ${whereSql}
     GROUP BY gr.game_id, gr.created_at
     ORDER BY gr.created_at DESC
     LIMIT $${limitIdx} OFFSET $${offsetIdx}`,
    params
  );

  const countParams = params.slice(0, -2);
  const countRes = await pool.query(
    `SELECT COUNT(DISTINCT gpr.game_id)::int AS cnt
     FROM game_player_records gpr
     WHERE ${whereSql}`,
    countParams
  );

  const gameIds = listRes.rows.map((r) => r.game_id);
  const playersByGame = new Map();
  if (gameIds.length > 0) {
    const playersRes = await pool.query(
      `SELECT game_id, user_id, username, score, rank
       FROM game_player_records
       WHERE game_id = ANY($1::varchar[])
       ORDER BY rank`,
      [gameIds]
    );
    for (const row of playersRes.rows) {
      if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
      playersByGame.get(row.game_id).push(row);
    }
  }

  const items = listRes.rows.map((row) => ({
    game_id: row.game_id,
    created_at: row.created_at,
    rule: row.rule,
    sub_rule: row.sub_rule,
    match_type: row.match_type,
    room_type: row.room_type || 'match',
    match_tier: row.match_tier,
    players: playersByGame.get(row.game_id) || [],
  }));

  return {
    items,
    total: countRes.rows[0]?.cnt || 0,
    limit: lim,
    offset: off,
  };
}

module.exports = {
  LADDER_TIERS,
  STAT_DATE_EXPR,
  CURRENT_STAT_DATE_EXPR,
  formatStatDate,
  mapTotalsRow,
  buildSceneTotalsQuery,
  getAsOfStatDate,
  querySceneTotals,
  querySceneTotalsFans,
  querySceneDailyGames,
  queryHomeHierarchyStats,
  queryRecentLadderRecords,
  fillFanByTier,
  GUOBIAO_FAN_KEYS,
  MODE_TO_GAME_TYPE,
  GAME_TYPE_LABEL,
};
