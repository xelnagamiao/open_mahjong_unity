const LOCKED_MESSAGE = '该复式密钥仍处于锁定状态，牌谱暂不可查看或下载';

// Use both the durable association and the title, so a missing association cannot expose a record.
// Expressions passed here are server-owned SQL, never request input.
// Use only when returning full records or source content. Metadata lists may
// include locked games and must not inspect every record JSON for visibility.
function visibleRecordSql(alias = 'gr') {
  const markedDuplicate = `(${alias}.record #>> '{game_title,is_duplicate}' = 'true'
    OR NULLIF(${alias}.record #>> '{game_title,duplicate_seed}', '') IS NOT NULL
    OR jsonb_typeof(${alias}.record #> '{game_title,duplicate_tiles}') = 'array')`;
  return `(NOT EXISTS (
    SELECT 1 FROM duplicate_games access_game LEFT JOIN duplicate_walls access_wall ON access_wall.id = access_game.wall_id
    WHERE access_game.game_id = ${alias}.game_id AND access_wall.is_unlocked IS DISTINCT FROM TRUE
  ) AND (
    (NULLIF(${alias}.record #>> '{game_title,duplicate_key}', '') IS NULL
      AND (NOT COALESCE(${markedDuplicate}, FALSE) OR EXISTS (
        SELECT 1 FROM duplicate_games verified_game JOIN duplicate_walls verified_wall ON verified_wall.id = verified_game.wall_id
        WHERE verified_game.game_id = ${alias}.game_id AND verified_wall.is_unlocked = TRUE)))
    OR EXISTS (SELECT 1 FROM duplicate_walls title_wall
      WHERE title_wall.key = ${alias}.record #>> '{game_title,duplicate_key}' AND title_wall.is_unlocked = TRUE)
  ))`;
}
function visibleGameSql(expression = 'gpr.game_id') {
  return `NOT EXISTS (SELECT 1 FROM game_records access_record
    WHERE access_record.game_id = ${expression} AND NOT ${visibleRecordSql('access_record')})`;
}
async function accessibleGameIds(db, ids) {
  if (!ids.length) return [];
  const result = await db.query(`SELECT gr.game_id FROM game_records gr
    WHERE gr.game_id = ANY($1::varchar[]) AND ${visibleRecordSql()}`, [ids]);
  const allowed = new Set(result.rows.map((row) => row.game_id));
  return ids.filter((id) => allowed.has(id));
}
async function recordIsLocked(db, gameId) {
  const result = await db.query(`SELECT 1 FROM game_records gr
    WHERE gr.game_id = $1 AND NOT ${visibleRecordSql()} LIMIT 1`, [gameId]);
  return result.rows.length > 0;
}
async function sharedSourceIsLocked(db, source) {
  // Converted external formats do not carry our metadata; native exports must retain lock enforcement.
  let record;
  try { record = typeof source === 'string' ? JSON.parse(source) : source; } catch { return false; }
  const title = record?.game_title;
  if (!title) return false;
  db = typeof db === 'function' ? db() : db;
  if (title.duplicate_key) {
    const result = await db.query('SELECT 1 FROM duplicate_walls WHERE key = $1 AND is_unlocked = TRUE', [String(title.duplicate_key)]);
    if (!result.rows.length) return true;
  }
  const gameId = title.game_id || record.game_id;
  const markedDuplicate = title.is_duplicate === true || Boolean(title.duplicate_seed) || Array.isArray(title.duplicate_tiles);
  if (!title.duplicate_key && markedDuplicate) {
    if (!gameId) return true;
    const verified = await db.query(`SELECT 1 FROM duplicate_games g JOIN duplicate_walls w ON w.id = g.wall_id
      WHERE g.game_id = $1 AND w.is_unlocked = TRUE`, [String(gameId)]);
    if (!verified.rows.length) return true;
  }
  return gameId ? recordIsLocked(db, String(gameId)) : false;
}

module.exports = { LOCKED_MESSAGE, visibleRecordSql, visibleGameSql, accessibleGameIds, recordIsLocked, sharedSourceIsLocked };
