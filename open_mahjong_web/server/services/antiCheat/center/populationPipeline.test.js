"use strict";
const test=require("node:test"), assert=require("node:assert/strict");
const {metric,binomialTail,indexGameFacts,summarizePair,summarizePlayer}=require("./directionalStatistics");
function game(id, changes={}, players=["A","B","C","D"]) {
  const counts={AB:45,AC:30,AD:25,BA:12,CA:3,DA:0,CB:30,DB:75,...changes};
  const pairs=players.flatMap(a=>players.filter(b=>b!==a).map(b=>{
    const events=counts[a+b]||0,received=players.filter(x=>x!==b).reduce((n,x)=>n+(counts[x+b]||0),0);
    const c={events,received,available:true};
    return {donor_id:a,recipient_id:b,delivery_supported:true,channels:{ALL:{...c},MELD:{...c},RON:{...c}}};
  }));
  return {game_id:id,pairs};
}
const context={donor_id:"A",recipient_id:"B",donor_name:"Alice",recipient_name:"Bob",donor_games:2,recipient_games:1,pair_key:"0:AB",rule_key:"r"};
test("fixed one-third inclusive binomial tails have no target sample cutoff",()=>{
  assert.equal(metric(0,0,1,2).rate,null);
  assert.equal(metric(0,0,1,2).occurrence_probability,null);
  assert.equal(metric(0,0,1,2).flag,false);
  assert.ok(Math.abs(binomialTail(1,1)-1/3)<1e-14);
  assert.ok(Math.abs(metric(1,1,1,1).occurrence_probability-1/3)<1e-12);
  assert.equal(metric(1,1,1,1).flag,false);
  assert.ok(Math.abs(binomialTail(3,3)-1/27)<1e-14);
  assert.equal(metric(3,3,1,1).flag,true);
  assert.equal(metric(10,10,10,10).flag,true);
  assert.equal(metric(100,100,1,1).flag,true);
  assert.equal(metric(0,20,1,1).occurrence_probability,1);
  assert.equal(metric(30,90,10,10).flag,false);
  assert.ok(Number.isFinite(binomialTail(40000,100000)));
  assert.throws(()=>binomialTail(2,1),/invalid_directional_counts/);
});
test("feeding and receiving use different complete-table denominators and only shared games",()=>{
  const shared=game("shared"),apart=game("apart",{AC:1000,AD:1000,AE:1000},["A","C","D","E"]);
  const index=indexGameFacts([shared,apart]);
  const pair=summarizePair([{game_id:"shared"}],index,context);
  assert.equal(pair.feeding.ALL.count,45);
  assert.equal(pair.feeding.ALL.total,100);
  assert.equal(pair.feeding.ALL.rate,.45);
  assert.equal(pair.receiving.ALL.total,150);
  assert.equal(pair.receiving.ALL.rate,.3);
  assert.equal(pair.feeding.ALL.coplay_rate,.5);
  assert.equal(pair.receiving.ALL.coplay_rate,1);
  const reverse=summarizePair([{game_id:"shared"}],index,{...context,donor_id:"B",recipient_id:"A",donor_games:1,recipient_games:2});
  assert.equal(reverse.receiving.ALL.count,12);
  assert.equal(reverse.receiving.ALL.total,15);
  assert.equal(reverse.receiving.ALL.rate,.8);
  assert.equal(reverse.receiving.ALL.coplay_rate,.5);
});
test("complete event counters protect missing-table denominators and channels stay independent",()=>{
  const g=game("g");
  for(const p of g.pairs){ p.channels.RON.events=p.donor_id==="A"&&p.recipient_id==="B"?5:0;p.channels.RON.received=p.recipient_id==="B"?5:0; }
  const pair=summarizePair([{game_id:"g"}],indexGameFacts([g]),context);
  assert.equal(pair.feeding.RON.rate,1);
  assert.equal(pair.feeding.RON.flag,true);
  assert.equal(pair.feeding.ALL.rate,.45);
  const broken=structuredClone(g);broken.pairs.pop();
  const missing=summarizePair([{game_id:"g"}],indexGameFacts([broken]),context);
  assert.equal(missing.feeding.ALL.total,0);
  assert.equal(missing.feeding.ALL.rate,null);
});
test("all-rule aggregation adds observed counts instead of taking maximum pool rates",()=>{
  const a=game("r1"),b=game("r2",{AB:0,AC:900,AD:0});
  const pair=summarizePair([{game_id:"r1"},{game_id:"r2"}],indexGameFacts([a,b]),{...context,rule_key:"__ALL__",donor_games:2,recipient_games:2});
  assert.equal(pair.feeding.ALL.count,45);
  assert.equal(pair.feeding.ALL.total,1000);
  assert.equal(pair.feeding.ALL.rate,.045);
  assert.equal(pair.shared_games,2);
});
test("player direction summaries rank actual rates with deterministic sample-size ties",()=>{
  const index=indexGameFacts([game("g")]);
  const ab=summarizePair([{game_id:"g"}],index,context);
  const ba=summarizePair([{game_id:"g"}],index,{...context,donor_id:"B",recipient_id:"A",donor_name:"Bob",recipient_name:"Alice",pair_key:"0:BA",donor_games:1,recipient_games:2});
  const payload=summarizePlayer({user_id:"A",name:"Alice",rule_key:"r",games:2,relations:[ab,ba]});
  assert.equal(payload.partner_count,1);
  assert.equal(payload.channels.ALL.feeding.rate,.45);
  assert.equal(payload.channels.ALL.receiving.rate,.8);
  assert.equal(payload.channels.ALL.receiving.partner_id,"B");
  assert.equal(payload.channels.ALL.rank_rate,.8);
  assert.equal(payload.score,undefined);
  const tied=structuredClone(ab);tied.recipient_id="C";tied.pair_key="0:AC";
  for(const c of Object.keys(tied.feeding)){tied.feeding[c].total*=2;tied.feeding[c].count*=2;}
  const ranked=summarizePlayer({user_id:"A",rule_key:"r",games:2,relations:[ab,tied]});
  assert.equal(ranked.channels.ALL.feeding.partner_id,"C");
  const empty=summarizePlayer({user_id:"A",rule_key:"r",games:2,relations:[]});
  assert.equal(empty.channels.ALL.feeding,null);
  assert.equal(empty.rank_rate,null);
});


test("probability output omits retired confidence calculations",()=>{
 const value=metric(4,4,1,1);
 assert.equal(Object.hasOwn(value,'nist_lower_95'),false);
 assert.equal(Object.hasOwn(value,'abnormality'),false);
 assert.equal(value.occurrence_probability,value.p_value);
 assert.ok(Math.abs(value.p_value-1/81)<1e-14);
 assert.equal(value.flag,true);
 assert.throws(()=>metric(2,1,1,1),/invalid_directional_counts/);
});
