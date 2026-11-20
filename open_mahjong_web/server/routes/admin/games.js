const express = require('express');
const { getPublicGameRecord } = require('../../services/publicGameRecord');
const router = express.Router();
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');

router.get('/search', async (req, res) => {
  try {
    const fetchAll = req.query.all === 'true' || req.query.all === '1';
    const gameId = (req.query.game_id || '').trim();
    const userId = req.query.user_id ? parseInt(req.query.user_id, 10) : null;
    const rule = (req.query.rule || '').trim();
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(50, Math.max(1, parseInt(req.query.limit, 10) || 20));
    const offset = (page - 1) * limit;

    const conditions = [];
    const params = [];
    let idx = 1;

    if (!fetchAll) {
      if (gameId) {
        conditions.push(`gr.game_id = COALESCE((SELECT replay_id FROM game_record_tables WHERE game_id=$${idx}), $${idx++})`);
        params.push(gameId);
      }
      if (userId && !Number.isNaN(userId)) {
        conditions.push(`EXISTS (
          SELECT 1 FROM game_player_records gpr JOIN game_record_tables t USING(game_id)
          WHERE t.replay_id = gr.game_id AND gpr.user_id = $${idx++}
        )`);
        params.push(userId);
      }
      if (rule) {
        conditions.push(`EXISTS (
          SELECT 1 FROM game_player_records gpr2 JOIN game_record_tables t USING(game_id)
          WHERE t.replay_id = gr.game_id AND gpr2.rule = $${idx++}
        )`);
        params.push(rule);
      }
      if (conditions.length === 0) {
        return res.status(400).json({
          success: false,
          message: '请至少填写一项筛选条件，或点击「获取全部」',
        });
      }
    }

    const where = conditions.length ? `WHERE ${conditions.join(' AND ')}` : '';
    params.push(limit, offset);

    const listRes = await pool.query(
      `SELECT gr.game_id, gr.created_at
       FROM game_replays gr
       ${where}
       ORDER BY gr.created_at DESC, gr.game_id DESC
       LIMIT $${idx++} OFFSET $${idx}`,
      params
    );

    const gameIds = listRes.rows.map((r) => r.game_id);
    let playersByGame = new Map();
    if (gameIds.length > 0) {
      const playersRes = await pool.query(
        `SELECT t.replay_id AS game_id, t.table_id, user_id, username, score, rank, rule, sub_rule, match_type, room_type
         FROM game_player_records p JOIN game_record_tables t USING(game_id)
         WHERE t.replay_id = ANY($1::varchar[])
         ORDER BY t.table_id, rank`,
        [gameIds]
      );
      for (const row of playersRes.rows) {
        if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
        playersByGame.get(row.game_id).push(row);
      }
    }

    const items = listRes.rows.map((row) => {
      const players = playersByGame.get(row.game_id) || []
      const first = players[0] || {}
      return {
        game_id: row.game_id,
        created_at: row.created_at,
        players,
        room_type: first.room_type || '',
        rule: first.rule || '',
        sub_rule: first.sub_rule || '',
        match_type: first.match_type || '',
      }
    });

    res.json({ success: true, data: { items, page, limit } });
  } catch (err) {
    console.error('admin games search:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/:gameId', async (req, res) => {
  try {
    const gameId = req.params.gameId;
    const includeRecord = req.query.include_record === 'true';
    res.set('Cache-Control', 'no-store');
    if (includeRecord) {
      const result = await getPublicGameRecord(gameId);
      if (result.status !== 200) return res.status(result.status).json({ success: false, message: result.message });
      return res.json({ success: true, data: result.data });
    }
    const gr = await pool.query(
      `SELECT game_id, created_at FROM game_replays
       WHERE game_id = COALESCE((SELECT replay_id FROM game_record_tables WHERE game_id=$1), $1)`, [gameId]
    );
    if (gr.rows.length === 0) {
      return res.status(404).json({ success: false, message: '对局不存在' });
    }

    const playersRes = await pool.query(
      `SELECT t.replay_id AS game_id, t.table_id, user_id, username, score, rank, rule, sub_rule, match_type, room_type,
              title_used, character_used, profile_used, voice_used
       FROM game_player_records p JOIN game_record_tables t USING(game_id)
       WHERE t.replay_id = $1
       ORDER BY t.table_id, rank`,
      [gr.rows[0].game_id]
    );

    const data = {
      ...gr.rows[0],
      players: playersRes.rows,
    };
    if (!includeRecord) {
      data.record_preview = '(省略完整牌谱，如需查看请勾选包含牌谱)';
    }

    res.json({ success: true, data });
  } catch (err) {
    console.error('admin game detail:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.delete('/:gameId', async (req, res) => {
  let db;
  try {
    const gameId = req.params.gameId;
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写删除原因' });
    }

    db = await pool.connect();
    await db.query('BEGIN');
    const before = await db.query(
      `SELECT game_id, created_at FROM game_replays WHERE game_id = COALESCE((SELECT replay_id FROM game_record_tables WHERE game_id=$1), $1) FOR UPDATE`,
      [gameId]
    );
    if (before.rows.length === 0) {
      await db.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '对局不存在' });
    }

    const replayId = before.rows[0].game_id;
    const players = await db.query(
      `SELECT t.table_id, user_id, username, score, rank FROM game_player_records JOIN game_record_tables t USING(game_id) WHERE t.replay_id = $1`,
      [replayId]
    );

    await db.query('DELETE FROM game_player_metrics WHERE game_id IN (SELECT game_id FROM game_record_tables WHERE replay_id=$1)', [replayId]);
    await db.query('DELETE FROM game_replays WHERE game_id=$1', [replayId]);
    await db.query('COMMIT');

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'game.delete',
      targetType: 'game',
      targetId: replayId,
      payload: { before: before.rows[0], players: players.rows },
      reason: String(reason).trim(),
    });

    res.json({ success: true, message: '对局已删除' });
  } catch (err) {
    if (db) await db.query('ROLLBACK');
    console.error('admin delete game:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    db?.release();
  }
});

module.exports = router;
