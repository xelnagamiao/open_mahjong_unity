const pool = require('../config/database');

async function listUserEvents(userId) {
  const result = await pool.query(
    `SELECT e.event_id, e.name, e.description, e.status, e.kind, e.reopen_requested, e.closed_at, e.created_at, e.updated_at,
            ea.role,
            (
              SELECT COUNT(*)::int FROM event_admins ea2
              WHERE ea2.event_id = e.event_id AND ea2.role = 'admin'
            ) AS admin_count,
            (
              SELECT COUNT(DISTINCT gpr.game_id)::int FROM game_player_records gpr
              WHERE gpr.event_id = e.event_id
            ) AS record_count
     FROM event_admins ea
     INNER JOIN events e ON e.event_id = ea.event_id
     WHERE ea.user_id = $1
     ORDER BY e.created_at DESC`,
    [userId]
  );
  return result.rows;
}

module.exports = { listUserEvents };
