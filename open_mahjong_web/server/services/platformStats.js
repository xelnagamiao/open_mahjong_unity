const { withRecordMetadataQuery } = require('./recordMetadataQuery');
const pool = require('../config/database');
const { GUOBIAO_FAN_KEYS } = require('../constants/guobiaoFanDict');
const { queryRiichiHomeStats } = require('./riichiHomeStats');
const { queryRecordHomeStats } = require('./recordHomeStats');

const LADDER_TIERS = ['beginner', 'intermediate', 'advanced', 'mcrpl'];
const EVENT_ALL = 'all';
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

const SCENE_METRIC_KEYS = [
  'total_games', 'total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
  'total_fan_score', 'total_win_turn', 'total_fangchong_score',
  'first_place_count', 'second_place_count', 'third_place_count', 'fourth_place_count',
  'fulu_round_count', 'cuohe_count', 'total_round_score',
];

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
  if (row.event_id != null) out.event_id = row.event_id;
  if (row.room_type != null) out.room_type = row.room_type;
  for (const k of SCENE_METRIC_KEYS) {
    out[k] = Number(row[k]) || 0;
  }
  return out;
}

function isEventScope(eventId) {
  return eventId != null && String(eventId).trim() !== '';
}

function isAllEvents(eventId) {
  return String(eventId || '').trim().toLowerCase() === EVENT_ALL;
}

/**
 * 在按场次汇总的结果末尾追加一行“总计”（match_tier = 'total'）。
 * 所有原始计数字段均可直接累加；比率类指标由前端基于合计重新计算。
 * 仅在未按 rule/game_type 拆分的汇总模式下追加，避免 detail 视图出现歧义。
 */
function appendSceneTotalRow(rows, enabled) {
  if (!enabled || !rows.length || rows.some((r) => r.match_tier === 'total')) return rows;
  const total = { match_tier: 'total' };
  for (const k of SCENE_METRIC_KEYS) {
    total[k] = rows.reduce((sum, r) => sum + (Number(r[k]) || 0), 0);
  }
  return [...rows, total];
}

