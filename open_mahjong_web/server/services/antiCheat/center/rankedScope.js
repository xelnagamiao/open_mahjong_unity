"use strict";
const { tableTitles } = require('../../replayTables');
const SCOPE_VERSION = "guobiao-ranked-v1";
const MODEL_VERSION = "center-directional-guobiao-ranked-v3";
// RankedQueue in server/match/rating_rules.py: four-player standard Guobiao.
// Explicit saved title metadata wins; legacy records fall back to participant metadata.
function sourcePredicate(alias = "gr", title = `${alias}.title`) {
  if (!/^[a-z][a-z0-9_]*$/i.test(alias)) throw new Error("invalid_sql_alias");
  const value = (field) =>
    `COALESCE(NULLIF(scope_title.title->>'${field}',''),NULLIF(p.${field}::text,''))`;
  return `EXISTS(SELECT 1 FROM (SELECT ${title} AS title OFFSET 0) scope_title
    CROSS JOIN public.game_player_records p WHERE p.game_id=${alias}.game_id
    GROUP BY p.game_id HAVING count(*)=4 AND count(DISTINCT p.user_id)=4 AND bool_and(COALESCE(
      ${value("rule")}='guobiao' AND ${value("room_type")}='match'
      AND COALESCE(${value("sub_rule")},'guobiao/standard') IN('guobiao','guobiao/standard')
      AND COALESCE(${value("event_id")},'')=''
      AND COALESCE(${value("match_tier")},'') IN('','beginner','intermediate','advanced','mcrpl')
      AND ${value("match_type")} ~ '^(1|2|4)/4(_rank)?$'
      AND (NULLIF(scope_title.title->>'match_queue_type','') IS NULL OR scope_title.title->>'match_queue_type' ~ '^(beginner|intermediate|advanced|mcrpl)_(dongfeng|banzhuang|quanzhuang)$')
    ,false)))`;
}
function cachedPredicate(alias = "g") {
  if (!/^[a-z][a-z0-9_]*$/i.test(alias)) throw new Error("invalid_sql_alias");
  return `EXISTS(SELECT 1 FROM ${tableTitles} scoped_source WHERE scoped_source.game_id=${alias}.game_id AND ${sourcePredicate("scoped_source")})`;
}
async function initializeScope(client) {
  await client.query(`CREATE TABLE IF NOT EXISTS anti_cheat_center.scope_source_inventory(game_id text PRIMARY KEY);
    CREATE TABLE IF NOT EXISTS anti_cheat_center.scope_game_versions(
    game_id text NOT NULL,source_hash text NOT NULL,valid_from bigint NOT NULL,valid_to bigint,
    PRIMARY KEY(game_id,valid_from));
    CREATE INDEX IF NOT EXISTS ac_scope_current ON anti_cheat_center.scope_game_versions(game_id) WHERE valid_to IS NULL;
    CREATE OR REPLACE VIEW anti_cheat_center.published_scope_games AS
    SELECT r.generation,v.game_id,v.source_hash FROM anti_cheat_center.stat_revisions r
    JOIN anti_cheat_center.scope_game_versions v ON v.valid_from<=r.revision AND (v.valid_to IS NULL OR v.valid_to>r.revision);`);
}
async function saveScopeInventory(pool, ids) {
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    await client.query("DELETE FROM anti_cheat_center.scope_source_inventory");
    await client.query(
      "INSERT INTO anti_cheat_center.scope_source_inventory SELECT unnest($1::text[])",
      [ids],
    );
    await client.query("COMMIT");
  } catch (error) {
    await client.query("ROLLBACK");
    throw error;
  } finally {
    client.release();
  }
}
async function synchronizeScopeInventory(pool) {
  const rows = (
    await pool.query(
      `SELECT gr.game_id FROM ${tableTitles} gr WHERE ${sourcePredicate("gr")}`,
    )
  ).rows;
  await saveScopeInventory(
    pool,
    rows.map((r) => r.game_id),
  );
  return rows;
}
function inventoryPredicate(alias = "g") {
  if (!/^[a-z][a-z0-9_]*$/i.test(alias)) throw new Error("invalid_sql_alias");
  return `EXISTS(SELECT 1 FROM anti_cheat_center.scope_source_inventory inventory WHERE inventory.game_id=${alias}.game_id)`;
}
async function publishScope(client, revision, games) {
  const rows = games.map((g) => ({
    game_id: g.game_id,
    source_hash: g.source_hash,
  }));
  await client.query(
    `UPDATE anti_cheat_center.scope_game_versions old SET valid_to=$1 WHERE valid_to IS NULL AND NOT EXISTS(
    SELECT 1 FROM jsonb_to_recordset($2::jsonb) x(game_id text,source_hash text) WHERE x.game_id=old.game_id AND x.source_hash=old.source_hash)`,
    [revision, JSON.stringify(rows)],
  );
  await client.query(
    `INSERT INTO anti_cheat_center.scope_game_versions(game_id,source_hash,valid_from)
    SELECT x.game_id,x.source_hash,$1 FROM jsonb_to_recordset($2::jsonb) x(game_id text,source_hash text)
    WHERE NOT EXISTS(SELECT 1 FROM anti_cheat_center.scope_game_versions old WHERE old.game_id=x.game_id AND old.valid_to IS NULL)`,
    [revision, JSON.stringify(rows)],
  );
}
module.exports = {
  SCOPE_VERSION,
  MODEL_VERSION,
  sourcePredicate,
  cachedPredicate,
  initializeScope,
  publishScope,
  saveScopeInventory,
  synchronizeScopeInventory,
  inventoryPredicate,
};
