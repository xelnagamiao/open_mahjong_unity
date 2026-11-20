const { withRecordMetadataQuery } = require('../../services/recordMetadataQuery');
const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { requirePlayer } = require('../../middleware/requirePlayer');

const MAX_LIMIT = 30;
const MAX_FAVORITES = 50;

function readPage(value, fallback, max) {
  const number = Number.parseInt(value, 10);
  if (!Number.isFinite(number)) return fallback;
  return Math.max(0, Math.min(max, number));
}

function validGameId(value) {
  return /^[0-9A-Za-z]{1,16}$/.test(String(value || '').trim());
}

// 2D lists Guobiao summaries. The public replay endpoint separately enforces
// duplicate unlock state before supplying the complete record.
router.get('/my-records', requirePlayer, async (req, res) => {
  const limit = Math.max(1, readPage(req.query.limit, 12, MAX_LIMIT));
  const offset = readPage(req.query.offset, 0, 10_000);
  const favoritesOnly = ['1', 'true'].includes(String(req.query.favorites_only || '').toLowerCase());
  const clauses = ['gpr.user_id = $1', "gpr.rule = 'guobiao'"];
  if (favoritesOnly) clauses.push('gpr.is_favorite = TRUE');
  const where = clauses.join(' AND ');

  try {
    const data = await withRecordMetadataQuery(pool, async (db) => {
      const countResult = await db.query(
        `SELECT COUNT(*)::int AS total
         FROM game_player_records gpr
         INNER JOIN game_records gr ON gr.game_id = gpr.game_id
         WHERE ${where}`,
        [req.player.userId],
      );
      const metaResult = await db.query(
        `SELECT gpr.game_id, gr.created_at, gpr.rule, gpr.sub_rule, gpr.match_type,
                gpr.room_type, gpr.is_favorite, COALESCE(gpr.note, '') AS note
         FROM game_player_records gpr
         INNER JOIN game_records gr ON gr.game_id = gpr.game_id
         WHERE ${where}
         ORDER BY gr.created_at DESC, gpr.game_id DESC
         LIMIT $2 OFFSET $3`,
        [req.player.userId, limit, offset],
      );
      const gameIds = metaResult.rows.map((row) => row.game_id);
      if (gameIds.length === 0) {
        return { items: [], total: countResult.rows[0]?.total || 0 };
      }
      const playersResult = await db.query(
        `SELECT game_id, user_id, username, score, rank, pt_change, original_player_index,
                title_used, character_used, profile_used, voice_used
         FROM game_player_records
         WHERE game_id = ANY($1::varchar[])
         ORDER BY game_id, rank NULLS LAST, original_player_index NULLS LAST, score DESC`,
        [gameIds],
      );
      const playersByGame = new Map();
      for (const row of playersResult.rows) {
        const list = playersByGame.get(row.game_id) || [];
        list.push({ ...row, pt_change: row.pt_change == null ? null : Number(row.pt_change) });
        playersByGame.set(row.game_id, list);
      }
      const items = metaResult.rows.map((row) => ({ ...row, players: playersByGame.get(row.game_id) || [] }));
      return { items, total: countResult.rows[0]?.total || 0 };
    });
    return res.json({ success: true, data });
  } catch (error) {
    console.error('2d my-records list:', error);
    return res.status(500).json({ success: false, message: '牌谱列表加载失败' });
  }
});

router.post('/my-records/:gameId/favorite', requirePlayer, async (req, res) => {
  const gameId = String(req.params.gameId || '').trim();
  const isFavorite = Boolean(req.body?.is_favorite);
  if (!validGameId(gameId)) return res.status(400).json({ success: false, message: '无效的牌谱 ID' });

  let db;
  try {
    db = await pool.connect();
    await db.query('BEGIN');
    await db.query('SELECT pg_advisory_xact_lock(hashtextextended($1,0))', ['replay-favorite:' + req.player.userId]);
    const { rows } = await db.query(`SELECT t.replay_id, bool_or(p.is_favorite) AS current
      FROM game_player_records p JOIN game_record_tables t USING(game_id)
      WHERE p.user_id=$2 AND p.rule='guobiao' AND t.replay_id=COALESCE(
        (SELECT replay_id FROM game_record_tables WHERE game_id=$1),$1) GROUP BY t.replay_id`,
      [gameId, req.player.userId]);
    if (!rows.length) {
      await db.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '未找到该牌谱记录' });
    }
    if (isFavorite && !rows[0].current) {
      const count = await db.query(`SELECT COUNT(DISTINCT t.replay_id)::int AS total
        FROM game_player_records p JOIN game_record_tables t USING(game_id)
        WHERE p.user_id=$1 AND p.is_favorite`, [req.player.userId]);
      if (count.rows[0].total >= MAX_FAVORITES) {
        await db.query('ROLLBACK');
        return res.status(400).json({ success: false, message: `收藏最多 ${MAX_FAVORITES} 局，请先取消部分收藏` });
      }
    }
    await db.query(`UPDATE game_player_records p SET is_favorite=$1 FROM game_record_tables t
      WHERE p.game_id=t.game_id AND t.replay_id=$2 AND p.user_id=$3 AND p.is_favorite IS DISTINCT FROM $1`,
      [isFavorite, rows[0].replay_id, req.player.userId]);
    await db.query('COMMIT');
    return res.json({ success: true, data: { is_favorite: isFavorite } });
  } catch (error) {
    if (db) await db.query('ROLLBACK');
    console.error('2d my-records favorite:', error);
    return res.status(500).json({ success: false, message: '收藏状态更新失败' });
  } finally {
    db?.release();
  }
});

module.exports = router;
