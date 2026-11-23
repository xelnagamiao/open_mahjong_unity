"use strict";
const { replayDDL, insertReplay } = require('../../testFixtures/replay');
const test = require("node:test"), assert = require("node:assert/strict");
const { Pool, Client } = require("pg");
const { createCenter } = require("./service");
const { runUpdate } = require("./processor");
const S = "anti_cheat_center", RK = "guobiao/standard";

async function setup(t) {
  require("dotenv").config({path:require("path").resolve(__dirname,"../../../../.env")});
  const host=process.env.DB_HOST||"localhost";
  assert.ok(["localhost","127.0.0.1","::1"].includes(host));
  const config={host,port:Number(process.env.DB_PORT)||5432,user:process.env.DB_USER||"postgres",password:process.env.DB_PASSWORD};
  const db="om_ac_direction_test_"+require("crypto").randomUUID().replaceAll("-","");
  const admin=new Client({...config,database:"postgres"});await admin.connect();await admin.query(`CREATE DATABASE "${db}"`);
  const pool=new Pool({...config,database:db});
  t.after(async()=>{await pool.end();assert.match(db,/^om_ac_direction_test_[a-f0-9]+$/);await admin.query(`DROP DATABASE "${db}"`);await admin.end();});
  await pool.query(`${replayDDL()};
    CREATE TABLE game_player_records(game_id text,user_id bigint,username text,original_player_index integer,score integer,rank integer,rule text,sub_rule text,room_type text,match_type text,match_tier text,event_id text);
    CREATE TABLE game_player_metrics(game_id text,user_id bigint,total_round_score integer,total_rounds integer);`);
  const center=createCenter({pool,launch:()=>{}});await center.ready();
  const facts=new Map();let calls=0;
  async function add(id,users,matrix,ron={},day=10,rule=RK) {
    users=users.map(String);const created_at=`2026-11-${day}T12:00:00Z`;
    await insertReplay(pool,[id,{},(await pool.query("SELECT ($1::timestamptz AT TIME ZONE 'Asia/Shanghai')::text AS stamp",[created_at])).rows[0].stamp]);
    for (const [index,user] of users.entries()) await pool.query("INSERT INTO game_player_records(game_id,user_id,username,original_player_index,rule,sub_rule,room_type,match_tier,match_type) VALUES($1,$2,$3,$4,'guobiao','guobiao/standard','match','beginner','4/4_rank')",[id,user,`p${user}`,index]);
    const players=users.map((user_id,i)=>({user_id,name:`p${user_id}`,original_index:i,score:0,hands:1,points_per_hand:0,score_per_hand:0,score_unit:"net_points"}));
    const pairs=[];
    for(const donor_id of users)for(const recipient_id of users)if(donor_id!==recipient_id){
      const channels={};
      for(const c of ["ALL","MELD","RON"]){
        const n=(a,b)=>c==="RON"?(ron[`${a}:${b}`]||0):c==="MELD"?(matrix[`${a}:${b}`]||0)-(ron[`${a}:${b}`]||0):(matrix[`${a}:${b}`]||0);
        channels[c]={available:true,events:n(donor_id,recipient_id),received:users.filter(u=>u!==recipient_id).reduce((s,u)=>s+n(u,recipient_id),0),discards:200,eligible_decisions:0};
      }
      pairs.push({donor_id,recipient_id,donor_name:`p${donor_id}`,recipient_name:`p${recipient_id}`,channels,shared_games:1,shared_hands:1,source_verified_hands:1,delivery_supported:true});
    }
    facts.set(id,{game_id:id,created_at,rule_key:rule,rule:"guobiao",room_type:"match",status:"ready",delivery_supported:true,players,pairs,evidence:[]});
  }
  async function update(){const job=await center.startUpdate(10000001);await runUpdate(pool,job.id,{extractor:async row=>{calls++;return structuredClone(facts.get(row.game_id));},throwErrors:true,concurrency:1,asOf:"2026-11-14T12:00:00Z"});return center.update(job.id);}
  const pair=async(a,b)=>(await pool.query(`SELECT payload FROM ${S}.published_pairs WHERE generation=(SELECT active_generation FROM ${S}.meta) AND window_days=0 AND donor_id=$1 AND recipient_id=$2`,[a,b])).rows[0].payload;
  const player=async(id)=>(await pool.query(`SELECT payload FROM ${S}.published_players WHERE generation=(SELECT active_generation FROM ${S}.meta) AND window_days=0 AND user_id=$1 AND rule_key=$2`,[id,RK])).rows[0].payload;
  return {pool,center,add,update,pair,player,calls:()=>calls};
}

