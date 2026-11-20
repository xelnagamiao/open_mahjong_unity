"use strict";
const { SCHEMA: S, fail } = require("./database");
const CHANNELS=["ALL","MELD","RON"];
const FIELDS=["rate","occurrence_probability","coplay_rate"];
function optionsOf(q={}) {
  function choice(name,allowed,fallback){const v=q[name]===undefined?fallback:q[name];if(!allowed.includes(v))fail(`筛选参数无效: ${name}`);return v;}
  const channel=choice("channel",CHANNELS,"ALL"),direction=choice("direction",["any","feeding","receiving"],"any");
  const sort_by=choice("sort_by",FIELDS,"rate"),filter_by=choice("filter_by",FIELDS,sort_by);
  const order=choice("order",["asc","desc"],"desc"),anomaly=choice("anomaly",["all","probability"],"all");
  function bound(name){if(q[name]===undefined)return null;const raw=q[name];if((typeof raw!=="string"&&typeof raw!=="number")||String(raw).trim()==="")fail(`筛选参数无效: ${name}`);const v=Number(raw);if(!Number.isFinite(v)||v<0||v>1)fail(`筛选参数无效: ${name}`);return v;}
  const min_value=bound("min_value"),max_value=bound("max_value");
  if(min_value!==null&&max_value!==null&&min_value>max_value)fail("筛选下限不能大于上限");
  return {channel,direction,sort_by,filter_by,order,anomaly,min_value,max_value,filtered:min_value!==null||max_value!==null||anomaly!=="all"};
}
function emptyChannels(){return Object.fromEntries(CHANNELS.map(c=>[c,{feeding:null,receiving:null,rank_rate:null}]));}
function present(payload,channel,sortValue=null){
  const {score,composite,channels,...identity}=payload;
  const all={...emptyChannels(),...channels};
  return {...identity,channels:all,channel,...all[channel],sort_value:sortValue};
}
function sortingValue(feeding,receiving,o){
  const field=o.sort_by;
  if(o.direction==="feeding")return `((${feeding})->>'${field}')::double precision`;
  if(o.direction==="receiving")return `((${receiving})->>'${field}')::double precision`;
  const fn=field==="occurrence_probability"?"LEAST":"GREATEST";
  return `${fn}(((${feeding})->>'${field}')::double precision,((${receiving})->>'${field}')::double precision)`;
}
function relationQuery(generation,days,rule,o,userId=null,search=null,plan={}){
  const args=[generation,days,rule],bind=v=>{args.push(v);return '$'+args.length;};
  const channel=bind(o.channel);
  const user=userId===null?null:bind(userId);
  const users=plan.users?bind(plan.users):null;
  const searchParam=search===null?null:bind('%'+String(search).trim().slice(0,120)+'%');
  const searchCte=searchParam?`requested_users AS MATERIALIZED (SELECT user_id FROM ${S}.published_players WHERE generation=$1 AND window_days=$2 AND rule_key=$3 AND (user_id ILIKE ${searchParam} OR payload->>'name' ILIKE ${searchParam})),`:'';
  const edgeScope=column=>user?` AND ${column}=${user}`:users?` AND ${column}=ANY(${users}::text[])`:searchParam?` AND ${column} IN(SELECT user_id FROM requested_users)`:'';
  const applicable=o.direction==="any"?"TRUE":`e.direction='${o.direction}'`;
  const minimum=o.min_value===null?null:bind(o.min_value);
  const maximum=o.max_value===null?null:bind(o.max_value);
  function predicate(metric){
    const conditions=[],field=`(${metric}->>'${o.filter_by}')::double precision`;
    if(minimum!==null)conditions.push(`${field}>=${minimum}`);
    if(maximum!==null)conditions.push(`${field}<=${maximum}`);
    if(o.anomaly==="probability")conditions.push(`(${metric}->>'occurrence_probability')::double precision<=0.05`);
    return conditions.length?conditions.join(' AND '):"TRUE";
  }
  const condition=predicate('e.metric');
  const scope=`e.channel=${channel} AND ${applicable}`;
  const strongOrder=o.sort_by==="occurrence_probability"?"ASC":"DESC";
  const projection=plan.selectedOnly?`donor_id,recipient_id,donor_name,recipient_name,rule_key,pair_key,shared_games,
    payload->'feeding'->${channel} AS feeding_metric,payload->'receiving'->${channel} AS receiving_metric`:'*';
  const expand=direction=>plan.selectedOnly?`(SELECT ${channel}::text AS key,${direction}_metric AS value)`:`jsonb_each(payload->'${direction}')`;
  const cte=`WITH ${searchCte} source AS MATERIALIZED (
    SELECT ${projection} FROM ${S}.published_pairs WHERE generation=$1 AND window_days=$2 AND rule_key=$3
    ${user?`AND (donor_id=${user} OR recipient_id=${user})`:users?`AND (donor_id=ANY(${users}::text[]) OR recipient_id=ANY(${users}::text[]))`:searchParam?`AND (donor_id IN(SELECT user_id FROM requested_users) OR recipient_id IN(SELECT user_id FROM requested_users))`:""}
  ), edges AS MATERIALIZED (
    SELECT donor_id AS user_id,recipient_id AS partner_id,recipient_name AS partner_name,rule_key,pair_key,shared_games,
      'feeding'::text AS direction,c.key AS channel,c.value AS metric
    FROM source CROSS JOIN LATERAL ${expand('feeding')} c WHERE c.key IN ('ALL','MELD','RON')${edgeScope('donor_id')}
    UNION ALL
    SELECT recipient_id,donor_id,donor_name,rule_key,pair_key,shared_games,'receiving',c.key,c.value
    FROM source CROSS JOIN LATERAL ${expand('receiving')} c WHERE c.key IN ('ALL','MELD','RON')${edgeScope('recipient_id')}
  ), matching AS (SELECT DISTINCT e.user_id FROM edges e WHERE ${scope} AND ${condition}),
  eligible AS (SELECT e.* FROM edges e WHERE NOT(${scope}) OR (${condition})),
  ranked AS (
    SELECT e.*,row_number() OVER(PARTITION BY user_id,channel,direction ORDER BY
      (metric->>'${o.sort_by}')::double precision ${strongOrder} NULLS LAST,
      (metric->>'total')::bigint DESC NULLS LAST,shared_games DESC,partner_id,pair_key) AS position
    FROM eligible e WHERE metric->>'rate' IS NOT NULL
  ), direction_summaries AS (
    SELECT user_id,channel,jsonb_object_agg(direction,metric||jsonb_build_object(
      'partner_id',partner_id,'partner_name',partner_name,'rule_key',rule_key,'pair_key',pair_key)) AS metrics
    FROM ranked WHERE position=1 GROUP BY user_id,channel
  ), summaries AS (
    SELECT user_id,jsonb_object_agg(channel,jsonb_build_object('feeding',metrics->'feeding','receiving',metrics->'receiving',
      'rank_rate',GREATEST((metrics#>>'{feeding,rate}')::double precision,(metrics#>>'{receiving,rate}')::double precision))) AS channels
    FROM direction_summaries GROUP BY user_id
  )`;
  return {args,bind,cte,channel,user,condition,predicate,applicable};
}
module.exports={CHANNELS,optionsOf,present,sortingValue,relationQuery};
