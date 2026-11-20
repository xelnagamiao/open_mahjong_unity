"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const crypto = require("node:crypto");
const source = process.env.CENTER_SOURCE_DIRECTORY || __dirname;
const web = path.resolve(source, "../../../..");
const { Client } = require(require.resolve("pg", { paths: [web] }));
const { initialize, SCHEMA: S } = require(path.join(source, "database"));
const { createPlayerView } = require(path.join(source, "playerView"));
const { pagination, windowDays } = require(path.join(source, "service"));
const { VERSION } = require(path.join(source, "populationPipeline"));
const { metric, summarizePlayer } = require(path.join(source, "directionalStatistics"));
const integration = { skip: process.env.ANTI_CHEAT_INTEGRATION !== "1" };
function assertProbabilityFields(value) {
  assert.equal(Object.hasOwn(value, "abnormality"), false);
  assert.equal(value.occurrence_probability, value.p_value);
  assert.equal(typeof value.occurrence_probability, "number");
  assert.ok(value.occurrence_probability >= 0 && value.occurrence_probability <= 1);
  assert.equal(Object.hasOwn(value,"nist_lower_95"),false);
  assert.equal(value.flag, value.occurrence_probability <= .05);
}

test("read-only full relationship selection finds high-confidence candidates hidden by a 1/1 rate maximum", integration, async t => {
  const { client, view, generation, meta } = await setup(t);
  const rate = await view.listPlayers({q:"player1", direction:"feeding"});
  assert.equal(rate.items[0].feeding.partner_id,"2");
  assert.equal(rate.items[0].feeding.rate,1);
  assert.equal(rate.items[0].feeding.occurrence_probability,1/3);
  const confident = await view.listPlayers({q:"player1", direction:"feeding",sort_by:"occurrence_probability",anomaly:"probability"});
  assert.equal(confident.total,1);
  assert.equal(confident.items[0].feeding.partner_id,"3");
  assert.equal(confident.items[0].feeding.count,80);
  assert.equal(confident.items[0].feeding.total,100);
  assert.ok(confident.items[0].sort_value<.05);
  assert.equal(confident.items[0].rank_rate,.8,"ranking by probability does not replace the displayed actual rate");
  const probability = await view.listPlayers({q:"player1",direction:"feeding",sort_by:"occurrence_probability",anomaly:"probability"});
  assert.equal(probability.items[0].feeding.partner_id,"3");
  assert.equal(probability.items[0].sort_value,probability.items[0].feeding.occurrence_probability);
  assert.equal((await meta()).active_generation,generation);
  assert.equal((await client.query(`SELECT count(*)::int n FROM ${S}.updates`)).rows[0].n,0);
  await client.query("ROLLBACK");
});

test("filters cannot combine the rate of one partner with another partner's or direction's anomaly", integration, async t => {
  const {client,view}=await setup(t);
  const split = {q:"player1",direction:"feeding",filter_by:"rate",min_value:1,max_value:1,anomaly:"probability"};
  assert.equal((await view.listPlayers(split)).total,0);
  assert.equal((await view.player("1",{...split,q:undefined})).partners.total,0);
  // B feeds A at 99%, but A's receipt share from B is only 20%; B's statistic must never satisfy A's filter.
  assert.equal((await view.listPlayers({q:"player1",direction:"receiving",min_value:.9,anomaly:"probability"})).total,0);
  const inclusive={q:"player1",direction:"feeding",filter_by:"rate",min_value:.8,max_value:.8,anomaly:"probability"};
  assert.equal((await view.listPlayers(inclusive)).items[0].feeding.partner_id,"3");
  const detail=await view.player("1",{...inclusive,q:undefined});
  assert.equal(detail.partners.total,1);assert.equal(detail.partners.items[0].user_id,"3");
  assert.equal(detail.partners.items[0].receiving.rate,.8,"unfiltered reverse direction remains visible");
  await client.query("ROLLBACK");
});

