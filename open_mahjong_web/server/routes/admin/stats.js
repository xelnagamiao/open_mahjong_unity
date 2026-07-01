const express = require('express');
const router = express.Router();
const pool = require('../../config/database');

// 场次等级 tier → (room_type, match_tier) 条件（用于 scene_daily_stats 查询）
function applyTierCondition(tier, where, params) {
  switch (tier) {
    case 'custom':
      params.push('custom');
      where.push(`room_type = $${params.length}`);
      break;
    case 'events':
      params.push('events');
      where.push(`room_type = $${params.length}`);
      break;
    case 'legacy_match':
      params.push('match');
      where.push(`room_type = $${params.length}`, `match_tier IS NULL`);
      break;
    case 'beginner':
    case 'intermediate':
    case 'advanced':
    case 'mcrpl':
      params.push('match');
      where.push(`room_type = $${params.length}`);
      params.push(tier);
      where.push(`match_tier = $${params.length}`);
      break;
    default:
      break;
  }
}

// 每日全站统计：对局数 / 用户量 / 最大在线
router.get('/daily', async (req, res) => {
  try {
    const days = Math.min(120, Math.max(1, parseInt(req.query.days) || 30));
    const result = await pool.query(
      `SELECT stat_date, game_count, active_users, max_online
       FROM daily_stats
       ORDER BY stat_date DESC
       LIMIT $1`,
      [days]
    );
    res.json({ success: true, data: result.rows });
  } catch (error) {
    console.error('admin stats daily error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 各场次每日统计：按 tier/game_type/日期筛选
router.get('/scene', async (req, res) => {
  try {
    const where = [];
    const params = [];
    if (req.query.date_from) {
      params.push(req.query.date_from);
      where.push(`stat_date >= $${params.length}`);
    }
    if (req.query.date_to) {
      params.push(req.query.date_to);
      where.push(`stat_date <= $${params.length}`);
    }
    if (req.query.tier) applyTierCondition(req.query.tier, where, params);
    if (req.query.game_type) {
      params.push(req.query.game_type);
      where.push(`game_type = $${params.length}`);
    }
    if (req.query.rule) {
      params.push(req.query.rule);
      where.push(`rule = $${params.length}`);
    }
    const whereSql = where.length ? `WHERE ${where.join(' AND ')}` : '';
    const result = await pool.query(
      `SELECT stat_date, room_type, match_tier, event_id, rule, game_type,
              total_games, total_rounds, win_count, self_draw_count, deal_in_count,
              total_fan_score, total_win_turn, total_fangchong_score,
              first_place_count, second_place_count, third_place_count, fourth_place_count,
              fulu_round_count, cuohe_count, total_round_score
       FROM scene_daily_stats
       ${whereSql}
       ORDER BY stat_date DESC, room_type, match_tier, game_type
       LIMIT 500`,
      params
    );
    res.json({ success: true, data: result.rows });
  } catch (error) {
    console.error('admin stats scene error:', error);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
