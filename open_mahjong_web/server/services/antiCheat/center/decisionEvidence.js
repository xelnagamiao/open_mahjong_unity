"use strict";
const S = "anti_cheat_center";
const VERSION = "decision-evidence-v1";
const empty = () => ({ with_nonhelpful_alternative: 0, sole_legal_candidate: 0, all_candidates_help: 0, unknown: 0, sampled_events: 0 });
async function initializeDecisionFacts(pool) {
  await pool.query(`CREATE TABLE IF NOT EXISTS ${S}.decision_facts(
    game_id text NOT NULL REFERENCES ${S}.games(game_id) ON DELETE CASCADE,
    pair_id text NOT NULL,payload jsonb NOT NULL,PRIMARY KEY(game_id,pair_id));
    CREATE INDEX IF NOT EXISTS ac_decision_facts_pair ON ${S}.decision_facts(pair_id,game_id);`);
}
async function ensureDecisionFacts(pool, gameIds, options={}) {
  await initializeDecisionFacts(pool);
  if (gameIds !== undefined && !Array.isArray(gameIds)) throw new TypeError("gameIds_must_be_array");
  const requested = gameIds === undefined ? null : [...new Set(gameIds.map(String))];
  if (requested?.length === 0) return { games: 0, pair_rows: 0, version: VERSION };
  let processed = 0, pairs = 0, cursor = "";
  const zeros = Object.fromEntries(["ALL", "MELD", "RON"].map((channel) => [channel, empty()]));
  while (true) {
    await options.cancelled?.();
    const ids = (await pool.query(`SELECT g.game_id FROM ${S}.games g
      LEFT JOIN ${S}.decision_facts d ON d.game_id=g.game_id AND d.pair_id=''
      WHERE ($1::text[] IS NULL OR g.game_id=ANY($1)) AND g.game_id>$2
        AND (d.game_id IS NULL OR d.payload->>'version' IS DISTINCT FROM $3)
      ORDER BY g.game_id LIMIT 50`, [requested, cursor, VERSION])).rows.map((r) => r.game_id);
    if (!ids.length) break;
    cursor = ids[ids.length - 1];
    const client = await pool.connect();
    try {
      await client.query("BEGIN");
      await client.query(`DELETE FROM ${S}.decision_facts WHERE game_id=ANY($1::text[])`, [ids]);
      // Only the minimal cached JSON fields enter the aggregate. No replay blobs
      // or arrays of full events are transferred to the worker process.
      const inserted = await client.query(`WITH minimal AS (
        SELECT game_id,pair_id,payload->>'family' AS family,payload->'tile' AS tile,
          CASE WHEN jsonb_typeof(payload->'legal_choices')='array' THEN payload->'legal_choices' ELSE '[]'::jsonb END AS legal,
          CASE WHEN jsonb_typeof(payload->'helpful_choices')='array' THEN payload->'helpful_choices' ELSE '[]'::jsonb END AS helpful,
          jsonb_typeof(payload->'legal_choices')='array' AND jsonb_typeof(payload->'helpful_choices')='array' AS arrays_known
        FROM ${S}.evidence WHERE game_id=ANY($1::text[]) AND payload->>'family' IN ('MELD','RON')
      ), classified AS (
        SELECT game_id,pair_id,family,CASE
          WHEN NOT COALESCE(arrays_known,false) OR jsonb_typeof(tile) IS DISTINCT FROM 'number'
            OR NOT legal @> jsonb_build_array(tile) OR NOT helpful @> jsonb_build_array(tile)
            OR EXISTS(SELECT 1 FROM jsonb_array_elements(legal||helpful) v WHERE jsonb_typeof(v)<>'number') THEN 'unknown'
          WHEN (SELECT count(DISTINCT v) FROM jsonb_array_elements(legal) v)=1 THEN 'sole_legal_candidate'
          WHEN helpful @> legal THEN 'all_candidates_help'
          ELSE 'with_nonhelpful_alternative' END AS kind
        FROM minimal
      ), counts AS (
        SELECT game_id,pair_id,c.channel,jsonb_build_object(
          'with_nonhelpful_alternative',count(*) FILTER(WHERE kind='with_nonhelpful_alternative'),
          'sole_legal_candidate',count(*) FILTER(WHERE kind='sole_legal_candidate'),
          'all_candidates_help',count(*) FILTER(WHERE kind='all_candidates_help'),
          'unknown',count(*) FILTER(WHERE kind='unknown'),'sampled_events',count(*)) AS counts
        FROM classified CROSS JOIN (VALUES('ALL'),('MELD'),('RON')) c(channel)
        WHERE c.channel='ALL' OR c.channel=family GROUP BY game_id,pair_id,c.channel
      ) INSERT INTO ${S}.decision_facts(game_id,pair_id,payload)
        SELECT game_id,pair_id,jsonb_build_object('version',$3::text,'channels',$2::jsonb||jsonb_object_agg(channel,counts),
          'scope','cached_successful_event_evidence_only_not_all_decisions') FROM counts GROUP BY game_id,pair_id`, [ids, JSON.stringify(zeros), VERSION]);
      pairs += inserted.rowCount;
      await client.query(`INSERT INTO ${S}.decision_facts(game_id,pair_id,payload)
        SELECT game_id,'',jsonb_build_object('version',$2::text,'complete',true)
        FROM ${S}.games WHERE game_id=ANY($1::text[]) ON CONFLICT(game_id,pair_id) DO UPDATE SET payload=excluded.payload`, [ids, VERSION]);
      await client.query("COMMIT");
      processed += ids.length;
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally { client.release(); }
    await options.progress?.({games:processed,pair_rows:pairs});
  }
  return { games: processed, pair_rows: pairs, version: VERSION };
}
module.exports = { VERSION, initializeDecisionFacts, ensureDecisionFacts };