test("global query ordering precedes pagination, keeps NULL last and selects representatives independently of order", integration, async t => {
  const {client,view}=await setup(t);
  for(const sort_by of ["rate","occurrence_probability","coplay_rate"]){
    const desc=await view.listPlayers({sort_by,order:"desc",page_size:100});
    const asc=await view.listPlayers({sort_by,order:"asc",page_size:100});
    assert.equal(desc.total,4);assert.equal(asc.total,4);
    assert.equal(desc.items.at(-1).user_id,"4");assert.equal(asc.items.at(-1).user_id,"4");
    assert.equal(desc.items.at(-1).sort_value,null);
    for(let i=1;i<3;i++){
      assert.ok(desc.items[i-1].sort_value>=desc.items[i].sort_value);
      assert.ok(asc.items[i-1].sort_value<=asc.items[i].sort_value);
    }
    for(const p of desc.items){const other=asc.items.find(r=>r.user_id===p.user_id);assert.deepEqual(p.feeding,other.feeding);assert.deepEqual(p.receiving,other.receiving);}
    const page=await view.listPlayers({sort_by,order:"desc",page_size:1,page:2});
    assert.equal(page.items[0].user_id,desc.items[1].user_id);
    assert.equal(new Set(desc.items.map(r=>r.user_id)).size,4);
  }
  const bounded=await view.listPlayers({min_value:0,max_value:1});
  assert.equal(bounded.total,3);assert.ok(bounded.items.every(p=>p.user_id!=="4"));
  const beyond=await view.listPlayers({page:9999,page_size:1});
  assert.equal(beyond.total,4);assert.deepEqual(beyond.items,[]);
  const filtered={direction:"feeding",sort_by:"occurrence_probability",anomaly:"probability"};
  const matched=await view.listPlayers({...filtered,page_size:100});
  assert.ok(matched.total>0);
  const beyondFiltered=await view.listPlayers({...filtered,page:9999,page_size:1});
  assert.equal(beyondFiltered.total,matched.total);assert.deepEqual(beyondFiltered.items,[]);
  for(const q of [{q:"no-such-player"},{q:"no-such-player",...filtered,page:9999,page_size:1}]){
    const none=await view.listPlayers(q);assert.equal(none.total,0);assert.deepEqual(none.items,[]);
  }
  await client.query("ROLLBACK");
});

test("all channels and both partner directions survive selected-channel filtering within rule and window", integration, async t => {
  const {client,view}=await setup(t);
  const list=await view.listPlayers({q:"player1",channel:"RON",direction:"feeding",min_value:.9,max_value:.9});
  assert.equal(list.total,1);const p=list.items[0];
  assert.equal(p.feeding.partner_id,"2");assert.equal(p.feeding.rate,.9);
  assert.equal(p.channels.ALL.feeding.rate,1);
  assert.equal(p.channels.MELD.feeding.rate,.7);
  assert.equal(p.channels.RON.feeding.rate,.9);
  for(const c of ["ALL","RON","MELD"])for(const direction of ["feeding","receiving"])assertProbabilityFields(p.channels[c][direction]);
  const detail=await view.player("1",{channel:"RON",direction:"feeding",min_value:.9,max_value:.9});
  assert.equal(detail.partners.total,1);const b=detail.partners.items[0];
  assert.equal(b.user_id,"2");assert.equal(b.feeding.rate,.9);assert.equal(b.receiving.rate,.1);
  assert.equal(b.channels.ALL.feeding.count,1);assert.equal(b.channels.ALL.feeding.total,1);
  assert.equal(b.channels.ALL.receiving.rate,.2);
  assert.equal(b.channels.ALL.feeding.coplay_rate,2/110);assert.equal(b.channels.ALL.receiving.coplay_rate,2/110);
  assert.deepEqual(detail.rule_pools.map(r=>r.rule_key),["mcr","riichi"]);
  const pool=await view.player("1",{rule_key:"mcr"});assert.equal(pool.partners.total,1);assert.equal(pool.partners.items[0].feeding.rate,.95);
  const week=await view.player("1",{window_days:7});assert.equal(week.partners.total,1);assert.equal(week.partners.items[0].feeding.rate,.3);assert.equal(week.partners.items[0].receiving.rate,.4);
  assert.equal((await view.listPlayers({rule_key:"absent"})).total,0);
  await client.query("ROLLBACK");
});

