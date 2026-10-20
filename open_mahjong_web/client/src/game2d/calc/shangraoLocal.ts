import {TILES, INDEX} from './localJokerHand'

const HONORS = [41,42,43,44,45,46,47]
const SEQUENCES = TILES.filter(t => t < 40 && t % 10 <= 7).map(t => [t,t+1,t+2])
const SPACED = Array.from({length:512}, (_, mask) => Array.from({length:9}, (_, i) => i+1).filter(n => mask & (1 << (n-1))))
  .filter(a => a.length <= 3 && a.slice(1).every((n,i) => n-a[i] >= 3))
const terminal = (t:number) => t >= 41 || t % 10 === 1 || t % 10 === 9
export function shangraoBao(indicator:number):number {
  if (!INDEX.has(indicator)) return 0
  if (indicator < 40) return Math.floor(indicator/10)*10 + indicator%10%9+1
  const cycle = indicator < 45 ? [41,42,43,44] : [45,47,46]
  return cycle[(cycle.indexOf(indicator)+1)%cycle.length]
}
function meld(code:string):number[]|null {
  if (!code || code.length !== 3 || !'skgG'.includes(code[0])) return null
  const tile = Number(code.slice(1))
  if (!INDEX.has(tile)) return null
  if (code[0] !== 's') return Array('gG'.includes(code[0]) ? 4 : 3).fill(tile)
  return tile < 40 && tile%10 >= 2 && tile%10 <= 8 ? [tile-1,tile,tile+1] : null
}
type Score = {points:number;base:number;pattern:string;shape:string;fan_values:Record<string,number>;multipliers:Record<string,number>;fan_names:string[]}
export class ShangraoLocal {
  private readonly indicator:number
  private readonly bao:number
  private readonly config:Record<string,any>
  private readonly memo = new Map<string,boolean>()
  constructor(private readonly melds:string[], private readonly context:Record<string,any>) {
    this.indicator = context.indicator ?? 0
    this.bao = shangraoBao(this.indicator)
    this.config = context.config ?? {}
  }
  private valid(hand:number[], complete=true):boolean {
    if (!this.bao || this.melds.length > 4 || hand.length+this.melds.length*3 !== (complete?14:13)) return false
    const physical = [...hand]
    for (const code of this.melds) { const group=meld(code); if (!group) return false; physical.push(...group) }
    return physical.every(t => INDEX.has(t) && physical.filter(x => x===t).length <= (t===this.indicator?3:4))
  }
  private groups(counts:number[], wild:number, count:number, triplets:boolean):boolean {
    if (wild<0 || count<0 || counts.reduce((a,b)=>a+b,0)+wild!==count*3) return false
    const first=counts.findIndex(n=>n>0)
    if (first<0) return true
    const key=counts.join('')+'/'+wild+'/'+count+'/'+triplets
    if (this.memo.has(key)) return this.memo.get(key)!
    let rest=[...counts];const used=Math.min(3,rest[first]);rest[first]-=used
    let result=this.groups(rest,wild-(3-used),count-1,triplets)
    if (!result && !triplets) for (const sequence of SEQUENCES.filter(s=>s.includes(TILES[first]))) {
      rest=[...counts];let need=0
      for (const tile of sequence) { const i=INDEX.get(tile)!;if(rest[i])rest[i]--;else need++ }
      if(this.groups(rest,wild-need,count-1,false)){result=true;break}
    }
    if(this.memo.size>=50000)this.memo.clear()
    this.memo.set(key,result);return result
  }
  private pairs(hand:number[],triplets=false):number[] {
    if(triplets&&this.melds.some(m=>m[0]==='s'))return []
    const wild=hand.filter(t=>t===this.bao).length,counts=TILES.map(t=>t===this.bao?0:hand.filter(x=>x===t).length),result=new Set<number>()
    if(wild>=2&&this.groups(counts,wild-2,4-this.melds.length,triplets))result.add(2)
    for(let i=0;i<counts.length;i++){
      const used=wild?1:2;if(counts[i]<used)continue
      const rest=[...counts];rest[i]-=used
      if(this.groups(rest,wild?wild-1:0,4-this.melds.length,triplets))result.add(wild?1:0)
    }
    return [...result]
  }
  private lan(hand:number[],allowBao:boolean):boolean {
    if(this.melds.length)return false
    const rest=hand.filter(t=>!allowBao||t!==this.bao)
    if(new Set(rest).size!==rest.length)return false
    let capacity=7
    for(const suit of [1,2,3]){
      const ranks=rest.filter(t=>Math.floor(t/10)===suit).map(t=>t%10),sizes=SPACED.filter(s=>ranks.every(r=>s.includes(r))).map(s=>s.length)
      if(!sizes.length)return false;capacity+=Math.max(...sizes)
    }
    return capacity>=14
  }
  private stars(hand:number[],allowBao:boolean,flown:boolean):boolean {
    const physical=[...hand,...this.melds.flatMap(m=>meld(m)!)],natural=new Set(physical.filter(t=>!allowBao||t!==this.bao)),missing=new Set(HONORS.filter(t=>!natural.has(t)))
    if(flown&&HONORS.includes(this.bao)&&!physical.includes(this.bao))missing.delete(this.bao)
    return missing.size<=(allowBao?hand.filter(t=>t===this.bao).length:0)
  }
  private seven(hand:number[],win:number,opening:boolean):boolean {
    if(this.melds.length)return false
    const rest=[...hand]
    if(!opening&&rest.includes(this.bao)){
      rest.splice(rest.indexOf(win),1)
      const heldBao=rest.indexOf(this.bao);if(heldBao<0)return false
      rest.splice(heldBao,1)
    }
    const wild=rest.filter(t=>t===this.bao).length,counts=new Map<number,number>()
    for(const tile of rest)if(tile!==this.bao)counts.set(tile,(counts.get(tile)??0)+1)
    const odd=[...counts.values()].reduce((n,c)=>n+c%2,0)
    return odd<=wild&&(wild-odd)%2===0
  }
  score(hand:number[],win:number,cut:number|null=null,opening=false,afterKong=false):Score|null {
    if(!this.valid(hand)||!hand.includes(win))return null
    const legacy=this.config.rule_version==='shangrao-v1'
    const wild=hand.filter(t=>t===this.bao).length,firstBao=!opening&&win===this.bao&&wild===1
    const pairs=firstBao?[]:this.pairs(hand),triplets=firstBao?[]:this.pairs(hand,true)
    const counts=new Map<number,number>();for(const t of hand)if(t!==this.bao)counts.set(t,(counts.get(t)??0)+1)
    const seven=!firstBao&&this.seven(hand,win,opening)
    const luxury=seven&&[...counts.values()].includes(4),allowBao=!!this.config.allow_special_bao
    const nine=hand.every(t=>terminal(t)||allowBao&&t===this.bao)&&this.melds.flatMap(m=>meld(m)!).every(terminal)
    let flown=!!this.context.flying_seven_stars
    if(cut===this.bao&&HONORS.includes(this.bao)){const before=[...hand];before.splice(before.indexOf(win),1);before.push(cut);flown||=!legacy||this.stars(before,false,false)}
    const stars=this.stars(hand,legacy&&allowBao,!!this.config.flying_seven_stars&&flown)
    const candidates:[string,number,string,number[]][]=[]
    if(pairs.length)candidates.push([wild?'钓头':'平和',1,'standard',pairs])
    if(triplets.length)candidates.push([wild?'碰碰和':'清碰',wild?2:4,'triplets',triplets])
    const sevenPairBao=wild>=2&&[...counts.values()].reduce((n,c)=>n+c%2,0)<=wild-2?2:Math.min(1,wild)
    if(seven)candidates.push([(luxury?'豪华':'')+(wild?'七对':'硬七对'),(wild?2:4)*(luxury?2:1),'seven',[sevenPairBao]])
    if(nine){
      candidates.push([stars?'七星九幺':'九幺',stars?2:1,'nine',[]])
      if(seven&&!wild)candidates.push(['九幺硬七',8,'seven',[]])
      if(triplets.length&&!wild)candidates.push(['九幺清碰',8,'triplets',triplets])
      if(!legacy&&allowBao&&wild&&!terminal(this.bao)&&terminal(win)){
        if(seven)candidates.push(['九幺七对',4,'seven',[sevenPairBao]])
        if(triplets.length)candidates.push(['九幺碰碰和',4,'triplets',triplets])
      }
    }
    if(this.lan(hand,legacy&&allowBao))candidates.push([stars?'七星十三烂':'十三烂',stars?4:2,'lan',[]])
    const natural=[...hand.filter(t=>t!==this.bao),...this.melds.flatMap(m=>meld(m)!)],suits=[...new Set(natural.map(t=>Math.floor(t/10)))]
    const qing=!!this.config.qingyise&&suits.length===1&&suits[0]>=1&&suits[0]<=3&&(!wild||suits.includes(Math.floor(win/10)))
    const zi=!!this.config.ziyise&&nine&&natural.every(t=>HONORS.includes(t))&&(legacy||!wild||HONORS.includes(win)),flying=(this.context.flying_bao??0)+Number(cut===this.bao)
    if(!legacy){
      if(qing)for(const [name,base,shape,pairChoices] of [...candidates])if(['standard','triplets','seven'].includes(shape)){
        const label=shape==='standard'?(wild?'清一色':'无宝清一色'):'清一色'+name
        candidates.push([label,shape==='standard'&&!wild?4:base*2,shape,pairChoices])
      }
      if(zi){
        candidates.unshift(['字一色',2,'nine',[]])
        if(!wild||HONORS.includes(win)){
          if(seven)candidates.push([wild?'字一色七对':'字一色硬七对',wild?8:16,'seven',[sevenPairBao]])
          if(triplets.length)candidates.push([wild?'字一色碰碰和':'字一色清碰',wild?8:16,'triplets',triplets])
        }
      }
    }
    if(flying<0||flying>4)return null
    let best:Score|null=null
    for(const [name,base,shape,pairChoices] of candidates){
      const multipliers:Record<string,number>={}
      if(legacy&&qing&&shape!=='nine'&&shape!=='lan')multipliers['清一色']=2
      if(legacy&&zi)multipliers['字一色']=2
      if(!this.melds.length&&shape!=='seven'&&shape!=='lan')multipliers['门清']=2
      if(this.melds.length===4)multipliers['单钓']=2
      if(afterKong)multipliers['杠上开花']=2
      if(pairChoices.length&&wild&&win===this.bao)multipliers['宝钓宝']=2
      if(win!==this.bao&&pairChoices.includes(2))multipliers['宝还原']=2
      if(flying)multipliers['飞宝']=2**flying
      const points=Object.values(multipliers).reduce((a,b)=>a*b,base)
      if(best&&(best.points>points||best.points===points&&best.base>=base))continue
      best={points,base,pattern:name,shape,fan_values:{[name]:base},multipliers,fan_names:[name,...Object.keys(multipliers)]}
    }
    return best
  }
  waiting(hand:number[]):number[]{return !this.valid(hand,false)?[]:TILES.filter(t=>this.score([...hand,t],t,null,true)!==null)}
  describe(hand:number[],win:number,cut:number|null=null):Record<string,any>{
    const score=this.score(hand,win,cut),points=this.context.blocked?0:score?.points??0
    return {tile:win,points,kind:points?'zimo':'wuyi',label:points?`仅自摸\n${points}分`:'未满足'}
  }
}
