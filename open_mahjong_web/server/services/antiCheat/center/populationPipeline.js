"use strict";
const { SCHEMA: S, pairId } = require("./database");
const { CHANNELS, indexGameFacts, summarizePair, summarizePlayer } = require("./directionalStatistics");
const { publish } = require("./versionStore");
const {MODEL_VERSION:VERSION,inventoryPredicate,synchronizeScopeInventory}=require("./rankedScope");
const ALL_RULES = "__ALL__";
const key = (rule, uid) => JSON.stringify([rule, String(uid)]);
const iso = value => new Date(value).toISOString();
async function refreshStatistics(pool, generation, config, { cancelled, progress, asOf, scopeScanned = false, pacer } = {}) {
  if(!scopeScanned)await synchronizeScopeInventory(pool);
  const meta = (await pool.query(`SELECT * FROM ${S}.meta WHERE id=1`)).rows[0];
  const dirty = (await pool.query(`SELECT * FROM ${S}.dirty_games`)).rows;
  const full = !meta.active_generation || meta.model_version !== VERSION || meta.model_revision !== config.revision;
  const affected = new Set(dirty.flatMap(r => r.players || []).map(String));
  const games = (await pool.query(
    `SELECT game_id,source_hash,created_at,rule_key,payload FROM ${S}.games g WHERE ${inventoryPredicate("g")} AND status IN ('ready','unsupported') AND created_at IS NOT NULL AND created_at<=$1 ORDER BY created_at,game_id`, [asOf]
  )).rows.map(r => ({ ...r.payload, game_id: String(r.game_id), source_hash:r.source_hash, created_at: iso(r.created_at), rule_key: r.rule_key, pairs: [] }));
  const priorScope=(await pool.query(`SELECT game_id FROM ${S}.scope_game_versions WHERE valid_to IS NULL`)).rows;
  const priorIds=new Set(priorScope.map(g=>g.game_id));
  for(const g of games)if(!priorIds.has(g.game_id))for(const p of g.players||[])affected.add(String(p.user_id));
  const nextIds=new Set(games.map(g=>g.game_id));
  const departed=priorScope.filter(g=>!nextIds.has(g.game_id)).map(g=>g.game_id);
  if(departed.length)for(const row of (await pool.query(`SELECT payload->'players' AS players FROM ${S}.games WHERE game_id=ANY($1::text[])`,[departed])).rows)
    for(const player of row.players||[])affected.add(String(player.user_id));
  const asOfTime = Date.parse(asOf), gameMap = new Map(games.map(g => [g.game_id,g]));
  if (meta.as_of && +new Date(meta.as_of) !== asOfTime)
    for (const g of games) if (config.windows_days.some(d => d > 0 && Date.parse(g.created_at) >= +new Date(meta.as_of)-d*86400000 && Date.parse(g.created_at) < asOfTime-d*86400000))
      for (const p of g.players || []) affected.add(String(p.user_id));
  // Only immutable extracted counters are needed; no replay, model fit, or points prediction.
  const fields = CHANNELS.map(c => `'${c}',jsonb_build_object('events',payload#>'{channels,${c},events}','received',payload#>'{channels,${c},received}','available',payload#>'{channels,${c},available}')`).join(',');
  const raw = (await pool.query(
    `SELECT game_id,pair_id,rule_key,donor_id,recipient_id,created_at,jsonb_build_object('donor_name',payload->'donor_name','recipient_name',payload->'recipient_name','delivery_supported',payload->'delivery_supported','channels',jsonb_build_object(${fields})) AS payload FROM ${S}.pairs WHERE game_id=ANY($1::text[]) ORDER BY created_at,game_id,pair_id`,[games.map(g=>g.game_id)]
  )).rows;
  const allGroups = new Map(), users = new Map();
  for (const g of games) for (const p of g.players || []) for (const rule of [g.rule_key,ALL_RULES]) {
    const k = key(rule,p.user_id);
    if (!users.has(k)) users.set(k,[]);
    users.get(k).push({ ...p, game_id:g.game_id, created_at:g.created_at });
  }
  for (const row of raw) {
    const g = gameMap.get(String(row.game_id));
    if (!g) continue;
    const sample = { ...row.payload, donor_id:String(row.donor_id), recipient_id:String(row.recipient_id), game_id:g.game_id, created_at:g.created_at };
    g.pairs.push(sample);
    for (const rule of [row.rule_key,ALL_RULES]) {
      const pid = pairId(rule,sample.donor_id,sample.recipient_id);
      if (!allGroups.has(pid)) allGroups.set(pid,{ meta:{ ...sample,rule_key:rule }, samples:[] });
      allGroups.get(pid).samples.push(sample);
    }
  }
  const gameIndex = indexGameFacts(games), targetIds = new Set(), affectedPlayers = new Set(affected);
  const groups = new Map();
  for (const [pid,g] of allGroups) if (full || affected.has(g.meta.donor_id) || affected.has(g.meta.recipient_id)) {
    targetIds.add(pid); groups.set(pid,g);
    affectedPlayers.add(g.meta.donor_id); affectedPlayers.add(g.meta.recipient_id);
  }
  if (!full) for (const old of (await pool.query(
    `SELECT DISTINCT pair_id,donor_id,recipient_id FROM ${S}.published_pairs WHERE generation=$1 AND (donor_id=ANY($2::text[]) OR recipient_id=ANY($2::text[]))`,
    [meta.active_generation,[...affected]]
  )).rows) {
    targetIds.add(old.pair_id); affectedPlayers.add(String(old.donor_id)); affectedPlayers.add(String(old.recipient_id));
  }
  let written=0, done=0;
  const pending=[];
  const flushPairs=async()=>{
    if (!pending.length) return;
    await pool.query(`INSERT INTO ${S}.pair_statistics(generation,window_days,pair_id,pair_key,donor_id,recipient_id,donor_name,recipient_name,rule_key,shared_games,score,flag_all,flag_meld,flag_ron,payload) SELECT $1,x.window_days,x.pair_id,x.pair_key,x.donor_id,x.recipient_id,x.donor_name,x.recipient_name,x.rule_key,x.shared_games,x.score,x.flag_all,x.flag_meld,x.flag_ron,x.payload FROM jsonb_to_recordset($2::jsonb) x(window_days integer,pair_id text,pair_key text,donor_id text,recipient_id text,donor_name text,recipient_name text,rule_key text,shared_games integer,score float,flag_all boolean,flag_meld boolean,flag_ron boolean,payload jsonb)`,[generation,JSON.stringify(pending.splice(0))]);
    if (pacer) await pacer.paceDbBatch();
  };
  for (const days of config.windows_days) {
    await cancelled();
    const cutoff = days ? asOfTime-days*86400000 : -Infinity;
    const inside = r => Date.parse(r.created_at)>=cutoff && Date.parse(r.created_at)<=asOfTime;
    const history = new Map([...users].map(([k,rows])=>[k,rows.filter(inside)]));
    for (const [pid,{meta:m,samples:all}] of groups) {
      if (++done%250===0) { await cancelled(); await progress('statistics',{processed:done,total:groups.size*config.windows_days.length,message:`正在汇总喂牌与收牌 ${done} / ${groups.size*config.windows_days.length}`}); }
      const samples=all.filter(inside);
      if (!samples.length) continue;
      const payload=summarizePair(samples,gameIndex,{
        pair_id:pid,pair_key:`${days}:${pid}`,window_days:days,donor_id:m.donor_id,recipient_id:m.recipient_id,
        donor_name:samples.at(-1).donor_name||m.donor_id,recipient_name:samples.at(-1).recipient_name||m.recipient_id,
        rule_key:m.rule_key,donor_games:(history.get(key(m.rule_key,m.donor_id))||[]).length,
        recipient_games:(history.get(key(m.rule_key,m.recipient_id))||[]).length,model_version:VERSION,as_of:asOf
      });
      const rates=[payload.feeding.ALL.rate,payload.receiving.ALL.rate].filter(Number.isFinite);
      pending.push({ ...payload,score:rates.length?Math.max(...rates):null,
        flag_all:payload.feeding.ALL.flag||payload.receiving.ALL.flag,
        flag_meld:payload.feeding.MELD.flag||payload.receiving.MELD.flag,
        flag_ron:payload.feeding.RON.flag||payload.receiving.RON.flag,payload });
      written++;
      if (pending.length>=250) await flushPairs();
    }
    await flushPairs();
    const staged=(await pool.query(`SELECT payload FROM ${S}.pair_statistics WHERE generation=$1 AND window_days=$2`,[generation,days])).rows;
    const unchanged=full?[]:(await pool.query(`SELECT payload FROM ${S}.published_pairs WHERE generation=$1 AND window_days=$2 AND NOT(pair_id=ANY($3::text[])) AND (donor_id=ANY($4::text[]) OR recipient_id=ANY($4::text[]))`,[meta.active_generation,days,[...targetIds],[...affectedPlayers]])).rows;
    const perUser=new Map();
    for (const {payload:r} of [...staged,...unchanged]) for (const uid of [r.donor_id,r.recipient_id]) {
      if (!full&&!affectedPlayers.has(uid)) continue;
      const k=key(r.rule_key,uid);
      if (!perUser.has(k)) perUser.set(k,[]);
      perUser.get(k).push(r);
    }
    const playerRows=[];
    for (const [k,hist] of history) {
      const [rule,uid]=JSON.parse(k);
      if (!hist.length || (!full&&!affectedPlayers.has(uid))) continue;
      const payload=summarizePlayer({user_id:uid,name:hist.at(-1).name||uid,rule_key:rule,window_days:days,games:hist.length,
        relations:perUser.get(k)||[],model_version:VERSION,as_of:asOf});
      playerRows.push({user_id:uid,rule_key:rule,score:payload.rank_rate,payload});
    }
    for (let n=0;n<playerRows.length;n+=250) {
      await pool.query(`INSERT INTO ${S}.player_pool_statistics(generation,window_days,user_id,rule_key,score,payload) SELECT $1,$2,x.user_id,x.rule_key,x.score,x.payload FROM jsonb_to_recordset($3::jsonb) x(user_id text,rule_key text,score float,payload jsonb)`,[generation,days,JSON.stringify(playerRows.slice(n,n+250))]);
      if (pacer) await pacer.paceDbBatch();
    }
  }
  await cancelled();
  await publish(pool,{scopeGames:games,generation,full,pairIds:[...targetIds],playerIds:[...affectedPlayers],predictionGameIds:[],modelId:null,config,version:VERSION,asOf,
    audit:{settings_revision:config.revision,method:'fixed_one_third_inclusive_binomial_tail',interval_method:'clopper_pearson_one_sided_95',baseline:1/3,affected_relations:targetIds.size,
      written_relation_windows:written,affected_players:affectedPlayers.size,incremental:!full,legacy_predictions_retired:full}});
}
module.exports={VERSION,ALL_RULES,refreshStatistics};
