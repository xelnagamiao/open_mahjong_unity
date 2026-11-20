const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const test = require('node:test');

const taskTmp = path.resolve(__dirname, '../../../.om_workspace/tests/classic-records');
fs.mkdirSync(taskTmp, { recursive: true });
const tmp = fs.mkdtempSync(path.join(taskTmp, 'catalog-'));
process.env.OM_DATA_DIR = tmp;

const store = require('../services/classicRecordsStore');

test('seeds the featured classic record once', () => {
  const items = store.listAll();
  assert.equal(items.some((item) => item.game_id === 'V9V0a25tKe' && item.description === '人和不够番'), true);
  const again = store.listAll();
  assert.equal(again.filter((item) => item.game_id === 'V9V0a25tKe').length, 1);
});

test('imports protagonist and allows changing it without retaining an old link player', () => {
  const created = store.createClassicRecord({ url: '/2d/record/PlayerTest?round=5&node=11&player=123&playerIndex=2', description: '主角' });
  assert.equal(created.protagonist_user_id, 123);
  assert.equal(created.protagonist_player_index, 2);
  const edited = store.updateClassicRecord(created.id, { protagonist_user_id: 456, protagonist_player_index: 0 });
  assert.match(edited.url_2d, /player=456&playerIndex=0$/);
  assert.match(edited.url_3d, /player=456&playerIndex=0$/);
  const indexOnly = store.updateClassicRecord(created.id, {protagonist_player_index: 3});
  assert.equal(indexOnly.protagonist_user_id, null);
  assert.equal(indexOnly.protagonist_player_index, 3);
  const replaced = store.updateClassicRecord(created.id, { url: '/game-unity?recordId=NewGame&player=789&playerIndex=1' });
  assert.equal(replaced.round, null);
  assert.equal(replaced.node, null);
  assert.equal(replaced.protagonist_user_id, 789);
  assert.equal(replaced.protagonist_player_index, 1);
  assert.throws(() => store.parseClassicInput({game_id: 'Test', description: 'Test', protagonist_player_index: 4}), /主角参数/);
});

test('backfills old catalog from node actor, persists it and rebuilds links with explicit overrides', async () => {
  const databasePath = require.resolve('../config/database');
  const previous = require.cache[databasePath];
  const record = {
    game_title: { p0_uid: 100, p1_uid: 101, p2_uid: 102, p3_uid: 103 },
    game_round: { r: { round_index: 5, seats: [1, 0, 3, 2], start_player_index: 0,
      action_ticks: [['reset', 0], ['c', 11], ['d', 12], ['c', 12], ['d', 13], ['c', 13], ['d', 14]] } },
  };
  let prefixReads = 0;
  let locked = false;
  require.cache[databasePath] = { exports: { query: async (sql, [argument]) => {
    if (sql.includes('WITH permitted AS')) {
      prefixReads++;
      return { rows: JSON.parse(argument).map(q=>({idx:q.idx,round:record.game_round.r})) };
    }
    return {rows: argument.map(id => ({
      id, game_id:id, table_id:'main', title:record.game_title, allowed:!locked, original_side:'main',created_at:'2026-01-01',
      players:[2,0,3,1].map(index => ({user_id:100+index,original_player_index:index,username:`P${index}`,rank:4-index,rule:'guobiao'})),
    }))};
  } } };
  try {
    const created = store.createClassicRecord({game_id: 'Backfill', round: 5, node: 7, description: '自动主角'});
    const [resolved] = await store.attachRecordMeta([created]);
    assert.equal(resolved.protagonist.username, 'P2');
    assert.equal(resolved.protagonist_user_id, 102);
    assert.equal(resolved.protagonist_player_index, 2);
    assert.match(resolved.url_3d, /player=102&playerIndex=2$/);
    assert.equal(store.listAll().find(item => item.id === created.id).protagonist_user_id, 102);
    const [manual] = await store.attachRecordMeta([{...created, protagonist_user_id: 100, protagonist_player_index: 0}], {strict: true, persist: false});
    assert.equal(manual.protagonist.username, 'P0');
    assert.equal(prefixReads,1);
    locked = true;
    const [hidden] = await store.attachRecordMeta([resolved], {persist:false});
    assert.equal(hidden.locked,true);
    assert.equal(hidden.protagonist,null);
    assert.doesNotMatch(hidden.url_3d,/player=/);
    assert.equal(prefixReads,1);
    await assert.rejects(store.attachRecordMeta([created],{strict:true,persist:false}),err=>err.status===403);
    locked = false;
    await assert.rejects(() => store.attachRecordMeta([{...created, protagonist_user_id: 999}], {strict: true, persist: false}), /主角必须/);
  } finally {
    if (previous) require.cache[databasePath] = previous;
    else delete require.cache[databasePath];
  }
});

