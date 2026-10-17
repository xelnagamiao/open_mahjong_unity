import {TILES,ORPHANS,prepare,shapes,meldTiles} from './localJokerHand'
const meld=(code:string)=>code==='Ydragon'?[45,46,47]:code==='Ywind'?[41,42,43,44]:meldTiles(code)
type Shape={kind:string;pair:number;closed:string[]}
function interpretations(hand:number[],melds:string[],win:number,qualify:boolean):Shape[]{
  if(melds.length>4||hand.length+3*melds.length!==14||hand.some(t=>!TILES.includes(t)))return []
  const all=[...hand];for(const code of melds){const group=meld(code);if(!group)return [];all.push(...group)}if(all.some(t=>all.filter(x=>x===t).length>4))return []
  const suits=new Set(all.filter(t=>t<40).map(t=>Math.floor(t/10))),eligible=!qualify||all.some(t=>ORPHANS.includes(t))&&(suits.size===3||suits.size===1&&all.every(t=>t<40)),result:Shape[]=[]
  const h=prepare(hand,[],{meldCount:4-melds.length,sevenPairs:melds.length===0})
  if(h)for(const s of shapes(h)){
    if(s.kind==='seven_pairs'){result.push({kind:'seven_pairs',pair:0,closed:[]});continue}
    const pair=Number(s.closed.find(m=>m[0]==='q')!.slice(1)),closed=s.closed.filter(m=>m[0]!=='q')
    if(eligible&&(!qualify||pair>=45||[...closed,...melds].some(m=>m[0]!=='S'&&m[0]!=='s')))result.push({kind:'standard',pair,closed})
  }
  if(eligible&&win>=45&&win<=47&&melds.length<4&&[45,46,47].every(t=>hand.includes(t))){
    const rest=[...hand];for(const t of [45,46,47])rest.splice(rest.indexOf(t),1)
    const reduced=prepare(rest,[],{meldCount:3-melds.length})
    if(reduced)for(const s of shapes(reduced))result.push({kind:'dragon',pair:Number(s.closed.find(m=>m[0]==='q')!.slice(1)),closed:s.closed.filter(m=>m[0]!=='q')})
  }
  return result
}
export function shenyangWaiting(hand:number[],melds:string[],qualify=false){return hand.length+3*melds.length===13?TILES.filter(t=>interpretations([...hand,t],melds,t,qualify).length):[]}
type Score={raw:number;kind:string}
function score(hand:number[],melds:string[],win:number,c:Record<string,any>,self:boolean,finalFour=false):Score|null {
  const before=[...hand];if(!before.includes(win))return null;before.splice(before.indexOf(win),1)
  const single=shenyangWaiting(before,melds,true).length===1,orphanOnly=ORPHANS.includes(win)&&![...before,...melds.flatMap(m=>meld(m)??[])].some(t=>ORPHANS.includes(t)),narrow=single&&(orphanOnly||shenyangWaiting(before,melds).length===1)
  let best:Score|null=null
  for(const s of interpretations(hand,melds,win,true)){
    if(s.kind==='dragon'&&!self)continue;let raw=s.kind==='seven_pairs'?4:0
    if(s.kind!=='seven_pairs'){
      if(hand.length===2)raw+=2
      else if(narrow&&(orphanOnly||s.kind==='dragon'||s.pair===win||s.closed.some(m=>{const t=Number(m.slice(1));return m[0]==='S'&&(t===win||t%10===2&&win===t+1||t%10===8&&win===t-1)})))raw++
      if(s.closed.length+melds.length+Number(s.kind==='dragon')===4&&[...s.closed,...melds].every(m=>m[0]!=='S'&&m[0]!=='s'))raw+=3
      if(s.kind==='dragon')raw+=2
    }
    const all=[...hand,...melds.flatMap(m=>meld(m)??[])];if(all.every(t=>t<40)&&new Set(all.map(t=>Math.floor(t/10))).size===1)raw+=4
    const counts=[...hand],borrowed:number[]=c.borrowed??[]
    for(const code of melds){if(code[0]==='Y'){raw+=2;continue}const t=Number(code.slice(1)),dragon=t>=45
      if('gG'.includes(code[0])||code[0]==='k'&&borrowed.includes(t))raw+=dragon?code[0]==='G'?4:2:code[0]==='G'?2:1
      else {counts.push(...meld(code)!);if(code[0]==='k'&&dragon)raw++}}
    raw+=[...new Set(counts)].filter(t=>counts.filter(x=>x===t).length===4).length
    if(s.kind!=='seven_pairs')raw+=s.closed.filter(m=>m[0]==='K'&&Number(m.slice(1))>=45).length
    raw+=Number(finalFour&&self)+Number(!self)+Number(!!c.long_hair)+Number(!!c.ready)
    if(!best||raw>best.raw||raw===best.raw&&s.kind>best.kind)best={raw,kind:s.kind}
  }
  return best
}
function fan(q:Score,payer:number,c:Record<string,any>){
  const winner=c.index??0,closed:boolean[]=c.closed??[false,false,false,false];let raw=q.raw+Number(winner===0||payer===0)
  if(closed.every(Boolean))raw+=4
  else {if([0,1,2,3].filter(i=>i!==winner).every(i=>closed[i]))raw+=2;else if(closed[payer])raw++;if(closed[winner]&&q.kind!=='seven_pairs')raw++}
  return Math.min(6,raw)
}
export function shenyangDescribe(hand:number[],melds:string[],win:number,c:Record<string,any>,cut:number|null){
  let threshold=c.passed_fan??-1;const winner=c.index??0,others=[0,1,2,3].filter(i=>i!==winner)
  if(cut!==null){const declined=[...hand];declined.splice(declined.indexOf(win),1);declined.push(cut);const q=score(declined,melds,cut,c,false);if(q)threshold=Math.max(threshold,Math.min(...others.map(p=>fan(q,p,c))))}
  const draw=score(hand,melds,win,c,true,!!c.final_four);if(!draw)return {status:'unmet',label:'未满足'}
  const self=others.reduce((n,p)=>n+2**fan(draw,p,c),0),ron=score(hand,melds,win,c,false),points=!ron||c.final_four?[]:others.filter(p=>fan(ron,p,c)>threshold).map(p=>2**fan(ron,p,c))
  if(!points.length)return {status:'self_draw',label:'仅自摸',self_draw_points:self}
  const low=Math.min(...points),high=Math.max(...points)
  return {status:'win',label:`点和${low===high?low:low+'–'+high}分\n自摸${self}分`,ron_min:low,ron_max:high,self_draw_points:self}
}