test("invalid filter arguments fail before querying and stale generations do not masquerade as current", integration, async t => {
  const {client,view}=await setup(t);
  for(const q of [{sort_by:"nist_lower_95"},{filter_by:"nist_lower_95"},{anomaly:"nist"},{channel:"NO"},{direction:"both"},{sort_by:"score"},{filter_by:"score"},{order:"up"},{min_value:-.1},{max_value:1.1},{min_value:"abc"},{min_value:.8,max_value:.2},{anomaly:"red"}]){
    await assert.rejects(view.listPlayers(q),{status:400});await assert.rejects(view.player("1",q),{status:400});
  }
  await client.query("ROLLBACK");await client.query(`UPDATE ${S}.meta SET model_version='old-population-model'`);await client.query("BEGIN READ ONLY");
  const stale=await view.listPlayers({});assert.equal(stale.model_ready,false);assert.equal(stale.total,0);assert.deepEqual(stale.items,[]);
  await assert.rejects(view.player("1"),{status:409});await client.query("ROLLBACK");
});

test("ranking cache reuses immutable generations, coalesces concurrent pages and invalidates on publication",integration,async t=>{
  const {client,meta}=await setup(t);
  let queries=0,rankingQueries=0;
  const pool={query:async(sql,args)=>{queries++;if(sql.includes('AS sort_value')&&sql.includes('ORDER BY sort_value'))rankingQueries++;return client.query(sql,args);}};
  let generationOverride=null;
  const current=async()=>({...await meta(),...(generationOverride?{active_generation:generationOverride}:{})});
  const view=createPlayerView({pool,meta:current,pagination,windowDays});
  const q={sort_by:'occurrence_probability',page_size:1};
  const [a,b]=await Promise.all([view.listPlayers(q),view.listPlayers(q)]);
  assert.deepEqual(a,b);assert.equal(rankingQueries,1);
  const after=queries;
  a.items[0].name='client mutation';
  const cached=await view.listPlayers(q);
  assert.notEqual(cached.items[0].name,'client mutation');assert.equal(queries,after+2,"cache hit only checks read-index availability, never rescans ranks");
  const next=await view.listPlayers({...q,page:2});assert.equal(next.total,a.total);assert.equal(rankingQueries,1);
  const ascending=await view.listPlayers({...q,order:'asc'});
  assert.equal(ascending.total,a.total);assert.equal(rankingQueries,1,"order toggle reuses the same global ranking");
  generationOverride=crypto.randomUUID();
  const empty=await view.listPlayers(q);assert.equal(empty.total,0);assert.equal(rankingQueries,2);
  generationOverride=null;
  assert.equal((await view.listPlayers(q)).total,a.total);assert.equal(rankingQueries,3);
  await client.query('ROLLBACK');
});

test("explicit rank profile backfill is idempotent, preserves published facts, and exactly matches fallback representatives",integration,async t=>{
  const {client,view,meta,generation}=await setup(t);
  const queries=[];
  for(const channel of ['ALL','RON','MELD'])for(const sort_by of ['rate','occurrence_probability','coplay_rate'])
    for(const direction of ['any','feeding','receiving'])queries.push({channel,sort_by,direction,page_size:100});
  const before=[];for(const q of queries)before.push(await view.listPlayers(q));
  await client.query('ROLLBACK');
  await client.query(`INSERT INTO ${S}.stat_revisions(generation) VALUES($1)`,[generation]);
  const original=(await client.query(`SELECT jsonb_agg(payload ORDER BY user_id,rule_key,window_days)::text AS value FROM ${S}.player_pool_statistics WHERE generation=$1`,[generation])).rows[0].value;
  const {backfillRankProfiles,buildRankProfiles}=require(path.join(source,'playerRankProfiles'));
  const adapter={connect:async()=>({query:(...args)=>client.query(...args),release(){}})};
  assert.equal((await backfillRankProfiles(adapter,generation)).already_complete,false);
  const count=Number((await client.query(`SELECT count(*) n FROM ${S}.player_rank_profiles`)).rows[0].n);
  assert.ok(count>0);
  assert.equal((await backfillRankProfiles(adapter,generation)).already_complete,true);
  assert.equal(Number((await client.query(`SELECT count(*) n FROM ${S}.player_rank_profiles`)).rows[0].n),count);
  assert.equal((await client.query(`SELECT jsonb_agg(payload ORDER BY user_id,rule_key,window_days)::text AS value FROM ${S}.player_pool_statistics WHERE generation=$1`,[generation])).rows[0].value,original);
  await client.query('BEGIN READ ONLY');
  const indexed=createPlayerView({pool:client,meta,pagination,windowDays});
  for(let i=0;i<queries.length;i++)assert.deepEqual(await indexed.listPlayers(queries[i]),before[i],JSON.stringify(queries[i]));
  await client.query('ROLLBACK');
  const next=crypto.randomUUID();await client.query(`INSERT INTO ${S}.stat_revisions(generation) VALUES($1)`,[next]);
  // Empty affected set advances only the completion marker; no profile rows are copied.
  await buildRankProfiles(client,next,{previousGeneration:generation,full:false,playerIds:[]});
  assert.equal(Number((await client.query(`SELECT count(*) n FROM ${S}.player_rank_profiles`)).rows[0].n),count);
});

