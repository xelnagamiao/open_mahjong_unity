"use strict";
// Explicit local maintenance only. No request handler imports/calls this entry.
async function migrateRankedScope(
  pool,
  { progress = async () => {}, asOf } = {},
) {
  const host = pool.options?.host || pool.config?.host;
  if (!["localhost", "127.0.0.1", "::1"].includes(host))
    throw new Error("local_pool_required");
  const { initialize, SCHEMA: S } = require("./database");
  const { LOCK } = require("./processor");
  const { VERSION, refreshStatistics } = require("./populationPipeline");
  await initialize(pool);
  const lease = await pool.connect();
  let locked = false;
  try {
    locked = (
      await lease.query("SELECT pg_try_advisory_lock($1) acquired", [LOCK])
    ).rows[0].acquired;
    if (!locked) throw new Error("local_update_already_running");
    const meta = (await pool.query(`SELECT * FROM ${S}.meta WHERE id=1`))
      .rows[0];
    if (meta.model_version === VERSION && meta.active_generation)
      return { already_current: true, generation: meta.active_generation };
    const generation = require("crypto").randomUUID();
    await refreshStatistics(pool, generation, meta.settings, {
      cancelled: async () => {},
      progress,
      asOf: asOf || meta.as_of || new Date().toISOString(),
    });
    return {
      already_current: false,
      generation,
      version: VERSION,
      reused_cached_facts: true,
    };
  } finally {
    if (locked) await lease.query("SELECT pg_advisory_unlock($1)", [LOCK]);
    lease.release();
  }
}
module.exports = { migrateRankedScope };
