import {TILES,INDEX,meldTiles} from './localJokerHand'
const SEQUENCES=[...TILES.filter(t=>t<40&&t%10<=7).map(t=>[t,t+1,t+2]),[41,42,43],[41,42,44],[41,43,44],[42,43,44],[45,46,47]]
const meld=(code:string):number[]=>code.startsWith('s[')?code.slice(2,-1).split(',').map(Number):code[0]==='s'&&code.length===7?[1,3,5].map(i=>Number(code.slice(i,i+2))):meldTiles(code)??[]
type Quote={fan:number;points:number;german:number;effective:number;shape:string}
export class NanchangLocal {
  jing:number[]
  private groupCache=new Map<string,boolean>()
  private shapeCache=new Map<string,string[]>()
  constructor(private melds:string[],private c:Record<string,any>){
    const primary=c.indicator??11,cycle=primary<40?Array.from({length:9},(_,i)=>Math.floor(primary/10)*10+i+1):primary<45?[41,42,43,44]:[45,47,46]
    this.jing=[primary,cycle[(cycle.indexOf(primary)+1)%cycle.length]]
  }
  private valid(hand:number[],complete=true){
    if(this.melds.length>4||hand.length+3*this.melds.length!==(complete?14:13))return false
    const all=[...hand];for(const code of this.melds){const group=meld(code);if(group.length<3||group.length>4||new Set(group).size!==1&&!SEQUENCES.some(s=>s.join(',')===[...group].sort((a,b)=>a-b).join(',')))return false;all.push(...group)}
    return all.every(t=>TILES.includes(t)&&all.filter(x=>x===t).length<=4)
  }
  private counts(hand:number[],natural=false,fixed?:number){
    const rest=[...hand];if(fixed!==undefined)rest.splice(rest.indexOf(fixed),1)
    return {counts:TILES.map(t=>rest.filter(x=>x===t&&(natural||!this.jing.includes(x))).length+Number(fixed===t)),wild:natural?0:rest.filter(t=>this.jing.includes(t)).length}
  }
  private groups(counts:number[],wild:number,groups:number,pair:number,triplets=false):boolean {
    if(wild<0||groups<0||counts.reduce((a,b)=>a+b,0)+wild!==groups*3+pair*2)return false
    const index=counts.findIndex(n=>n>0);if(index<0)return true
    const key=counts.join(',')+`/${wild}/${groups}/${pair}/${triplets}`;if(this.groupCache.has(key))return this.groupCache.get(key)!
    let found=false
    if(pair){const used=Math.min(2,counts[index]),rest=[...counts];rest[index]-=used;found=this.groups(rest,wild-2+used,groups,0,triplets)}
    if(!found&&groups>0){const used=Math.min(3,counts[index]),rest=[...counts];rest[index]-=used;found=this.groups(rest,wild-3+used,groups-1,pair,triplets)}
    if(!found&&groups>0&&!triplets)for(const sequence of SEQUENCES.filter(s=>s.includes(TILES[index]))){const rest=[...counts];let need=0;for(const t of sequence){const i=INDEX.get(t)!;if(rest[i])rest[i]--;else need++}if(this.groups(rest,wild-need,groups-1,pair)){found=true;break}}
    this.groupCache.set(key,found);return found
  }
  private pairs(counts:number[],wild:number,number=7){return counts.reduce((a,b)=>a+b,0)+wild===number*2&&counts.reduce((a,b)=>a+b%2,0)<=wild}
  private lan(counts:number[],wild:number){
    if(counts.some(n=>n>1)||counts.reduce((a,b)=>a+b,0)+wild!==14)return false
    let capacity=7
    for(let suit=1;suit<=3;suit++){
      let present=0;for(let r=1;r<=9;r++)if(counts[INDEX.get(suit*10+r)!])present|=1<<(r-1)
      let best=-1;for(let mask=0;mask<512;mask++){if((mask&present)!==present)continue;let last=-3,n=0,okay=true;for(let r=0;r<9;r++)if(mask&(1<<r)){if(r-last<3){okay=false;break}last=r;n++}if(okay)best=Math.max(best,n)}
      if(best<0)return false;capacity+=best
    }
    return capacity>=14
  }
  private shapes(hand:number[],win=0,self=true,natural=false){
    if(!this.valid(hand)||!self&&!hand.includes(win))return []
    const key=[...hand].sort((a,b)=>a-b).join(',')+`/${win}/${self}/${natural}`;if(this.shapeCache.has(key))return this.shapeCache.get(key)!
    const h=this.counts(hand,natural,self?undefined:win),result=[]
    if(this.groups(h.counts,h.wild,4-this.melds.length,1))result.push('standard')
    if(this.melds.every(m=>new Set(meld(m)).size===1)&&this.groups(h.counts,h.wild,4-this.melds.length,1,true))result.push('all_triplets')
    if(!this.melds.length){if(this.pairs(h.counts,h.wild))result.push('seven_pairs');if(this.lan(h.counts,h.wild))result.push('thirteen_lan')}
    this.shapeCache.set(key,result);return result
  }
  private jingdiao(hand:number[]){
    if(!this.valid(hand,false))return false
    for(const tile of this.jing.filter(t=>hand.includes(t))){const rest=[...hand];rest.splice(rest.indexOf(tile),1);const h=this.counts(rest);if(this.groups(h.counts,h.wild,4-this.melds.length,0)||!this.melds.length&&this.pairs(h.counts,h.wild,6))return true}
    return false
  }
  waiting(hand:number[]){return this.valid(hand,false)?TILES.filter(t=>this.shapes([...hand,t]).length>0):[]}
  private score(hand:number[],win:number,self:boolean,payer:number|null,discarded:number[],other=true,declared=true):Quote|null {
    const shapes=this.shapes(hand,win,self);if(!shapes.length)return null
    const natural=this.shapes(hand,win,self,true),before=[...hand];before.splice(before.indexOf(win),1)
    const jd=declared&&!!this.c.declared_jingdiao&&self&&this.jingdiao(before),winner=this.c.index??1,common=discarded.reduce((n,t)=>n+(t===this.jing[0]?2:t===this.jing[1]?1:0),0)+Number(jd)
    let best:Quote|null=null
    for(const shape of [...shapes].sort()){
      const isNatural=natural.includes(shape),german=isNatural?other?5:10:0,fan=common+(isNatural?other?1:2:0)+(shape==='all_triplets'?2:shape==='seven_pairs'?isNatural?2:1:shape==='thirteen_lan'?Array.from({length:7},(_,i)=>i+41).every(t=>hand.includes(t))?2:1:0)
      const points=[0,1,2,3].filter(i=>i!==winner).reduce((n,i)=>n+2**Math.min(6,fan+Number(self)+Number(winner===0||i===0)+Number(i===payer))+(self||i===payer?german+(jd?5:0):0),0)
      const q={fan,points,german,effective:Math.min(6,fan+Number(winner===0)+(self||payer===0?.5:0)),shape}
      if(!best||q.points>best.points||q.points===best.points&&(q.fan>best.fan||q.fan===best.fan&&q.shape>best.shape))best=q
    }
    return best
  }
  describe(hand:number[],win:number,cut:number|null=null){
    const winner=this.c.index??1,discarded=[...(this.c.discarded??[])];if(cut!==null)discarded.push(cut)
    const before=[...hand];before.splice(before.indexOf(win),1);const jd=this.jingdiao(before);let floor=6
    if(jd)for(const t of TILES){const q=this.score([...before,t],t,false,(winner+1)%4,discarded,true,false);if(q)floor=Math.min(floor,Math.min(6,q.fan+Number(winner===0)))}
    const status=(q:Quote|null,self:boolean)=>!q||this.c.peida?'unmet':!q.fan?'below_minimum':!self&&(this.c.declared_jingdiao||q.effective<=(this.c.missed_fan??-1)||jd&&Math.min(6,q.fan+Number(winner===0))<=floor)?'self_draw_only':'allowed'
    const statuses:Record<number,string>={},points:Record<number,number>={},upper=[]
    for(const payer of [0,1,2,3].filter(i=>i!==winner)){const q=this.score(hand,win,false,payer,discarded),state=status(q,false);statuses[payer]=state;points[payer]=state==='allowed'?q!.points:0;upper.push(state==='allowed'&&q!.german>0?this.score(hand,win,false,payer,discarded,false)!.points:points[payer])}
    const own=this.score(hand,win,true,null,discarded),selfStatus=status(own,true),values=Object.values(points)
    return {tile:win,melds:[...this.melds],status:Object.values(statuses).includes('allowed')?'allowed':selfStatus==='below_minimum'?'below_minimum':selfStatus==='allowed'?'self_draw_only':'unmet',self_draw:selfStatus==='allowed',self_draw_points:selfStatus==='allowed'?own!.points:0,ron_status:statuses,ron_points:points,points:Math.max(...values),points_min:Math.min(...values.filter(v=>v>0),Infinity)===Infinity?0:Math.min(...values.filter(v=>v>0)),points_max:Math.max(...upper)}
  }
}
