/**
 * /api/player 与 /api/bot/player 共用的 info / records / rank 查询逻辑
 */
const pool = require('../config/database');
const { INFO_FAN_DICT } = require('../constants/playerFanDicts');
const { getScoreBounds, getPromotionProgress } = require('../utils/rankNames');

const LIST_PAGE_MAX = 50;

const ruleConfig = {
  guobiao: { historyTable: 'guobiao_history_stats', fanTable: 'guobiao_fan_stats' },
  riichi: { historyTable: 'riichi_history_stats', fanTable: 'riichi_fan_stats' },
  qingque: { historyTable: 'qingque_history_stats', fanTable: 'qingque_fan_stats' },
  classical: { historyTable: 'classical_history_stats', fanTable: 'classical_fan_stats' },
  changsha: { historyTable: 'changsha_history_stats', fanTable: null },
};

const HISTORY_FIELDS = new Set([
  'user_id', 'rule', 'mode', 'total_games', 'total_rounds', 'win_count',
  'self_draw_count', 'deal_in_count', 'total_fan_score', 'total_win_turn',
  'total_fangchong_score', 'first_place_count', 'second_place_count',
  'third_place_count', 'fourth_place_count', 'fulu_round_count',
  'created_at', 'updated_at',
]);

const GAME_TYPE_MATCH_TYPES = {
  dongfeng: ['1/4', '1/4_rank'],
  banzhuang: ['2/4', '2/4_rank'],
  xifeng: ['3/4'],
  quanzhuang: ['4/4', '4/4_rank'],
};

function extractFanStats(fanRow) {
  if (!fanRow) return null;
  const fanStats = {};
  for (const [key, value] of Object.entries(fanRow)) {
    if (HISTORY_FIELDS.has(key)) continue;
    if (value !== null && value !== 0) fanStats[key] = value;
  }
  return Object.keys(fanStats).length > 0 ? fanStats : null;
}

async function queryRuleStats(userId, rule) {
  const cfg = ruleConfig[rule];
  if (!cfg) return [];

  let historyResult;
  try {
    historyResult = await pool.query(
      `SELECT * FROM ${cfg.historyTable} WHERE user_id = $1 ORDER BY rule, mode`,
      [userId]
    );
  } catch (err) {
    if (err && err.code === '42P01') return [];
    throw err;
  }

  let fanRows = [];
  if (cfg.fanTable) {
    const fanResult = await pool.query(
      `SELECT * FROM ${cfg.fanTable} WHERE user_id = $1`,
      [userId]
    );
    fanRows = fanResult.rows;
  }

  let fanTotal = null;
  if (fanRows.length) {
    const merged = {};
    for (const row of fanRows) {
      const extracted = extractFanStats(row);
      if (!extracted) continue;
      for (const [k, v] of Object.entries(extracted)) {
        merged[k] = (merged[k] || 0) + v;
      }
    }
    if (Object.keys(merged).length > 0) fanTotal = merged;
  }

  return historyResult.rows.map((row, idx) => ({
    rule: row.rule,
    mode: row.mode,
    total_games: row.total_games,
    total_rounds: row.total_rounds,
    win_count: row.win_count,
    self_draw_count: row.self_draw_count,
    deal_in_count: row.deal_in_count,
    total_fan_score: row.total_fan_score,
    total_win_turn: row.total_win_turn,
    total_fangchong_score: row.total_fangchong_score,
    first_place_count: row.first_place_count,
    second_place_count: row.second_place_count,
    third_place_count: row.third_place_count,
    fourth_place_count: row.fourth_place_count,
    fulu_round_count: row.fulu_round_count,
    cuohe_count: row.cuohe_count,
    total_round_score: row.total_round_score,
    fan_stats: idx === 0 ? fanTotal : null,
  }));
}

async function resolveUserId(key) {
  const raw = String(key == null ? '' : key).trim();
  if (!raw) return null;
  if (/^\d+$/.test(raw)) {
    const id = parseInt(raw, 10);
    return Number.isNaN(id) ? null : id;
  }
  const r = await pool.query('SELECT user_id FROM users WHERE username = $1 LIMIT 1', [raw]);
  if (r.rows.length === 0) return null;
  return parseInt(r.rows[0].user_id, 10);
}

function tierToConditions(tier, params) {
  switch (tier) {
    case 'rank':
      params.push('match');
      return [`gpr.room_type = $${params.length}`];
    case 'custom':
      params.push('custom');
      return [`gpr.room_type = $${params.length}`];
    case 'events':
      params.push('events');
      return [`gpr.room_type = $${params.length}`];
    case 'beginner':
    case 'intermediate':
    case 'advanced':
    case 'mcrpl': {
      params.push('match');
      const c1 = `gpr.room_type = $${params.length}`;
      params.push(tier);
      const c2 = `gpr.match_tier = $${params.length}`;
      return [c1, c2];
    }
    default:
      return null;
  }
}

