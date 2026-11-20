const { parseReplay } = require('../utils/parseReplay');
const pool = require('../config/database');
const { visibleRecordSql, visibleReplayTableSql, LOCKED_MESSAGE } = require('./duplicateRecordAccess');

const GAME_ID_PATTERN = /^[0-9A-Za-z]{1,16}$/;

function containsSharedData(payload) {
  const title = payload.game_title || {};
  const match = payload.duplicate_match || {};
  return [payload.duplicate_boards, payload.doubles_result, title.doubles_result, title.duplicate_seed,
    title.duplicate_tiles, title.duplicate_round_tiles, match.boards, match.scores, match.small_scores, match.imp_scores]
    .some(value => typeof value === 'object' ? value != null && Object.keys(value).length > 0 : value != null && value !== '');
}

function presentReplay(row) {
  const record = parseReplay(row.body);
  const allowed = new Set(row.allowed || []);
  const roster = row.participants || [];
  let selected = row.table_id;
  let hidden = false;
  const players = [];
  for (const [key, table] of Object.entries(record.tables)) {
    // Some historical rosters were built from entire game_player_records rows.
    // Personal annotations belong to each account's list.
    for (const player of table.players || []) {
      delete player.note;
      delete player.is_favorite;
    }
    if (!table.payload) continue;
    if (!allowed.has(table.table_record_id)) {
      if (selected === key) return { status: 403, message: LOCKED_MESSAGE };
      record.tables[key] = { table_id: key, table_record_id: null, record_state: 'missing',
        reason: 'access_denied', payload: null, round_meta: {}, players: [] };
      hidden = true;
      continue;
    }
    const names = new Map(roster.filter(p => p.table_id === key && Number(p.user_id) > 10).map(p => [Number(p.user_id), p.username]));
    for (const p of table.players) {
      const name = names.get(Number(p.user_id));
      if (name != null) p.username = name;
      players.push({ ...p, table_id: key });
    }
  }
  if (!selected) selected = ['main', 'open', 'closed'].find(k => record.tables[k]?.payload);
  if (!selected || !record.tables[selected]?.payload) return { status: 403, message: LOCKED_MESSAGE };
  if (hidden) {
    if (Object.values(record.tables).some(t => t.payload && containsSharedData(t.payload))) {
      return { status: 403, message: LOCKED_MESSAGE };
    }
    record.completeness = 'partial';
    record.settlements = [];
    record.result = null;
    record.provenance = {};
    for (const team of record.teams || []) team.members = team.members.filter(m => record.tables[m.table_id]?.payload);
    for (const board of record.board_catalog || []) delete board.wall_definition;
  }
  const selectedTable = record.tables[selected];
  const scene = roster.find(p => p.table_id === selected);
  const data = { game_id: row.game_id, cloud_saved: true, created_at: row.created_at,
    rule: record.rule, sub_rule: record.sub_rule, record, selected_table_id: selected, players,
    room_type: selectedTable.payload.game_title?.room_type || scene?.room_type || null,
    match_type: scene?.match_type || selectedTable.payload.game_title?.match_type || null };
  if (row.table_id) {
    const rounds = Object.entries(selectedTable.round_meta);
    data.legacy_cursor_map = {
      round_map: Object.fromEntries(rounds.map(([k, r]) => [k, r.table_round_id])),
      node_map: Object.fromEntries(rounds.map(([k, r]) => [k, r.legacy_node_map])),
    };
  }
  return { status: 200, data };
}

// Select the body and table permissions in one database snapshot. Both old table
// links and the canonical game ID resolve to the same physical game_replays row.
async function readGameRecords(db, ids) {
  if (!ids.length) return new Map();
  const { rows } = await db.query(`SELECT req.id, r.game_id, r.created_at, r.record::text AS body, link.table_id,
      ARRAY(SELECT gr.game_id FROM game_record_metadata gr
            WHERE gr.replay_id = r.game_id AND ${visibleRecordSql()}) AS allowed,
      (SELECT jsonb_agg(jsonb_build_object('table_id', t.table_id, 'user_id', p.user_id,
                    'username', p.username, 'room_type', p.room_type, 'match_type', p.match_type))
         FROM game_record_tables t JOIN game_player_records p USING(game_id)
        WHERE t.replay_id = r.game_id) AS participants
    FROM unnest($1::varchar[]) req(id)
    LEFT JOIN game_record_tables link ON link.game_id = req.id
    JOIN game_replays r ON r.game_id = COALESCE(link.replay_id, req.id)`, [ids]);
  return new Map(rows.map(row => [row.id, presentReplay(row)]));
}

async function getPublicGameRecord(gameId, db = pool) {
  if (!GAME_ID_PATTERN.test(gameId)) return { status: 400, message: '牌谱编号格式不正确' };
  const records = await readGameRecords(db, [gameId]);
  return records.get(gameId) || { status: 404, message: '没有找到这份牌谱' };
}

// Download quotas and ZIP entries count one game, even when both old links are selected.
async function downloadableReplays(db, ids, { userId = null, eventId = null } = {}) {
  if (!ids.length) return [];
  const { rows } = await db.query(`SELECT gr.replay_id,(array_agg(req.id ORDER BY req.pos))[1] AS requested_id
    FROM unnest($1::varchar[]) WITH ORDINALITY req(id, pos)
    LEFT JOIN game_record_tables link ON link.game_id=req.id
    JOIN game_record_metadata gr ON gr.replay_id=COALESCE(link.replay_id, req.id)
    WHERE (link.table_id IS NULL OR gr.table_id=link.table_id) AND ${visibleReplayTableSql('gr')}
      AND (($2::bigint IS NULL AND $3::text IS NULL) OR EXISTS (
        SELECT 1 FROM game_record_tables owner JOIN game_player_records p USING(game_id)
        WHERE owner.replay_id=gr.replay_id AND ($2::bigint IS NULL OR p.user_id=$2)
          AND ($3::text IS NULL OR (p.event_id=$3 AND p.room_type='events'))))

    GROUP BY gr.replay_id ORDER BY MIN(req.pos)`, [ids, userId, eventId]);
  return rows;
}

async function downloadableReplayIds(db, ids, scope) {
  return (await downloadableReplays(db, ids, scope)).map(row => row.replay_id);
}

module.exports = { getPublicGameRecord, readGameRecords, downloadableReplays, downloadableReplayIds, GAME_ID_PATTERN };
