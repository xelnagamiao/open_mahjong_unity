"use strict";
// Derived data lives in PostgreSQL. No file report is part of the serving path.
const SCHEMA = "anti_cheat_center";
const VERSION = "center-facts-v4";
const DEFAULTS = {
  revision: 1,
  windows_days: [0, 7, 30, 90],
  confidence: 0.95,
};
function fail(message, status = 400) {
  throw Object.assign(new Error(message), { status });
}
function settings(input) {
  if (!input || typeof input !== "object") fail("参数格式错误");
  const out = {
    ...DEFAULTS,
    ...input,
  };
  for (const [k, lo, hi] of [
    ["revision", 1, 1e9],
  ])
    if (!Number.isInteger(out[k]) || out[k] < lo || out[k] > hi)
      fail(`${k} 超出允许范围`);
  if (out.confidence !== 0.95) fail("当前参考置信水平为 95%");
  if (
    !Array.isArray(out.windows_days) ||
    !out.windows_days.length ||
    out.windows_days.length > 8 ||
    out.windows_days.some((n) => !Number.isInteger(n) || n < 0 || n > 3650)
  )
    fail("统计窗口格式错误");
  out.windows_days = [...new Set(out.windows_days)].sort((a, b) => a - b);
  return {
    revision: out.revision,
    windows_days: out.windows_days,
    confidence: out.confidence,
  };
}
async function initialize(pool) {
  await pool.query(`CREATE SCHEMA IF NOT EXISTS ${SCHEMA};
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.meta (id integer PRIMARY KEY CHECK(id=1), settings jsonb NOT NULL, active_generation uuid, as_of timestamptz, model_revision integer NOT NULL DEFAULT 0, facts_revision bigint NOT NULL DEFAULT 0, stats_facts_revision bigint NOT NULL DEFAULT -1);
    ALTER TABLE ${SCHEMA}.meta ADD COLUMN IF NOT EXISTS model_version text NOT NULL DEFAULT '';
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.updates (
      id uuid PRIMARY KEY, status text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), started_at timestamptz, finished_at timestamptz,
      heartbeat timestamptz, requested_by bigint, cancel_requested boolean NOT NULL DEFAULT false, settings jsonb NOT NULL,
      source_games integer NOT NULL DEFAULT 0, new_games integer NOT NULL DEFAULT 0, changed_games integer NOT NULL DEFAULT 0,
      deleted_games integer NOT NULL DEFAULT 0, unchanged_games integer NOT NULL DEFAULT 0, processed integer NOT NULL DEFAULT 0,
      failed_games integer NOT NULL DEFAULT 0, calculator_calls integer NOT NULL DEFAULT 0, progress jsonb NOT NULL DEFAULT '{}', error text);
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.games (
      game_id text PRIMARY KEY, source_hash text NOT NULL, feature_version text NOT NULL, created_at timestamptz,
      rule_key text, rule text, status text NOT NULL, payload jsonb NOT NULL, processed_at timestamptz NOT NULL DEFAULT now());
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.pairs (
      game_id text NOT NULL REFERENCES ${SCHEMA}.games(game_id) ON DELETE CASCADE,
      pair_id text NOT NULL, rule_key text NOT NULL, donor_id text NOT NULL, recipient_id text NOT NULL,
      created_at timestamptz NOT NULL, payload jsonb NOT NULL, PRIMARY KEY(game_id,pair_id));
    CREATE INDEX IF NOT EXISTS ac_pairs_pair ON ${SCHEMA}.pairs(pair_id,created_at);
    CREATE INDEX IF NOT EXISTS ac_pairs_donor ON ${SCHEMA}.pairs(rule_key,donor_id,created_at);
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.evidence (
      game_id text NOT NULL REFERENCES ${SCHEMA}.games(game_id) ON DELETE CASCADE, ordinal integer NOT NULL,
      pair_id text NOT NULL, created_at timestamptz NOT NULL, payload jsonb NOT NULL, PRIMARY KEY(game_id,ordinal));
    CREATE INDEX IF NOT EXISTS ac_evidence_pair ON ${SCHEMA}.evidence(pair_id,created_at DESC);
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.pair_statistics (
      generation uuid NOT NULL, window_days integer NOT NULL, pair_id text NOT NULL, pair_key text NOT NULL,
      donor_id text NOT NULL, recipient_id text NOT NULL, donor_name text, recipient_name text, rule_key text NOT NULL,
      shared_games integer NOT NULL, score double precision, flag_all boolean NOT NULL DEFAULT false,
      flag_meld boolean NOT NULL DEFAULT false, flag_ron boolean NOT NULL DEFAULT false,
      payload jsonb NOT NULL, PRIMARY KEY(generation,window_days,pair_id));
    CREATE INDEX IF NOT EXISTS ac_statistics_ranking ON ${SCHEMA}.pair_statistics(generation,window_days,score DESC NULLS LAST,pair_id);
    CREATE INDEX IF NOT EXISTS ac_statistics_receiver ON ${SCHEMA}.pair_statistics(generation,window_days,recipient_id,rule_key,shared_games DESC);
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.player_pool_statistics (
      generation uuid NOT NULL, window_days integer NOT NULL, user_id text NOT NULL, rule_key text NOT NULL, score double precision, payload jsonb NOT NULL,
      PRIMARY KEY(generation,window_days,user_id,rule_key));
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.game_predictions (
      generation uuid NOT NULL,game_id text NOT NULL,user_id text NOT NULL,rule_key text NOT NULL,created_at timestamptz NOT NULL,payload jsonb NOT NULL,
      PRIMARY KEY(generation,game_id,user_id));
    CREATE INDEX IF NOT EXISTS ac_game_prediction_player ON ${SCHEMA}.game_predictions(generation,rule_key,user_id,created_at DESC);
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.reviews (pair_key text PRIMARY KEY, status text NOT NULL, note text NOT NULL, updated_by bigint, updated_at timestamptz NOT NULL DEFAULT now());
    CREATE TABLE IF NOT EXISTS ${SCHEMA}.audit (id bigserial PRIMARY KEY, at timestamptz NOT NULL DEFAULT now(), actor bigint, action text NOT NULL, detail jsonb NOT NULL);
  `);
  await pool.query(
    `INSERT INTO ${SCHEMA}.meta(id,settings) VALUES(1,$1) ON CONFLICT DO NOTHING`,
    [JSON.stringify(DEFAULTS)],
  );
  await require("./versionStore").initializeVersions(pool);
  await pool.query(`ALTER TABLE ${SCHEMA}.meta ADD COLUMN IF NOT EXISTS settings_model text NOT NULL DEFAULT '';
    WITH changed AS (
    UPDATE ${SCHEMA}.meta SET settings_model='fixed-directional-share',
      settings=(settings-'min_games'-'min_dates'-'min_events'-'min_reference_games'-'bootstrap_samples'-'weights')
      ||jsonb_build_object('revision',(settings->>'revision')::int+1)
    WHERE settings_model<>'fixed-directional-share'
      OR settings ?| ARRAY['min_games','min_dates','min_events','min_reference_games','bootstrap_samples','weights'] RETURNING settings
  ) INSERT INTO ${SCHEMA}.audit(action,detail)
    SELECT 'settings.directional_reset',settings FROM changed;`);
}
function pairId(rule, a, b) {
  return require("crypto")
    .createHash("sha256")
    .update(JSON.stringify([rule, String(a), String(b)]))
    .digest("hex")
    .slice(0, 24);
}
// Fingerprints are checked only on an explicit update. Reading a page never scans replay blobs.
const FINGERPRINT = `md5(jsonb_build_array(gr.created_at::text,gr.record,COALESCE((SELECT jsonb_agg(jsonb_build_array(p.user_id::text,p.original_player_index,p.score,p.rank,p.rule,p.sub_rule,p.room_type,p.match_type,p.match_tier,p.event_id,m.total_round_score,m.total_rounds) ORDER BY p.user_id,p.original_player_index) FROM public.game_player_records p LEFT JOIN public.game_player_metrics m ON m.game_id=p.game_id AND m.user_id=p.user_id WHERE p.game_id=gr.game_id),'[]'::jsonb))::text)`;
async function scan(pool) {
  const rows=(await pool.query(`SELECT gr.game_id, ${FINGERPRINT} AS source_hash, gr.created_at::text FROM public.game_records gr WHERE ${require("./rankedScope").sourcePredicate("gr")} ORDER BY gr.created_at,gr.game_id`)).rows;
  await require('./rankedScope').saveScopeInventory(pool,rows.map(r=>r.game_id));
  return rows;
}

