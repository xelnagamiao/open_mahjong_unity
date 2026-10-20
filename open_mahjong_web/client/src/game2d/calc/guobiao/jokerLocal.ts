import {TILES,INDEX,prepare,canWin,shapes,winningTiles,shapeTiles,meldTiles,limit,sum,type LocalHand,type Policy} from '../localJokerHand'
import {Chinese_Hepai_Check,PlayerTiles} from './gbHepai'
import {tingpaiCheck} from './gbTingpai'
import type {FourJokersHintRequest,FourJokersHint} from './fourJokers'

const SEASONS=[51,52,53,54]
export const KNITTED=[[1,2,3],[1,3,2],[2,1,3],[2,3,1],[3,1,2],[3,2,1]].map(p=>p.flatMap((r,s)=>[0,1,2].map(i=>(s+1)*10+r+3*i)).sort((a,b)=>a-b))
const policy=(count=4):Policy=>({jokers:[59],physical:[...TILES,59],supply:{59:4},meldCount:count,closedCap:4,exposedCap:true,sevenPairs:count===4,orphans:count===4})
const huahun=(sub:string)=>sub==='guobiao/huahun'
const singleCache=new Map<string,boolean>()
function prepared(hand:number[],melds:string[],sub:string,complete=true):LocalHand|null {
  const hua=huahun(sub),flower=hua&&melds.includes('G51')
  if(hua&&(hand.some(t=>!INDEX.has(t)&&!SEASONS.includes(t))||SEASONS.some(t=>hand.filter(x=>x===t).length>1)
    ||melds.filter(m=>m==='G51').length>1||flower&&hand.some(t=>SEASONS.includes(t))))return null
  return prepare(hua?hand.map(t=>SEASONS.includes(t)?59:t):hand,flower?melds.filter(m=>m!=='G51'):melds,policy(flower?3:4),complete)
}
function* choose(values:number[],count:number,start=0):Generator<number[]> {
  if(count===0){yield [];return}
  for(let i=start;i<=values.length-count;i++)for(const rest of choose(values,count-1,i+1))yield [values[i],...rest]
}
function* unconnected(h:LocalHand):Generator<number[]> {
  if(h.melds.length||sum(h.counts)+h.ghosts!==14||h.counts.some(n=>n>1))return
  const natural=TILES.filter((t,i)=>h.counts[i]>0)
  for(const p of KNITTED){const domain=[...p,41,42,43,44,45,46,47]
    if(natural.some(t=>!domain.includes(t)))continue
    for(const targets of choose(domain.filter(t=>!natural.includes(t)),h.ghosts))yield [...natural,...targets].sort((a,b)=>a-b)
  }
}
function* knittedRemainders(h:LocalHand):Generator<{pattern:number[];rest:LocalHand}> {
  if(h.melds.length>1)return
  for(const pattern of KNITTED){const missing=pattern.filter(t=>!h.counts[INDEX.get(t)!]).length
    if(missing>h.ghosts)continue
    const present=pattern.filter(t=>h.counts[INDEX.get(t)!]>0)
    for(let spend=0;spend<=Math.min(h.ghosts-missing,present.length);spend++)for(const retained of choose(present,spend)) {
      const counts=[...h.counts];for(const t of present)if(!retained.includes(t))counts[INDEX.get(t)!]--
      const rest=prepare(counts.flatMap((n,i)=>Array(n).fill(TILES[i])).concat(Array(h.ghosts-missing-spend).fill(59)),h.melds,policy(1))
      if(!rest)continue
      for(const t of pattern)rest.caps[INDEX.get(t)!]--
      if(!rest.counts.some((n,i)=>n>rest.caps[i]))yield {pattern,rest}
    }
  }
}
function complete(h:LocalHand|null,flower:boolean):boolean {
  return !!h&&(canWin(h)||!flower&&(!unconnected(h).next().done||[...knittedRemainders(h)].some(p=>canWin(p.rest))))
}
export function jokerWaiting(hand:number[],melds:string[],sub:string):number[] {
  const h=prepared(hand,melds,sub,false);if(!h)return []
  return [...TILES,...(huahun(sub)?SEASONS:[59])].filter(tile=>{
    const normalized=SEASONS.includes(tile)?59:tile
    if(huahun(sub)&&SEASONS.includes(tile)?hand.includes(tile):(h.physical[normalized]??0)>=limit(h.policy,normalized))return false
    return complete(prepared([...hand,tile],melds,sub),melds.includes('G51'))
  })
}
function single(logical:number[],melds:string[],win:number,flower:boolean):boolean {
  const before=[...logical],i=before.indexOf(win);if(i<0)return false;before.splice(i,1)
  const key=JSON.stringify([[...before].sort((a,b)=>a-b),melds,win]);if(singleCache.has(key))return singleCache.get(key)!
  let waits:number[]
  if(flower)waits=jokerWaiting(before,melds,'guobiao/huahun')
  else {const physical=[...before,...melds.flatMap(m=>meldTiles(m)??[])];waits=[...tingpaiCheck(before,melds)].filter(t=>physical.filter(x=>x===t).length<4)}
  const yes=waits.length===1&&waits[0]===win
  if(singleCache.size>=4096)singleCache.clear();singleCache.set(key,yes);return yes
}
class FlowerScorer extends Chinese_Hepai_Check {
  constructor(){super(false,{...Chinese_Hepai_Check.count_model_dict_default,huagang:88})}
  override fan_count_output(p:PlayerTiles,codes:string,zimo:boolean,way:string[]):[number,string[]]{
    p.fan_list.push('huagang');const result=super.fan_count_output(p,codes,zimo,way);result[1]=result[1].filter(f=>f!=='undefined');result[1].push('花杠');return result
  }
}
export function jokerScore(hand:number[],melds:string[],way:string[],win:number,sub:string,multi=false,bonus='add8',progressive=true):{score:number;qualifying:number;fans:string[]} {
  const h=prepared(hand,melds,sub),flower=huahun(sub)&&melds.includes('G51'),joker=huahun(sub)?SEASONS.includes(win):win===59
  const empty={score:0,qualifying:0,fans:[] as string[]}
  if(!h||!hand.includes(win)||joker&&!way.some(w=>['自摸','杠上开花','last_deal','妙手回春'].includes(w)))return empty
  const flowers=way.filter(w=>w==='花牌').length
  if(huahun(sub)&&(flowers>4||!['add8','double','none'].includes(bonus)))return empty
  const baseWay=way.filter(w=>w!=='和单张'&&!(joker&&w==='和绝张')&&!(huahun(sub)&&w==='花牌'))
  const allowed=!multi||!h.ghosts,checker=flower?new FlowerScorer():new Chinese_Hepai_Check()
  let best=0,fans:string[]=[]
  const resolved=(logical:number[],tile:number)=>allowed&&single(logical,melds,tile,flower)?[...baseWay,'和单张']:[...baseWay]
  const accept=(result:[number,string[]])=>{if(result[0]>best){best=result[0];fans=result[1]}}
  for(const shape of shapes(h)){const logical=shapeTiles(shape)
    for(const tile of winningTiles(h,shape,joker?59:win)) {
      if(shape.kind!=='standard'){accept(checker.hepai_check(logical,h.melds,resolved(logical,tile),tile));continue}
      const player=new PlayerTiles([],[...h.melds,...(flower?['H51']:[]),...shape.closed],14);player.initial_combination_count=h.melds.length
      const original=allowed?player.deepCopy():null
      let score=checker.fan_count(player,tile,[...baseWay])
      if(allowed&&score[0]+1>best){const way=resolved(logical,tile);if(way.length>baseWay.length)score=checker.fan_count(original!,tile,way)}
      accept(score)
    }
  }
  if(!flower){const seen=new Set<string>()
    function* rare():Generator<number[]>{yield*unconnected(h!);for(const p of knittedRemainders(h!))for(const s of shapes(p.rest))yield [...p.pattern,...shapeTiles(s)].sort((a,b)=>a-b)}
    for(const logical of rare()) {const counts=TILES.map(t=>logical.filter(x=>x===t).length)
      for(const tile of joker?TILES.filter((t,i)=>counts[i]>h.counts[i]):[win]){const key=logical.join(',')+'|'+tile;if(seen.has(key))continue;seen.add(key);accept(checker.hepai_check(logical,h.melds,resolved(logical,tile),tile))}
    }
  }
  if(!best)return empty
  const qualifying=huahun(sub)?best:best-flowers
  if(huahun(sub)){
    if(flowers){best+=progressive?[0,4,8,16,32][flowers]:flowers;fans.push(progressive?`黑花${flowers}朵`:`花牌*${flowers}`)}
    if(!h.ghosts&&!flower&&bonus!=='none'){const extra=bonus==='add8'?8:best;best+=extra;fans.push(bonus==='add8'?'无癞子加番':`无癞子翻倍*${extra}`)}
  }
  return {score:best,qualifying,fans}
}
export function describeJoker(q:FourJokersHintRequest):FourJokersHint {
  const sub=q.sub_rule??'guobiao/four_jokers',ghosts=q.hand_tiles.filter(t=>huahun(sub)?SEASONS.includes(t):t===59).length+(q.tiles_combination.includes('G51')?4:0)
  const limit=huahun(sub)?[8,8,16,32,64][Math.min(4,ghosts)]:q.hepai_limit
  const score=(way:string[])=>jokerScore(q.hand_tiles,q.tiles_combination,way,q.get_tile,sub,q.joker_multi_wait,q.no_joker_bonus,q.black_flower_progressive)
  const ron=score(q.ron_way),self=score(q.self_way)
  const canRon=ron.score>0&&ron.qualifying>=limit,canSelf=self.score>0&&self.qualifying>=limit
  if(canRon)return {kind:canSelf?'dianhe':'ron_only',label:canSelf?`${ron.score}番`:'仅点和',score:ron.score}
  return canSelf?{kind:'zimo',label:`自摸${self.score}番`,score:self.score}
    :{kind:'wuyi',label:'未起和',score:Math.max(ron.score,self.score)}
}
