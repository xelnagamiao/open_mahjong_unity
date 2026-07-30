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

// The 2D record browser deliberately only exposes Guobiao games: every row can
// be opened by the public /2d/record/:gameId viewer without a rules mismatch.
router.get('/my-records', requirePlayer, async (req, res) => {
  const limit = Math.max(1, readPage(req.query.limit, 12, MAX_LIMIT));
  const offset = readPage(req.query.offset, 0, 10_000);
  const favoritesOnly = ['1', 'true'].includes(String(req.query.favorites_only || '').toLowerCase());
  const clauses = ['gpr.user_id = $1', "gpr.rule = 'guobiao'"];
  if (favoritesOnly) clauses.push('gpr.is_favorite = TRUE');
  const where = clauses.join(' AND ');

  try {
    const countResult = await pool.query(
      `SELECT COUNT(*)::int AS total
       FROM game_player_records gpr
       INNER JOIN game_records gr ON gr.game_id = gpr.game_id
       WHERE ${where}`,
      [req.player.userId],
    );
    const metaResult = await pool.query(
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
      return res.json({ success: true, data: { items: [], total: countResult.rows[0]?.total || 0 } });
    }
    const playersResult = await pool.query(
      `SELECT game_id, user_id, username, score, rank, original_player_index,
              title_used, character_used, profile_used, voice_used
       FROM game_player_records
       WHERE game_id = ANY($1::varchar[])
       ORDER BY game_id, rank NULLS LAST, original_player_index NULLS LAST, score DESC`,
      [gameIds],
    );
    const playersByGame = new Map();
    for (const row of playersResult.rows) {
      const list = playersByGame.get(row.game_id) || [];
      list.push(row);
      playersByGame.set(row.game_id, list);
    }
    const items = metaResult.rows.map((row) => ({ ...row, players: playersByGame.get(row.game_id) || [] }));
    return res.json({ success: true, data: { items, total: countResult.rows[0]?.total || 0 } });
  } catch (error) {
    console.error('2d my-records list:', error);
    return res.status(500).json({ success: false, message: '牌谱列表加载失败' });
  }
});

router.post('/my-records/:gameId/favorite', requirePlayer, async (req, res) => {
  const gameId = String(req.params.gameId || '').trim();
  const isFavorite = Boolean(req.body?.is_favorite);
  if (!validGameId(gameId)) return res.status(400).json({ success: false, message: '无效的牌谱 ID' });

  try {
    const owned = await pool.query(
      `SELECT is_favorite FROM game_player_records
       WHERE game_id = $1 AND user_id = $2 AND rule = 'guobiao'`,
      [gameId, req.player.userId],
    );
    if (owned.rows.length === 0) return res.status(404).json({ success: false, message: '未找到该牌谱记录' });
    const current = Boolean(owned.rows[0].is_favorite);
    if (current === isFavorite) return res.json({ success: true, data: { is_favorite: current } });
    if (isFavorite) {
      const count = await pool.query(
        'SELECT COUNT(*)::int AS total FROM game_player_records WHERE user_id = $1 AND is_favorite = TRUE',
        [req.player.userId],
      );
      if ((count.rows[0]?.total || 0) >= MAX_FAVORITES) {
        return res.status(400).json({ success: false, message: `收藏最多 ${MAX_FAVORITES} 局，请先取消部分收藏` });
      }
    }
    await pool.query(
      'UPDATE game_player_records SET is_favorite = $1 WHERE game_id = $2 AND user_id = $3',
      [isFavorite, gameId, req.player.userId],
    );
    return res.json({ success: true, data: { is_favorite: isFavorite } });
  } catch (error) {
    console.error('2d my-records favorite:', error);
    return res.status(500).json({ success: false, message: '收藏状态更新失败' });
  }
});

module.exports = router;
