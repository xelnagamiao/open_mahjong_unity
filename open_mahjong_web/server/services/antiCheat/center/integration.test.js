"use strict";
const { replayDDL, insertReplay } = require('../../testFixtures/replay');
const test = require("node:test"),
  assert = require("node:assert/strict"),
  crypto = require("crypto"),
  path = require("path");
const { Pool, Client } = require("pg");
const { createCenter } = require("./service");
const { runUpdate } = require("./processor");
const { SCHEMA: S, readGame } = require("./database");
const { extractGameFacts } = require("./facts");
const { physicalWinningRecord } = require("../fixtures.test-helper");
async function setup(t) {
  require("dotenv").config({
    path: path.resolve(__dirname, "../../../../.env"),
  });
  require("dotenv").config({
    path: path.resolve(__dirname, "../../../../.env.anti-cheat.local"),
  });
  const host = process.env.DB_HOST || "localhost";
  assert.ok(["localhost", "127.0.0.1", "::1"].includes(host));
  const config = {
    host,
    port: Number(process.env.DB_PORT) || 5432,
    user: process.env.DB_USER || "postgres",
    password: process.env.DB_PASSWORD,
  };
  const db = "om_ac_test_" + crypto.randomUUID().replaceAll("-", "");
  const admin = new Client({ ...config, database: "postgres" });
  await admin.connect();
  await admin.query(`CREATE DATABASE "${db}"`);
  const pool = new Pool({ ...config, database: db, max: 8 });
  t.after(async () => {
    await pool.end();
    assert.match(db, /^om_ac_test_[a-f0-9]+$/);
    await admin.query(`DROP DATABASE "${db}"`);
    await admin.end();
  });
  await pool.query(`${replayDDL()};
    CREATE TABLE game_player_records(game_id text,user_id bigint,username text,original_player_index integer,score integer,rank integer,rule text,sub_rule text,room_type text,match_type text,match_tier text,event_id text);
    CREATE TABLE game_player_metrics(game_id text,user_id bigint,total_round_score integer,total_rounds integer);`);
  const launched = [];
  const center = createCenter({ pool, launch: (id) => launched.push(id) });
  await center.ready();
  const insert = async (id, stamp = "2026-11-10T12:00:00Z") => {
    const fixture = physicalWinningRecord(id),
      record = fixture.record;
    await insertReplay(pool,[id,record,(await pool.query("SELECT ($1::timestamptz AT TIME ZONE 'Asia/Shanghai')::text AS stamp",[stamp])).rows[0].stamp]);
    for (let seat = 0; seat < 4; seat++) {
      const uid = record.game_title[`p${seat}_uid`],
        score = [-56, 72, -8, -8][seat];
      await pool.query(
        `INSERT INTO game_player_records VALUES($1,$2,$3,$4,$5,1,'guobiao','guobiao/standard','match','4/4_rank','beginner',null)`,
        [id, uid, "player" + uid, seat, score],
      );
      await pool.query("INSERT INTO game_player_metrics VALUES($1,$2,$3,1)", [
        id,
        uid,
        score,
      ]);
    }
  };
  let calls = 0;
  const extractor = async (row) => {
    calls++;
    return extractGameFacts(row);
  };
  const update = async () => {
    const j = await center.startUpdate(10000001);
    await runUpdate(pool, j.id, {
      extractor,
      concurrency: 1,
      throwErrors: true,
      asOf: "2026-11-14T12:00:00Z",
    });
    return center.update(j.id);
  };
  return { pool, center, insert, update, launched, calls: () => calls };
}

test("directional revisions replace changed facts, isolate event evidence and retain prior publication", {skip:process.env.ANTI_CHEAT_INTEGRATION!=="1"}, async t=>{
  const x=await setup(t);await x.insert("one");await x.update();
  const first=(await x.center.listPairs({window_days:0})).items.find(p=>p.feeding.RON.count>0);
  const gen=(await x.pool.query(`SELECT active_generation FROM ${S}.meta`)).rows[0].active_generation;
  assert.equal((await x.center.pair(first.pair_key)).evidence.status,"ready");
  assert.ok((await x.center.pair(first.pair_key)).evidence.total>0,"aggregate pair evidence resolves original rule pair IDs");
  const source=await readGame(x.pool,"one"),fact=await extractGameFacts(source);
  await require("./database").commitFacts(x.pool,fact,source.source_hash);
  const pending=await x.center.pair(first.pair_key);
  assert.equal(pending.evidence.status,"pending_update");assert.deepEqual(pending.evidence.items,[]);
  assert.deepEqual(pending.feeding,first.feeding);
  await x.update();assert.equal(x.calls(),1);
  await x.insert("two","2026-11-11T12:00:00Z");await x.update();assert.equal(x.calls(),2);
  const two=(await x.center.listPairs({window_days:0})).items.find(p=>p.pair_key===first.pair_key);
  assert.equal(two.shared_games,2);assert.equal(two.feeding.RON.count,first.feeding.RON.count*2);
  await x.pool.query("UPDATE game_replays SET record=jsonb_set(record,'{tables,main,payload,game_title,correction}','1') WHERE game_id='one'");
  assert.equal((await x.update()).changed_games,1);assert.equal(x.calls(),3);
  assert.equal((await x.center.pair(first.pair_key)).shared_games,2,"correction replaces contribution instead of doubling it");
  await x.pool.query("DELETE FROM game_replays WHERE game_id='two'");
  assert.equal((await x.update()).deleted_games,1);
  assert.equal((await x.center.pair(first.pair_key)).shared_games,1);
  const old=(await x.pool.query(`SELECT payload FROM ${S}.published_pairs WHERE generation=$1 AND pair_key=$2`,[gen,first.pair_key])).rows[0].payload;
  assert.deepEqual(old,first,"previous publication stays unchanged across source correction and deletion");
  assert.equal((await x.center.overview()).model_description.baseline,1/3);
});