test('creates, updates and deletes a classic record from a share URL', () => {
  const created = store.createClassicRecord({
    url: 'https://salasasa.cn/game-unity?recordId=AbCdEfGh12&round=2&node=0',
    description: '测试牌谱',
    sort: 1,
  });
  assert.equal(created.game_id, 'AbCdEfGh12');
  assert.equal(created.round, 2);
  assert.equal(created.node, 0);
  assert.match(created.url_2d, /\/2d\/record\/AbCdEfGh12\?round=2&node=0/);

  const updated = store.updateClassicRecord(created.id, {
    game_id: created.game_id,
    round: 3,
    node: 8,
    description: '改过的描述',
    sort: 20,
  });
  assert.equal(updated.round, 3);
  assert.equal(updated.description, '改过的描述');

  store.deleteClassicRecord(created.id);
  assert.equal(store.listAll().some((item) => item.id === created.id), false);
});

test('PostgreSQL classic catalog checks old table links, mixed access and source node positions', {
  skip: !process.env.DUPLICATE_TEST_DATABASE_URL,
}, async () => {
  const { Pool } = require('pg');
  const { ensureDuplicateTables } = require('../utils/duplicateTables');
  const { createReplayTables, insertPairedTable } = require('./testFixtures/replay');
  const dsn = process.env.DUPLICATE_TEST_DATABASE_URL;
  const schema = 'classic_access_' + process.pid + '_' + Date.now();
  const admin = new Pool({connectionString:dsn});
  await admin.query('CREATE SCHEMA ' + schema);
  const db = new Pool({connectionString:dsn,options:'-c search_path='+schema});
  const moduleId = require.resolve('../config/database');
  const previous = require.cache[moduleId];
  require.cache[moduleId] = {exports:db};
  try {
    await ensureDuplicateTables(db);
    await createReplayTables(db);
    await db.query('CREATE TABLE game_player_records(game_id text,user_id bigint,username text,score int,rank int,original_player_index int,rule text)');
    const pair = {id:'e49903c3-106f-4d37-9a3b-2365f5d06fbb',record_ids:{open:'oldOpen',closed:'oldClosed'}};
    const bodyId = 'Pclassic';
    for (const side of ['open','closed']) {
      await insertPairedTable(db,bodyId,pair,side,{});
      const rounds = {round_index_3:{round_index:3,seats:[0,1,2,3],start_player_index:0,action_ticks:[['p',11,2],['p',12,1]]}};
      const meta = {round_index_3:{legacy_node_map:[0,0,1,2]}};
      await db.query(`UPDATE game_replays SET record=jsonb_set(jsonb_set(record,ARRAY['tables',$1,'payload','game_round'],$2::jsonb),
          ARRAY['tables',$1,'round_meta'],$3::jsonb) WHERE game_id=$4`,[side,rounds,meta,bodyId]);
    }
    await db.query('UPDATE game_replays SET summary=$1::jsonb',[{access:{open:{has_shared_content:false},closed:{has_shared_content:false}}}]);
    const wall = (await db.query(`INSERT INTO duplicate_walls(key,owner_user_id,scope,wall_type,rule,tiles,seed)
       VALUES('classic_lock',101,'personal','key','sichuan','[]','test') RETURNING id`)).rows[0];
    await db.query('INSERT INTO duplicate_games(game_id,wall_id) VALUES($1,$2)',['oldClosed',wall.id]);
    const base = {round:3,node:2,description:'Position'};
    const read = items => store.attachRecordMeta(items,{persist:false});
    const [open,closed,opposite,initial] = await read([
      {...base,game_id:'oldOpen'},{...base,game_id:'oldClosed',protagonist_user_id:202},
      {...base,game_id:'oldClosed',table:'open'},{...base,game_id:bodyId},
    ]);
    assert.equal(open.protagonist.user_id,102);
    assert.equal(closed.locked,true);
    assert.equal(closed.protagonist,null);
    assert.doesNotMatch(closed.url_3d,/player=/);
    assert.equal(opposite.protagonist,null);
    assert.equal(initial.protagonist.user_id,101);
    await db.query("UPDATE game_replays SET summary=jsonb_set(summary,'{access,open,has_shared_content}','true')");
    assert.equal((await read([{...base,game_id:'oldOpen'}]))[0].locked,true);
    await db.query('UPDATE duplicate_walls SET is_unlocked=true WHERE id=$1',[wall.id]);
    const [unlocked,v2] = await read([{...base,game_id:'oldClosed'},{...base,game_id:bodyId,table:'closed',cursor:'v2'}]);
    assert.equal(unlocked.protagonist.user_id,202);
    assert.equal(v2.protagonist.user_id,201);
  } finally {
    if (previous) require.cache[moduleId]=previous; else delete require.cache[moduleId];
    await db.end();
    await admin.query('DROP SCHEMA '+schema+' CASCADE');
    await admin.end();
  }
});
