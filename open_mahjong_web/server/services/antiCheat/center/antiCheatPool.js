"use strict";

const { Pool, types } = require("pg");
const config = require("../../../config/config");

// Match web database.js DATE formatting (YYYY-MM-DD string).
types.setTypeParser(1082, (val) => val);

/**
 * Dedicated low-footprint PostgreSQL pool for background anti-cheat workers.
 * Caps connections at max=3 (configurable up to 6) and sets session-level
 * guardrails so offline analysis never starves live game/web pools.
 */
function createAntiCheatPool(overrides = {}) {
  const max = Math.max(
    1,
    Math.min(
      6,
      Number(overrides.max || process.env.ANTI_CHEAT_PG_POOL_MAX) || 3,
    ),
  );
  const statementTimeoutMs = Math.max(
    5000,
    Number(
      overrides.statementTimeoutMs || process.env.ANTI_CHEAT_PG_STATEMENT_TIMEOUT_MS,
    ) || 30000,
  );
  const workMem = String(
    overrides.workMem || process.env.ANTI_CHEAT_PG_WORK_MEM || "16MB",
  ).replace(/[^0-9A-Za-z]/g, "") || "16MB";

  const pool = new Pool({
    host: config.db.host,
    user: config.db.user,
    password: config.db.password,
    database: config.db.database,
    port: config.db.port,
    max,
    idleTimeoutMillis: 30000,
    connectionTimeoutMillis: 5000,
    application_name: overrides.applicationName || "anti_cheat_worker",
  });

  pool.on("connect", (client) => {
    client
      .query(
        `SET statement_timeout = ${Math.floor(statementTimeoutMs)}; SET work_mem = '${workMem}';`,
      )
      .catch(() => {});
  });

  pool.on("error", (err) => {
    console.error("anti-cheat PG pool idle client error:", err);
  });

  return pool;
}

module.exports = { createAntiCheatPool };