function buildSceneTotalsQuery(groupByTierOnly, extraWhere, params, { roomType = 'match' } = {}) {
  const innerWhere = [
    roomType === 'events'
      ? "room_type = 'events' AND event_id IS NOT NULL"
      : "room_type = 'match'",
  ];
  if (roomType !== 'events') {
    const tierPlaceholders = LADDER_TIERS.map((_, i) => `$${i + 1}`).join(', ');
    innerWhere.push(`match_tier IN (${tierPlaceholders})`);
  }
  innerWhere.push(...extraWhere);
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
    const orderSql = roomType === 'events'
      ? 'ORDER BY match_tier'
      : "ORDER BY array_position(ARRAY['beginner','intermediate','advanced','mcrpl']::varchar[], match_tier)";
    return {
      sql: `
        SELECT match_tier, MAX(event_id) AS event_id, MAX(room_type) AS room_type, ${SCENE_METRIC_SUMS}
        FROM (${inner}) g
        GROUP BY match_tier
        ${orderSql}
      `,
      params,
    };
  }
  return {
    sql: `
      SELECT match_tier, rule, game_type, MAX(event_id) AS event_id, MAX(room_type) AS room_type, ${SCENE_METRIC_SUMS}
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

async function querySceneTotals({ asOfDate, tier, gameType, rule, detail, eventId } = {}) {
  const extraWhere = [];
  let params;
  let roomType = 'match';
  if (isEventScope(eventId)) {
    roomType = 'events';
    params = [];
    if (!isAllEvents(eventId)) {
      params.push(String(eventId).trim());
      extraWhere.push(`(event_id = $${params.length} OR match_tier = $${params.length})`);
    }
  } else {
    params = [...LADDER_TIERS];
    if (tier && LADDER_TIERS.includes(tier)) {
      params.push(tier);
      extraWhere.push(`match_tier = $${params.length}`);
    }
  }
  if (asOfDate) {
    params.push(asOfDate);
    extraWhere.push(`${STAT_DATE_EXPR} <= $${params.length}::date`);
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
  const { sql, params: queryParams } = buildSceneTotalsQuery(
    groupByTierOnly, extraWhere, params, { roomType },
  );
  const result = await pool.query(sql, queryParams);
  return appendSceneTotalRow(result.rows.map(mapTotalsRow), groupByTierOnly);
}

function fillFanByKeys(rawByTier, keys) {
  const byTier = {};
  for (const key of keys) {
    byTier[key] = {};
    for (const fanKey of GUOBIAO_FAN_KEYS) {
      byTier[key][fanKey] = Number(rawByTier[key]?.[fanKey]) || 0;
    }
  }
  const total = {};
  for (const fanKey of GUOBIAO_FAN_KEYS) {
    total[fanKey] = keys.reduce((sum, key) => sum + (byTier[key][fanKey] || 0), 0);
  }
  byTier.total = total;
  return byTier;
}

function fillFanByTier(rawByTier) {
  return fillFanByKeys(rawByTier, LADDER_TIERS);
}

async function querySceneTotalsFans({ asOfDate, tier, rule, eventId } = {}) {
  const where = [];
  const params = [];
  if (isEventScope(eventId)) {
    if (isAllEvents(eventId)) {
      where.push('match_tier IN (SELECT event_id FROM events)');
    } else {
      params.push(String(eventId).trim());
      where.push(`match_tier = $${params.length}`);
    }
  } else {
    const start = params.length;
    params.push(...LADDER_TIERS);
    where.push(`match_tier IN (${LADDER_TIERS.map((_, i) => `$${start + i + 1}`).join(', ')})`);
    if (tier && LADDER_TIERS.includes(tier)) {
      params.push(tier);
      where.push(`match_tier = $${params.length}`);
    }
  }
  if (asOfDate) {
    params.push(asOfDate);
    where.push(`stat_date <= $${params.length}::date`);
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
  if (isEventScope(eventId)) {
    const keys = isAllEvents(eventId)
      ? Object.keys(rawByTier)
      : [String(eventId).trim()];
    return fillFanByKeys(rawByTier, keys);
  }
  return fillFanByTier(rawByTier);
}

async function querySceneDailyGames({ dateFrom, dateTo, asOfDate, tier, gameType, rule, eventId } = {}) {
  const where = [];
  const params = [];
  if (isEventScope(eventId)) {
    where.push("room_type = 'events'");
    if (!isAllEvents(eventId)) {
      params.push(String(eventId).trim());
      where.push(`(event_id = $${params.length} OR match_tier = $${params.length})`);
    }
  } else {
    where.push("room_type = 'match'");
    params.push(...LADDER_TIERS);
    where.push(`match_tier IN (${LADDER_TIERS.map((_, i) => `$${i + 1}`).join(', ')})`);
    if (tier && LADDER_TIERS.includes(tier)) {
      params.push(tier);
      where.push(`match_tier = $${params.length}`);
    }
  }
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

/**
 * 首页统计按真实牌谱的场次/局制归口，不从混合个人累计倒推桌数。
 * 日麻使用逐玩家摘要，其余规则使用牌谱参与者和逐场指标。
 */
async function queryHomeHierarchyStats() {
  const rows = [
    ...await queryRiichiHomeStats(pool),
    ...await queryRecordHomeStats(pool, ['riichi'], { exclude: true }),
  ];
  return {
    rows,
    meta: {
      hierarchy: ['rule', 'room_type', 'mode', 'match_tier'],
      note: '场次按原始牌谱归口，对局数去重；缺少完整指标时保留可核验的记录计数',
    },
  };
}

/**
 * 平台最近牌谱（天梯或比赛场，与数据站对局记录字段对齐，含同桌玩家）
 * @param {{ matchTier?: string|null, eventId?: string|null, limit?: number, offset?: number }} opts
 */
async function queryRecentLadderRecords({ matchTier = null, eventId = null, limit = 20, offset = 0 } = {}) {
  return withRecordMetadataQuery(pool, async (db) => {
    const lim = Math.min(50, Math.max(1, parseInt(limit, 10) || 20));
    const off = Math.max(0, parseInt(offset, 10) || 0);
    const params = [];
    // A list exposes game summaries; full replay endpoints enforce unlock state.
    const conditions = [];

    if (isEventScope(eventId)) {
      conditions.push(`gpr.room_type = 'events'`);
      if (!isAllEvents(eventId)) {
        params.push(String(eventId).trim());
        conditions.push(`gpr.event_id = $${params.length}`);
      }
    } else {
      conditions.push(`gpr.room_type = 'match'`);
      if (matchTier && LADDER_TIERS.includes(matchTier)) {
        params.push(matchTier);
        conditions.push(`gpr.match_tier = $${params.length}`);
      } else {
        params.push(LADDER_TIERS);
        conditions.push(`gpr.match_tier = ANY($${params.length}::varchar[])`);
      }
    }

    const whereSql = conditions.join(' AND ');
    const limitIdx = params.length + 1;
    const offsetIdx = params.length + 2;
    params.push(lim, off);

    const listRes = await db.query(
      `WITH page AS MATERIALIZED (
         SELECT gr.game_id, gr.created_at
         FROM game_records gr
         WHERE EXISTS (
           SELECT 1 FROM game_player_records gpr
           WHERE gpr.game_id = gr.game_id AND ${whereSql}
         )
         ORDER BY gr.created_at DESC, gr.game_id DESC
         LIMIT $${limitIdx} OFFSET $${offsetIdx}
       )
       SELECT x.*, ev.name AS event_name
       FROM (
         SELECT page.game_id, page.created_at,
                MAX(gpr.rule) AS rule,
                MAX(gpr.sub_rule) AS sub_rule,
                MAX(gpr.match_type) AS match_type,
                MAX(gpr.room_type) AS room_type,
                MAX(gpr.match_tier) AS match_tier,
                MAX(gpr.event_id) AS event_id
         FROM page
         JOIN game_player_records gpr ON gpr.game_id = page.game_id
         WHERE ${whereSql}
         GROUP BY page.game_id, page.created_at
       ) x
       LEFT JOIN events ev ON ev.event_id = x.event_id
       ORDER BY x.created_at DESC, x.game_id DESC`,
      params
    );

    const countParams = params.slice(0, -2);
    const countRes = await db.query(
      `SELECT COUNT(*)::int AS cnt
       FROM game_records gr
       WHERE EXISTS (
         SELECT 1 FROM game_player_records gpr
         WHERE gpr.game_id = gr.game_id AND ${whereSql}
       )`,
      countParams
    );

    const gameIds = listRes.rows.map((r) => r.game_id);
    const playersByGame = new Map();
    if (gameIds.length > 0) {
      const playersRes = await db.query(
        `SELECT game_id, user_id, username, score, rank, pt_change
         FROM game_player_records
         WHERE game_id = ANY($1::varchar[])
         ORDER BY rank`,
        [gameIds]
      );
      for (const row of playersRes.rows) {
        if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
        playersByGame.get(row.game_id).push({
          ...row,
          pt_change: row.pt_change == null ? null : Number(row.pt_change),
        });
      }
    }

    const items = listRes.rows.map((row) => ({
      game_id: row.game_id,
      created_at: row.created_at,
      rule: row.rule,
      sub_rule: row.sub_rule,
      match_type: row.match_type,
      room_type: row.room_type || (isEventScope(eventId) ? 'events' : 'match'),
      match_tier: row.match_tier,
      event_id: row.event_id,
      event_name: row.event_name,
      players: playersByGame.get(row.game_id) || [],
    }));

    return {
      items,
      total: countRes.rows[0]?.cnt || 0,
      limit: lim,
      offset: off,
    };
  });
}

async function listPlatformEvents() {
  const result = await pool.query(
    `SELECT e.event_id, e.name, e.status, e.created_at,
            COUNT(DISTINCT gpr.game_id)::int AS game_count
     FROM events e
     LEFT JOIN game_player_records gpr
       ON gpr.event_id = e.event_id AND gpr.room_type = 'events'
     GROUP BY e.event_id, e.name, e.status, e.created_at
     ORDER BY
       CASE e.status WHEN 'active' THEN 0 WHEN 'registered' THEN 1 ELSE 2 END,
       e.created_at DESC`
  );
  return result.rows;
}

module.exports = {
  LADDER_TIERS,
  EVENT_ALL,
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
  listPlatformEvents,
  fillFanByTier,
  GUOBIAO_FAN_KEYS,
  MODE_TO_GAME_TYPE,
  GAME_TYPE_LABEL,
};