test("retired scoring settings are removed once without replaying facts",{skip:process.env.ANTI_CHEAT_INTEGRATION!=="1"},async t=>{
 const x=await setup(t),{initialize,settings}=require("./database");
 await x.insert("settings-migrate");await x.update();
 const before=(await x.pool.query(`SELECT * FROM ${S}.meta`)).rows[0];
 await x.pool.query(`UPDATE ${S}.meta SET settings=settings||'{"min_games":99,"min_dates":20,"min_events":500,"weights":{"delivery":0.8,"encounter":0.2,"points":0}}'::jsonb`);
 await initialize(x.pool);
 const migrated=(await x.pool.query(`SELECT * FROM ${S}.meta`)).rows[0];
 for(const key of ['min_games','min_dates','min_events','weights']){assert.equal(Object.hasOwn(migrated.settings,key),false);assert.equal(Object.hasOwn(settings({[key]:99}),key),false);}
 assert.ok(migrated.settings.revision>before.settings.revision);assert.equal(migrated.facts_revision,before.facts_revision);
 const audit=(await x.pool.query(`SELECT count(*)::int n FROM ${S}.audit`)).rows[0].n;
 await initialize(x.pool);assert.deepEqual((await x.pool.query(`SELECT settings FROM ${S}.meta`)).rows[0].settings,migrated.settings);
 assert.equal((await x.pool.query(`SELECT count(*)::int n FROM ${S}.audit`)).rows[0].n,audit);
 await x.update();assert.equal(x.calls(),1);assert.equal((await x.pool.query(`SELECT model_version FROM ${S}.meta`)).rows[0].model_version,require('./populationPipeline').VERSION);
});

