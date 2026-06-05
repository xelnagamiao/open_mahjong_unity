const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');
const {
  RANK_NAME_TO_INDEX,
  LEADERBOARD_MIN_USER_ID,
  LEADERBOARD_LIMIT_DEFAULT,
  isValidRankName,
} = require('../../utils/rankNames');

router.get('/leaderboard', async (req, res) => {
  try {
    const limit = Math.min(
      200,
      Math.max(1, parseInt(req.query.limit, 10) || LEADERBOARD_LIMIT_DEFAULT)
    );

    const result = await pool.query(
      `SELECT r.user_id, r.guobiao_rank, r.guobiao_score,
              u.username, COALESCE(us.profile_image_id, 1) AS profile_image_id
       FROM rank_data r
       JOIN users u ON r.user_id = u.user_id
       LEFT JOIN user_settings us ON us.user_id = r.user_id
       WHERE r.user_id > $1 AND r.guobiao_rank != '10级'`,
      [LEADERBOARD_MIN_USER_ID]
    );

    const entries = result.rows.map((row) => ({
      user_id: row.user_id,
      guobiao_rank: row.guobiao_rank,
      guobiao_score: parseFloat(row.guobiao_score),
      username: row.username || '',
      profile_image_id: row.profile_image_id,
      _rank_index: RANK_NAME_TO_INDEX[row.guobiao_rank] ?? 0,
    }));

    entries.sort(
      (a, b) =>
        b._rank_index - a._rank_index ||
        b.guobiao_score - a.guobiao_score ||
        a.user_id - b.user_id
    );

    const sliced = entries.slice(0, limit).map((e, i) => ({
      rank_position: i + 1,
      user_id: e.user_id,
      username: e.username,
      profile_image_id: e.profile_image_id,
      guobiao_rank: e.guobiao_rank,
      guobiao_score: e.guobiao_score,
    }));

    res.json({ success: true, data: sliced });
  } catch (err) {
    console.error('admin leaderboard:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const result = await pool.query(
      `SELECT guobiao_rank, guobiao_score, updated_at FROM rank_data WHERE user_id = $1`,
      [userId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '无段位数据' });
    }
    const row = result.rows[0];
    res.json({
      success: true,
      data: {
        guobiao_rank: row.guobiao_rank,
        guobiao_score: parseFloat(row.guobiao_score),
        updated_at: row.updated_at,
      },
    });
  } catch (err) {
    console.error('admin get rank:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.put('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { guobiao_rank, guobiao_score, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }
    if (!isValidRankName(guobiao_rank)) {
      return res.status(400).json({ success: false, message: '无效的段位名称' });
    }
    const score = parseFloat(guobiao_score);
    if (Number.isNaN(score)) {
      return res.status(400).json({ success: false, message: '无效的分数' });
    }

    const beforeRes = await pool.query(
      `SELECT guobiao_rank, guobiao_score FROM rank_data WHERE user_id = $1`,
      [userId]
    );

    await pool.query(
      `INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score, updated_at)
       VALUES ($1, $2, $3, CURRENT_TIMESTAMP)
       ON CONFLICT (user_id) DO UPDATE SET
         guobiao_rank = EXCLUDED.guobiao_rank,
         guobiao_score = EXCLUDED.guobiao_score,
         updated_at = CURRENT_TIMESTAMP`,
      [userId, guobiao_rank, score]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'rank.update',
      targetType: 'user',
      targetId: userId,
      payload: {
        before: beforeRes.rows[0] || null,
        after: { guobiao_rank, guobiao_score: score },
      },
      reason: String(reason).trim(),
    });

    res.json({
      success: true,
      data: { guobiao_rank, guobiao_score: score },
    });
  } catch (err) {
    console.error('admin update rank:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:userId/reset', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }

    const beforeRes = await pool.query(
      `SELECT guobiao_rank, guobiao_score FROM rank_data WHERE user_id = $1`,
      [userId]
    );

    await pool.query(
      `INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score, updated_at)
       VALUES ($1, '10级', 0, CURRENT_TIMESTAMP)
       ON CONFLICT (user_id) DO UPDATE SET
         guobiao_rank = '10级',
         guobiao_score = 0,
         updated_at = CURRENT_TIMESTAMP`,
      [userId]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'rank.reset',
      targetType: 'user',
      targetId: userId,
      payload: { before: beforeRes.rows[0] || null, after: { guobiao_rank: '10级', guobiao_score: 0 } },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: { guobiao_rank: '10级', guobiao_score: 0 } });
  } catch (err) {
    console.error('admin reset rank:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
