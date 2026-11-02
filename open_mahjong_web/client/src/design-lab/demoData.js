const players = ['雾岛','青空','秋见','南栀']
const fanNames = {menqianqing:'门前清',pinghu:'平和',duanyao:'断幺',jianke:'箭刻',yaojiuke:'幺九刻',quemen:'缺一门',yibangao:'一般高',lianliu:'连六',hunseqinglong:'花龙',qingyise:'清一色',qinglong:'清龙',qiduizi:'七对'}
const factorFor = rule => ({guobiao:1,riichi:.72,hongque:.56,qingque:.31})[rule] || .2
export function demoDataset(rule, days = 30, eventId = '') {
  const factor = factorFor(rule) * (eventId ? .16 : 1)
  const tiers = ['beginner','intermediate','advanced']
  const totals = tiers.map((match_tier,i) => {
    const total_games = Math.round([6428,3816,1552][i]*factor)
    const total_rounds=total_games*16
    const win_count=Math.round(total_rounds*.96)
    return {match_tier,total_games,total_rounds,win_count,self_draw_count:Math.round(win_count*.29),deal_in_count:Math.round(win_count*.71),total_fan_score:win_count*15.6,total_win_turn:win_count*12.8,total_fangchong_score:win_count*10.2,fulu_round_count:Math.round(total_rounds*4*.61),cuohe_count:0,total_round_score:0,first_place_count:total_games,second_place_count:total_games,third_place_count:total_games,fourth_place_count:total_games}
  })
  const total={match_tier:'total'}
  Object.keys(totals[0]).filter(k=>k!=='match_tier').forEach(k=>total[k]=totals.reduce((s,t)=>s+t[k],0))
  totals.push(total)
  const end=new Date('2026-10-22T12:00:00Z'),start=new Date(end)
  start.setUTCDate(start.getUTCDate()-days+1)
  const daily=[]
  for(let i=0;i<days;i++){const date=new Date(start);date.setUTCDate(date.getUTCDate()+i);tiers.forEach((t,j)=>daily.push({stat_date:date.toISOString().slice(0,10),match_tier:t,total_games:Math.max(0,Math.round((37+Math.sin(i*.53+j)*17+(i%7===5?18:0)-j*7)*factor))}))}
  const fans={}
  tiers.forEach((t,index)=>fans[t]=Object.fromEntries(Object.keys(fanNames).map((key,i)=>[key,Math.round((6200/(i+1)**1.24)*(1-index*.28)*factor)])))
  fans.total=Object.fromEntries(Object.keys(fanNames).map(k=>[k,tiers.reduce((s,t)=>s+fans[t][k],0)]))
  return {totals,daily,fans,fan_dict:fanNames,supports_fan_stats:true,events:[{event_id:'demo-autumn',name:'秋日交流赛 · 示例'}],meta:{as_of_date:'2026-10-22',date_from:start.toISOString().slice(0,10),date_to:'2026-10-22'}}
}
export function demoRecords(rule, tier = 'total', offset=0) {
  return { total:96, items:Array.from({length:12},(_,i)=>{
    const index=i+offset
    const date=new Date('2026-10-22T13:42:00Z');date.setUTCMinutes(date.getUTCMinutes()-index*47)
    return {game_id:'DEMO-'+String(1026-index),created_at:date.toISOString(),rule,match_type:'4/4_rank',match_tier:tier==='total'?['intermediate','beginner','advanced'][i%3]:tier,room_type:'match',players:players.map((username,j)=>({user_id:'example-'+j,username,score:[126,42,-66,-102][(j+i)%4],rank:(j+i)%4+1,pt_change:null}))}
  })}
}
export function demoPlayer(name, rule) {
  const records=demoRecords(rule).items
  records.forEach(r=>r.players[0].username=name||'青空')
  return {info:{user_id:'example-0',user_settings:{username:name||'青空'}},stats:{total_games:248,total_rounds:3968,win_count:1064,self_draw_count:312,deal_in_count:516,total_fan_score:17236,total_win_turn:14151,fulu_round_count:2416,total_fangchong_score:7482,total_round_score:3196,first_place_count:82,second_place_count:68,third_place_count:54,fourth_place_count:44,details_available:true},records}
}
export const demoWalls=[
  {id:'demo-1',key:'DEMO-KEY-AUTUMN',name:'秋日交流 · 同牌山样本',scope:'event',event_name:'秋日交流赛',wall_type:'key',rule:'guobiao/standard',round_count:16,created_at:'2026-10-18T04:00:00Z',unlocked_at:'2026-10-21T12:00:00Z',is_unlocked:true},
  {id:'demo-2',key:'DEMO-KEY-PRACTICE',name:'周末练习',scope:'personal',owner_username:'青空',wall_type:'key',rule:'guobiao/standard',round_count:8,created_at:'2026-10-17T04:00:00Z',unlocked_at:'2026-10-20T12:00:00Z',is_unlocked:true}
]