async function setup(t) {
  require(require.resolve("dotenv", { paths: [web] })).config({ path: path.join(web, ".env") });
  const host = process.env.DB_HOST || "localhost";
  assert.ok(["localhost", "127.0.0.1", "::1"].includes(host), "local test database required");
  const config = { host, port: Number(process.env.DB_PORT) || 5432, user: process.env.DB_USER || "postgres", password: process.env.DB_PASSWORD };
  const name = "om_ac_player_test_" + crypto.randomUUID().replaceAll("-", "");
  const admin = new Client({ ...config, database: "postgres" });
  await admin.connect();
  await admin.query(`CREATE DATABASE "${name}"`);
  const client = new Client({ ...config, database: name });
  t.after(async () => {
    await client.end();
    assert.match(name, /^om_ac_player_test_[a-f0-9]+$/);
    await admin.query(`DROP DATABASE "${name}"`);
    await admin.end();
  });
  await client.connect();
  await initialize(client);
  await client.query(`CREATE OR REPLACE VIEW ${S}.published_pairs AS SELECT * FROM ${S}.pair_statistics;
    CREATE OR REPLACE VIEW ${S}.published_players AS SELECT * FROM ${S}.player_pool_statistics;
    CREATE OR REPLACE VIEW ${S}.published_predictions AS SELECT * FROM ${S}.game_predictions;`);
  const generation = crypto.randomUUID(), stale = crypto.randomUUID();
  await client.query(`UPDATE ${S}.meta SET active_generation=$1,as_of=$2,model_version=$3`, [generation, "2026-11-16T12:00:00Z", VERSION]);
  const value = (rate, total=100, shared=8, games=110) => metric(rate === null ? 0 : Math.round(rate * total), rate === null ? 0 : total, shared, games);
  async function addPlayer(uid, rule, rates, games, { gen=generation, days=0, total=100 }={}) {
    const channels = Object.fromEntries(["ALL", "MELD", "RON"].map((c,i) => {
      const [sent,received] = rates[i];
      return [c,{feeding: sent === null ? null : value(sent,total),receiving:received === null ? null : value(received,total),
        rank_rate:sent === null && received === null ? null : Math.max(sent ?? -1,received ?? -1)}];
    }));
    const payload={user_id:uid,name:"player"+uid,rule_key:rule,games,partner_count:2,channels,rank_rate:channels.ALL.rank_rate};
    await client.query(`INSERT INTO ${S}.player_pool_statistics(generation,window_days,user_id,rule_key,score,payload) VALUES($1,$2,$3,$4,$5,$6)`,[gen,days,uid,rule,payload.rank_rate,payload]);
  }
  await addPlayer("1","__ALL__",[[.45,.8],[.2,.3],[.9,.1]],110);
  await addPlayer("2","__ALL__",[[.7,.6],[.95,.1],[.2,.1]],20);
  await addPlayer("3","__ALL__",[[.7,.5],[.4,.2],[1,.2]],30,{total:200});
  await addPlayer("4","__ALL__",[[null,null],[null,null],[null,null]],1);
  await addPlayer("1","mcr",[[.99,.1],[.2,.1],[.3,.1]],10);
  await addPlayer("1","riichi",[[.12,.1],[.2,.1],[.3,.1]],100);
  await addPlayer("1","__ALL__",[[.25,.1],[.2,.1],[.3,.1]],4,{days:7});
  await addPlayer("2","__ALL__",[[.95,.1],[.2,.1],[.3,.1]],5,{days:7});
  await addPlayer("9","__ALL__",[[1,1],[1,1],[1,1]],999,{gen:stale});
  async function addPair(a,b,sent,received,{rule="__ALL__",days=0,gen=generation,shared=8,donorGames=110,recipientGames=30,ronSent=sent,ronReceived=received}={}) {
    const pid=JSON.stringify([rule,a,b]),pair_key=days+":"+pid;
    const payload={pair_key,donor_id:a,recipient_id:b,donor_name:"player"+a,recipient_name:"player"+b,rule_key:rule,
      shared_games:shared,donor_games:donorGames,recipient_games:recipientGames,
      feeding:{ALL:value(sent,100,shared,donorGames),MELD:value(sent,100,shared,donorGames),RON:value(ronSent,100,shared,donorGames)},
      receiving:{ALL:value(received,100,shared,recipientGames),MELD:value(received,100,shared,recipientGames),RON:value(ronReceived,100,shared,recipientGames)}};
    await client.query(`INSERT INTO ${S}.pair_statistics(generation,window_days,pair_id,pair_key,donor_id,recipient_id,donor_name,recipient_name,rule_key,shared_games,score,payload) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)`,[gen,days,pid,pair_key,a,b,payload.donor_name,payload.recipient_name,rule,shared,Math.max(sent,received),payload]);
  }
  await addPair("1","2",.7,.15,{shared:2,recipientGames:20,ronSent:.9});
  await addPair("2","1",.99,.2,{shared:2,donorGames:20,recipientGames:110,ronReceived:.1});
  await addPair("1","3",.1,.99,{ronSent:.05});
  await addPair("3","1",.05,.8,{donorGames:30,recipientGames:110,ronReceived:.2});
  await addPair("1","9",1,1,{gen:stale});
  await addPair("1","2",.95,.1,{rule:"mcr",donorGames:10});
  await addPair("2","1",.2,.3,{rule:"mcr",recipientGames:10});
  await addPair("1","2",.3,.1,{days:7,shared:2,donorGames:4,recipientGames:5});
  await addPair("2","1",.8,.4,{days:7,shared:2,donorGames:5,recipientGames:4});
  await client.query(`UPDATE ${S}.pair_statistics SET payload=jsonb_set(payload,'{feeding,ALL}',$1::jsonb)
    WHERE generation=$2 AND window_days=0 AND rule_key='__ALL__' AND donor_id='1' AND recipient_id='2'`,[JSON.stringify(metric(1,1,2,110)),generation]);
  await client.query(`UPDATE ${S}.pair_statistics SET payload=jsonb_set(payload,'{feeding,ALL}',$1::jsonb)
    WHERE generation=$2 AND window_days=0 AND rule_key='__ALL__' AND donor_id='1' AND recipient_id='3'`,[JSON.stringify(metric(80,100,8,110)),generation]);
  // Publish fixtures through the same summarizer as the actual pipeline.
  const fixtures=(await client.query(`SELECT generation,window_days,user_id,rule_key,payload FROM ${S}.player_pool_statistics`)).rows;
  for(const row of fixtures){
    const relations=(await client.query(`SELECT payload FROM ${S}.pair_statistics WHERE generation=$1 AND window_days=$2 AND rule_key=$3 AND (donor_id=$4 OR recipient_id=$4)`,[row.generation,row.window_days,row.rule_key,row.user_id])).rows.map(r=>r.payload);
    const payload=summarizePlayer({...row.payload,relations});
    await client.query(`UPDATE ${S}.player_pool_statistics SET payload=$1,score=$2 WHERE generation=$3 AND window_days=$4 AND rule_key=$5 AND user_id=$6`,[payload,payload.rank_rate,row.generation,row.window_days,row.rule_key,row.user_id]);
  }
  const meta=async()=>(await client.query(`SELECT * FROM ${S}.meta WHERE id=1`)).rows[0];
  // PostgreSQL rejects writes throughout all list/detail assertions.
  await client.query("BEGIN READ ONLY");
  return {client,view:createPlayerView({pool:client,meta,pagination,windowDays}),meta,generation};
}


