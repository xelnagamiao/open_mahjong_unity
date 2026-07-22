const express = require('express');
const router = express.Router();
const pool = require('../config/database');
const { createWindowLimiter, getClientIp } = require('../middleware/rateLimit');
const {
  resolveUserId,
  fetchPlayerRankStats,
  buildRecordFilters,
  buildRecordMeta,
  listPublicEvents,
  fetchPublicEventDetail,
  LIST_PAGE_MAX,
} = require('../services/playerPublicApi');
const {
  handlePlayerInfo,
  handlePlayerRecords,
  handlePlayerRankStats,
} = require('../services/playerQueryHandlers');

// 重查询限流：仅 info / records；成功响应才计数，404 等失败不占配额
const playerQueryLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 30,
  keyFn: (req) => `${getClientIp(req)}:player-query`,
  countSuccessfulOnly: true,
});

// 下载专用限流：每 IP 每日最多 10 次（单局 + 批量合并计数），防止无限拉取牌谱
const downloadLimiter = createWindowLimiter({
  windowMs: 86_400_000,
  max: 10,
  keyFn: (req) => `${getClientIp(req)}:download`,
});

// 列表分页硬上限：防止一次拉取过多数据
const DOWNLOAD_MAX_GAMES = 50;
// 每日分析：生产环境每 IP 每日 3 次批量拉取牌谱 JSON（≤500 局）；开发/调试不限
const ANALYZE_DAILY_MAX = 3;
// 按「自然日 4 点」对齐：4 点后 key 翻页，与每日聚合刷新一致
const ANALYZE_MAX_GAMES = 500;
function _analyzeBucketKey(req) {
  const now = new Date();
  // 4 点前归到前一天的分析周期
  const d = new Date(now);
  if (d.getHours() < 4) d.setDate(d.getDate() - 1);
  const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  return `${getClientIp(req)}:analyze:${ymd}`;
}
const analyzeLimiter = createWindowLimiter({
  windowMs: 86_400_000,
  max: ANALYZE_DAILY_MAX,
  keyFn: _analyzeBucketKey,
});

router.get('/info/:key', playerQueryLimiter, handlePlayerInfo);
router.get('/records/:key', playerQueryLimiter, handlePlayerRecords);

