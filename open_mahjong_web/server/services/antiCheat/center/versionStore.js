"use strict";
const S = "anti_cheat_center";
const TABLES = {
  pairs: {
    stage: "pair_statistics",
    key: ["window_days", "pair_id"],
    columns:
      "window_days,pair_id,pair_key,donor_id,recipient_id,donor_name,recipient_name,rule_key,shared_games,score,flag_all,flag_meld,flag_ron,payload",
  },
  players: {
    stage: "player_pool_statistics",
    key: ["window_days", "user_id", "rule_key"],
    columns: "window_days,user_id,rule_key,score,payload",
  },
  predictions: {
    stage: "game_predictions",
    key: ["game_id", "user_id"],
    columns: "game_id,user_id,rule_key,created_at,payload",
  },
};
async function initializeVersions(pool) {
  await require("./playerRankProfiles").initializeRankProfiles(pool);
  await pool.query(`CREATE TABLE IF NOT EXISTS ${S}.stat_revisions(revision bigserial PRIMARY KEY,generation uuid UNIQUE NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
    CREATE TABLE IF NOT EXISTS ${S}.population_models(id uuid PRIMARY KEY,version text NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),payload jsonb NOT NULL);
    ALTER TABLE ${S}.meta ADD COLUMN IF NOT EXISTS population_model_id uuid;
    CREATE TABLE IF NOT EXISTS ${S}.dirty_games(game_id text PRIMARY KEY,players jsonb NOT NULL DEFAULT '[]',rules jsonb NOT NULL DEFAULT '[]',reference_changed boolean NOT NULL DEFAULT false);
    ALTER TABLE ${S}.dirty_games ADD COLUMN IF NOT EXISTS reference_changed boolean NOT NULL DEFAULT false;
    CREATE OR REPLACE FUNCTION ${S}.track_game_change() RETURNS trigger AS $$
    DECLARE f jsonb; gid text; rk text;
    BEGIN
      IF TG_OP='DELETE' THEN f=OLD.payload;gid=OLD.game_id;rk=OLD.rule_key; ELSE f=NEW.payload;gid=NEW.game_id;rk=NEW.rule_key; END IF;
      INSERT INTO ${S}.dirty_games(game_id,players,rules,reference_changed)
      VALUES(gid,COALESCE((SELECT jsonb_agg(p->>'user_id') FROM jsonb_array_elements(COALESCE(f->'players','[]')) p),'[]'),jsonb_build_array(rk),TG_OP='DELETE')
      ON CONFLICT(game_id) DO UPDATE SET players=(SELECT COALESCE(jsonb_agg(DISTINCT v),'[]') FROM jsonb_array_elements(${S}.dirty_games.players||excluded.players) v),rules=(SELECT COALESCE(jsonb_agg(DISTINCT v),'[]') FROM jsonb_array_elements(${S}.dirty_games.rules||excluded.rules) v),reference_changed=${S}.dirty_games.reference_changed OR excluded.reference_changed;
      RETURN NULL;
    END; $$ LANGUAGE plpgsql;
    DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_trigger WHERE tgname='ac_track_game_change' AND tgrelid='${S}.games'::regclass) THEN CREATE TRIGGER ac_track_game_change AFTER INSERT OR DELETE ON ${S}.games FOR EACH ROW EXECUTE FUNCTION ${S}.track_game_change(); END IF; END $$;`);
  await require("./rankedScope").initializeScope(pool);
  for (const [kind, t] of Object.entries(TABLES)) {
    await pool.query(`CREATE TABLE IF NOT EXISTS ${S}.${kind}_versions AS SELECT ${t.columns},0::bigint AS valid_from,NULL::bigint AS valid_to FROM ${S}.${t.stage} WITH NO DATA;
      CREATE UNIQUE INDEX IF NOT EXISTS ac_${kind}_version_key ON ${S}.${kind}_versions(${t.key.join(",")},valid_from);
      CREATE INDEX IF NOT EXISTS ac_${kind}_version_current ON ${S}.${kind}_versions(valid_to,valid_from);
      CREATE OR REPLACE VIEW ${S}.published_${kind} AS
      SELECT generation,${t.columns} FROM ${S}.${t.stage}
      UNION ALL SELECT r.generation,${t.columns
        .split(",")
        .map((c) => "v." + c)
        .join(
          ",",
        )} FROM ${S}.stat_revisions r JOIN ${S}.${kind}_versions v ON v.valid_from<=r.revision AND (v.valid_to IS NULL OR v.valid_to>r.revision);`);
  }
  await pool.query(`CREATE INDEX IF NOT EXISTS ac_pair_versions_recipient ON ${S}.pairs_versions(recipient_id,rule_key,window_days,shared_games DESC) WHERE valid_to IS NULL;
    CREATE INDEX IF NOT EXISTS ac_pair_versions_donor ON ${S}.pairs_versions(donor_id,rule_key,window_days) WHERE valid_to IS NULL;
    CREATE INDEX IF NOT EXISTS ac_player_versions_user ON ${S}.players_versions(user_id,window_days,rule_key) WHERE valid_to IS NULL;
    CREATE INDEX IF NOT EXISTS ac_prediction_versions_user ON ${S}.predictions_versions(user_id,rule_key,created_at DESC) WHERE valid_to IS NULL;`);
  await pool.query(`CREATE INDEX IF NOT EXISTS ac_pair_versions_snapshot_receiver ON ${S}.pairs_versions(recipient_id,rule_key,window_days,valid_from,valid_to);
    CREATE INDEX IF NOT EXISTS ac_pair_versions_snapshot_donor ON ${S}.pairs_versions(donor_id,rule_key,window_days,valid_from,valid_to);
    CREATE INDEX IF NOT EXISTS ac_prediction_versions_snapshot ON ${S}.predictions_versions(user_id,rule_key,created_at DESC,valid_from,valid_to);`);
}
async function publish(
  pool,
  {
    generation,
    scopeGames,
    full,
    pairIds,
    playerIds,
    predictionGameIds,
    modelId,
    config,
    version,
    asOf,
    audit,
  },
) {
  const c = await pool.connect();
  try {
    await c.query("BEGIN");
    const previousGeneration=(await c.query(`SELECT active_generation FROM ${S}.meta WHERE id=1`)).rows[0]?.active_generation;
    const revision = (
      await c.query(
        `INSERT INTO ${S}.stat_revisions(generation) VALUES($1) RETURNING revision`,
        [generation],
      )
    ).rows[0].revision;
    for (const [kind, t] of Object.entries(TABLES)) {
      const field =
        kind === "pairs"
          ? "pair_id"
          : kind === "players"
            ? "user_id"
            : "game_id";
      const keys =
        kind === "pairs"
          ? pairIds
          : kind === "players"
            ? playerIds
            : predictionGameIds;
      await c.query(
        `UPDATE ${S}.${kind}_versions SET valid_to=$1 WHERE valid_to IS NULL AND ($2 OR ${field}=ANY($3::text[]))`,
        [revision, full, keys],
      );
      await c.query(
        `INSERT INTO ${S}.${kind}_versions(${t.columns},valid_from) SELECT ${t.columns},$2 FROM ${S}.${t.stage} WHERE generation=$1`,
        [generation, revision],
      );
      await c.query(`DELETE FROM ${S}.${t.stage} WHERE generation=$1`, [
        generation,
      ]);
    }
    if(scopeGames)await require("./rankedScope").publishScope(c,revision,scopeGames);
    await require("./playerRankProfiles").buildRankProfiles(c,generation,{previousGeneration,playerIds,full});
    await c.query(
      `UPDATE ${S}.meta SET active_generation=$1,as_of=$2,model_revision=$3,model_version=$4,population_model_id=$5,stats_facts_revision=facts_revision WHERE id=1`,
      [generation, asOf, config.revision, version, modelId],
    );
    await c.query(
      `INSERT INTO ${S}.audit(action,detail) VALUES('statistics.updated',$1)`,
      [JSON.stringify({ generation, model_version: version, ...audit })],
    );
    await c.query(`DELETE FROM ${S}.dirty_games`);
    await c.query("COMMIT");
  } catch (e) {
    await c.query("ROLLBACK");
    throw e;
  } finally {
    c.release();
  }
}
module.exports = { initializeVersions, publish };