test("cached ascending ranking reverses value groups without reversing stable ties or moving NULLs first",async()=>{
  const {createPlayerRanking}=require(path.join(source,"playerRanking"));
  const {optionsOf}=require(path.join(source,"playerQuery"));
  const rows=[{user_id:'a',sort_value:.8,tie_total:30},{user_id:'b',sort_value:.8,tie_total:20},{user_id:'c',sort_value:.8,tie_total:20},{user_id:'d',sort_value:.2,tie_total:10},{user_id:'e',sort_value:null,tie_total:null}];
  let globalQueries=0;
  const pool={query:async(sql,args)=>{
    if(sql.includes('AS sort_value')){globalQueries++;return {rows:structuredClone(rows)};}
    return {rows:args[3].map(user_id=>({user_id,payload:{user_id,channels:{ALL:{feeding:null,receiving:null,rank_rate:null}}}}))};
  }};
  const reader=createPlayerRanking({pool}),m={active_generation:'generation'},p={page:1,page_size:10,offset:0};
  const desc=await reader.list(m,optionsOf({}),p,0,'__ALL__',null);
  const asc=await reader.list(m,optionsOf({order:'asc'}),p,0,'__ALL__',null);
  assert.deepEqual(desc.items.map(r=>r.user_id),['a','b','c','d','e']);
  assert.deepEqual(asc.items.map(r=>r.user_id),['d','a','b','c','e']);
  const page=await reader.list(m,optionsOf({order:'asc'}),{page:2,page_size:2,offset:2},0,'__ALL__',null);
  assert.deepEqual(page.items.map(r=>r.user_id),['b','c']);
  assert.equal(globalQueries,1);
});
test("indexed directional probability ceilings equal full relation queries at every boundary",integration,async t=>{
 const {client,view,meta,generation}=await setup(t);
 const queries=[];
 for(const channel of ['ALL','RON','MELD']) for(const direction of ['feeding','receiving'])
 for(const anomaly of ['all','probability']) for(const max_value of [0,1/3,.05,.9,1])
 for(const order of ['asc','desc']) queries.push({channel,direction,sort_by:'occurrence_probability',filter_by:'occurrence_probability',max_value,anomaly,order,page_size:2});
 queries.push({direction:'feeding',sort_by:'occurrence_probability',min_value:0,max_value:1/3,page_size:1,page:2});
 queries.push({direction:'receiving',sort_by:'occurrence_probability',min_value:0,page_size:100});
 queries.push({direction:'feeding',sort_by:'occurrence_probability',anomaly:'probability',q:'player1'});
 const expected=[];for(const q of queries)expected.push(await view.listPlayers(q));
 await client.query('ROLLBACK');await client.query(`INSERT INTO ${S}.stat_revisions(generation) VALUES($1)`,[generation]);
 const {backfillRankProfiles}=require(path.join(source,'playerRankProfiles'));
 await backfillRankProfiles({connect:async()=>({query:(...args)=>client.query(...args),release(){}})},generation);
 await client.query('BEGIN READ ONLY');
 const statements=[];const indexed=createPlayerView({pool:{query:(sql,args)=>{statements.push(sql);return client.query(sql,args);}},meta,pagination,windowDays});
 for(let i=0;i<queries.length;i++)assert.deepEqual(await indexed.listPlayers(queries[i]),expected[i],JSON.stringify(queries[i]));
 assert.equal(statements.some(sql=>sql.includes('raw AS MATERIALIZED')),false,'ceiling queries never scan complete relation aggregates');
 assert.ok(statements.some(sql=>sql.includes('profiles->')&&sql.includes('<=')&&sql.includes('AS sort_value')));
 assert.ok(statements.some(sql=>sql.includes('pair_id=ANY')),'page representatives use indexed pair pointers');
 // A positive lower bound or both directions can remove a minimum representative.
 for(const q of [{direction:'feeding',min_value:.01,max_value:1},{direction:'any',max_value:.05}]) {
  statements.length=0;const full={...q,sort_by:'occurrence_probability'};
  const result=await indexed.listPlayers(full);assert.deepEqual(result,await view.listPlayers(full));assert.ok(statements.some(sql=>sql.includes('raw AS MATERIALIZED')));
 }
 await client.query('ROLLBACK');
});
