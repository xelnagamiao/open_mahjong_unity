"use strict";

const { SCHEMA, fail, pairId, readGame } = require("./database");
const { VERSION } = require("./populationPipeline");

function createRobertSource(pool) {
  async function findGames(query, snapshot) {
    if (!snapshot.active_generation || snapshot.model_version !== VERSION) {
      fail("请先更新国标段位场统计再复核", 409);
    }
    const days = query.window_days;
    const rule = query.rule_key;
    if (!snapshot.settings.windows_days.includes(days)) fail("统计窗口不存在", 400);
    const cutoff = days
      ? new Date(new Date(snapshot.as_of).getTime() - days * 86400000)
      : null;
    // A review and its ranking must refer to the same published set of games.
    const { rows } = await pool.query(
      `SELECT manifest.game_id, manifest.source_hash
       FROM ${SCHEMA}.pairs p
       JOIN ${SCHEMA}.published_scope_games manifest
         ON manifest.game_id=p.game_id AND manifest.generation=$1
       WHERE p.donor_id=$2 AND p.recipient_id=$3
         AND ($4='__ALL__' OR p.rule_key=$4)
         AND ($5::timestamptz IS NULL OR p.created_at >= $5)
         AND p.created_at <= $6
       ORDER BY p.created_at, p.game_id`,
      [snapshot.active_generation, query.donor_id, query.recipient_id, rule, cutoff, snapshot.as_of],
    );
    const expected = await pool.query(
      `SELECT shared_games FROM ${SCHEMA}.published_pairs
       WHERE generation=$1 AND window_days=$2 AND rule_key=$3 AND pair_id=$4`,
      [snapshot.active_generation, days, rule, pairId(rule, query.donor_id, query.recipient_id)],
    );
    if (!expected.rowCount) fail("所选范围内没有双方同桌的国标段位场", 404);
    if (rows.length !== expected.rows[0].shared_games) {
      fail("同桌记录已变化，请更新国标段位场统计后重新复核", 409);
    }
    return rows;
  }

  async function readRecord(entry) {
    const row = await readGame(pool, entry.game_id);
    if (!row || row.source_hash !== entry.source_hash) {
      fail("该牌谱已变更，请更新国标段位场统计后重新复核", 409);
    }
    return row;
  }
  return { findGames, readRecord };
}

module.exports = { createRobertSource };