// 历史赛事列表（含已关闭），供比赛场筛选下拉
router.get('/events', async (req, res) => {
  try {
    const items = await listPublicEvents();
    res.json({ success: true, data: { items } });
  } catch (error) {
    console.error('player events list:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/events/:eventId', async (req, res) => {
  try {
    const detail = await fetchPublicEventDetail(req.params.eventId);
    if (!detail) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    res.json({ success: true, data: detail });
  } catch (error) {
    console.error('player events detail:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 单局牌谱下载：直接返回原始 record JSON
router.get('/record/:gameId', downloadLimiter, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    if (!gameId) {
      return res.status(400).json({ success: false, message: '无效的 game_id' });
    }
    const result = await pool.query(
      `SELECT record FROM game_records WHERE game_id = $1`,
      [gameId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '牌谱不存在' });
    }
    const raw = result.rows[0].record;
    const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="${gameId}.json"`);
    res.send(body);
  } catch (error) {
    console.error('单局牌谱下载错误:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 批量牌谱下载：按筛选/指定 game_ids 流式打包 ZIP，每局一个原始 record JSON
router.post('/records/download', downloadLimiter, async (req, res) => {
  try {
    const userId = parseInt(req.body?.user_id);
    if (isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户ID' });
    }
    const query = {
      rule: req.body.rule || null,
      sub_rule: req.body.sub_rule || null,
      room_type: req.body.room_type || null,
      match_tier: req.body.match_tier || null,
      tier: req.body.tier || null,
      game_type: req.body.game_type || null,
      date_from: req.body.date_from || null,
      date_to: req.body.date_to || null,
    };
    const requestedIds = Array.isArray(req.body.game_ids)
      ? req.body.game_ids.map(String).filter(Boolean)
      : [];

    let gameIds = [];
    if (requestedIds.length > 0) {
      // 指定 ID 模式：仅保留属于该用户的，防越权
      const idResult = await pool.query(
        `SELECT DISTINCT game_id FROM game_player_records
         WHERE user_id = $1 AND game_id = ANY($2::varchar[])`,
        [userId, requestedIds]
      );
      gameIds = idResult.rows.map(r => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `单次最多下载 ${DOWNLOAD_MAX_GAMES} 局，当前选中 ${gameIds.length} 局，请减少选择`,
        });
      }
    } else {
      // 筛选模式：取全部命中 game_id（仅 ID，开销低），超出上限提示缩小范围
      const params = [];
      const conditions = buildRecordFilters(userId, query, params);
      const sql = `
        SELECT game_id FROM (
          SELECT DISTINCT gpr.game_id, gr.created_at
          FROM game_player_records gpr
          JOIN game_records gr ON gr.game_id = gpr.game_id
          WHERE ${conditions.join(' AND ')}
        ) sub
        ORDER BY created_at DESC
      `;
      const idResult = await pool.query(sql, params);
      gameIds = idResult.rows.map(r => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `单次最多下载 ${DOWNLOAD_MAX_GAMES} 局，当前筛选命中 ${gameIds.length} 局，请缩小时间范围或场次`,
        });
      }
    }

    if (gameIds.length === 0) {
      return res.status(404).json({ success: false, message: '没有匹配的牌谱' });
    }

    const recordsResult = await pool.query(
      `SELECT game_id, record FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    );
    const byGame = new Map(recordsResult.rows.map(r => [r.game_id, r.record]));

    const archiver = require('archiver');
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', `attachment; filename="player_${userId}_records.zip"`);
    const archive = archiver('zip', { zlib: { level: 5 } });
    archive.on('error', (err) => {
      console.error('zip 打包错误:', err);
      if (!res.headersSent) {
        res.status(500).json({ success: false, message: '打包失败' });
      } else {
        res.end();
      }
    });
    archive.pipe(res);
    for (const gameId of gameIds) {
      const raw = byGame.get(gameId);
      if (raw === undefined || raw === null) continue;
      const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
      archive.append(body, { name: `${gameId}.json` });
    }
    await archive.finalize();
  } catch (error) {
    console.error('批量牌谱下载错误:', error);
    if (!res.headersSent) {
      res.status(500).json({ success: false, message: '服务器内部错误' });
    }
  }
});

// ===== 公开排行榜（前 N 名，快捷查询用） =====
const { RANK_NAME_TO_INDEX, LEADERBOARD_MIN_USER_ID } = require('../utils/rankNames');

router.get('/leaderboard', async (req, res) => {
  try {
    const limit = Math.min(20, Math.max(1, parseInt(req.query.limit, 10) || 10));
    const result = await pool.query(
      `SELECT r.user_id, r.guobiao_rank, r.guobiao_score, u.username
       FROM rank_data r
       JOIN users u ON r.user_id = u.user_id
       WHERE r.user_id > $1 AND r.guobiao_rank != '10级'`,
      [LEADERBOARD_MIN_USER_ID]
    );
    const entries = result.rows.map((row) => ({
      user_id: row.user_id,
      username: row.username || '',
      guobiao_rank: row.guobiao_rank,
      guobiao_score: parseFloat(row.guobiao_score),
      _idx: RANK_NAME_TO_INDEX[row.guobiao_rank] ?? 0,
    }));
    entries.sort((a, b) => b._idx - a._idx || b.guobiao_score - a.guobiao_score || a.user_id - b.user_id);
    const sliced = entries.slice(0, limit).map((e, i) => ({
      rank_position: i + 1,
      user_id: e.user_id,
      username: e.username,
      guobiao_rank: e.guobiao_rank,
      guobiao_score: e.guobiao_score,
    }));
    res.json({ success: true, data: sliced });
  } catch (err) {
    console.error('public leaderboard:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// ===== 热点查询：记录手动输入 + 一周内 Top N =====
async function ensureSearchLogTable() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS player_search_log (
      id BIGSERIAL PRIMARY KEY,
      key TEXT NOT NULL,
      user_id BIGINT,
      username TEXT,
      ip TEXT,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    )
  `);
}

router.post('/search-log', async (req, res) => {
  try {
    const key = String(req.body?.key ?? '').trim();
    if (!key) return res.status(400).json({ success: false, message: '缺少查询关键词' });
    await ensureSearchLogTable();
    const userId = req.body?.user_id ? parseInt(req.body.user_id, 10) : null;
    const username = req.body?.username ? String(req.body.username).slice(0, 64) : null;
    await pool.query(
      `INSERT INTO player_search_log (key, user_id, username, ip) VALUES ($1, $2, $3, $4)`,
      [key.slice(0, 64), Number.isFinite(userId) ? userId : null, username, req.ip || null]
    );
    res.json({ success: true });
  } catch (err) {
    console.error('search-log:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/hot', async (req, res) => {
  try {
    const limit = Math.min(20, Math.max(1, parseInt(req.query.limit, 10) || 10));
    await ensureSearchLogTable();
    // 一周内按 key 计数 Top N，附带最近一次命中的 user_id/username
    const result = await pool.query(
      `SELECT key, COUNT(*) AS cnt,
              (array_agg(user_id ORDER BY created_at DESC))[1] AS user_id,
              (array_agg(username ORDER BY created_at DESC))[1] AS username
       FROM player_search_log
       WHERE created_at >= NOW() - INTERVAL '7 days'
       GROUP BY key
       ORDER BY cnt DESC, key
       LIMIT $1`,
      [limit]
    );
    // 缺 username 时从 users 表补全（历史记录可能只存了 uid）
    const needLookup = [];
    const items = result.rows.map((r) => {
      const userId = r.user_id ? parseInt(r.user_id, 10) : null;
      let username = r.username || null;
      if (!username) {
        const lookupId = userId || (/^\d+$/.test(String(r.key)) ? parseInt(r.key, 10) : null);
        if (lookupId) needLookup.push(lookupId);
      }
      return {
        key: r.key,
        count: parseInt(r.cnt, 10),
        user_id: userId,
        username,
        _lookupId: !username ? (userId || (/^\d+$/.test(String(r.key)) ? parseInt(r.key, 10) : null)) : null,
      };
    });
    if (needLookup.length) {
      const uniq = [...new Set(needLookup)];
      const nameRows = await pool.query(
        'SELECT user_id, username FROM users WHERE user_id = ANY($1::bigint[])',
        [uniq]
      );
      const nameMap = new Map(nameRows.rows.map((row) => [parseInt(row.user_id, 10), row.username]));
      for (const item of items) {
        if (!item.username && item._lookupId) {
          item.username = nameMap.get(item._lookupId) || null;
        }
        delete item._lookupId;
      }
    } else {
      for (const item of items) delete item._lookupId;
    }
    res.json({ success: true, data: items });
  } catch (err) {
    console.error('hot:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// ===== 每日分析：批量拉取牌谱 JSON（≤500 局），客户端本地分析 =====
// 顺位统计：与对局记录列表同源（game_player_records.rank）
router.get('/rank-stats/:key', playerQueryLimiter, handlePlayerRankStats);

// 范围对局数：全部 / 天梯 / 初级 / 中级 / 高级 / mcrpl / 自定义 / 比赛场
router.get('/scope-counts/:key', playerQueryLimiter, async (req, res) => {
  try {
    const userId = await resolveUserId(req.params.key);
    if (userId == null) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }
    const base = {
      rule: req.query.rule || null,
      sub_rule: req.query.sub_rule || null,
      game_type: req.query.game_type || null,
      date_from: req.query.date_from || null,
      date_to: req.query.date_to || null,
    };
    const sceneKeys = ['rank', 'custom', 'beginner', 'intermediate', 'advanced', 'mcrpl', 'events'];
    const tierByScene = {
      rank: 'rank',
      custom: 'custom',
      beginner: 'beginner',
      intermediate: 'intermediate',
      advanced: 'advanced',
      mcrpl: 'mcrpl',
      events: 'events',
    };
    const results = await Promise.all(
      sceneKeys.map((key) => {
        const tier = tierByScene[key];
        return fetchPlayerRankStats(userId, { ...base, tier });
      })
    );
    const data = {};
    sceneKeys.forEach((key, i) => {
      data[key] = results[i].total_games;
    });
    res.json({ success: true, data });
  } catch (error) {
    console.error('scope-counts:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/records/batch-json', analyzeLimiter, async (req, res) => {
  try {
    const userId = parseInt(req.body?.user_id);
    if (isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户ID' });
    }
    const query = {
      rule: req.body.rule || null,
      sub_rule: req.body.sub_rule || null,
      room_type: req.body.room_type || null,
      match_tier: req.body.match_tier || null,
      tier: req.body.tier || null,
      game_type: req.body.game_type || null,
      date_from: req.body.date_from || null,
      date_to: req.body.date_to || null,
    };
    const params = [];
    const conditions = buildRecordFilters(userId, query, params);
    const sql = `
      SELECT sub.game_id, gr.record, gpr.rank
      FROM (
        SELECT DISTINCT gpr.game_id, gr.created_at
        FROM game_player_records gpr
        JOIN game_records gr ON gr.game_id = gpr.game_id
        WHERE ${conditions.join(' AND ')}
      ) sub
      JOIN game_records gr ON gr.game_id = sub.game_id
      JOIN game_player_records gpr ON gpr.game_id = sub.game_id AND gpr.user_id = $1
      ORDER BY sub.created_at DESC
      LIMIT $${params.length + 1}
    `;
    params.push(ANALYZE_MAX_GAMES);
    const recordsResult = await pool.query(sql, params);
    const items = recordsResult.rows.map(r => ({
      game_id: r.game_id,
      record: r.record,
      rank: r.rank != null ? Number(r.rank) : null,
    }));
    res.json({ success: true, data: { items, total: items.length, cap: ANALYZE_MAX_GAMES } });
  } catch (error) {
    console.error('batch-json:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
