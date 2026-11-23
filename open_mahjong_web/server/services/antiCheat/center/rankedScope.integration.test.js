"use strict";
const { replayDDL, insertReplay } = require('../../testFixtures/replay');
const test = require("node:test"),
  assert = require("node:assert/strict"),
  crypto = require("node:crypto");
const { Pool, Client } = require("pg");
const { initialize, commitFacts, scan, readGame } = require("./database");
const {
  sourcePredicate,
  SCOPE_VERSION,
  MODEL_VERSION,
} = require("./rankedScope");
const { migrateRankedScope } = require("./migrateRankedScope");
const { createCenter } = require("./service");
const { runUpdate } = require("./processor");
const S = "anti_cheat_center";
test(
  "ranked scope migration reuses cached facts, rejects mixed snapshots and preserves excluded history",
  { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" },
  async (t) => {
    require("dotenv").config({
      path: require("path").resolve(__dirname, "../../../../.env"),
    });
    const host = process.env.DB_HOST || "localhost";
    assert.ok(["localhost", "127.0.0.1", "::1"].includes(host));
    const config = {
      host,
      port: Number(process.env.DB_PORT) || 5432,
      user: process.env.DB_USER || "postgres",
      password: process.env.DB_PASSWORD,
    };
    const name = "om_ac_scope_" + crypto.randomUUID().replaceAll("-", "");
    const admin = new Client({ ...config, database: "postgres" });
    await admin.connect();
    await admin.query(`CREATE DATABASE "${name}"`);
    const pool = new Pool({ ...config, database: name });
    t.after(async () => {
      await pool.end();
      await admin.query(`DROP DATABASE "${name}"`);
      await admin.end();
    });
    await pool.query(`${replayDDL()};
    CREATE TABLE game_player_records(game_id text,user_id bigint,username text,original_player_index integer,score integer,rank integer,rule text,sub_rule text,room_type text,match_type text,match_tier text,event_id text);
    CREATE TABLE game_player_metrics(game_id text,user_id bigint,total_round_score integer,total_rounds integer);`);
    await initialize(pool);
    const specs = [
      ["legacy", {}, {}],
      ["league", { match_tier: "mcrpl" }, {}],
      ["custom", { room_type: "custom" }, {}],
      ["event", { room_type: "events", event_id: "cup" }, {}],
      ["base", { room_type: "base" }, {}],
      ["sanma", { sub_rule: "guobiao/sanma", match_type: "4/3_rank" }, {}],
      ["riichi", { rule: "riichi" }, {}],
      ["title-custom", {}, { room_type: "custom" }],
      ["disguised-event", {}, { event_id: "cup" }],
      ["unknown-mode", { match_type: "unknown" }, {}],
      ["missing-mode", { match_type: null }, {}],
      ["unknown-queue", {}, { match_queue_type: "mystery_rank" }],
      ["conflicting", {}, {}],
    ];
    const users = ["1", "2", "3", "4"];
    for (const [id, override, title] of specs) {
      await insertReplay(pool,[id,{game_title:title},'2026-11-10 12:00:00']);
      const meta = {
        rule: "guobiao",
        sub_rule: "guobiao/standard",
        room_type: "match",
        match_type: "4/4",
        match_tier: null,
        event_id: null,
        ...override,
      };
      for (let i = 0; i < 4; i++)
        await pool.query(
          `INSERT INTO game_player_records VALUES($1,$2::bigint,$2::text, $3,0,1,$4,$5,$6,$7,$8,$9)`,
          [
            id,
            users[i],
            i,
            meta.rule,
            meta.sub_rule,
            meta.room_type,
            meta.match_type,
            meta.match_tier,
            meta.event_id,
          ],
        );
      const channels = (n, d) =>
        Object.fromEntries(
          ["ALL", "MELD", "RON"].map((c) => [
            c,
            { events: n, received: d, available: true },
          ]),
        );
      const pairs = users.flatMap((a) =>
        users
          .filter((b) => b !== a)
          .map((b) => ({
            donor_id: a,
            recipient_id: b,
            donor_name: a,
            recipient_name: b,
            delivery_supported: true,
            channels: channels(
              a === "1" && b === "2" ? 5 : 0,
              b === "2" ? 5 : 0,
            ),
          })),
      );
      const hash = (
        await pool.query(
          `SELECT ${require("./database").FINGERPRINT || "'cached'"} AS hash FROM game_record_tables gr WHERE game_id=$1`,
          [id],
        )
      ).rows[0].hash;
      await commitFacts(
        pool,
        {
          game_id: id,
          created_at: "2026-11-10T04:00:00Z",
          status: "ready",
          feature_version: require("./database").VERSION,
          rule_key: "guobiao-standard",
          ...meta,
          players: users.map((user_id) => ({ user_id, name: user_id })),
          pairs,
          evidence: [],
        },
        hash,
      );
    }
    await pool.query(
      `UPDATE game_player_records SET room_type='custom' WHERE game_id='conflicting' AND user_id=4`,
    );
    const selected = await scan(pool);
    assert.deepEqual(selected.map((r) => r.game_id).sort(), [
      "league",
      "legacy",
    ]);
    // Use the unchanged scanner fingerprint for existing valid caches, avoiding extraction.
    for (const row of selected)
      await pool.query(
        `UPDATE ${S}.games SET source_hash=$1 WHERE game_id=$2`,
        [row.source_hash, row.game_id],
      );
    const old = crypto.randomUUID();
    await pool.query(
      `INSERT INTO ${S}.player_pool_statistics(generation,window_days,user_id,rule_key,score,payload) VALUES($1,0,'mixed-only','__ALL__',1,'{"user_id":"mixed-only"}')`,
      [old],
    );
    await pool.query(
      `UPDATE ${S}.meta SET active_generation=$1,model_version='center-directional-v2',as_of='2026-11-14T12:00:00Z'`,
      [old],
    );
    const center = createCenter({ pool, launch: () => {} });
    await center.ready();
    assert.equal((await center.listPlayers({})).total, 0);
    await assert.rejects(center.pair("anything"), { status: 409 });
    const oldOverview = await center.overview();
    assert.equal(oldOverview.summary.source_games, 2);
    assert.equal(oldOverview.summary.cached_games, 2);
    assert.equal(oldOverview.summary.player_count, 0);
    assert.equal(await readGame(pool, "custom"), null);
    const result = await migrateRankedScope(pool);
    assert.equal(result.version, MODEL_VERSION);
    assert.equal(result.reused_cached_facts, true);
    const again = await migrateRankedScope(pool);
    assert.equal(again.already_current, true);
    assert.equal(again.generation, result.generation);
    assert.equal(
      Number((await pool.query(`SELECT count(*) n FROM ${S}.games`)).rows[0].n),
      specs.length,
    );
    assert.equal(
      (
        await pool.query(
          `SELECT count(*)::int n FROM ${S}.published_players WHERE generation=$1`,
          [old],
        )
      ).rows[0].n,
      1,
    );
    const membership = (
      await pool.query(
        `SELECT game_id,source_hash FROM ${S}.published_scope_games WHERE generation=$1 ORDER BY game_id`,
        [result.generation],
      )
    ).rows;
    assert.deepEqual(
      membership.map((g) => g.game_id),
      ["league", "legacy"],
    );
    assert.ok(membership.every((g) => g.source_hash));
    const list = await center.listPlayers({ page_size: 100 });
    assert.equal(list.total, 4);
    assert.equal(list.items[0].games, 2);
    const overview = await center.overview();
    assert.equal(overview.scope_version, SCOPE_VERSION);
    assert.equal(overview.summary.source_games, 2);
    assert.equal(overview.summary.cached_games, 2);
    // Moving a cached game out of scope must publish a new snapshot, not delete its facts or replay it.
    await pool.query(
      `UPDATE game_replays SET record=jsonb_set(record,'{tables,main,payload,game_title,room_type}','"custom"') WHERE game_id='league'`,
    );
    const job = await center.startUpdate(1);
    await runUpdate(pool, job.id, {
      extractor: async () => {
        throw Error("must not extract unchanged or excluded facts");
      },
      throwErrors: true,
      asOf: "2026-11-14T12:00:00Z",
    });
    assert.equal((await center.update(job.id)).calculator_calls, 0);
    const current = (
      await pool.query(`SELECT active_generation FROM ${S}.meta`)
    ).rows[0].active_generation;
    assert.notEqual(current, result.generation);
    assert.equal(
      (
        await pool.query(
          `SELECT count(*)::int n FROM ${S}.published_scope_games WHERE generation=$1`,
          [current],
        )
      ).rows[0].n,
      1,
    );
    assert.equal(
      (
        await pool.query(
          `SELECT count(*)::int n FROM ${S}.published_scope_games WHERE generation=$1`,
          [result.generation],
        )
      ).rows[0].n,
      2,
    );
    assert.equal(
      (
        await pool.query(
          `SELECT count(*)::int n FROM ${S}.games WHERE game_id='league'`,
        )
      ).rows[0].n,
      1,
    );
    // Source-only newcomer is not part of the explicitly published scope.
    await pool.query(
      `WITH x AS (INSERT INTO game_replays(game_id,record,created_at) SELECT 'not-updated',jsonb_set(record,'{tables,main,table_record_id}','"not-updated"'),created_at FROM game_replays WHERE game_id='legacy' RETURNING game_id,created_at) INSERT INTO game_record_tables SELECT game_id,game_id,'main',created_at FROM x`,
    );
    await pool.query(
      `INSERT INTO game_player_records SELECT 'not-updated',user_id,username,original_player_index,score,rank,rule,sub_rule,room_type,match_type,match_tier,event_id FROM game_player_records WHERE game_id='legacy'`,
    );
    assert.equal(
      (
        await pool.query(
          `SELECT count(*)::int n FROM ${require("../../replayTables").tableTitles} gr JOIN ${S}.published_scope_games s ON s.game_id=gr.game_id AND s.generation=$1 WHERE ${sourcePredicate("gr")}`,
          [current],
        )
      ).rows[0].n,
      1,
    );
  },
);
