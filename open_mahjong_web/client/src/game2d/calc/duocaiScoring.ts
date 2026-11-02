import {Chinese_Hepai_Check, PlayerTiles} from './guobiao/gbHepai'
import {tingpaiCheck} from './guobiao/gbTingpai'
import {joker, bonus, dora} from './duocaiTiles'
import {KNITTED, groupCode, type DuocaiShape, type Variant} from './duocaiShapes'

export interface DuocaiContext {
  tsumo?:boolean;seat_wind?:number;round_wind?:number;riichi?:number;ippatsu?:boolean;rinshan?:boolean;chankan?:boolean
  last?:boolean;tenhou?:boolean;chiihou?:boolean;last_copy?:boolean;indicators?:number[];ura_indicators?:number[]
}
export interface DuocaiWin {
  variant:Variant;shape:DuocaiShape;yaku_fan:number;fan:number;visible:number;fu:number;yakuman:number;entries:string[]
  meets_minimum:boolean;meets_conditions:boolean;valid:boolean;base_points:number
}
type Score = {yaku:number;fu:number;yakuman:number;entries:[string,number][];bonus:number;dora:number;ura:number;pureOpen:number;pureClosed:number}
const counts = (tiles:number[]) => tiles.reduce((map,t)=>map.set(t,(map.get(t)??0)+1),new Map<number,number>())
const allPhysical = (shape:DuocaiShape) => [...shape.physical,...shape.groups.filter(g=>g.external).flatMap(g=>g.physical)]
function pureKongs(shape:DuocaiShape):[number,number] {
  const groups=shape.groups.filter(g=>g.kind==='kong'&&g.physical.every(joker))
  return [groups.filter(g=>!g.concealed).length,groups.filter(g=>g.concealed).length]
}
function waitKind(shape:DuocaiShape):string {
  if(shape.winning_group<0)return ''
  const g=shape.groups[shape.winning_group],start=g.logical[0],win=shape.winning_face
  if(['pair','seven_pair'].includes(g.kind))return 'pair'
  if(g.kind==='triplet')return 'triplet'
  if(g.kind!=='sequence')return ''
  if(win===start+1)return 'closed'
  return start%10===1&&win===start+2||start%10===7&&win===start?'edge':'two_sided'
}
const singleWaitCache=new Map<string,boolean>()
function singleWait(shape:DuocaiShape):boolean {
  const before=shape.assignment.slice(0,-1).sort((a,b)=>a-b),melds=shape.groups.filter(g=>g.external).map(groupCode)
  const key=JSON.stringify([before,melds,shape.winning_face])
  if(singleWaitCache.has(key))return singleWaitCache.get(key)!
  const waits=tingpaiCheck(before,melds),result=waits.length===1&&waits[0]===shape.winning_face
  if(singleWaitCache.size>=8192)singleWaitCache.clear()
  singleWaitCache.set(key,result)
  return result
}
function guobiaoScore(shape:DuocaiShape,ctx:DuocaiContext):Score {
  const codes:string[]=[],fans:string[]=[]
  for(const group of shape.groups){
    if(group.kind==='knitted'){codes.push('z'+KNITTED.findIndex(p=>p.join(',')===[...group.logical].sort((a,b)=>a-b).join(',')));fans.push('zuhelong')}
    else if(shape.kind!=='seven_pairs')codes.push(groupCode(group))
  }
  const player=new PlayerTiles([],codes,14)
  player.initial_combination_count=shape.groups.filter(g=>g.external).length
  player.fan_list=fans
  if(shape.kind==='seven_pairs'){
    player.hand_tiles=[...shape.assignment].sort((a,b)=>a-b)
    const faces=[...new Set(shape.assignment)].sort((a,b)=>a-b)
    player.fan_list=[faces.length===7&&faces[0]<40&&Math.floor(faces[6]/10)===Math.floor(faces[0]/10)&&faces[6]-faces[0]===6?'lianqidui':'qiduizi']
  }else if(shape.kind==='thirteen_orphans')player.fan_list=['shisanyao']
  else if(shape.kind==='unconnected'){
    const honors=shape.assignment.filter(v=>v>=40).length
    player.fan_list=honors===7?['qixingbukao']:['quanbukao',...(honors===5?['zuhelong']:[])]
  }
  const way=[ctx.tsumo?'自摸':'点和','自风'+'东南西北'[(ctx.seat_wind??42)-41],'场风'+'东南西北'[(ctx.round_wind??41)-41]]
  if(ctx.rinshan)way.push('杠上开花')
  else if(ctx.chankan)way.push('抢杠和')
  else if(ctx.last)way.push(ctx.tsumo?'last_deal':'last_cut')
  if(ctx.last_copy)way.push('和绝张')
  if(!ctx.tsumo&&shape.winning_group>=0&&shape.groups[shape.winning_group].kind==='triplet'){
    player.combination_list[shape.winning_group]=player.combination_list[shape.winning_group].toLowerCase();way.push('暗转明')
  }
  const wait=waitKind(shape)
  if(['pair','closed','edge'].includes(wait)&&!['seven_pairs','thirteen_orphans'].includes(shape.kind)&&singleWait(shape))player.fan_list.push(({pair:'dandiaojiang',closed:'qianzhang',edge:'bianzhang'} as Record<string,string>)[wait])
  const [fan,names]=new Chinese_Hepai_Check().fan_count(player,shape.winning_face,way,true)
  const [pureOpen,pureClosed]=pureKongs(shape)
  return {yaku:fan,fu:0,yakuman:0,entries:names.map(n=>[n,0]),bonus:allPhysical(shape).reduce((sum,t)=>sum+bonus(t),0),dora:0,ura:0,pureOpen,pureClosed}
}
export function riichiBasePoints(han:number,fu:number,yakuman=0):number {
  if(yakuman)return 8000*yakuman
  if(han<=0)return 0
  if(han>=13)return 8000
  if(han>=11)return 6000
  if(han>=8)return 4000
  if(han>=6)return 3000
  const raw=fu*2**(han+2)
  return han>=5||raw>=1920?2000:raw
}
export function meetsConditions(physical:number[],visible:number,fu:number,variant:Variant):boolean {
  return physical.every(t=>t===5037?visible%2!==0:t===5038?visible%2===0:t===5036?(variant==='guobiao'?visible>=24:riichiBasePoints(visible,fu)>=2000):true)
}
function doraCount(shape:DuocaiShape,indicators:number[]):number {
  const logical=counts([...shape.assignment,...shape.groups.filter(g=>g.external).flatMap(g=>g.logical)]),wild=allPhysical(shape).filter(joker).length
  return indicators.reduce((n,t)=>n+(joker(t)?wild:logical.get(dora(t))??0),0)
}
function riichiScore(shape:DuocaiShape,ctx:DuocaiContext):Score {
  const groups=shape.groups,closed=groups.every(g=>g.concealed)
  const logical=[...shape.assignment,...groups.filter(g=>g.external).flatMap(g=>g.logical)],count=counts(logical),faces=[...count.keys()]
  const sequences=groups.filter(g=>g.kind==='sequence'),triplets=groups.filter(g=>['triplet','kong'].includes(g.kind)),kongs=triplets.filter(g=>g.kind==='kong')
  const pair=groups.find(g=>g.kind==='pair')?.logical[0]??0,seat=ctx.seat_wind??42,round=ctx.round_wind??41
  const concealed=groups.filter((g,i)=>['triplet','kong'].includes(g.kind)&&g.concealed&&(ctx.tsumo||i!==shape.winning_group||g.kind==='kong')).length
  const dragons=new Set(triplets.map(g=>g.logical[0]).filter(t=>t>=45)),winds=new Set(triplets.map(g=>g.logical[0]).filter(t=>t>=41&&t<=44))
  const wait=waitKind(shape),yakuman:string[]=[],yaku:[string,number][]=[]
  const suits=new Set(faces.filter(t=>t<40).map(t=>Math.floor(t/10))),hasHonors=faces.some(t=>t>=40)
  const terminal=(t:number)=>t<40&&[1,9].includes(t%10),orphan=(t:number)=>t>=40||terminal(t)
  if(shape.kind==='thirteen_orphans')yakuman.push(new Set(shape.assignment.slice(0,-1)).size===13?'国士无双十三面':'国士无双')
  if(faces.every(t=>t>=40))yakuman.push('字一色')
  if(faces.every(terminal))yakuman.push('清老头')
  if(faces.every(t=>[32,33,34,36,38,47].includes(t)))yakuman.push('绿一色')
  if(dragons.size===3)yakuman.push('大三元')
  if(winds.size===4)yakuman.push('大四喜')
  else if(winds.size===3&&pair>=41&&pair<=44)yakuman.push('小四喜')
  if(concealed===4)yakuman.push(wait==='pair'?'四暗刻单骑':'四暗刻')
  if(kongs.length===4)yakuman.push('四杠子')
  if(closed&&!groups.some(g=>g.external)&&suits.size===1&&!hasHonors){
    const suit=[...suits][0]*10,target=counts([suit+1,suit+1,suit+1,...Array.from({length:7},(_,i)=>suit+i+2),suit+9,suit+9,suit+9])
    if([...target].every(([t,n])=>(count.get(t)??0)>=n)){
      const before=counts(shape.assignment.slice(0,-1))
      yakuman.push([...target].every(([t,n])=>before.get(t)===n)?'纯正九莲宝灯':'九莲宝灯')
    }
  }
  if(ctx.tsumo&&!groups.some(g=>g.external)){
    if(ctx.tenhou&&seat===41)yakuman.push('天和')
    else if(ctx.chiihou&&seat!==41)yakuman.push('地和')
  }
  const pinfu=closed&&sequences.length===4&&wait==='two_sided'&&![45,46,47,seat,round].includes(pair)
  if(shape.kind==='seven_pairs')yaku.push(['七对子',2])
  if(faces.every(t=>!orphan(t)))yaku.push(['断幺九',1])
  if(suits.size===1)yaku.push(hasHonors?['混一色',closed?3:2]:['清一色',closed?6:5])
  if(faces.every(orphan))yaku.push(['混老头',2])
  if(shape.kind==='standard'){
    for(const group of triplets){
      const value=group.logical[0]
      if(value>=45)yaku.push(['役牌·'+({45:'中',46:'白',47:'发'} as Record<number,string>)[value],1])
      if(value===seat)yaku.push(['自风',1])
      if(value===round)yaku.push(['场风',1])
    }
    if(pinfu)yaku.push(['平和',1])
    if(triplets.length===4)yaku.push(['对对和',2])
    if(concealed===3)yaku.push(['三暗刻',2])
    if(kongs.length===3)yaku.push(['三杠子',2])
    if(dragons.size===2&&pair>=45)yaku.push(['小三元',2])
    const seq=counts(sequences.map(g=>g.logical[0])),trip=new Set(triplets.map(g=>g.logical[0])),identical=[...seq.values()].reduce((n,c)=>n+Math.floor(c/2),0)
    if(closed&&identical)yaku.push(identical>=2?['二杯口',3]:['一杯口',1])
    if([1,2,3,4,5,6,7].some(r=>[1,2,3].every(s=>seq.has(s*10+r))))yaku.push(['三色同顺',closed?2:1])
    if([1,2,3,4,5,6,7,8,9].some(r=>[1,2,3].every(s=>trip.has(s*10+r))))yaku.push(['三色同刻',2])
    if([1,2,3].some(s=>[1,4,7].every(r=>seq.has(s*10+r))))yaku.push(['一气通贯',closed?2:1])
    if(sequences.length&&groups.every(g=>g.logical.some(orphan)))yaku.push(hasHonors?['混全带幺九',closed?2:1]:['纯全带幺九',closed?3:2])
  }
  if(closed){
    if(ctx.riichi){yaku.push(ctx.riichi===2?['双立直',2]:['立直',1]);if(ctx.ippatsu)yaku.push(['一发',1])}
    if(ctx.tsumo)yaku.push(['门前清自摸和',1])
  }
  if(ctx.rinshan&&ctx.tsumo)yaku.push(['杠上开花',1])
  else if(ctx.chankan&&!ctx.tsumo)yaku.push(['抢杠',1])
  else if(ctx.last)yaku.push([ctx.tsumo?'海底捞月':'河底捞鱼',1])
  let fu=25
  if(!['seven_pairs','thirteen_orphans'].includes(shape.kind)){
    if(pinfu)fu=ctx.tsumo?20:30
    else{
      fu=20+(closed&&!ctx.tsumo?10:0)+(ctx.tsumo?2:0)+2*(Number(pair>=45)+Number(pair===seat)+Number(pair===round))+(['pair','closed','edge'].includes(wait)?2:0)
      groups.forEach((g,gi)=>{
        if(!['triplet','kong'].includes(g.kind))return
        const hidden=g.concealed&&(ctx.tsumo||gi!==shape.winning_group||g.kind==='kong')
        fu+=2*(orphan(g.logical[0])?2:1)*(hidden?2:1)*(g.kind==='kong'?4*2**(g.logical.length-4):1)
      })
      fu=Math.max(30,Math.ceil(fu/10)*10)
    }
  }
  const [pureOpen,pureClosed]=pureKongs(shape)
  if(yakuman.length)return {yaku:13*yakuman.length,fu:25,yakuman:yakuman.length,entries:yakuman.map(n=>[n,13]),bonus:0,dora:0,ura:0,pureOpen,pureClosed}
  return {yaku:yaku.reduce((n,row)=>n+row[1],0),fu,yakuman:0,entries:yaku,bonus:allPhysical(shape).reduce((n,t)=>n+bonus(t),0),dora:doraCount(shape,ctx.indicators??[]),ura:ctx.riichi&&closed?doraCount(shape,ctx.ura_indicators??[]):0,pureOpen,pureClosed}
}
export function scoreDuocaiShape(shape:DuocaiShape,variant:Variant,ctx:DuocaiContext={}):DuocaiWin {
  const score=variant==='guobiao'?guobiaoScore(shape,ctx):riichiScore(shape,ctx)
  let visible=score.yakuman?score.yakuman*13:score.yaku+score.bonus+score.dora,fan=visible+(score.yakuman?0:score.ura),yakuman=score.yakuman
  let entries:string[]=[]
  if(variant==='guobiao'){
    visible+=64*score.pureOpen+88*score.pureClosed;fan=visible
    entries=score.entries.map(row=>row[0])
    if(score.pureOpen)entries.push(`纯赖子明杠×${score.pureOpen}（${64*score.pureOpen}番）`)
    if(score.pureClosed)entries.push(`纯赖子暗杠×${score.pureClosed}（${88*score.pureClosed}番）`)
    if(score.bonus)entries.push(`金银赤宝牌（${score.bonus>0?'+':''}${score.bonus}番）`)
  }else{
    const extra=score.pureOpen+2*score.pureClosed
    yakuman+=extra
    const rows=extra&&!score.yakuman?[]:[...score.entries]
    if(extra){
      fan=visible=13*yakuman
      if(score.pureOpen)rows.push([`纯赖子明杠×${score.pureOpen}`,13*score.pureOpen])
      if(score.pureClosed)rows.push([`纯赖子暗杠×${score.pureClosed}`,26*score.pureClosed])
    }else{
      if(score.bonus)rows.push(['金银赤宝牌',score.bonus])
      if(score.dora)rows.push(['宝牌',score.dora])
      if(score.ura)rows.push(['里宝牌',score.ura])
    }
    entries=rows.map(([name,n])=>`${name}（${n}番）`)
  }
  const meets_minimum=score.yaku>=(variant==='guobiao'?8:1),meets_conditions=meetsConditions(allPhysical(shape),visible,score.fu,variant)
  return {variant,shape,yaku_fan:score.yaku,fan,visible,fu:yakuman?25:score.fu,yakuman,entries,meets_minimum,meets_conditions,valid:meets_minimum&&meets_conditions,base_points:variant==='guobiao'?Math.max(0,fan):riichiBasePoints(fan,score.fu,yakuman)}
}
