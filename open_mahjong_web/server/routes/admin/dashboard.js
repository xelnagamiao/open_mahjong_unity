const express = require('express');
const router = express.Router();
const pool = require('../../config/database');

router.get('/summary', async (req, res) => {
  try {
    const counts = await pool.query(`
      SELECT
        COUNT(*) FILTER (WHERE NOT is_tourist AND user_id >= 10000001)::int AS registered_users,
        COUNT(*) FILTER (WHERE is_tourist)::int AS tourist_users,
        COUNT(*)::int AS total_users
      FROM users
    `);

    const gamesToday = await pool.query(`
      SELECT COUNT(*)::int AS cnt
      FROM game_records
      WHERE created_at >= CURRENT_DATE
    `);

    const rankPlayers = await pool.query(`
      SELECT COUNT(*)::int AS cnt
      FROM rank_data
      WHERE user_id > 10000000 AND guobiao_rank != '10级'
    `);

    const row = counts.rows[0];
    res.json({
      success: true,
      data: {
        registered_users: row.registered_users,
        tourist_users: row.tourist_users,
        total_users: row.total_users,
        games_today: gamesToday.rows[0].cnt,
        leaderboard_eligible: rankPlayers.rows[0].cnt,
      },
    });
  } catch (err) {
    console.error('admin dashboard error:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