test("directional publication preserves shared-game denominators, reverse relationships and incremental coplay",{skip:process.env.ANTI_CHEAT_INTEGRATION!=="1"},async t=>{
  const x=await setup(t);
  await x.add("shared",[1,2,3,4],{"1:2":45,"1:3":30,"1:4":25,"2:1":12,"3:1":3,"3:2":30,"4:2":75},{"1:2":5,"2:1":2});
  await x.update();
  const ab=await x.pair("1","2"),ba=await x.pair("2","1");
  assert.equal(ab.feeding.ALL.count,45);assert.equal(ab.feeding.ALL.total,100);assert.equal(ab.feeding.ALL.rate,.45);
  assert.equal(ab.receiving.ALL.total,150);assert.equal(ab.receiving.ALL.rate,.3);
  assert.equal(ba.receiving.ALL.count,12);assert.equal(ba.receiving.ALL.total,15);assert.equal(ba.receiving.ALL.rate,.8);
  assert.equal(ab.feeding.MELD.total,95);assert.equal(ab.feeding.MELD.count,40);
  assert.equal(ab.feeding.RON.count,5);assert.equal(ab.feeding.RON.total,5);
  assert.ok(Math.abs(ab.feeding.RON.occurrence_probability-1/243)<1e-12);
  assert.equal(ab.feeding.RON.occurrence_probability,ab.feeding.RON.p_value);
  assert.equal(Object.hasOwn(ab.feeding.RON, "nist_lower_95"), false);
  assert.equal(Object.hasOwn(ab.feeding.RON,"abnormality"),false);
  assert.equal(ab.feeding.RON.flag,true,"one shared game is allowed to cross the exact binomial anomaly line");
  const p=await x.player("1");
  assert.equal(p.channels.ALL.feeding.partner_id,"2");assert.equal(p.channels.ALL.feeding.rate,.45);
  assert.equal(p.channels.ALL.receiving.partner_id,"2");assert.equal(p.channels.ALL.receiving.rate,.8);
  assert.equal(p.channels.ALL.rank_rate,.8);
  const detailA=await x.center.player("1",{window_days:0,channel:"ALL"});
  const partnerB=detailA.partners.items.find(p=>p.user_id==="2");
  assert.equal(partnerB.feeding.rate,.45,"left side is current player A feeding B");
  assert.equal(partnerB.receiving.rate,.8,"right side is current player A receiving from B, not B receiving from A");
  const detailB=await x.center.player("2",{window_days:0,channel:"ALL"});
  const partnerA=detailB.partners.items.find(p=>p.user_id==="1");
  assert.equal(partnerA.feeding.rate,1);assert.equal(partnerA.receiving.rate,.3);
  const ronList=await x.center.listPlayers({window_days:0,channel:"RON",page_size:100});
  const ronPlayer=ronList.items.find(r=>r.user_id==="1");
  const ronDetail=await x.center.player("1",{window_days:0,channel:"RON"});
  const ronPartner=ronDetail.partners.items.find(r=>r.user_id==="2");
  for(const sent of [ronPlayer.feeding,ronDetail.player.feeding,ronPartner.feeding]){
    assert.ok(Math.abs(sent.occurrence_probability-1/243)<1e-12);
  assert.equal(Object.hasOwn(sent, "nist_lower_95"), false);
    assert.equal(Object.hasOwn(sent,"abnormality"),false);
  }
  for(const received of [ronPlayer.receiving,ronDetail.player.receiving,ronPartner.receiving]){
    assert.ok(Math.abs(received.occurrence_probability-1/9)<1e-12);
  assert.equal(Object.hasOwn(received, "nist_lower_95"), false);
    assert.equal(Object.hasOwn(received,"abnormality"),false);
  }
  const generation=(await x.pool.query(`SELECT active_generation FROM ${S}.meta`)).rows[0].active_generation;
  await x.update();assert.equal(x.calls(),1);
  assert.equal((await x.pool.query(`SELECT active_generation FROM ${S}.meta`)).rows[0].active_generation,generation);
  await x.add("without-b",[1,5,6,7],{"1:5":100,"5:1":100},{},11);
  await x.update();assert.equal(x.calls(),2);
  const after=await x.pair("1","2");
  assert.equal(after.feeding.ALL.total,100,"A without B never changes shared-game feeding denominator");
  assert.equal(after.receiving.ALL.total,150);
  assert.equal(after.feeding.ALL.coplay_rate,.5);assert.equal(after.receiving.ALL.coplay_rate,1);
  const pa=await x.player("1");assert.equal(pa.channels.ALL.feeding.partner_id,"5");assert.equal(pa.channels.ALL.rank_rate,1);
  const pb=await x.player("2");
  assert.ok(pb.channels.ALL,"B retains its published partner summaries when only A adds a new table");
  const list=await x.center.listPlayers({window_days:0,page_size:100});
  assert.equal(new Set(list.items.map(r=>r.user_id)).size,list.items.length);
  for(let i=1;i<list.items.length;i++)assert.ok((list.items[i-1].rank_rate??-1)>=(list.items[i].rank_rate??-1));
  const paged=await x.center.listPlayers({window_days:0,page_size:1,page:2});
  assert.equal(paged.items[0].user_id,list.items[1].user_id,"pagination follows global rate ordering");
});

test("default player view aggregates actual directional counts across rules before taking maxima",{skip:process.env.ANTI_CHEAT_INTEGRATION!=="1"},async t=>{
  const x=await setup(t);
  await x.add("pool-one",[1,2,3,4],{"1:2":9,"1:3":1,"2:1":2,"3:1":8});
  await x.add("pool-two",[1,2,3,4],{"1:2":1,"1:3":89,"2:1":8,"3:1":2},{},11,"alternate-rule");
  await x.update();
  const list=await x.center.listPlayers({window_days:0,page_size:100,channel:"ALL"});
  const p=list.items.find(p=>p.user_id==="1");
  assert.equal(p.feeding.partner_id,"3");
  assert.equal(p.feeding.count,90);assert.equal(p.feeding.total,100);assert.equal(p.feeding.rate,.9);
  assert.equal(p.receiving.rate,.5,"global receiving must sum counts, not select one rule's 80% rate");
  assert.equal(p.rank_rate,.9);
  const detail=await x.center.player("1",{window_days:0,channel:"ALL"});
  assert.equal(detail.selected_rule_key,"__ALL__");
  assert.equal(detail.partners.total,3,"same partner in two rules appears once");
  assert.equal(detail.rule_pools.length,2);
  const filtered=await x.center.listPlayers({window_days:0,page_size:100,channel:"ALL",rule_key:RK});
  const firstRule=filtered.items.find(p=>p.user_id==="1");
  assert.equal(firstRule.feeding.partner_id,"2");assert.equal(firstRule.feeding.rate,.9);
  assert.equal(firstRule.receiving.partner_id,"3");assert.equal(firstRule.receiving.rate,.8);
});
