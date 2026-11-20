"use strict";
const { SCHEMA: S } = require("./database");
const {
  rankProfilesReady,
  readProfileSummaries,
} = require("./playerRankProfiles");
const { present, relationQuery, sortingValue } = require("./playerQuery");
// Generation-scoped bounded caches: ranking stores only IDs and two scalars.
// Page cache contains at most 16 requested pages, never a platform JSON export.
function createPlayerRanking({ pool }) {
  let generation = null,
    indexedGeneration = null,
    rankingCells = 0;
  const rankings = new Map(),
    pages = new Map(),
    inflight = new Map(),
    rankingInflight = new Map();
  const put = (cache, key, value, cap) => {
    while (cache.size >= cap) cache.delete(cache.keys().next().value);
    cache.set(key, value);
  };
  async function compute(m, o, p, days, rule, search) {
    if (generation !== m.active_generation) {
      generation = m.active_generation;
      rankings.clear();
      pages.clear();
      rankingCells = 0;
    }
    const token = generation;
    const normalizedSearch = search
      ? String(search).trim().slice(0, 120)
      : null;
    const rankingOptions = { ...o, order: "desc" };
    // A direction's minimum probability satisfies an upper bound iff at least
    // one of its relationships does. Positive lower bounds do not share this
    // property and must retain the full relationship query.
    const probabilityCeiling = o.filtered &&
      (o.direction === "feeding" || o.direction === "receiving") &&
      o.sort_by === "occurrence_probability" &&
      o.filter_by === "occurrence_probability" &&
      (o.min_value === null || o.min_value === 0) &&
      (o.anomaly === "all" || o.anomaly === "probability");
    const usesProfiles = (!o.filtered && o.sort_by !== "rate") || probabilityCeiling;
    if (usesProfiles && indexedGeneration !== token && await rankProfilesReady(pool, token)) {
      indexedGeneration = token;
    }
    const indexed = usesProfiles && indexedGeneration === token;
    const cacheKey = JSON.stringify([token, days, rule, normalizedSearch, rankingOptions, indexed]);
    const pageKey = JSON.stringify([
      cacheKey,
      o.order,
      p.page,
      p.page_size,
    ]);
    if (pages.has(pageKey)) return structuredClone(pages.get(pageKey));
    let ranked = rankings.get(cacheKey);
    const fast = o.sort_by === "rate" && !o.filtered;
    if (!ranked) {
      const args = [token, days, rule, o.channel];
      const searchSql = normalizedSearch
        ? "AND (user_id ILIKE $5 OR payload->>'name' ILIKE $5)"
        : "";
      if (normalizedSearch) args.push("%" + normalizedSearch + "%");
      const base = `SELECT ${fast ? "user_id,payload" : "user_id"} FROM ${S}.published_players WHERE generation=$1 AND window_days=$2 AND rule_key=$3 ${searchSql}`;
      let sql;
      if (indexed) {
        const fp = `r.profiles->($4::text || ':feeding:${o.sort_by}')`,
          rp = `r.profiles->($4::text || ':receiving:${o.sort_by}')`;
        const value = (part) => `(${part}->>'value')::float`;
        const sort =
          o.direction === "feeding"
            ? value(fp)
            : o.direction === "receiving"
              ? value(rp)
              : `${o.sort_by === "occurrence_probability" ? "LEAST" : "GREATEST"}(${value(fp)},${value(rp)})`;
        sql = `WITH base AS MATERIALIZED (${base}) SELECT b.user_id,${sort} AS sort_value,
          GREATEST((${fp}->>'total')::bigint,(${rp}->>'total')::bigint) AS tie_total
          FROM base b JOIN ${S}.stat_revisions snapshot ON snapshot.generation=$1 LEFT JOIN ${S}.player_rank_profiles r
          ON r.window_days=$2 AND r.rule_key=$3 AND r.user_id=b.user_id AND r.valid_from<=snapshot.revision AND (r.valid_to IS NULL OR r.valid_to>snapshot.revision)`;
        if (probabilityCeiling) {
          const upper = o.anomaly === "probability"
            ? Math.min(o.max_value ?? 1, 0.05)
            : o.max_value ?? 1;
          args.push(upper);
          sql += ` WHERE ${sort}>=0 AND ${sort}<=$${args.length}`;
        }
      } else if (fast) {
        const f = "payload->'channels'->$4->'feeding'",
          r = "payload->'channels'->$4->'receiving'";
        sql = `SELECT user_id,${sortingValue(f, r, o)} AS sort_value,GREATEST((${f}->>'total')::bigint,(${r}->>'total')::bigint) AS tie_total FROM (${base}) p`;
      } else {
        const bind = (v) => {
          args.push(v);
          return "$" + args.length;
        };
        const scope =
          o.direction === "any" ? "TRUE" : `direction='${o.direction}'`;
        const criteria = [];
        if (o.min_value !== null)
          criteria.push(`${o.filter_by}>=${bind(o.min_value)}`);
        if (o.max_value !== null)
          criteria.push(`${o.filter_by}<=${bind(o.max_value)}`);
        if (o.anomaly === "probability")
          criteria.push("occurrence_probability<=0.05");
        const condition = criteria.length ? criteria.join(" AND ") : "TRUE";
        const needed = new Set(["rate", o.sort_by, o.filter_by]);
        if (o.anomaly === "probability") needed.add("occurrence_probability");
        const scalarColumns = [...needed]
          .map((field) => `(metric->>'${field}')::float AS ${field}`)
          .join(",");
        const aggregate =
          o.sort_by === "occurrence_probability" ? "min" : "max";
        sql = `WITH base AS MATERIALIZED (${base}), raw AS MATERIALIZED (
          SELECT donor_id,recipient_id,pair_key,shared_games,payload->'feeding'->$4 AS f,payload->'receiving'->$4 AS r
          FROM ${S}.published_pairs WHERE generation=$1 AND window_days=$2 AND rule_key=$3
          ${normalizedSearch ? "AND (donor_id IN(SELECT user_id FROM base) OR recipient_id IN(SELECT user_id FROM base))" : ""}
        ), edges AS ${o.filtered ? "MATERIALIZED" : "NOT MATERIALIZED"} (
          SELECT user_id,partner_id,pair_key,shared_games,direction,
          ${scalarColumns},
          (metric->>'total')::bigint AS total FROM raw CROSS JOIN LATERAL
          (VALUES(donor_id,recipient_id,'feeding',f),(recipient_id,donor_id,'receiving',r)) e(user_id,partner_id,direction,metric)
          ${normalizedSearch ? "WHERE user_id IN(SELECT user_id FROM base)" : ""}
        ), matching AS (SELECT DISTINCT user_id FROM edges WHERE ${scope} AND ${condition}), eligible AS MATERIALIZED (
          SELECT user_id,direction,${o.sort_by} AS value,total FROM edges
          WHERE rate IS NOT NULL AND (NOT(${scope}) OR (${condition}))
        ), best_values AS (
          SELECT user_id,direction,${aggregate}(value) AS value FROM eligible GROUP BY user_id,direction
        ), best AS (
          SELECT v.user_id,v.direction,v.value,max(e.total) AS total FROM best_values v JOIN eligible e
          ON e.user_id=v.user_id AND e.direction=v.direction AND e.value IS NOT DISTINCT FROM v.value
          GROUP BY v.user_id,v.direction,v.value
        ), scores AS (
          SELECT user_id,${aggregate}(value) FILTER(WHERE ${scope}) AS sort_value,max(total) AS tie_total FROM best GROUP BY user_id
        ) SELECT b.user_id,s.sort_value,s.tie_total FROM base b LEFT JOIN scores s USING(user_id)
          ${o.filtered ? "WHERE b.user_id IN(SELECT user_id FROM matching)" : ""}`;
      }
      let pendingRanking = rankingInflight.get(cacheKey);
      if (!pendingRanking) {
        pendingRanking = pool
          .query(
            `${sql} ORDER BY sort_value ${rankingOptions.order} NULLS LAST,tie_total DESC NULLS LAST,user_id`,
            args,
          )
          .then((result) => result.rows);
        rankingInflight.set(cacheKey, pendingRanking);
      }
      try {
        ranked = await pendingRanking;
      } finally {
        rankingInflight.delete(cacheKey);
      }
      if (
        token === generation &&
        ranked.length <= 100000 &&
        !rankings.has(cacheKey)
      ) {
        while (
          rankings.size &&
          (rankingCells + ranked.length > 100000 || rankings.size >= 32)
        ) {
          const oldest = rankings.keys().next().value;
          rankingCells -= rankings.get(oldest).length;
          rankings.delete(oldest);
        }
        rankings.set(cacheKey, ranked);
        rankingCells += ranked.length;
      }
    }
    let ordered = ranked;
    if (o.order === "asc") {
      // Reverse value groups, not rows: both orders retain the database's
      // tie_total DESC / user_id ordering and keep all NULL values last.
      const groups = [],
        missing = [];
      for (const row of ranked) {
        if (row.sort_value === null) {
          missing.push(row);
          continue;
        }
        const last = groups.at(-1);
        if (last && last[0].sort_value === row.sort_value) last.push(row);
        else groups.push([row]);
      }
      ordered = groups.reverse().flat().concat(missing);
    }
    const selected = ordered.slice(p.offset, p.offset + p.page_size);
    const items = [];
    if (selected.length) {
      const ids = selected.map((r) => r.user_id);
      const payloads = (
        await pool.query(
          `SELECT user_id,payload FROM ${S}.published_players WHERE generation=$1 AND window_days=$2 AND rule_key=$3 AND user_id=ANY($4::text[])`,
          [token, days, rule, ids],
        )
      ).rows;
      const byId = new Map(payloads.map((r) => [r.user_id, r.payload]));
      if (indexed) {
        const channels = await readProfileSummaries(
          pool,
          token,
          days,
          rule,
          ids,
          o.sort_by,
        );
        for (const [id, payload] of byId)
          payload.channels = channels.get(id) || {};
      } else if (!fast) {
        const detail = relationQuery(token, days, rule, o, null, null, {
          users: ids,
        });
        const summaries = (
          await pool.query(
            `${detail.cte} SELECT user_id,channels FROM summaries`,
            detail.args,
          )
        ).rows;
        const channels = new Map(summaries.map((r) => [r.user_id, r.channels]));
        for (const [id, payload] of byId)
          payload.channels = channels.get(id) || {};
      }
      for (const row of selected)
        if (byId.has(row.user_id))
          items.push(present(byId.get(row.user_id), o.channel, row.sort_value));
    }
    const result = {
      items,
      total: ranked.length,
      model_ready: true,
      page: p.page,
      page_size: p.page_size,
    };
    if (token === generation && p.page_size <= 100)
      put(pages, pageKey, structuredClone(result), 16);
    return result;
  }
  async function list(m, o, p, days, rule, search) {
    const key = JSON.stringify([
      m.active_generation,
      o,
      p.page,
      p.page_size,
      days,
      rule,
      search,
    ]);
    if (inflight.has(key)) return structuredClone(await inflight.get(key));
    const pending = compute(m, o, p, days, rule, search);
    inflight.set(key, pending);
    try {
      return await pending;
    } finally {
      inflight.delete(key);
    }
  }
  return { list };
}
module.exports = { createPlayerRanking };
