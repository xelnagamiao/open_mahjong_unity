const { parseReplay } = require('../utils/parseReplay');
const LOCKED_MESSAGE = '该复式密钥未解禁，牌谱暂不可查看或下载';

function trustedTrainingRecordSql(alias = 'gr') {
  return `COALESCE((${alias}.record #>> '{game_title,wall_source}' = 'training'
    AND ${alias}.record #>> '{game_title,duplicate_wall_type}' = 'training'
    AND ${alias}.record #>> '{game_title,is_duplicate}' = 'true'
    AND ${alias}.record #>> '{game_title,sub_rule}' IN ('sichuan/doubles_duplicate','sichuan/xueliu_doubles_duplicate')
    AND NULLIF(${alias}.record #>> '{game_title,duplicate_key}', '') IS NULL
    AND NOT EXISTS (SELECT 1 FROM duplicate_games training_link WHERE training_link.game_id = ${alias}.game_id)
    AND EXISTS (SELECT 1 FROM sichuan_duplicate_matches training_match
      WHERE training_match.id = CASE WHEN ${alias}.record #>> '{game_title,duplicate_match_id}'
          ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        THEN (${alias}.record #>> '{game_title,duplicate_match_id}')::uuid END
        AND training_match.wall_id IS NULL AND training_match.data->>'wall_source' = 'training'
        AND ${alias}.record #>> '{game_title,duplicate_table}' IN ('open', 'closed')
        AND training_match.data->'record_ids'->>(${alias}.record #>> '{game_title,duplicate_table}') = ${alias}.game_id)), FALSE)`;
}

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
      AND (NOT COALESCE(${markedDuplicate}, FALSE) OR ${trustedTrainingRecordSql(alias)} OR EXISTS (
        SELECT 1 FROM duplicate_games verified_game JOIN duplicate_walls verified_wall ON verified_wall.id = verified_game.wall_id
        WHERE verified_game.game_id = ${alias}.game_id AND verified_wall.is_unlocked = TRUE)))
    OR EXISTS (SELECT 1 FROM duplicate_walls title_wall
      WHERE title_wall.key = ${alias}.record #>> '{game_title,duplicate_key}' AND title_wall.is_unlocked = TRUE)
  ))`;
}
// Any source-derived field returned for a table must honor the same mixed-table
// restriction as a full replay download. The alias is supplied by the server.
function visibleReplayTableSql(alias) {
  return `(${visibleRecordSql(alias)} AND NOT EXISTS (
    SELECT 1 FROM game_record_metadata sibling
    WHERE sibling.replay_id=${alias}.replay_id AND sibling.game_id<>${alias}.game_id
      AND NOT ${visibleRecordSql('sibling')}
      AND COALESCE((SELECT (r.summary->'access'->${alias}.table_id->>'has_shared_content')::boolean
           FROM game_replays r WHERE r.game_id=${alias}.replay_id),true)))`;
}

function visibleGameSql(expression = 'gpr.game_id') {
  return `NOT EXISTS (SELECT 1 FROM game_record_metadata access_record
    WHERE access_record.game_id = ${expression} AND NOT ${visibleRecordSql('access_record')})`;
}
async function accessibleGameIds(db, ids) {
  if (!ids.length) return [];
  const result = await db.query(`SELECT gr.game_id FROM game_record_metadata gr
    WHERE gr.game_id = ANY($1::varchar[]) AND ${visibleRecordSql()}`, [ids]);
  const allowed = new Set(result.rows.map((row) => row.game_id));
  return ids.filter((id) => allowed.has(id));
}
async function recordIsLocked(db, gameId, matchId = null) {
  // A converter share serves the uploaded bytes verbatim, including any embedded
  // sibling results. Only release a saved game's share if all its tables are public.
  const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(matchId) ? matchId : null;
  const result = await db.query(`SELECT 1 FROM game_record_metadata gr
    WHERE gr.replay_id IN (
      SELECT $1::varchar
      UNION SELECT t.replay_id FROM game_record_tables t WHERE t.game_id = $1
      UNION SELECT t.replay_id FROM sichuan_duplicate_matches m
        JOIN game_record_tables t ON t.game_id IN (m.data->'record_ids'->>'open', m.data->'record_ids'->>'closed')
        WHERE m.id = $2::uuid
    ) AND NOT ${visibleRecordSql()} LIMIT 1`, [gameId, uuid]);
  return result.rows.length > 0;
}
async function sharedSourceIsLocked(db, source, sourceTableId = null) {
  // Converted external formats do not carry our metadata; native exports must retain lock enforcement.
  let record;
  try { record = typeof source === 'string' ? parseReplay(source) : source; } catch { return false; }
  if (record?.record && !record.game_title) {
    sourceTableId ||= record.game_id;
    record = record.record;
  }
  if (record?.format === 'open_mahjong.match_record') {
    if (!record.tables || typeof record.tables !== 'object') return true;
    if (sourceTableId || record.match_format === 'paired_tables_2v2') {
      const connection = typeof db === 'function' ? db() : db;
      if (await recordIsLocked(connection, sourceTableId, record.match_id)) return true;
    }
    for (const table of Object.values(record.tables)) {
      if (table?.record_state === 'missing' && table.payload == null) continue;
      if (!table?.payload?.game_title) return true;
      if (await sharedSourceIsLocked(db, table.payload, table.table_record_id)) return true;
    }
    return false;
  }
  const title = record?.game_title;
  if (!title) return false;
  db = typeof db === 'function' ? db() : db;
  if (title.duplicate_key) {
    const result = await db.query('SELECT 1 FROM duplicate_walls WHERE key = $1 AND is_unlocked = TRUE', [String(title.duplicate_key)]);
    if (!result.rows.length) return true;
  }
  const gameId = sourceTableId || title.game_id || title.cloud_game_id || record.game_id;
  const markedDuplicate = title.is_duplicate === true || Boolean(title.duplicate_seed) || Array.isArray(title.duplicate_tiles)
    || title.wall_source === 'training' || title.duplicate_wall_type === 'training';
  if (!title.duplicate_key && markedDuplicate) {
    if (!gameId) return true;
    if (title.wall_source === 'training' || title.duplicate_wall_type === 'training') {
      // A user-supplied title (or somebody else's training ID) cannot unlock a
      // formal record. Only the exact persisted training record may be shared.
      const training = await db.query(`SELECT 1 FROM game_records gr JOIN game_replays r ON r.game_id=gr.replay_id
        WHERE gr.game_id = $1 AND (gr.record = $2::jsonb OR r.record->'tables'->gr.table_id->'payload' = $2::jsonb)
        AND ${trustedTrainingRecordSql()} LIMIT 1`, [String(gameId), JSON.stringify(record)]);
      if (!training.rows.length) return true;
    } else {
      const verified = await db.query(`SELECT 1 FROM duplicate_games g JOIN duplicate_walls w ON w.id = g.wall_id
      WHERE g.game_id = $1 AND w.is_unlocked = TRUE`, [String(gameId)]);
      if (!verified.rows.length) return true;
    }
  }
  return gameId || title.duplicate_match_id ? recordIsLocked(db, gameId, title.duplicate_match_id) : false;
}

module.exports = { LOCKED_MESSAGE, visibleReplayTableSql, trustedTrainingRecordSql, visibleRecordSql, visibleGameSql, accessibleGameIds, recordIsLocked, sharedSourceIsLocked };
