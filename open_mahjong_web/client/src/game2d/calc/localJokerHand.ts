/** Exact wildcard structure DP. All computation stays in the client. */
export const TILES = [1,2,3].flatMap(s=>Array.from({length:9},(_,r)=>s*10+r+1)).concat([41,42,43,44,45,46,47])
export const ORPHANS = [11,19,21,29,31,39,41,42,43,44,45,46,47]
export const INDEX = new Map(TILES.map((t,i)=>[t,i]))
export interface Policy {
  jokers?: number[]; physical?: number[]; logical?: number[]; supply?: Record<number,number>; map?: Record<number,number>
  meldCount?: number; closedCap?: number; exposedCap?: boolean; sevenPairs?: boolean; orphans?: boolean; meldKinds?: string
  meldPhysical?:number[][];allowExposedJokers?:boolean
}
export interface LocalHand {counts:number[]; caps:number[]; ghosts:number; physical:Record<number,number>; melds:string[]; policy:Policy}
export interface Shape {kind:string; logical:number[]; closed:string[]}
export const sum=(a:number[])=>a.reduce((x,y)=>x+y,0)
export const natural=(p:Policy,t:number)=>p.map?.[t]??t
export const limit=(p:Policy,t:number)=>p.supply?.[t]??(t>=51?1:4)
export const shapeTiles=(s:Shape)=>s.logical.flatMap((n,i)=>Array(n).fill(TILES[i])) as number[]
export function meldTiles(code:string):number[]|null {
  if(!/^[sSkKgGq]\d\d$/.test(code))return null
  const t=Number(code.slice(1));if(!INDEX.has(t))return null
  return 'sS'.includes(code[0]) ? t<40 && t%10>=2 && t%10<=8 ? [t-1,t,t+1] : null
    : Array(code[0]==='q'?2:'kK'.includes(code[0])?3:4).fill(t)
}
export function prepare(hand:number[],melds:string[],p:Policy,complete=true,tail?:number):LocalHand|null {
  const count=p.meldCount??4
  if(melds.length>count || hand.length+3*melds.length!==3*count+(tail??(complete?2:1)))return null
  const h:LocalHand={counts:Array(34).fill(0),caps:Array(34).fill(0),ghosts:0,physical:{},melds:[...melds],policy:p}
  const logical=p.logical??TILES,physical=p.physical??TILES,jokers=p.jokers??[],opens=Array(34).fill(0)
  for(const tile of hand) {
    if(!physical.includes(tile))return null
    h.physical[tile]=(h.physical[tile]??0)+1
    if(jokers.includes(tile))h.ghosts++
    else {const t=natural(p,tile),i=INDEX.get(t);if(i==null||!logical.includes(t))return null;h.counts[i]++}
  }
  for(let m=0;m<melds.length;m++) {
    const code=melds[m]
    const group=meldTiles(code);if(!group||!(p.meldKinds??'skgG').includes(code[0]))return null
    const actual=p.meldPhysical?p.meldPhysical.length===melds.length?p.meldPhysical[m]:null:group
    if(!actual||actual.length!==group.length)return null
    for(let j=0;j<group.length;j++) {
      const tile=actual[j]
      if(!physical.includes(tile)||!p.allowExposedJokers&&jokers.includes(tile))return null
      h.physical[tile]=(h.physical[tile]??0)+1
      const t=p.meldPhysical?group[j]:natural(p,tile),i=INDEX.get(t);if(!jokers.includes(tile)&&natural(p,tile)!==t||i==null||!logical.includes(t))return null;opens[i]++
    }
  }
  if(Object.entries(h.physical).some(([t,n])=>n>limit(p,Number(t))))return null
  for(let i=0;i<34;i++) {
    h.caps[i]=!logical.includes(TILES[i])?0:p.closedCap?p.closedCap-(p.exposedCap?opens[i]:0):hand.length
    if(h.counts[i]>h.caps[i])return null
  }
  return h
}
type Step={pair:number;pung:number;sequence:number;cost:number}
class Suit {
  counts:number[];caps:number[];budget:number;mask:number
  memo=new Map<string,number>();patterns=new Map<string,Shape[]>()
  constructor(h:LocalHand,public offset:number,length:number){this.counts=h.counts.slice(offset,offset+length);this.caps=h.caps.slice(offset,offset+length);this.budget=h.ghosts;this.mask=(1<<(h.ghosts+1))-1}
  *steps(rank:number,a:number,b:number,eye:number):Generator<Step> {
    const max=Math.min(this.caps[rank],this.counts[rank]+this.budget)
    for(let pair=0;pair<=eye;pair++)for(let pung=0;pung*3<=max-a-b-pair*2;pung++) {
      const basis=a+b+pair*2+pung*3,seqs=this.counts.length===9&&rank<7?max-basis:0
      for(let sequence=0;sequence<=seqs;sequence++) {
        const cost=basis+sequence-this.counts[rank]
        if(cost>=0&&cost<=this.budget)yield {pair,pung,sequence,cost}
      }
    }
  }
  resources(rank:number,a:number,b:number,eye:number):number {
    if(rank===this.counts.length)return a===0&&b===0&&eye===0?1:0
    const key=[rank,a,b,eye].join(',');if(this.memo.has(key))return this.memo.get(key)!
    let bits=0;for(const s of this.steps(rank,a,b,eye))bits|=this.resources(rank+1,s.sequence,a,eye-s.pair)<<s.cost
    bits&=this.mask;this.memo.set(key,bits);return bits
  }
  profile(eye:number){return this.resources(0,0,0,eye)}
  getPatterns(eye:number,cost:number):Shape[] {
    const key=eye+','+cost;if(this.patterns.has(key))return this.patterns.get(key)!
    const result:Shape[]=[],logical=Array(this.counts.length).fill(0),codes:string[]=[]
    const walk=(rank:number,a:number,b:number,pair:number,left:number)=>{
      if(rank===this.counts.length){if(left===0&&a===0&&b===0&&pair===0)result.push({kind:'standard',logical:[...logical],closed:[...codes]});return}
      for(const s of this.steps(rank,a,b,pair)) {
        const next=left-s.cost;if(next<0||!(this.resources(rank+1,s.sequence,a,pair-s.pair)&(1<<next)))continue
        logical[rank]=this.counts[rank]+s.cost;const start=codes.length
        if(s.pair)codes.push('q'+TILES[this.offset+rank])
        for(let n=0;n<s.pung;n++)codes.push('K'+TILES[this.offset+rank])
        for(let n=0;n<s.sequence;n++)codes.push('S'+(TILES[this.offset+rank]+1))
        walk(rank+1,s.sequence,a,pair-s.pair,next);codes.length=start
      }
    }
    if(this.profile(eye)&(1<<cost))walk(0,0,0,eye,cost)
    this.patterns.set(key,result);return result
  }
}
const suits=(h:LocalHand)=>[new Suit(h,0,9),new Suit(h,9,9),new Suit(h,18,9),new Suit(h,27,7)]
function standard(h:LocalHand,groups:Suit[],pairCount=1):boolean {
  let states=[1,0];const mask=(1<<(h.ghosts+1))-1
  for(const suit of groups) {
    const next=[0,0]
    for(let eye=0;eye<=pairCount;eye++)for(let p=0;p+eye<=pairCount;p++)for(let used=0;used<=h.ghosts;used++)if(states[eye]&(1<<used))next[eye+p]|=suit.profile(p)<<used
    states=next.map(n=>n&mask)
  }
  return !!(states[pairCount]&(1<<h.ghosts))
}
function pairResources(h:LocalHand):number[] {
  const bits=Array(35).fill(0),mask=(1<<(h.ghosts+1))-1;bits[34]=1
  for(let i=33;i>=0;i--){const n=h.counts[i];for(let total=n+n%2;total<=Math.min(n+h.ghosts,h.caps[i]);total+=2)bits[i]|=bits[i+1]<<(total-n);bits[i]&=mask}
  return bits
}
function* orphanShapes(h:LocalHand):Generator<Shape> {
  if(h.counts.some((n,i)=>n>0&&!ORPHANS.includes(TILES[i])))return
  for(const pair of ORPHANS){const logical=TILES.map(t=>t===pair?2:ORPHANS.includes(t)?1:0)
    if(logical.some((n,i)=>n<h.counts[i]||n>h.caps[i])||sum(logical)-sum(h.counts)!==h.ghosts)continue
    yield {kind:'thirteen_orphans',logical,closed:[]}
  }
}
export function canWin(h:LocalHand|null):boolean {
  return !!h&&(standard(h,suits(h))||!h.melds.length&&
    (!!h.policy.sevenPairs&&!!(pairResources(h)[0]&(1<<h.ghosts))||!!h.policy.orphans&&!orphanShapes(h).next().done))
}
export const canFormMelds=(h:LocalHand|null)=>!!h&&standard(h,suits(h),0)
export const canFormPairs=(h:LocalHand|null)=>!!h&&!!(pairResources(h)[0]&(1<<h.ghosts))
export function* shapes(h:LocalHand|null):Generator<Shape> {
  if(!h)return
  const groups=suits(h),logical=Array(34).fill(0),codes:string[]=[]
  function* combine(index:number,left:number,eye:number):Generator<Shape> {
    if(index===4){if(left===0&&eye===0)yield {kind:'standard',logical:[...logical],closed:[...codes]};return}
    const suit=groups[index]
    for(let p=0;p<=eye;p++)for(let cost=0;cost<=left;cost++) {
      if(!(suit.profile(p)&(1<<cost)))continue
      for(const pattern of suit.getPatterns(p,cost)) {
        logical.splice(suit.offset,pattern.logical.length,...pattern.logical)
        const start=codes.length;codes.push(...pattern.closed);yield*combine(index+1,left-cost,eye-p);codes.length=start
      }
    }
  }
  if(standard(h,groups))yield*combine(0,h.ghosts,1)
  if(h.melds.length)return
  if(h.policy.sevenPairs){const bits=pairResources(h)
    function* pairs(index:number,left:number):Generator<Shape>{
      if(index===34){if(left===0)yield {kind:'seven_pairs',logical:[...logical],closed:[]};return}
      const n=h!.counts[index]
      for(let total=n+n%2;total<=Math.min(n+left,h!.caps[index]);total+=2){const next=left-(total-n);if(!(bits[index+1]&(1<<next)))continue;logical[index]=total;yield*pairs(index+1,next)}
    }
    if(bits[0]&(1<<h.ghosts))yield*pairs(0,h.ghosts)
  }
  if(h.policy.orphans)yield*orphanShapes(h)
}
export const winningTiles=(h:LocalHand,s:Shape,win:number)=>(h.policy.jokers??[]).includes(win)
  ? TILES.filter((t,i)=>s.logical[i]>h.counts[i]):[natural(h.policy,win)]
