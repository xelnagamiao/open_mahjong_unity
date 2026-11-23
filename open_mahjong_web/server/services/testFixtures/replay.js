function singleBody(id, payload, players = []) {
  return { format: 'open_mahjong.match_record', schema_version: 2, match_id: 'single:' + id,
    match_format: 'single_table', rule: payload.game_title?.rule, sub_rule: payload.game_title?.sub_rule,
    tables: { main: { table_id: 'main', table_record_id: id, payload, players, round_meta: {} } } };
}

function resultRow(id, payload, players = []) {
  return { id, game_id: id, table_id: 'main', allowed: [id], participants: [],
    body: JSON.stringify(singleBody(id, payload, players)) };
}

function replayDDL(temporary = false) {
  const kind = temporary ? 'TEMP TABLE' : 'TABLE';
  return `CREATE ${kind} game_replays(game_id text PRIMARY KEY, record jsonb NOT NULL,
    created_at timestamp NOT NULL DEFAULT now(), summary jsonb);
    CREATE ${kind} game_record_tables(game_id text PRIMARY KEY, replay_id text REFERENCES game_replays ON DELETE CASCADE,
    table_id text, created_at timestamp NOT NULL DEFAULT now(), training_match_id uuid, UNIQUE(replay_id,table_id))`;
}
async function createReplayTables(pool, temporary = false) { await pool.query(replayDDL(temporary)); }

async function insertReplay(pool, [id, record, date]) {
  const body = singleBody(id, typeof record === 'string' ? JSON.parse(record) : record);
  await pool.query(`WITH replay AS (INSERT INTO game_replays(game_id,record,created_at)
    VALUES($1,$2,COALESCE($3::timestamp,now())) RETURNING game_id,created_at)
    INSERT INTO game_record_tables(game_id,replay_id,table_id,created_at) SELECT game_id,game_id,'main',created_at FROM replay`, [id, body, date]);
}

async function insertPairedTable(pool, gameId, pair, side, title) {
  const sourceId = pair.record_ids[side];
  const players = Array.from({ length: 4 }, (_, seat) => ({ user_id: (side === 'open' ? 100 : 200) + seat,
    username: side + seat, rank: seat + 1, score: 3 - seat, original_player_index: seat }));
  const payload = { game_title: { ...title, rule: 'sichuan', sub_rule: 'sichuan/doubles_duplicate',
    duplicate_match_id: pair.id, duplicate_table: side }, game_round: {} };
  const table = { table_id: side, table_record_id: sourceId, payload, players, round_meta: {} };
  const body = { format: 'open_mahjong.match_record', schema_version: 2, match_format: 'paired_tables_2v2',
    match_id: pair.id, rule: 'sichuan', sub_rule: 'sichuan/doubles_duplicate', tables: { [side]: table } };
  await pool.query(`INSERT INTO game_replays(game_id,record) VALUES($1,$2)
    ON CONFLICT(game_id) DO UPDATE SET record=jsonb_set(game_replays.record,'{tables}',
      (game_replays.record->'tables') || (EXCLUDED.record->'tables'))`, [gameId, body]);
  await pool.query('INSERT INTO game_record_tables(game_id,replay_id,table_id) VALUES($1,$2,$3)', [sourceId,gameId,side]);
  for (const p of players) {
    await pool.query(`INSERT INTO game_player_records(game_id,user_id,username,score,rank,original_player_index)
      VALUES($1,$2,$3,$4,$5,$6)`, [sourceId,p.user_id,p.username,p.score,p.rank,p.original_player_index]);
  }
}

module.exports = { singleBody, resultRow, replayDDL, createReplayTables, insertReplay, insertPairedTable };
