import {PHYSICAL, TILES, DOMAIN, SUPPLY, joker, face} from './duocaiTiles'
import {duocaiShapes, type DuocaiMeld, type Variant} from './duocaiShapes'
import {scoreDuocaiShape, meetsConditions, type DuocaiWin, type DuocaiContext} from './duocaiScoring'
export {duocaiShapes} from './duocaiShapes'
export {scoreDuocaiShape, riichiBasePoints} from './duocaiScoring'
export {PHYSICAL, DOMAIN, SUPPLY} from './duocaiTiles'

const compare=(a:number[],b:number[])=>{for(let i=0;i<a.length;i++)if(a[i]!==b[i])return a[i]-b[i];return 0}
const scoreKey=(s:DuocaiWin)=>[s.base_points,s.fan,s.fu]
export function* duocaiScores(hand:number[],melds:DuocaiMeld[]=[],variant:Variant='guobiao',context:DuocaiContext={}):Generator<DuocaiWin> {
  if(!['guobiao','riichi'].includes(variant))throw new Error('夺彩仅开放国标与立直')
  for(const shape of duocaiShapes(hand,melds,variant))yield scoreDuocaiShape(shape,variant,context)
}
const scoreCache=new Map<string,DuocaiWin|null>()
function evaluateContexts(hand:number[],melds:DuocaiMeld[],variant:Variant,contexts:DuocaiContext[]):(DuocaiWin|null)[] {
  if(!['guobiao','riichi'].includes(variant))throw new Error('夺彩仅开放国标与立直')
  const keys=contexts.map(context=>JSON.stringify([hand,melds,variant,context]))
  const missing=keys.flatMap((key,i)=>scoreCache.has(key)?[]:[i])
  if(!missing.length)return keys.map(key=>scoreCache.get(key)!)
  const best:(DuocaiWin|null)[]=contexts.map(()=>null),fallback:(DuocaiWin|null)[]=contexts.map(()=>null)
  for(const shape of duocaiShapes(hand,melds,variant))for(const i of missing){
    const result=scoreDuocaiShape(shape,variant,contexts[i]),prior=fallback[i],valid=best[i]
    if(!prior||compare([Number(result.meets_conditions),Number(result.meets_minimum),...scoreKey(result)],[Number(prior.meets_conditions),Number(prior.meets_minimum),...scoreKey(prior)])>0)fallback[i]=result
    if(result.valid&&(!valid||compare(scoreKey(result),scoreKey(valid))>0))best[i]=result
  }
  const results=keys.map((key,i)=>missing.includes(i)?best[i]??fallback[i]:scoreCache.get(key)!)
  if(scoreCache.size+missing.length>=2048)scoreCache.clear()
  results.forEach((result,i)=>scoreCache.set(keys[i],result))
  return results
}
export function evaluateDuocai(hand:number[],melds:DuocaiMeld[]=[],variant:Variant='guobiao',context:DuocaiContext={}):DuocaiWin|null {
  return evaluateContexts(hand,melds,variant,[context])[0]
}
const structureCache=new Map<string,boolean>()
export function structuralDuocai(hand:number[],melds:DuocaiMeld[]=[],variant:Variant='guobiao'):boolean {
  const key=JSON.stringify([hand,melds,variant])
  if(structureCache.has(key))return structureCache.get(key)!
  const result=!duocaiShapes(hand,melds,variant).next().done
  if(structureCache.size>=8192)structureCache.clear()
  structureCache.set(key,result)
  return result
}
export function logicalDuocaiWaits(hand:number[],melds:DuocaiMeld[]=[],variant:Variant='riichi'):number[] {
  return TILES.filter(tile=>PHYSICAL.some(t=>!joker(t)&&face(t)===tile&&structuralDuocai([...hand,t],melds,variant)))
}
export function duocaiDiscardFuriten(hand:number[],melds:DuocaiMeld[],discards:number[],winningTile:number,context:DuocaiContext={}):boolean {
  const discarded=new Set<number>(),conditional=new Set<number>()
  for(const value of discards){
    if([5030,5031].includes(value))continue
    if([5036,5037,5038].includes(value))conditional.add(value)
    else for(const tile of DOMAIN.get(value)??[])discarded.add(tile)
  }
  if(discarded.size&&logicalDuocaiWaits(hand,melds).some(t=>discarded.has(t)))return true
  if(!conditional.size)return false
  const effective=[...duocaiScores([...hand,winningTile],melds,'riichi',context)].filter(s=>s.valid)
  return effective.length>0&&[...conditional].some(t=>effective.every(s=>meetsConditions([t],s.visible,s.fu,'riichi')))
}
export function duocaiWinPoints(score:DuocaiWin|null,self:boolean,dealer=false,honba=0):number {
  if(!score?.valid||score.fan<=0)return 0
  if(score.variant==='guobiao')return self?3*(100+score.fan):300+score.fan
  const round=(n:number)=>Math.ceil(n/100)*100,base=score.base_points
  return (self?dealer?3*round(base*2):round(base*2)+2*round(base):round(base*(dealer?6:4)))+300*honba
}
export class DuocaiLocal {
  readonly melds:DuocaiMeld[]
  readonly variant:Variant
  constructor(_melds:string[],readonly context:Record<string,any>={}){
    this.melds=(context.physical_melds??[]).map((m:any)=>({physical:[...m.physical],concealed:!!m.concealed,declared:m.declared??'peng',source:m.source??-1}))
    this.variant=context.variant??'guobiao'
    if(!['guobiao','riichi'].includes(this.variant))throw new Error('夺彩仅开放国标与立直')
  }
  waiting(hand:number[]):number[]{return PHYSICAL.filter(t=>structuralDuocai([...hand,t],this.melds,this.variant))}
  describe(hand:number[],win:number,cut:number|null=null):Record<string,any>{
    const c=this.context,before=hand.slice(0,-1),discards=[...(c.discards??[]),...(cut===null?[]:[cut])]
    const visible=[...(c.visible_tiles??[]),...(cut===null?[]:[cut])]
    const common:DuocaiContext={seat_wind:c.seat_wind??42,round_wind:c.round_wind??41,riichi:c.riichi??0,
      ippatsu:cut===null&&!!c.ippatsu,indicators:c.indicators??[],last_copy:visible.filter((t:number)=>face(t)===face(win)).length===3&&face(win)!==0}
    const ronContext={...common,tsumo:false,last:c.remaining===0},selfContext={...common,tsumo:true,
      rinshan:cut===null&&!!c.has_draw_slot&&!!c.after_kong,last:cut===null&&c.has_draw_slot?c.remaining===0:c.remaining===1}
    const [ron,self]=evaluateContexts(hand,this.melds,this.variant,[ronContext,selfContext])
    const furiten=this.variant==='riichi'&&(!!c.riichi_furiten||cut===null&&!!c.temporary_furiten||duocaiDiscardFuriten(before,this.melds,discards,win,ronContext))
    const canRon=!!ron?.valid&&!furiten&&!c.blocked,canSelf=!!self?.valid&&!c.blocked
    const dealer=(c.seat_wind??42)===41,ronPoints=duocaiWinPoints(ron,false,dealer,c.honba??0),selfPoints=duocaiWinPoints(self,true,dealer,c.honba??0)
    let kind='wuyi',label='未满足',points=0
    if(canRon){kind=canSelf?'dianhe':'ron_only';points=ronPoints;label=canSelf?`${points}点`:`仅点和\n${points}点`}
    else if(canSelf){kind='zimo';points=selfPoints;label=`仅自摸\n${points}点`}
    else if((ron?.meets_conditions&&!ron.meets_minimum||self?.meets_conditions&&!self.meets_minimum)&&!c.blocked){label='未起和'}
    return {tile:win,kind,label,points,ron_points:canRon?ronPoints:0,self_points:canSelf?selfPoints:0,can_ron:canRon,can_self:canSelf,
      fan:canRon?ron!.fan:canSelf?self!.fan:Math.max(ron?.fan??0,self?.fan??0),furiten,
      remaining:Math.max(0,(SUPPLY.get(win)??0)-(c.visible_tiles??[]).filter((t:number)=>t===win).length-before.filter(t=>t===win).length-Number(cut===win))}
  }
}
export function duocaiSnapshot(request:{hand:number[];melds:string[];cuts?:number[];context:Record<string,any>}){
  const {hand,melds,context:c={}}=request,engine=new DuocaiLocal(melds,c)
  const result={source_hand_tiles:[...hand],source_melds:[...melds],win_blocked:!!c.blocked,waiting_tiles:[] as number[],waiting_details:{} as Record<number,any>,waiting_by_discard:{} as Record<number,number[]>,waiting_details_by_discard:{} as Record<number,any>}
  const rows=(before:number[],cut:number|null)=>{
    const waits=engine.waiting(before),details=Object.fromEntries(waits.map(tile=>[tile,engine.describe([...before,tile],tile,cut)]))
    if(cut===null){result.waiting_tiles=waits;result.waiting_details=details}
    else{result.waiting_by_discard[cut]=waits;result.waiting_details_by_discard[cut]=details}
  }
  if(hand.length%3===1)rows(hand,null)
  else if(hand.length%3===2)for(const cut of request.cuts??[...new Set(hand)]){
    const before=[...hand],index=before.indexOf(cut)
    if(index>=0){before.splice(index,1);rows(before,cut)}
  }
  return result
}
export function duocaiHintRequest(request:{hand:number[];melds:string[];cuts?:number[];context:Record<string,any>;scope?:string;cut?:number|null;win?:number}){
  if(request.scope==='waiting')return {waiting_tiles:new DuocaiLocal(request.melds,request.context).waiting(request.hand)}
  if(request.scope==='describe'){
    if(!Number.isInteger(request.win)||request.hand.at(-1)!==request.win)throw new Error('和牌实体必须位于和牌槽')
    return {row:new DuocaiLocal(request.melds,request.context).describe(request.hand,request.win!,request.cut??null)}
  }
  return duocaiSnapshot(request)
}