test(
  "cancelled update keeps committed game facts and the next request resumes remaining games",
  { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" },
  async (t) => {
    const x = await setup(t);
    await x.insert("resume-one");
    await x.insert("resume-two", "2026-11-11T12:00:00Z");
    const job = await x.center.startUpdate(10000001);
    let calls = 0;
    await runUpdate(x.pool, job.id, {
      concurrency: 1,
      extractor: async (row) => {
        const fact = await extractGameFacts(row);
        if (++calls === 1) await x.center.cancel(job.id, 10000001);
        return fact;
      },
    });
    assert.equal((await x.center.update(job.id)).status, "cancelled");
    assert.equal(
      (await x.pool.query(`SELECT count(*)::int AS n FROM ${S}.games`)).rows[0]
        .n,
      1,
    );
    const resumed = await x.update();
    assert.equal(resumed.processed, 1);
    assert.equal(x.calls(), 1);
    assert.equal((await x.center.listPairs({})).items[0].shared_games, 2);
  },
);
test(
  "configuration revision conflict is preserved and old batch/source endpoints do not exist",
  { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" },
  async (t) => {
    const x = await setup(t),
      cfg = await x.center.getSettings();
    assert.equal(
      (await x.center.saveSettings(cfg, 10000001)).revision,
      cfg.revision + 1,
    );
    await assert.rejects(
      x.center.saveSettings(cfg, 10000001),
      (e) => e.status === 409,
    );
    const express = require("express"),
      { createRouter } = require("../../../routes/admin/antiCheat");
    const app = express();
    app.use(express.json());
    app.use((req, res, next) => {
      if (req.get("X-Test-Admin") === "yes") req.admin = { userId: 10000001 };
      next();
    });
    app.use("/api", createRouter({ center: x.center }));
    const server = await new Promise((r) => {
      const s = app.listen(0, "127.0.0.1", () => r(s));
    });
    t.after(() => new Promise((r) => server.close(r)));
    const base = "http://127.0.0.1:" + server.address().port + "/api";
    assert.equal((await fetch(base + "/overview")).status, 401);
    assert.equal(
      (await fetch(base + "/overview", { headers: { "X-Test-Admin": "yes" } }))
        .status,
      200,
    );
    assert.equal(
      (await fetch(base + "/snapshots", { headers: { "X-Test-Admin": "yes" } }))
        .status,
      404,
    );
    assert.equal(
      (await fetch(base + "/runs", { headers: { "X-Test-Admin": "yes" } }))
        .status,
      404,
    );
    assert.equal(x.launched.length, 0);
  },
);

test(
  "parallel work claims each item once; same-cutoff no-op preserves publication and expiry reuses facts",
  { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" },
  async (t) => {
    const x = await setup(t);
    for (let i = 0; i < 7; i++)
      await x.insert("parallel-" + i, "2026-11-01T12:00:00Z");
    let calls = 0;
    const extractor = async (row) => {
      calls++;
      await new Promise((r) => setTimeout(r, 3));
      return extractGameFacts(row);
    };
    const first = await x.center.startUpdate(10000001);
    await runUpdate(x.pool, first.id, {
      extractor,
      concurrency: 4,
      throwErrors: true,
      asOf: "2026-11-02T12:00:00Z",
    });
    assert.equal(calls, 7);
    assert.equal((await x.center.update(first.id)).status, "completed");
    assert.equal((await x.center.listPairs({ window_days: 7 })).total, 12);
    const beforeEmpty = (
      await x.pool.query(
        `SELECT active_generation,as_of,population_model_id,model_revision,facts_revision,stats_facts_revision,(SELECT count(*)::int FROM ${S}.stat_revisions) AS revisions FROM ${S}.meta`,
      )
    ).rows[0];
    const empty = await x.center.startUpdate(10000001);
    await runUpdate(x.pool, empty.id, {
      asOf: "2026-11-02T12:00:00Z",
      extractor,
      concurrency: 4,
      throwErrors: true,
    });
    const afterEmpty = (
      await x.pool.query(
        `SELECT active_generation,as_of,population_model_id,model_revision,facts_revision,stats_facts_revision,(SELECT count(*)::int FROM ${S}.stat_revisions) AS revisions FROM ${S}.meta`,
      )
    ).rows[0];
    assert.deepEqual(
      afterEmpty,
      beforeEmpty,
      "an unchanged effective window must not rebuild rankings",
    );
    assert.equal((await x.center.update(empty.id)).processed, 0);
    assert.equal(calls, 7);
    const second = await x.center.startUpdate(10000001);
    await runUpdate(x.pool, second.id, {
      extractor,
      concurrency: 4,
      throwErrors: true,
      asOf: "2026-11-14T12:00:00Z",
    });
    assert.equal(calls, 7, "window expiry recalculates cached statistics only");
    assert.equal((await x.center.listPairs({ window_days: 7 })).total, 0);
    assert.equal((await x.center.listPairs({ window_days: 0 })).total, 12);
  },
);

test(
  "unchanged unidentifiable input is excluded once and retried after the saved input is corrected",
  { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" },
  async (t) => {
    const x = await setup(t);
    await x.insert("duplicate-identity");
    await x.pool.query(
      "UPDATE game_replays SET record=jsonb_set(record,'{tables,main,payload,game_title,p1_uid}',record#>'{tables,main,payload,game_title,p0_uid}')",
    );
    const first = await x.update();
    assert.equal(first.failed_games, 0);
    assert.equal(first.progress.invalid_games, 1);
    assert.equal(
      (await x.pool.query(`SELECT status FROM ${S}.games`)).rows[0].status,
      "invalid",
    );
    let overview = await x.center.overview();
    assert.equal(overview.quality.invalid_records, 1);
    assert.equal(overview.summary.pending_games, 0);
    assert.equal(overview.quality.issues[0].code, "invalid_player_identity");
    const beforeNoop=(await x.pool.query(`SELECT active_generation FROM ${S}.meta`)).rows[0].active_generation;
    await x.update();
    assert.equal((await x.pool.query(`SELECT active_generation FROM ${S}.meta`)).rows[0].active_generation,beforeNoop,"excluded invalid cached facts do not trigger repeated scope publication");
    assert.equal(
      x.calls(),
      1,
      "unchanged invalid input is not repeatedly replayed",
    );
    const fixed =
      physicalWinningRecord("duplicate-identity").record.game_title.p1_uid;
    await x.pool.query(
      "UPDATE game_replays SET record=jsonb_set(record,'{tables,main,payload,game_title,p1_uid}',$1::jsonb)",
      [JSON.stringify(fixed)],
    );
    const next = await x.update();
    assert.equal(next.changed_games, 1);
    assert.equal(x.calls(), 2);
    overview = await x.center.overview();
    assert.equal(overview.quality.invalid_records, 0);
    assert.equal(overview.summary.analyzed_games, 1);
  },
);
