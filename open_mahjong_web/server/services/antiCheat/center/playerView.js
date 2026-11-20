"use strict";
const { SCHEMA:S,fail }=require("./database");
const {VERSION:MODEL_VERSION}=require("./populationPipeline");
const {CHANNELS,optionsOf,present,sortingValue,relationQuery}=require("./playerQuery");
const {createPlayerRanking}=require("./playerRanking");
const ALL_RULES="__ALL__";
function channelOf(q){return optionsOf(q).channel;}
function createPlayerView({pool,meta,pagination,windowDays}) {
  const ranking=createPlayerRanking({pool});
  async function listPlayers(q={}) {
    const o=optionsOf(q),m=await meta(),p=pagination(q),days=windowDays(q);
    if(!m.active_generation||m.model_version!==MODEL_VERSION)return {items:[],total:0,page:p.page,page_size:p.page_size,model_ready:false};
    return ranking.list(m,o,p,days,q.rule_key||ALL_RULES,q.q);
  }

  async function player(id,q={}) {
    if(typeof id!=="string"||id.length>120)fail("玩家标识无效");
    const o=optionsOf(q),m=await meta(),p=pagination(q),days=windowDays(q);
    if(!m.active_generation||m.model_version!==MODEL_VERSION)fail("统计尚未更新",409);
    const pools=(await pool.query(`SELECT payload,rule_key FROM ${S}.published_players WHERE generation=$1 AND window_days=$2 AND user_id=$3 ORDER BY rule_key`,[m.active_generation,days,id])).rows;
    const rule=q.rule_key||ALL_RULES,selected=pools.find(r=>r.rule_key===rule);
    if(!selected)fail("该窗口没有玩家数据",404);
    const query=relationQuery(m.active_generation,days,rule,o,id);
    const where=[`a.donor_id=${query.user}`];
    if(q.q){const search=query.bind('%'+String(q.q).trim().slice(0,120)+'%');where.push(`(a.recipient_id ILIKE ${search} OR a.recipient_name ILIKE ${search})`);}
    // Each row in source represents one direction. Pair up only this player's two
    // directions before sorting, then fetch the full channel metrics for the page.
    const qualify = (direction, metric) => {
      if (!o.filtered || (o.direction !== "any" && o.direction !== direction)) return metric;
      return `CASE WHEN ${query.predicate(metric)} THEN ${metric} END`;
    };
    const feeding = qualify("feeding", `(a.payload->'feeding'->${query.channel})`);
    const receiving = qualify("receiving", `(b.payload->'receiving'->${query.channel})`);
    let match = "TRUE";
    if (o.filtered) {
      const conditions = [];
      if (o.direction !== "receiving") conditions.push("feeding IS NOT NULL");
      if (o.direction !== "feeding") conditions.push("receiving IS NOT NULL");
      match = conditions.join(" OR ");
    }
    // Published player summaries contain the exact unfiltered rate representatives.
    const usePublished = o.sort_by === "rate" && !o.filtered;
    const summarySelect = usePublished ? "NULL::jsonb" : "(SELECT channels FROM summaries LIMIT 1)";
    const order = `sort_value ${o.order} NULLS LAST, tie_total DESC NULLS LAST, shared_games DESC, user_id`;
    const result = (await pool.query(`${query.cte},
      partner_metrics AS (
        SELECT a.recipient_id AS user_id, a.shared_games, ${feeding} AS feeding, ${receiving} AS receiving
        FROM source a LEFT JOIN source b ON b.donor_id=a.recipient_id AND b.recipient_id=a.donor_id
        WHERE ${where.join(" AND ")}
      ), candidates AS MATERIALIZED (
        SELECT user_id, shared_games, ${sortingValue("feeding","receiving",o)} AS sort_value,
          GREATEST((feeding->>'total')::bigint,(receiving->>'total')::bigint) AS tie_total
        FROM partner_metrics WHERE ${match}
      ), page_rows AS (
        SELECT * FROM candidates ORDER BY ${order} LIMIT ${query.bind(p.page_size)} OFFSET ${query.bind(p.offset)}
      ), totals AS (
        SELECT count(*)::int AS total, ${summarySelect} AS summary_channels FROM candidates
      )
      SELECT totals.*, page_rows.*, a.recipient_name AS name, (a.payload->>'recipient_games')::int AS games,
        a.payload->'feeding' AS all_feeding, b.payload->'receiving' AS all_receiving
      FROM totals LEFT JOIN page_rows ON TRUE
      LEFT JOIN source a ON a.donor_id=${query.user} AND a.recipient_id=page_rows.user_id
      LEFT JOIN source b ON b.recipient_id=${query.user} AND b.donor_id=page_rows.user_id
      ORDER BY page_rows.sort_value ${o.order} NULLS LAST, page_rows.tie_total DESC NULLS LAST,
        page_rows.shared_games DESC, page_rows.user_id`,query.args)).rows;
    const total = result[0].total;
    const selectedSummary = (usePublished ? selected.payload.channels : result[0].summary_channels) || {};
    const rows = result.filter(row => row.user_id !== null).map(row => {
      const channels = {};
      for (const channel of CHANNELS) {
        const feeding = row.all_feeding?.[channel] ?? null;
        const receiving = row.all_receiving?.[channel] ?? null;
        const rates = [feeding?.rate, receiving?.rate].filter(Number.isFinite);
        channels[channel] = { feeding, receiving, rank_rate: rates.length ? Math.max(...rates) : null };
      }
      return {user_id:row.user_id, name:row.name, games:row.games, rule_key:rule,
        shared_games:row.shared_games, sort_value:row.sort_value, channels};
    });
    const selectedPayload={...selected.payload,channels:selectedSummary};
    const summary=selectedSummary[o.channel]||{};
    const chosen=o.direction==='feeding'?[summary.feeding]:o.direction==='receiving'?[summary.receiving]:[summary.feeding,summary.receiving];
    const numbers=chosen.map(v=>v?.[o.sort_by]).filter(Number.isFinite);
    const sortValue=numbers.length?(o.sort_by==='occurrence_probability'?Math.min(...numbers):Math.max(...numbers)):null;
    return {player:present(selectedPayload,o.channel,sortValue),selected_rule_key:rule,
      rule_pools:pools.filter(r=>r.rule_key!==ALL_RULES).map(r=>present(r.payload,o.channel)),
      partners:{items:rows.map(row=>present(row,o.channel,row.sort_value)),total,page:p.page,page_size:p.page_size},as_of:m.as_of};
  }
  return {listPlayers,player};
}
module.exports={createPlayerView,channelOf,present,ALL_RULES};