function buildRecordFilters(userId, query, params) {
  const conditions = ['gpr.user_id = $1'];
  params.push(userId);

  if (query.tier) {
    const tierConds = tierToConditions(query.tier, params);
    if (tierConds) conditions.push(...tierConds);
  } else if (query.room_type) {
    conditions.push(`gpr.room_type = $${params.push(query.room_type)}`);
  } else if (query.match_tier) {
    conditions.push(`gpr.match_tier = $${params.push(query.match_tier)}`);
  }

  if (query.event_id) {
    conditions.push(`gpr.event_id = $${params.push(String(query.event_id).trim())}`);
  }

  if (query.rule) conditions.push(`gpr.rule = $${params.push(query.rule)}`);
  if (query.sub_rule) conditions.push(`gpr.sub_rule = $${params.push(query.sub_rule)}`);
  if (query.game_type) {
    const mts = GAME_TYPE_MATCH_TYPES[query.game_type];
    if (mts && mts.length) {
      conditions.push(`gpr.match_type = ANY($${params.push(mts)}::varchar[])`);
    }
  }
  if (query.date_from) conditions.push(`gr.created_at >= $${params.push(query.date_from)}`);
  if (query.date_to) conditions.push(`gr.created_at < $${params.push(query.date_to)}`);
  return conditions;
}

function buildRecordMeta(gameRecord, playersRows) {
  let rule = '';
  let subRule = null;
  let matchType = null;
  let roomType = null;
  let eventId = null;
  try {
    const recordData = typeof gameRecord.record === 'string'
      ? JSON.parse(gameRecord.record)
      : gameRecord.record;
    const title = recordData?.game_title || {};
    rule = title.rule || recordData?.rule || '';
    subRule = title.sub_rule || null;
    matchType = title.match_type || null;
    roomType = title.room_type || null;
    eventId = title.event_id || null;
  } catch (_) {
    // record 解析失败时回退到玩家行字段
  }
  const sampleRow = playersRows.find((r) => r.game_id === gameRecord.game_id);
  if (sampleRow) {
    if (!rule) rule = sampleRow.rule || '';
    if (!subRule) subRule = sampleRow.sub_rule || null;
    if (!matchType) matchType = sampleRow.match_type || null;
    if (!roomType) roomType = sampleRow.room_type || null;
    if (!eventId) eventId = sampleRow.event_id || null;
  }
  return {
    rule,
    sub_rule: subRule,
    match_type: matchType,
    room_type: roomType,
    event_id: eventId,
  };
}

function parseRecordQuery(reqQuery) {
  return {
    rule: reqQuery.rule || null,
    sub_rule: reqQuery.sub_rule || null,
    room_type: reqQuery.room_type || null,
    match_tier: reqQuery.match_tier || null,
    tier: reqQuery.tier || null,
    event_id: reqQuery.event_id || null,
    game_type: reqQuery.game_type || null,
    date_from: reqQuery.date_from || null,
    date_to: reqQuery.date_to || null,
  };
}

function parsePagination(reqQuery) {
  const offset = Math.max(0, parseInt(reqQuery.offset, 10) || 0);
  const limit = Math.min(LIST_PAGE_MAX, Math.max(1, parseInt(reqQuery.limit, 10) || 20));
  return { offset, limit };
}

/** 与 GET /api/player/info/:key 的 data 字段一致 */
async function fetchPlayerInfo(userId) {
  const userSettingsResult = await pool.query(`
    SELECT
      us.user_id,
      us.title_id,
      us.profile_image_id,
      us.character_id,
      us.voice_id,
      u.username
    FROM user_settings us
    INNER JOIN users u ON us.user_id = u.user_id
    WHERE us.user_id = $1
  `, [userId]);

  if (userSettingsResult.rows.length === 0) return null;

  const userSettings = userSettingsResult.rows[0];
  const [guobiaoStats, riichiStats, qingqueStats, classicalStats, changshaStats, rankResult] = await Promise.all([
    queryRuleStats(userId, 'guobiao'),
    queryRuleStats(userId, 'riichi'),
    queryRuleStats(userId, 'qingque'),
    queryRuleStats(userId, 'classical'),
    queryRuleStats(userId, 'changsha'),
    pool.query(
      'SELECT guobiao_rank, guobiao_score, updated_at FROM rank_data WHERE user_id = $1',
      [userId]
    ),
  ]);

  const rankRow = rankResult.rows[0] || {
    guobiao_rank: '10级',
    guobiao_score: 0,
    updated_at: null,
  };
  const guobiao_rank = rankRow.guobiao_rank;
  const guobiao_score = parseFloat(rankRow.guobiao_score) || 0;

  return {
    user_id: userId,
    user_settings: {
      user_id: userSettings.user_id,
      username: userSettings.username,
      title_id: userSettings.title_id,
      profile_image_id: userSettings.profile_image_id,
      character_id: userSettings.character_id,
      voice_id: userSettings.voice_id,
    },
    rank: {
      guobiao_rank,
      guobiao_score,
      updated_at: rankRow.updated_at || null,
      bounds: getScoreBounds(guobiao_rank),
      progress: getPromotionProgress(guobiao_rank, guobiao_score),
    },
    guobiao_stats: guobiaoStats,
    riichi_stats: riichiStats,
    qingque_stats: qingqueStats,
    classical_stats: classicalStats,
    changsha_stats: changshaStats,
    fan_dict: INFO_FAN_DICT,
  };
}

