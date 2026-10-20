import {TILES,ORPHANS,INDEX,prepare,canWin,canFormPairs,canFormMelds,shapes,winningTiles,shapeTiles,meldTiles,type Policy,type LocalHand} from './localJokerHand'
import {NanchangLocal} from './nanchangLocal'
import {ShangraoLocal} from './shangraoLocal'
import {shenyangWaiting,shenyangDescribe} from './shenyangLocal'

const NUMBERS=TILES.filter(t=>t<40)
export interface LocalHintRequest {rule:string;hand:number[];melds:string[];cuts?:number[];context:Record<string,any>}
export function regionalPolicy(rule:string,c:Record<string,any>,natural=false):Policy {
  let p:Policy
  switch(rule){
    case 'wuhan':{const s=wuhanSpecial(c.indicator);p={jokers:[s.joker],logical:TILES.filter(t=>!s.skins.includes(t)),closedCap:4};break}
    case 'nanjing':p={physical:[...NUMBERS,41,42,43,44],logical:[...NUMBERS,41,42,43,44],sevenPairs:true,meldKinds:'kgG'};break
    case 'jiujiang':p={physical:[...NUMBERS,45,46],logical:[...NUMBERS,45,46],sevenPairs:true};break
    case 'singapore':p={orphans:true};break
    case 'guangdong':p={jokers:[55,56,57,58],physical:[...TILES,55,56,57,58],closedCap:4,sevenPairs:true,orphans:true,meldKinds:'kgG'};break
    case 'hangzhou':p={jokers:[46],sevenPairs:true};break
    case 'hongzhong':p={jokers:[45],physical:[...NUMBERS,45],logical:NUMBERS,closedCap:4,sevenPairs:true,meldKinds:'kgG'};break
    case 'fuzhou':p={jokers:[c.jin],physical:NUMBERS,logical:NUMBERS,supply:{[c.jin]:3},meldCount:5};break
    case 'malaysia':{const logical=[...TILES.filter(t=>t>=21&&t<=29),...TILES.filter(t=>t>=41)];p={jokers:[81],physical:[...logical,81],logical,supply:{81:4},sevenPairs:true,allowExposedJokers:true};break}
    case 'wenzhou':p={jokers:[c.caishen],map:{46:c.caishen},supply:{[c.caishen]:3},meldCount:5};break
    default:throw new Error('Unsupported local rule: '+rule)
  }
  if(c.meld_records)p.meldPhysical=c.meld_records.map((row:any)=>row.physical.map((physical:number,i:number)=>({physical,logical:row.logical[i]})).sort((a:any,b:any)=>a.logical-b.logical).map((x:any)=>x.physical))
  if(natural)p.jokers=[]
  return p
}
function prepared(rule:string,hand:number[],melds:string[],c:Record<string,any>,complete=true,natural=false,tail?:number){
  if(rule==='fuzhou'&&!NUMBERS.includes(c.jin)||rule==='wenzhou'&&!TILES.includes(c.caishen))return null
  return prepare(hand,melds,regionalPolicy(rule,c,natural),complete,tail)
}
function eightPairs(h:LocalHand|null):boolean {
  if(!h||h.melds.length||h.counts.reduce((a,b)=>a+b,0)+h.ghosts!==17)return false
  const need=Math.abs(h.counts.reduce((a,b)=>a+b%2,0)-1);return need<=h.ghosts&&(h.ghosts-need)%2===0
}
function complete(rule:string,hand:number[],melds:string[],c:Record<string,any>):boolean {
  const h=prepared(rule,hand,melds,c);return !!h&&(canWin(h)||rule==='wenzhou'&&(h.ghosts>=3||eightPairs(h))||rule==='guangdong'&&h.ghosts===4)
}
export function regionalWaiting(rule:string,hand:number[],melds:string[],c:Record<string,any>):number[]{
  if(rule==='shangrao')return new ShangraoLocal(melds,c).waiting(hand)
  if(rule==='shenyang')return shenyangWaiting(hand,melds)
  if(rule==='nanchang')return new NanchangLocal(melds,c).waiting(hand)
  if(rule==='wuhan')return TILES.filter(tile=>wuhanShapes([...hand,tile],melds,c).length>0)
  const h=prepared(rule,hand,melds,c,false);if(!h)return []
  return (h.policy.physical??TILES).filter(t=>complete(rule,[...hand,t],melds,c))
}
function hangzhouScore(hand:number[],melds:string[],win:number,c:Record<string,any>,cut:number|null):number {
  const h=prepared('hangzhou',hand,melds,c);if(!h||!canWin(h))return -1
  const pairs=!melds.length&&canFormPairs(h),luxury=hand.some(t=>t!==46&&hand.filter(x=>x===t).length===4)||hand.filter(t=>t===46).length===4&&hand.filter(t=>t!==46).every(t=>hand.filter(x=>x===t).length%2===0)
  let fan=pairs?luxury?4:2:0;if(!hand.includes(46))fan++
  const before=[...hand];before.splice(before.indexOf(win),1)
  if(before.includes(46)){
    before.splice(before.indexOf(46),1);const rest=prepared('hangzhou',before,melds,c,true,false,0)
    if(canFormMelds(rest)||!melds.length&&canFormPairs(rest))fan+=1+(cut===null?c.cai_piao_count??0:cut===46?(c.cai_piao_count??0)+1:0)
  }
  return Math.min(4,fan)
}
function hongzhongScore(hand:number[],melds:string[],win:number,c:Record<string,any>):number {
  const h=prepared('hongzhong',hand,melds,c);if(!h)return -1
  let best=-1;const external=melds.flatMap(m=>meldTiles(m)??[])
  for(const shape of shapes(h))for(const logicalWin of winningTiles(h,shape,win)){
    let fan=hand.includes(45)?0:1;const all=[...shapeTiles(shape),...external]
    if(new Set(all.map(t=>Math.floor(t/10))).size===1)fan+=2
    if(shape.kind==='seven_pairs')fan+=shape.logical[INDEX.get(logicalWin)!]===4?3:2
    else {if(shape.closed.every(s=>s[0]!=='S'))fan++;if([1,2,3].some(s=>['S'+(s*10+2),'S'+(s*10+5),'S'+(s*10+8)].every(code=>shape.closed.includes(code))))fan++}
    best=Math.max(best,Math.min(4,fan));if(best===4)return best
  }
  return best
}
function allPungs(hand:number[],melds:string[],jin:number,pair:boolean):boolean {
  if(melds.some(m=>m[0]==='s'))return false
  const counts=new Map<number,number>();for(const t of hand)if(t!==jin)counts.set(t,(counts.get(t)??0)+1)
  const gold=hand.filter(t=>t===jin).length
  for(const choice of pair?[0,...counts.keys()]:[0]){
    const cost=pair?Math.max(0,2-(counts.get(choice)??0)):0,need=[...counts].reduce((sum,[t,n])=>sum+(3-Math.max(0,n-(t===choice?2:0))%3)%3,0)
    if(cost+need<=gold&&(gold-cost-need)%3===0)return true
  }
  return false
}
function fuzhouScore(hand:number[],melds:string[],c:Record<string,any>):number {
  const jin=c.jin;if(!complete('fuzhou',hand,melds,c))return 0
  const gold=hand.filter(t=>t===jin).length;let patterns=0
  for(const size of [3,2,0]){
    if(gold<size)continue;const rest=[...hand.filter(t=>t!==jin),...Array(gold-size).fill(jin)]
    if(size===3&&!canWin(prepare(rest,melds,{...regionalPolicy('fuzhou',c),meldCount:4})))continue
    if(size===2&&!canFormMelds(prepared('fuzhou',rest,melds,c,true,false,0)))continue
    patterns=(size===3?120:size===2?60:0)+(allPungs(rest,melds,jin,size!==2)?15:0);break
  }
  if(hand.length===2&&melds.length===5)patterns+=60
  const physical=[...hand,...melds.flatMap(m=>meldTiles(m)??[])];if(new Set(physical.filter(t=>t!==jin).map(t=>Math.floor(t/10))).size===1)patterns+=gold>0?30:60
  const flowers:number[]=c.flowers??[],sets=TILES.filter(t=>t>=41).filter(t=>flowers.filter(x=>x===t).length===4).length+[51,55].filter(start=>[0,1,2,3].every(i=>flowers.includes(start+i))).length
  if(flowers.length===1)patterns+=15;if(!flowers.length&&!melds.some(m=>'gG'.includes(m[0])))patterns+=30
  if(c.golden_middle)patterns+=40;patterns+=60*(c.youjin??0)
  return (c.base_score??15)+flowers.length+sets*2+gold+melds.reduce((sum,m)=>sum+(m[0]==='G'?2:m[0]==='g'?1:0),0)+(c.dealer_streak??0)*3+patterns
}
function wenzhouScore(hand:number[],melds:string[],c:Record<string,any>,self:boolean):number {
  const h=prepared('wenzhou',hand,melds,c);if(!h)return 0
  const standard=canWin(h),eight=eightPairs(h),natural=prepared('wenzhou',hand,melds,c,true,true);let value=0
  if(standard)value=self||canWin(natural)?2:1;if(eight)value=Math.max(value,self||eightPairs(natural)?4:1)
  if(h.ghosts>=3)value=Math.max(value,standard||eight?4:2);return value
}
export function regionalDescribe(rule:string,hand:number[],melds:string[],win:number,c:Record<string,any>,cut:number|null=null):Record<string,any>{
  if(rule==='shangrao')return new ShangraoLocal(melds,c).describe(hand,win,cut)
  if(rule==='shenyang')return shenyangDescribe(hand,melds,win,c,cut)
  if(rule==='nanchang')return new NanchangLocal(melds,c).describe(hand,win,cut)
  switch(rule){
    case 'wuhan':return wuhanDescribe(hand,melds,win,c)
    case 'nanjing':return nanjingDescribe(hand,melds,win,c,cut)
    case 'jiujiang':case 'singapore':{
      const ron=rule==='jiujiang'?jiujiangScore(hand,melds,win,c,false,cut):singaporeScore(hand,melds,win,c,false),self=ron?0:rule==='jiujiang'?jiujiangScore(hand,melds,win,c,true,cut):singaporeScore(hand,melds,win,c,true)
      return {points:Math.max(ron,self),kind:ron?'dianhe':self?'zimo':'wuyi'}
    }
    case 'guangdong':return guangdongDescribe(hand,melds,win,c,cut)
    case 'hangzhou':case 'hongzhong':{
      const fan=rule==='hangzhou'?hangzhouScore(hand,melds,win,c,cut):hongzhongScore(hand,melds,win,c),blocked=fan<0||c.blocked
      return {kind:blocked?'wuyi':'zimo',fan:Math.max(0,fan),base_score:fan<0?0:1<<fan,label:blocked?'未满足':`自摸${fan}番${1<<fan}分`}
    }
    case 'fuzhou':{
      const limited=c.locked_waits?.length&&!c.locked_waits.includes(win),self=c.discarded_gold||cut===c.jin||c.golden_middle||c.youjin_active||win===c.jin||c.last_four,points=limited||c.blocked?0:fuzhouScore(hand,melds,c)
      return {kind:points===0?'unmet':self?'self_only':'ron',points,label:points===0?'未满足':points+'分'}
    }
    case 'wenzhou':{
      const ron=wenzhouScore(hand,melds,c,false),self=wenzhouScore(hand,melds,c,true);let threshold=c.passed_multiplier??0
      if(cut!==null){const declined=[...hand];declined.splice(declined.indexOf(win),1);declined.push(cut);threshold=Math.max(threshold,wenzhouScore(declined,melds,c,false))}
      return {tile:win,ron:ron>threshold,self_draw:self>0,ron_multiplier:ron,self_draw_multiplier:self,kind:ron>threshold?'dianhe':self>0?'zimo':'wuyi',label:ron>threshold?`点和${ron}倍\n自摸${self}倍`:self>0?`自摸${self}倍`:'未满足'}
    }
    case 'malaysia':return {kind:'wuyi',label:'结构听牌 · 须满5番或满番'}
    default:throw new Error('Unsupported local hint rule: '+rule)
  }
}
function guangdongDescribe(hand:number[],melds:string[],win:number,c:Record<string,any>,cut:number|null){
  const h=prepared('guangdong',hand,melds,c),discarded=(c.discarded_ghosts??0)+(cut!==null&&cut>=55&&cut<=58?1:0)
  let ordinary=-1,four=false
  if(h&&h.ghosts+discarded<=4&&discarded>=0&&discarded<=4){
    four=h.ghosts===4
    for(const shape of shapes(h))for(const logicalWin of winningTiles(h,shape,win)){
      const all=[...shapeTiles(shape),...melds.flatMap(m=>meldTiles(m)??[])],suits=new Set(all.filter(t=>t<40).map(t=>Math.floor(t/10))),honors=all.some(t=>t>=41)
      let concealed=melds.every(m=>m[0]==='G')?1:0,flush=suits.size===1?honors?4:8:0,fan=concealed+flush
      if(shape.kind==='seven_pairs')fan=6+flush
      else if(shape.kind==='thirteen_orphans')fan=16
      else {
        const codes=[...melds,...shape.closed],pungs=new Set(codes.filter(m=>'kKgG'.includes(m[0])).map(m=>Number(m.slice(1)))),pair=Number(shape.closed.find(m=>m[0]==='q')!.slice(1)),allPungs=codes.every(m=>m[0]!=='S')
        const terminal=allPungs&&all.every(t=>t>=41||t%10===1||t%10===9),winds=[41,42,43,44].filter(t=>pungs.has(t)),dragons=[45,46,47].filter(t=>pungs.has(t))
        if(allPungs)fan+=4
        if(terminal)fan+=12-4
        if(dragons.length===3)fan+=16;else if(dragons.length===2&&pair>=45&&!pungs.has(pair))fan+=12
        if(winds.length===4)fan+=16-(allPungs&&!terminal?4:0);else if(winds.length===3&&pair>=41&&pair<=44&&!pungs.has(pair))fan+=12
        if(!melds.length){const before=shapeTiles(shape);before.splice(before.indexOf(logicalWin),1)
          if([1,2,3].some(s=>before.every(t=>Math.floor(t/10)===s)&&[1,1,1,2,3,4,5,6,7,8,9,9,9].every((r,i)=>before.sort((a,b)=>a-b)[i]===s*10+r)))fan+=16-concealed-flush
        }
      }
      ordinary=Math.max(ordinary,fan)
    }
  }
  const coefficient=(h?.ghosts===0?2:1)*2**discarded,ronFan=Math.max(ordinary,four?12:-1),selfFan=Math.max(ordinary>=0?ordinary+1:-1,four?12:-1)
  const ron=!(win>=55&&win<=58)&&ronFan>=2&&(c.require_minimum_score===false||ronFan*coefficient>=4),self=selfFan>=2&&(c.require_minimum_score===false||selfFan*coefficient>=4)
  const r=ron?ronFan:0,s=self?selfFan:0,rs=r*coefficient,ss=s*coefficient
  return {tile:win,ron,self_draw:self,ron_fan:r,self_draw_fan:s,ron_score:rs,self_draw_score:ss,fan:Math.max(r,s),score:Math.max(rs,ss),kind:ron?'dianhe':self?'zimo':'wuyi',label:ron?`点和${r}番${rs}分`+(self?`\n自摸${s}番${ss}分`:''):self?`自摸${s}番${ss}分`:'未起和'}
}
const WUHAN_WINDS=[41,42,43,44,47,46],WUHAN_JIANG=NUMBERS.filter(t=>[2,5,8].includes(t%10))
function wuhanSpecial(indicator:number){
  if(indicator<40)return {joker:Math.floor(indicator/10)*10+indicator%10%9+1,skins:[Math.floor(indicator/10)*10+(indicator%10+7)%9+1,indicator,45]}
  const i=WUHAN_WINDS.indexOf(indicator===45?44:indicator)
  return {joker:WUHAN_WINDS[(i+1)%6],skins:[WUHAN_WINDS[(i+5)%6],indicator===45?44:indicator,45]}
}
type WuhanShape={names:number;gold:boolean;hard:boolean;seed:boolean}
function wuhanShapes(hand:number[],melds:string[],c:Record<string,any>):WuhanShape[]{
  if(!TILES.includes(c.indicator))return []
  const {joker,skins}=wuhanSpecial(c.indicator),h=prepared('wuhan',hand,melds,c),result:WuhanShape[]=[]
  if(!h||hand.some(t=>skins.includes(t)))return result
  const external=melds.flatMap(m=>meldTiles(m)??[]);if(external.some(t=>t===joker||skins.includes(t)))return result
  const ordinary=[...hand.filter(t=>t!==joker),...external]
  for(const domain of [WUHAN_WINDS,WUHAN_JIANG])if(ordinary.every(t=>domain.includes(t)))result.push({names:1,gold:domain===WUHAN_WINDS,hard:domain.includes(joker)||!hand.includes(joker),seed:true})
  const physical=TILES.map(t=>hand.filter(x=>x===t).length)
  for(const shape of shapes(h)){
    const all=[...shapeTiles(shape),...external],pair=Number(shape.closed.find(m=>m[0]==='q')!.slice(1)),gold=all.every(t=>WUHAN_WINDS.includes(t))
    result.push({names:Number([...shape.closed,...melds].every(m=>!['S','s'].includes(m[0])))+Number(all.every(t=>t<40)&&new Set(all.map(t=>Math.floor(t/10))).size===1)+Number(all.every(t=>WUHAN_JIANG.includes(t)))+Number(gold),gold,hard:shape.logical.every((n,i)=>n===physical[i]),seed:WUHAN_JIANG.includes(pair)&&hand.includes(pair)})
  }
  return result
}
function wuhanTableFan(row:any,joker:number){
  return row.melds.filter((m:string)=>m[0]!=='G').length+row.melds.reduce((n:number,m:string)=>n+(m[0]==='G'?2:m[0]==='g'?(row.added??[]).includes(Number(m.slice(1)))?1:2:0),0)+row.specials.reduce((n:number,t:number)=>n+(t===joker?2:t===45?1:0),0)
}
function wuhanPayment(shape:WuhanShape,names:number,c:Record<string,any>,payer:number|null):number {
  const {table,index:winner}=c,{joker}=wuhanSpecial(c.indicator),others=[0,1,2,3].filter(i=>i!==winner),shapeFan=(names?names+2:0)+Number(shape.hard)
  const fans=others.map(i=>wuhanTableFan(table[winner],joker)+wuhanTableFan(table[i],joker)+shapeFan+Number(!names&&(winner===0||i===0))+Number(payer===null||payer===i))
  if(!shape.gold&&Math.min(...fans)<6)return 0
  const gold=shape.gold||Math.min(...fans)>=10,unopened=others.filter(i=>!table[i].melds.length&&!table[i].specials.length)
  return others.reduce((sum,i,n)=>sum+(gold?100:[0,0,0,0,0,0,10,20,40,60,80][Math.min(10,fans[n])])*(WUHAN_WINDS.includes(joker)||WUHAN_JIANG.includes(joker)||gold&&(c.previous_gold===winner||c.previous_gold===i)?2:1)*(unopened.length===3?4:unopened.includes(i)?2:1),0)
}
function wuhanDescribe(hand:number[],melds:string[],win:number,c:Record<string,any>){
  const row=(kind:string,label:string,points:number)=>({kind,label,points})
  if(c.blocked||melds.every(m=>m[0]==='G'))return row('none','未满足',0)
  const {joker,skins}=wuhanSpecial(c.indicator),sea=!!c.sea,shapeFan=(s:WuhanShape)=>(s.names+Number(sea)?s.names+Number(sea)+2:0)+Number(s.hard)
  const best=wuhanShapes(hand,melds,c).filter(s=>(s.names||s.seed)&&(s.names||sea||hand.filter(t=>t===joker).length<2)).sort((a,b)=>Number(b.gold)-Number(a.gold)||shapeFan(b)-shapeFan(a))[0]
  if(!best)return row('none','未满足',0)
  const names=best.names+Number(sea),ron=!sea&&win!==joker&&!skins.includes(win)?[0,1,2,3].filter(i=>i!==c.index).map(i=>wuhanPayment(best,names,c,i)).filter(p=>p>0):[]
  if(ron.length){const low=Math.min(...ron),high=Math.max(...ron);return row('ron',low===high?low+'点':`${low}～${high}点`,low)}
  const self=wuhanPayment(best,names,c,null);return self?row('tsumo_only','仅自摸',self):row('below_minimum','未起和',0)
}
function nanjingEvaluate(hand:number[],melds:string[],win:number,c:Record<string,any>){
  const h=prepared('nanjing',hand,melds,c),flowers:number[]=c.flowers??[]
  if(!h||!hand.includes(win)||flowers.some(t=>!(t>=45&&t<=47||t>=51&&t<=58)||flowers.filter(x=>x===t).length>(t<50?4:1)))return null
  const before=[...hand];before.splice(before.indexOf(win),1)
  const unique=regionalWaiting('nanjing',before,melds,c).length===1,all=[...hand,...melds.flatMap(m=>meldTiles(m)??[])],suits=new Set(all.filter(t=>t<40).map(t=>Math.floor(t/10))).size,honors=all.some(t=>t>=41),pung=c.pung_claimed??melds.some(m=>m[0]==='k')
  let best:{points:number;pattern:number;raw:number;qualified:boolean}|null=null
  for(const shape of shapes(h)){
    let pattern=suits===1?honors?20:30:0,qualified=suits===1;const seven=shape.kind==='seven_pairs'
    if(seven){pattern+=before.filter(t=>t===win).length===3?90:40;qualified=true}
    else if(hand.length===2){pattern+=60;qualified=true}
    else if([...shape.closed,...melds].every(m=>!['S','s'].includes(m[0]))){pattern+=20;qualified=true}
    if(!pung&&!seven){pattern+=10;qualified=true}if(!flowers.length)pattern+=20
    const narrow=!seven&&unique&&shape.closed.some(m=>{const t=Number(m.slice(1));return m==='q'+win||m[0]==='S'&&(t===win||t%10===2&&win===t+1||t%10===8&&win===t-1)}),last=narrow&&(c.public_pungs??[]).includes(win)
    if(last)pattern+=20;qualified||=flowers.length>=4
    const winds=seven?0:[...shape.closed,...melds].filter(m=>'kKgG'.includes(m[0])&&Number(m.slice(1))>=41&&Number(m.slice(1))<=44).length
    const raw=10+pattern+flowers.length+Number(suits===2)+Number(narrow&&!last)+winds+melds.reduce((n,m)=>n+(m[0]==='G'?2:m[0]==='g'?1:0),0),q={points:Math.min(120,raw),pattern,raw,qualified}
    if(!best||q.qualified&&!best.qualified||q.qualified===best.qualified&&q.raw>best.raw)best=q
  }
  return best
}
function nanjingDescribe(hand:number[],melds:string[],win:number,c:Record<string,any>,cut:number|null){
  let water:number|null=c.passed_win_value??null
  if(cut!==null){const declined=[...hand];declined.splice(declined.indexOf(win),1);declined.push(cut);const old=nanjingEvaluate(declined,melds,cut,c);if(old?.qualified)water=Math.max(water??0,old.pattern)}
  const q=nanjingEvaluate(hand,melds,win,c)
  return {kind:!q||c.peida?'unmet':!q.qualified?'not_ready':water!==null&&q.pattern<=water?'self_only':'ron',points:q?.qualified?q.points:0,pattern_points:q?.pattern??0,flower_count:(c.flowers??[]).length}
}
function jiujiangChain(groups:number[],luxury:boolean):number {
  if(!groups.length)return 0
  const first=groups[0];let best=jiujiangChain(groups.slice(1),luxury)
  if(luxury&&groups.filter(t=>t===first).length>=2){const rest=[...groups];rest.splice(rest.indexOf(first),1);rest.splice(rest.indexOf(first),1);best=Math.max(best,2+jiujiangChain(rest,luxury))}
  if(first<40)for(let length=3;length<=10-first%10;length++){const run=Array.from({length},(_,i)=>first+i);if(!run.every(t=>groups.includes(t)))break;const rest=[...groups];for(const t of run)rest.splice(rest.indexOf(t),1);best=Math.max(best,length-1+jiujiangChain(rest,luxury))}
  return best
}
function jiujiangScore(hand:number[],melds:string[],win:number,c:Record<string,any>,self:boolean,cut:number|null):number {
  const flowers:number[]=c.flowers??[];if(flowers.length>4||flowers.some(t=>t!==47))return 0
  const h=prepared('jiujiang',hand,melds,c);if(!h)return 0
  const before=[...hand];before.splice(before.indexOf(win),1);const single=regionalWaiting('jiujiang',before,melds,c).length===1,all=[...hand,...melds.flatMap(m=>meldTiles(m)??[])],suits=new Set(all.filter(t=>t<40).map(t=>Math.floor(t/10))).size,common=suits===1?all.some(t=>t>=41)?1:4:0;let best=0
  for(const shape of shapes(h)){
    const sets=shape.closed.filter(m=>m[0]!=='q'),codes=[...sets,...melds];let big=common
    if(shape.kind==='seven_pairs'){
      const groups=[...new Set(hand)].flatMap(t=>Array(hand.filter(x=>x===t).length/2).fill(t)).sort((a,b)=>a-b);big+=4+jiujiangChain(groups,true)
      if(new Set(groups).size===7&&groups[0]<40&&groups[6]===groups[0]+6&&suits===1)big+=6
    }else{
      for(const suit of [1,2,3])if([2,5,8].every(r=>codes.some(m=>m[0].toLowerCase()==='s'&&Number(m.slice(1))===suit*10+r)))big+=2
      if(codes.every(m=>m[0].toLowerCase()!=='s'))big+=2+jiujiangChain(codes.map(m=>Number(m.slice(1))).sort((a,b)=>a-b),false)
    }
    let points=big+Number(melds.length===4)+(cut===null&&!c.first_discard_done?10:0)+(c.declared?10:0)
    points+=(big?2:1)*(Number(melds.every(m=>m[0]==='G'))+Number(self)+Number(single));const arrows=new Set()
    for(const code of codes){if(code[0].toLowerCase()==='s')continue;const t=Number(code.slice(1));if(t===45||t===46){arrows.add(t);points+=code[0]==='G'||sets.includes(code)&&(self||t!==win)?2:1}points+=code[0]==='G'?2:code[0]==='g'?1:0}
    if(arrows.size===2)points+=2;if(flowers.length)points++;if(flowers.length===4)points+=3;best=Math.max(best,points)
  }
  return best
}
function nineGates(hand:number[],melds:string[]){return !melds.length&&hand.length===14&&hand.every(t=>t<40)&&new Set(hand.map(t=>Math.floor(t/10))).size===1&&[1,2,3,4,5,6,7,8,9].every(r=>hand.filter(t=>t%10===r).length>=([1,9].includes(r)?3:1))}
function singaporeScore(hand:number[],melds:string[],win:number,c:Record<string,any>,self:boolean):number {
  const seat=c.seat??0,round=c.round_wind??41,flowers:number[]=c.flowers??[]
  if(seat<0||seat>3||round<41||round>44||new Set(flowers).size!==flowers.length||flowers.some(t=>!(t>=51&&t<=58||t>=61&&t<=64)))return 0
  const h=prepared('singapore',hand,melds,c);if(!h)return 0
  const before=[...hand];before.splice(before.indexOf(win),1);const multiple=regionalWaiting('singapore',before,melds,c).length>=2
  const animals=flowers.filter(t=>t>=61).length,bonus=[51,55].reduce((n,start)=>n+([0,1,2,3].every(i=>flowers.includes(start+i))?2:flowers.includes(start+seat)?1:0),0)+(animals===4?5:animals);let best=0
  for(const shape of shapes(h)){
    if(shape.kind==='thirteen_orphans')return 5
    const codes=[...shape.closed.filter(m=>m[0]!=='q'),...melds],pungs=codes.filter(m=>'kKgG'.includes(m[0])).map(m=>Number(m.slice(1))),pair=Number(shape.closed.find(m=>m[0]==='q')!.slice(1)),all=[...hand,...melds.flatMap(m=>meldTiles(m)??[])],allPungs=codes.every(m=>m[0].toLowerCase()!=='s'),dragons=pungs.filter(t=>t>=45).length,winds=pungs.filter(t=>t>=41&&t<=44).length
    if(dragons===3||winds===4||all.every(t=>t>=41)||allPungs&&self&&melds.every(m=>m[0]==='G')||melds.filter(m=>'gG'.includes(m[0])).length===4||all.every(t=>t<40&&[1,9].includes(t%10))||nineGates(hand,melds))return 5
    let fan=bonus+(dragons===2&&pair>=45?3:dragons)+(winds===3&&pair>=41&&pair<=44?3+Number(seat+41===round&&pungs.includes(round)):Number(pungs.includes(seat+41))+Number(pungs.includes(round)))
    if(allPungs)fan+=2
    const suits=new Set(all.filter(t=>t<40).map(t=>Math.floor(t/10))).size,flush=suits===1&&all.every(t=>t<40),green=all.every(t=>[32,33,34,36,38,47].includes(t));fan+=green||flush?4:suits===1?2:0
    if(allPungs&&pair<40&&[1,9].includes(pair%10)&&all.every(t=>ORPHANS.includes(t)))fan+=2
    let ping=codes.every(m=>m[0].toLowerCase()==='s')&&pair<45&&pair!==seat+41&&pair!==round
    if(ping&&!self)ping=multiple&&(pair===win||shape.closed.filter(m=>m[0]==='S').some(m=>{const t=Number(m.slice(1));return Math.abs(t-win)===1&&!(win%10===3&&(t-1)%10===1)&&!(win%10===7&&(t+1)%10===9)}))
    if(ping)fan+=!flowers.length&&flush?1:flowers.length?1:4;best=Math.max(best,Math.min(5,fan))
  }
  return best
}
export function regionalSnapshot(request:LocalHintRequest){
  const {rule,hand,melds,context:c={}}=request
  const nanchang=rule==='nanchang'?new NanchangLocal(melds,c):null
  const shangrao=rule==='shangrao'?new ShangraoLocal(melds,c):null
  const result={source_hand_tiles:[...hand],source_melds:[...melds],win_blocked:!!c.blocked,waiting_tiles:[] as number[],waiting_details:{} as Record<number,any>,waiting_by_discard:{} as Record<number,number[]>,waiting_details_by_discard:{} as Record<number,any>}
  function rows(before:number[],cut:number|null){
    const waits=shangrao?.waiting(before)??nanchang?.waiting(before)??regionalWaiting(rule,before,melds,c),details=Object.fromEntries(waits.map(tile=>[tile,{tile,...(shangrao?.describe([...before,tile],tile,cut)??nanchang?.describe([...before,tile],tile,cut)??regionalDescribe(rule,[...before,tile],melds,tile,c,cut))}]))
    if(cut===null){result.waiting_tiles=waits;result.waiting_details=details}else {result.waiting_by_discard[cut]=waits;result.waiting_details_by_discard[cut]=details}
  }
  if(hand.length%3===1)rows(hand,null)
  else if(hand.length%3===2)for(const cut of request.cuts??[...new Set(hand)].sort((a,b)=>a-b)){const before=[...hand],index=before.indexOf(cut);if(index>=0){before.splice(index,1);rows(before,cut)}}
  return result
}
