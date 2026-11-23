"use strict";
const { replayDDL, insertReplay } = require('../../testFixtures/replay');
const test=require("node:test"),assert=require("node:assert/strict");
const {Pool,Client}=require("pg");
const {createCenter}=require("./service"),{SCHEMA:S}=require("./database");
test("summary overview skips quality and relationship scans while retaining stale-update recovery and route compatibility",{skip:process.env.ANTI_CHEAT_INTEGRATION!=="1"},async t=>{
 require("dotenv").config({path:require("path").resolve(__dirname,"../../../../.env")});
 const host=process.env.DB_HOST||"localhost";assert.ok(["localhost","127.0.0.1","::1"].includes(host));
 const config={host,port:Number(process.env.DB_PORT)||5432,user:process.env.DB_USER||"postgres",password:process.env.DB_PASSWORD};
 const db="om_ac_overview_test_"+require("crypto").randomUUID().replaceAll("-","");
 const admin=new Client({...config,database:"postgres"});await admin.connect();await admin.query(`CREATE DATABASE "${db}"`);
 const pool=new Pool({...config,database:db});t.after(async()=>{await pool.end();assert.match(db,/^om_ac_overview_test_[a-f0-9]+$/);await admin.query(`DROP DATABASE "${db}"`);await admin.end();});
 await pool.query(replayDDL()); await insertReplay(pool,['one',{}]); await insertReplay(pool,['two',{}]);
 const queries=[],wrapped={query:(sql,args)=>{queries.push(sql);return pool.query(sql,args);},connect:()=>pool.connect()};
 let launched=0;const center=createCenter({pool:wrapped,launch:()=>launched++});await center.ready();
 // Unscanned raw games are not silently assumed to belong to the ranked scope.
 const unscanned=await center.overview({detail:"summary"});
 assert.equal(unscanned.summary.source_games,0);
 assert.equal(unscanned.summary.source_count_basis,"last_explicit_scope_scan");
 // Simulate the explicit scanner's metadata-only scope result, not a GET backfill.
 await require("./rankedScope").saveScopeInventory(pool,["one","two"]);
 const stale=require("crypto").randomUUID(),active=require("crypto").randomUUID();
 await pool.query(`INSERT INTO ${S}.updates(id,status,created_at,settings) VALUES($1,'analyzing',now()-interval '2 minutes','{}'),($2,'queued',now(),'{}')`,[stale,active]);
 queries.length=0;const light=await center.overview({detail:"summary"});
 assert.equal(light.summary.source_games,2);assert.equal(light.summary.last_updated,null);assert.equal(light.active_update.id,active);assert.equal(light.quality,undefined);assert.equal(light.model_ready,false);
 assert.ok(!queries.some(sql=>/FROM\s+anti_cheat_center\.(games|published_pairs|published_players)\b/i.test(sql)),"summary must not aggregate games payloads or published relationships");
 assert.equal((await pool.query(`SELECT status FROM ${S}.updates WHERE id=$1`,[stale])).rows[0].status,"interrupted");
 assert.equal(launched,0,"GET recovery never starts analysis");
 const full=await center.overview();assert.equal(full.summary.source_games,2);assert.equal(full.summary.cached_games,0);assert.deepEqual(full.quality.issues,[]);assert.equal(full.quality.issues_limit,50);
 const express=require("express"),{createRouter}=require("../../../routes/admin/antiCheat");const app=express();
 app.use((req,res,next)=>{req.admin={userId:10000001};next();});app.use(createRouter({center}));
 const server=await new Promise(resolve=>{const s=app.listen(0,"127.0.0.1",()=>resolve(s));});t.after(()=>new Promise(resolve=>server.close(resolve)));
 const base="http://127.0.0.1:"+server.address().port;
 const summary=await(await fetch(base+"/overview?detail=summary")).json();assert.equal(summary.data.detail,"summary");assert.equal(summary.data.quality,undefined);
 const detailed=await(await fetch(base+"/overview")).json();assert.ok(detailed.data.quality);
});