/** 与 GET /api/player/records/:key 的 data 字段一致 */
async function fetchPlayerRecords(userId, query, offset, limit) {
  const pageParams = [];
  const conditions = buildRecordFilters(userId, query, pageParams);
  const pageSql = `
    SELECT game_id, created_at, COUNT(*) OVER() AS total
    FROM (
      SELECT DISTINCT gpr.game_id, gr.created_at
      FROM game_player_records gpr
      JOIN game_records gr ON gr.game_id = gpr.game_id
      WHERE ${conditions.join(' AND ')}
    ) sub
    ORDER BY created_at DESC
    LIMIT $${pageParams.length + 1} OFFSET $${pageParams.length + 2}
  `;
  pageParams.push(limit, offset);
  const pageResult = await pool.query(pageSql, pageParams);

  const total = pageResult.rows.length > 0 ? parseInt(pageResult.rows[0].total, 10) : 0;
  const gameIds = pageResult.rows.map((r) => r.game_id);
  const createdAtByGame = new Map(pageResult.rows.map((r) => [r.game_id, r.created_at]));

  const filterResult = await pool.query(`
    SELECT DISTINCT room_type, match_tier, event_id, rule
    FROM game_player_records
    WHERE user_id = $1
    ORDER BY room_type, match_tier, event_id, rule
  `, [userId]);
  const filters = filterResult.rows.map((r) => ({
    room_type: r.room_type,
    match_tier: r.match_tier,
    event_id: r.event_id,
    rule: r.rule,
  }));

  if (gameIds.length === 0) {
    return { total, items: [], filters };
  }

  const playersResult = await pool.query(
    `SELECT game_id, user_id, username, score, rank, rule, sub_rule, match_type, room_type,
            match_tier, event_id,
            title_used, character_used, profile_used, voice_used
     FROM game_player_records
     WHERE game_id = ANY($1::varchar[])
     ORDER BY game_id, rank`,
    [gameIds]
  );

  const playersByGame = new Map();
  const metaByGame = new Map();
  for (const row of playersResult.rows) {
    if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
    playersByGame.get(row.game_id).push({
      user_id: row.user_id,
      username: row.username,
      score: row.score,
      rank: row.rank,
      title_used: row.title_used,
      character_used: row.character_used,
      profile_used: row.profile_used,
      voice_used: row.voice_used,
    });
    if (!metaByGame.has(row.game_id)) {
      metaByGame.set(row.game_id, {
        rule: row.rule || '',
        sub_rule: row.sub_rule || null,
        match_type: row.match_type || null,
        room_type: row.room_type || null,
        match_tier: row.match_tier || null,
        event_id: row.event_id || null,
      });
    }
  }

  const items = [];
  for (const gameId of gameIds) {
    const meta = metaByGame.get(gameId) || {};
    items.push({
      game_id: gameId,
      created_at: createdAtByGame.get(gameId),
      rule: meta.rule,
      sub_rule: meta.sub_rule,
      match_type: meta.match_type,
      room_type: meta.room_type,
      match_tier: meta.match_tier || null,
      event_id: meta.event_id || null,
      event_name: null,
      players: playersByGame.get(gameId) || [],
    });
  }

  // 批量补全赛事名称（含已关闭历史赛事）
  const eventIds = [...new Set(items.map((it) => it.event_id).filter(Boolean))];
  if (eventIds.length > 0) {
    const nameRes = await pool.query(
      `SELECT event_id, name FROM events WHERE event_id = ANY($1::varchar[])`,
      [eventIds]
    );
    const nameMap = new Map(nameRes.rows.map((r) => [r.event_id, r.name]));
    for (const it of items) {
      if (it.event_id) it.event_name = nameMap.get(it.event_id) || null;
    }
  }

  return { total, items, filters };
}

