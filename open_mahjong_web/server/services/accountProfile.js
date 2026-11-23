const { tableTitles } = require('./replayTables');
const { STATISTICS_RULES, subRuleSql, statsKeySql } = require('./statisticsCatalog');
const RULES = new Map(STATISTICS_RULES.flatMap(row => [[row.key, row], [row.sub_rule, row]]));

function shanghaiDay(now = new Date()) {
  return new Date(now.getTime() + 8 * 3600000).toISOString().slice(0, 10);
}

function activityYear(value, today = shanghaiDay()) {
  const current = Number(today.slice(0, 4));
  if (value === undefined || value === '') return current;
  if (!/^\d{4}$/.test(String(value)) || Number(value) < 2000 || Number(value) > current) {
    const error = new Error('请选择有效的年份'); error.status = 400; throw error;
  }
  return Number(value);
}

async function getAccountProfile(db, userId, requestedYear, now = new Date()) {
  const today = shanghaiDay(now), year = activityYear(requestedYear, today);
  const start = `${year}-01-01`, end = year === Number(today.slice(0, 4)) ? today : `${year}-12-31`;
  const user = (await db.query(`SELECT u.user_id,u.username,u.created_at::date AS joined_on,
    COALESCE(s.profile_image_id,1) AS profile_image_id,COALESCE(s.title_id,0) AS title_id
    FROM users u LEFT JOIN user_settings s USING(user_id) WHERE u.user_id=$1`, [userId])).rows[0];
  if (!user) return null;
  // The saved game timestamp is a Beijing-time TIMESTAMP without time zone.
  // Count complete recorded games, never individual hands or duplicated seats.
  const played = `EXISTS (SELECT 1 FROM game_record_tables t JOIN game_player_records p USING(game_id)
    WHERE t.replay_id=g.game_id AND p.user_id=$1)`;
  const [totals, activity, favorites] = await Promise.all([
    db.query(`SELECT COUNT(*)::int AS total_games,COUNT(DISTINCT g.created_at::date)::int AS active_days,
      MIN(g.created_at)::date AS first_game_on,MAX(g.created_at)::date AS last_game_on
      FROM game_replays g WHERE ${played}`, [userId]),
    db.query(`SELECT g.created_at::date AS date,COUNT(*)::int AS games
      FROM game_replays g
      WHERE ${played} AND g.created_at >= $2::date AND g.created_at < $3::date + INTERVAL '1 day'
      GROUP BY 1 ORDER BY 1`, [userId, start, end]),
    db.query(`SELECT COALESCE(${statsKeySql(subRuleSql('p', 'g'))},${subRuleSql('p', 'g')},p.rule) AS rule,
      COUNT(DISTINCT g.replay_id)::int AS games FROM game_player_records p JOIN ${tableTitles} g USING(game_id)
      WHERE p.user_id=$1 GROUP BY 1 ORDER BY games DESC,rule ASC LIMIT 6`, [userId]),
  ]);
  const days = activity.rows.map(row => ({ date: String(row.date).slice(0, 10), games: Number(row.games) }));
  return {
    ...user, ...totals.rows[0], year, today, timezone: 'Asia/Shanghai',
    activity: { start, end, days, games: days.reduce((sum, day) => sum + day.games, 0), active_days: days.length },
    favorite_rules: favorites.rows.map(row => ({ ...row, label: RULES.get(row.rule)?.stats_name || row.rule })),
  };
}
module.exports = { getAccountProfile, activityYear, shanghaiDay };
