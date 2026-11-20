"use strict";
const S = "anti_cheat_center";
const PROFILE_VERSION = 2;
async function initializeRankProfiles(client) {
  await client.query(`CREATE TABLE IF NOT EXISTS ${S}.player_rank_profile_generations(
    generation uuid PRIMARY KEY,version integer NOT NULL,completed_at timestamptz NOT NULL DEFAULT now());
    CREATE TABLE IF NOT EXISTS ${S}.player_rank_profiles(
      generation uuid NOT NULL,window_days integer NOT NULL,rule_key text NOT NULL,user_id text NOT NULL,
      profiles jsonb NOT NULL,valid_from bigint NOT NULL,valid_to bigint,
      PRIMARY KEY(generation,window_days,rule_key,user_id));
    ALTER TABLE ${S}.player_rank_profiles ADD COLUMN IF NOT EXISTS valid_from bigint,
      ADD COLUMN IF NOT EXISTS valid_to bigint;
    CREATE INDEX IF NOT EXISTS ac_rank_profiles_snapshot ON ${S}.player_rank_profiles(window_days,rule_key,valid_from,valid_to,user_id);`);
}
// Called only by explicit publication/backfill, never by a GET reader.
async function buildRankProfiles(
  client,
  generation,
  { previousGeneration = null, playerIds = [], full = true } = {},
) {
  await client.query(
    "SELECT pg_advisory_xact_lock(hashtext('anti_cheat_rank_profiles'))",
  );
  const exists = (
    await client.query(
      `SELECT 1 FROM ${S}.player_rank_profile_generations WHERE generation=$1 AND version=$2`,
      [generation, PROFILE_VERSION],
    )
  ).rows.length;
  if (exists) return { already_complete: true };
  const previous =
    previousGeneration &&
    (
      await client.query(
        `SELECT 1 FROM ${S}.player_rank_profile_generations WHERE generation=$1 AND version=$2`,
        [previousGeneration, PROFILE_VERSION],
      )
    ).rows.length;
  const rebuildAll = full || !previous;
  const revision = (
    await client.query(
      `SELECT revision FROM ${S}.stat_revisions WHERE generation=$1`,
      [generation],
    )
  ).rows[0]?.revision;
  if (revision === undefined) throw new Error("published_revision_required");
  await client.query(
    `UPDATE ${S}.player_rank_profiles SET valid_to=$1 WHERE valid_to IS NULL AND ($2 OR user_id=ANY($3::text[]))`,
    [revision, rebuildAll, playerIds],
  );
  await client.query(
    `WITH players AS MATERIALIZED (
    SELECT window_days,rule_key,user_id FROM ${S}.published_players WHERE generation=$1 AND ($2 OR user_id=ANY($3::text[]))
  ), edges AS MATERIALIZED (
    SELECT p.window_days,p.rule_key,p.pair_id,p.pair_key,p.shared_games,e.user_id,e.partner_id,e.direction,c.key AS channel,c.value AS metric
    FROM ${S}.published_pairs p CROSS JOIN LATERAL
      (VALUES(p.donor_id,p.recipient_id,'feeding',p.payload->'feeding'),(p.recipient_id,p.donor_id,'receiving',p.payload->'receiving')) e(user_id,partner_id,direction,metrics)
    CROSS JOIN LATERAL jsonb_each(e.metrics) c
    WHERE generation=$1 AND ($2 OR p.donor_id=ANY($3::text[]) OR p.recipient_id=ANY($3::text[])) AND ($2 OR e.user_id=ANY($3::text[])) AND c.key IN('ALL','MELD','RON') AND c.value->>'rate' IS NOT NULL
  ), candidates AS (
    SELECT e.*,f.field,(metric->>f.field)::float AS value,(metric->>'total')::bigint AS total,
      row_number() OVER(PARTITION BY window_days,rule_key,user_id,channel,direction,f.field ORDER BY
        CASE WHEN f.field='occurrence_probability' THEN -(metric->>f.field)::float ELSE (metric->>f.field)::float END DESC NULLS LAST,
        (metric->>'total')::bigint DESC NULLS LAST,shared_games DESC,partner_id,pair_key) AS rn
    FROM edges e CROSS JOIN (VALUES('rate'),('occurrence_probability'),('coplay_rate')) f(field)
  ), profiles AS (
    SELECT window_days,rule_key,user_id,jsonb_object_agg(channel||':'||direction||':'||field,
      jsonb_build_object('pair_id',pair_id,'pair_key',pair_key,'value',value,'total',total)) AS profiles
    FROM candidates WHERE rn=1 GROUP BY window_days,rule_key,user_id
  ) INSERT INTO ${S}.player_rank_profiles(generation,window_days,rule_key,user_id,profiles,valid_from)
    SELECT $1,p.window_days,p.rule_key,p.user_id,COALESCE(r.profiles,'{}'::jsonb),$4::bigint
    FROM players p LEFT JOIN profiles r USING(window_days,rule_key,user_id)
    ON CONFLICT(generation,window_days,rule_key,user_id) DO UPDATE SET
      profiles=excluded.profiles,valid_from=excluded.valid_from,valid_to=NULL`,
    [generation, rebuildAll, playerIds, revision],
  );
  await client.query(
    `INSERT INTO ${S}.player_rank_profile_generations(generation,version) VALUES($1,$2)
     ON CONFLICT(generation) DO UPDATE SET version=excluded.version,completed_at=now()`,
    [generation, PROFILE_VERSION],
  );
  return { already_complete: false, full: rebuildAll };
}
async function backfillRankProfiles(pool, generation) {
  const c = await pool.connect();
  try {
    await c.query("BEGIN");
    await c.query(
      `SELECT pg_advisory_xact_lock(hashtext('anti_cheat_rank_profiles'))`,
    );
    await initializeRankProfiles(c);
    const known = (
      await c.query(`SELECT 1 FROM ${S}.meta WHERE active_generation=$1`, [
        generation,
      ])
    ).rows.length;
    if (!known) throw new Error("unknown_published_generation");
    const result = await buildRankProfiles(c, generation);
    await c.query("COMMIT");
    return result;
  } catch (e) {
    await c.query("ROLLBACK");
    throw e;
  } finally {
    c.release();
  }
}
async function rankProfilesReady(pool, generation) {
  const table = (
    await pool.query(
      `SELECT to_regclass('${S}.player_rank_profile_generations') AS name`,
    )
  ).rows[0]?.name;
  if (!table) return false;
  return !!(
    await pool.query(
      `SELECT 1 FROM ${S}.player_rank_profile_generations WHERE generation=$1 AND version=$2`,
      [generation, PROFILE_VERSION],
    )
  ).rows.length;
}
async function readProfileSummaries(
  pool,
  generation,
  days,
  rule,
  users,
  field,
) {
  const pointers = (
    await pool.query(
      `SELECT r.user_id,k.key,k.value->>'pair_id' AS pair_id
    FROM ${S}.stat_revisions snapshot JOIN ${S}.player_rank_profiles r
      ON r.valid_from<=snapshot.revision AND (r.valid_to IS NULL OR r.valid_to>snapshot.revision)
    CROSS JOIN LATERAL jsonb_each(r.profiles) k
    WHERE snapshot.generation=$1 AND r.window_days=$2 AND r.rule_key=$3 AND r.user_id=ANY($4::text[]) AND split_part(k.key,':',3)=$5`,
      [generation, days, rule, users, field],
    )
  ).rows;
  const byUser = new Map();
  if (!pointers.length) return byUser;
  // Bind the actual page's IDs so the snapshot index performs point lookups.
  const ids = [...new Set(pointers.map(pointer => pointer.pair_id))];
  const { rows } = await pool.query(
    `SELECT pair_id, pair_key, donor_id, recipient_id, donor_name, recipient_name,
      payload->'feeding' AS feeding, payload->'receiving' AS receiving
     FROM ${S}.published_pairs
     WHERE generation=$1 AND window_days=$2 AND rule_key=$3 AND pair_id=ANY($4::text[])`,
    [generation, days, rule, ids],
  );
  const pairs = new Map(rows.map(row => [row.pair_id, row]));
  for (const { user_id, key, pair_id } of pointers) {
    const r = pairs.get(pair_id);
    const [channel, direction] = key.split(":");
    if (!byUser.has(user_id)) byUser.set(user_id, {});
    const channels = byUser.get(user_id);
    channels[channel] ||= { feeding: null, receiving: null, rank_rate: null };
    channels[channel][direction] = {
      ...r[direction][channel],
      partner_id: direction === "feeding" ? r.recipient_id : r.donor_id,
      partner_name: direction === "feeding" ? r.recipient_name : r.donor_name,
      rule_key: rule,
      pair_key: r.pair_key,
    };
  }
  for (const channels of byUser.values())
    for (const c of Object.values(channels)) {
      const rates = [c.feeding?.rate, c.receiving?.rate].filter(
        Number.isFinite,
      );
      c.rank_rate = rates.length ? Math.max(...rates) : null;
    }
  return byUser;
}
module.exports = {
  PROFILE_VERSION,
  initializeRankProfiles,
  buildRankProfiles,
  backfillRankProfiles,
  rankProfilesReady,
  readProfileSummaries,
};