/** 与 GET /api/player/rank-stats/:key 的 data 字段一致 */
async function fetchPlayerRankStats(userId, query) {
  const params = [];
  const conditions = buildRecordFilters(userId, query, params);
  const sql = `
    SELECT
      COUNT(*)::int AS total_games,
      COUNT(*) FILTER (WHERE gpr.rank = 1)::int AS first_place_count,
      COUNT(*) FILTER (WHERE gpr.rank = 2)::int AS second_place_count,
      COUNT(*) FILTER (WHERE gpr.rank = 3)::int AS third_place_count,
      COUNT(*) FILTER (WHERE gpr.rank = 4)::int AS fourth_place_count
    FROM game_player_records gpr
    JOIN game_records gr ON gr.game_id = gpr.game_id
    WHERE ${conditions.join(' AND ')}
  `;
  const result = await pool.query(sql, params);
  const row = result.rows[0] || {};
  return {
    total_games: Number(row.total_games) || 0,
    first_place_count: Number(row.first_place_count) || 0,
    second_place_count: Number(row.second_place_count) || 0,
    third_place_count: Number(row.third_place_count) || 0,
    fourth_place_count: Number(row.fourth_place_count) || 0,
  };
}

/** Bot API 专用：国标段位查询 */
async function fetchPlayerRank(userId) {
  const userResult = await pool.query(
    'SELECT user_id, username FROM users WHERE user_id = $1 LIMIT 1',
    [userId]
  );
  if (userResult.rows.length === 0) return null;

  const { username } = userResult.rows[0];
  const rankResult = await pool.query(
    'SELECT guobiao_rank, guobiao_score, updated_at FROM rank_data WHERE user_id = $1',
    [userId]
  );

  const rankRow = rankResult.rows[0] || {
    guobiao_rank: '10级',
    guobiao_score: 0,
    updated_at: null,
  };
  const guobiao_rank = rankRow.guobiao_rank;
  const guobiao_score = parseFloat(rankRow.guobiao_score);

  return {
    user_id: userId,
    username,
    guobiao_rank,
    guobiao_score,
    updated_at: rankRow.updated_at || null,
    bounds: getScoreBounds(guobiao_rank),
    progress: getPromotionProgress(guobiao_rank, guobiao_score),
  };
}

/** 公开：全部历史赛事（含已关闭），供数据查询站筛选 */
async function listPublicEvents() {
  const result = await pool.query(
    `SELECT event_id, name, description, status, closed_at, created_at
     FROM events
     ORDER BY
       CASE status WHEN 'active' THEN 0 WHEN 'registered' THEN 1 ELSE 2 END,
       created_at DESC`
  );
  return result.rows;
}

async function fetchPublicEventDetail(eventId) {
  const id = String(eventId || '').trim();
  if (!id) return null;
  const eventRes = await pool.query(
    `SELECT event_id, name, description, status, closed_at, created_at, updated_at
     FROM events WHERE event_id = $1`,
    [id]
  );
  if (eventRes.rows.length === 0) return null;
  const event = eventRes.rows[0];
  const countRes = await pool.query(
    `SELECT COUNT(DISTINCT game_id)::int AS game_count
     FROM game_player_records
     WHERE event_id = $1`,
    [id]
  );
  const ownerRes = await pool.query(
    `SELECT ea.user_id, u.username
     FROM event_admins ea
     LEFT JOIN users u ON u.user_id = ea.user_id
     WHERE ea.event_id = $1 AND ea.role = 'owner'
     LIMIT 1`,
    [id]
  );
  const announcementsRes = await pool.query(
    `SELECT a.announcement_id, a.title, a.body, a.created_by, a.created_at,
            u.username AS author_username
     FROM event_announcements a
     LEFT JOIN users u ON u.user_id = a.created_by
     WHERE a.event_id = $1
     ORDER BY a.created_at DESC
     LIMIT 50`,
    [id]
  );
  return {
    ...event,
    game_count: countRes.rows[0]?.game_count || 0,
    owner_user_id: ownerRes.rows[0]?.user_id || null,
    owner_username: ownerRes.rows[0]?.username || null,
    announcements: announcementsRes.rows,
  };
}

module.exports = {
  LIST_PAGE_MAX,
  resolveUserId,
  queryRuleStats,
  buildRecordFilters,
  buildRecordMeta,
  parseRecordQuery,
  parsePagination,
  fetchPlayerInfo,
  fetchPlayerRecords,
  fetchPlayerRankStats,
  fetchPlayerRank,
  listPublicEvents,
  fetchPublicEventDetail,
};