async function readGame(pool, id) {
  const result = await pool.query(
    `SELECT gr.game_id,gr.created_at::text,gr.record,${FINGERPRINT} AS source_hash,
    COALESCE((SELECT jsonb_agg((to_jsonb(p) - 'note' - 'is_favorite') || jsonb_build_object('total_round_score',m.total_round_score,'total_rounds',m.total_rounds) ORDER BY p.original_player_index NULLS LAST,p.user_id)
      FROM public.game_player_records p LEFT JOIN public.game_player_metrics m ON m.game_id=p.game_id AND m.user_id=p.user_id WHERE p.game_id=gr.game_id),'[]'::jsonb) AS players
    FROM public.game_records gr WHERE gr.game_id=$1 AND ${require("./rankedScope").sourcePredicate("gr")}`,
    [id],
  );
  if (!result.rows.length) return null;
  const row = result.rows[0];
  row.created_at_timezone = "Asia/Shanghai";
  const p = row.players[0] || {},
    title = row.record?.game_title || {};
  for (const k of [
    "rule",
    "sub_rule",
    "room_type",
    "match_type",
    "match_tier",
    "event_id",
  ])
    row[k] = title[k] !== undefined && title[k] !== null && title[k] !== "" ? title[k] : p[k] ?? null;
  return row;
}
async function commitFacts(pool, fact, sourceHash) {
  const c = await pool.connect();
  try {
    await c.query("BEGIN");
    await c.query(`DELETE FROM ${SCHEMA}.games WHERE game_id=$1`, [
      fact.game_id,
    ]);
    const { pairs = [], evidence = [], ...metadata } = fact;
    const time = fact.created_at || null;
    await c.query(
      `INSERT INTO ${SCHEMA}.games(game_id,source_hash,feature_version,created_at,rule_key,rule,status,payload) VALUES($1,$2,$3,$4,$5,$6,$7,$8)`,
      [
        fact.game_id,
        sourceHash,
        VERSION,
        time,
        fact.rule_key || "",
        fact.rule || "unknown",
        fact.status || "ready",
        JSON.stringify(metadata),
      ],
    );
    if (time && pairs.length) {
      const rows = pairs.map((p) => ({
        ...p,
        pair_id: pairId(fact.rule_key, p.donor_id, p.recipient_id),
      }));
      await c.query(
        `INSERT INTO ${SCHEMA}.pairs(game_id,pair_id,rule_key,donor_id,recipient_id,created_at,payload)
        SELECT $1,x.pair_id,$2,x.donor_id,x.recipient_id,$3,x.data FROM jsonb_to_recordset($4::jsonb) x(pair_id text,donor_id text,recipient_id text,data jsonb)`,
        [
          fact.game_id,
          fact.rule_key,
          time,
          JSON.stringify(
            rows.map((p) => ({
              pair_id: p.pair_id,
              donor_id: String(p.donor_id),
              recipient_id: String(p.recipient_id),
              data: p,
            })),
          ),
        ],
      );
      if (evidence.length)
        await c.query(
          `INSERT INTO ${SCHEMA}.evidence(game_id,ordinal,pair_id,created_at,payload)
        SELECT $1,x.ordinal,x.pair_id,$2,x.data FROM jsonb_to_recordset($3::jsonb) x(ordinal integer,pair_id text,data jsonb)`,
          [
            fact.game_id,
            time,
            JSON.stringify(
              evidence
                .filter((e) => e.donor_id != null && e.recipient_id != null)
                .map((e, i) => ({
                  ordinal: i,
                  pair_id: pairId(fact.rule_key, e.donor_id, e.recipient_id),
                  data: e,
                })),
            ),
          ],
        );
    }
    await c.query(
      `UPDATE ${SCHEMA}.meta SET facts_revision=facts_revision+1 WHERE id=1`,
    );
    await c.query("COMMIT");
  } catch (e) {
    await c.query("ROLLBACK");
    throw e;
  } finally {
    c.release();
  }
}
module.exports = {
  SCHEMA,
  VERSION,
  DEFAULTS,
  settings,
  fail,
  initialize,
  pairId,
  scan,
  readGame,
  commitFacts,
};
