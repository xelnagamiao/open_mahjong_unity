import {shapes as templates, type LocalHand} from './localJokerHand'
import {TILES, joker, face, validPhysical, allows} from './duocaiTiles'

export type Variant = 'guobiao'|'riichi'
export interface DuocaiMeld {physical:number[];concealed:boolean;declared:string;source?:number}
export interface DuocaiGroup {kind:string;logical:number[];physical:number[];indices:number[];external:boolean;concealed:boolean}
export interface DuocaiShape {kind:string;groups:DuocaiGroup[];winning_group:number;winning_face:number;assignment:number[];physical:number[]}
export const KNITTED = [
  [11,14,17,22,25,28,33,36,39], [11,14,17,23,26,29,32,35,38],
  [12,15,18,21,24,27,33,36,39], [13,16,19,21,24,27,32,35,38],
  [13,16,19,22,25,28,31,34,37], [12,15,18,23,26,29,31,34,37],
]
export function groupCode(group:DuocaiGroup):string {
  let code=({sequence:'S',triplet:'K',pair:'q',seven_pair:'q',kong:'G'} as Record<string,string>)[group.kind]
  if(group.external&&!group.concealed)code=code.toLowerCase()
  return code+group.logical[group.kind==='sequence'?1:0]
}
type Template = {kind:string;logical:number[]}
type Slot = {gi:number;pos:number;values:number[]}
const candidateCache=new Map<string,Map<string,number[]>>()
function matchingSlots(hand:number[],template:Template[],external=false,declared=''):Slot[] {
  const handKey=hand.join(',')
  let memo=candidateCache.get(handKey)
  if(!memo){if(candidateCache.size>=64)candidateCache.clear();memo=new Map();candidateCache.set(handKey,memo)}
  return template.flatMap((g,gi)=>g.logical.map((v,pos)=>{
    const key=[g.kind,g.logical[0],v,pos,external,declared].join(',')
    let values=memo!.get(key)
    if(!values){values=hand.flatMap((t,i)=>allows(t,v,g.kind,pos,g.logical[0],external,declared)?[i]:[]);memo!.set(key,values)}
    return {gi,pos,values}
  }))
}
function match(hand:number[], template:Template[], winningSlot=-1, external=false, declared='', slots=matchingSlots(hand,template,external,declared)):DuocaiGroup[]|null {
  if(slots.length!==hand.length||slots.some(s=>!s.values.length))return null
  const owners=Array(hand.length).fill(-1), last=hand.length-1
  if(winningSlot>=0){if(!slots[winningSlot].values.includes(last))return null;owners[last]=winningSlot}
  function assign(slot:number,visited:boolean[]):boolean {
    for(const i of slots[slot].values){
      if(winningSlot>=0&&i===last||visited[i])continue
      visited[i]=true
      if(owners[i]<0||assign(owners[i],visited)){owners[i]=slot;return true}
    }
    return false
  }
  const order=slots.map((_,i)=>i).sort((a,b)=>slots[a].values.length-slots[b].values.length)
  if(!order.every(i=>i===winningSlot||assign(i,Array(hand.length).fill(false))))return null
  const groups=template.map(g=>({...g,indices:Array(g.logical.length).fill(-1),physical:[] as number[],external,concealed:!external}))
  owners.forEach((slot,i)=>{const {gi,pos}=slots[slot];groups[gi].indices[pos]=i})
  for(const g of groups)g.physical=g.indices.map(i=>hand[i])
  return groups
}
function* matched(hand:number[],template:Template[]):Generator<DuocaiGroup[]> {
  let slot=0
  const seen=new Set<string>()
  const slots=matchingSlots(hand,template)
  if(slots.some(s=>!s.values.length))return
  for(const group of template)for(const logical of group.logical){
    const key=JSON.stringify([group.kind,group.logical,logical])
    if(!seen.has(key)){
      const groups=match(hand,template,slot,false,'',slots)
      if(groups){seen.add(key);yield groups}
    }
    slot++
  }
}
export function meldOptions(meld:DuocaiMeld):DuocaiGroup[] {
  if(meld.physical.length<3||!validPhysical(meld.physical))return []
  const result:DuocaiGroup[]=[]
  for(const kind of meld.physical.length===3?['sequence','triplet']:['kong'])for(const value of TILES){
    if(kind==='sequence'&&(value>=40||value%10>7))continue
    const logical=kind==='sequence'?[value,value+1,value+2]:Array(meld.physical.length).fill(value)
    if(match(meld.physical,[{kind,logical}],-1,true,meld.declared))result.push({kind,logical,physical:[...meld.physical],indices:[],external:true,concealed:meld.concealed})
  }
  return result
}
function* product<T>(arrays:T[][],depth=0,prefix:T[]=[]):Generator<T[]> {
  if(depth===arrays.length){yield [...prefix];return}
  for(const value of arrays[depth]){prefix.push(value);yield*product(arrays,depth+1,prefix);prefix.pop()}
}
function* choose<T>(values:T[],n:number,start=0,prefix:T[]=[]):Generator<T[]> {
  if(n===0){yield [...prefix];return}
  for(let i=start;i<=values.length-n;i++){prefix.push(values[i]);yield*choose(values,n-1,i+1,prefix);prefix.pop()}
}
function templateHand(hand:number[],meldCount:number,seven=true):LocalHand {
  const counts=TILES.map(v=>hand.filter(t=>!joker(t)&&face(t)===v).length)
  return {counts,caps:Array(34).fill(hand.length),ghosts:hand.filter(joker).length,physical:{},melds:Array(meldCount).fill(''),policy:{sevenPairs:seven,orphans:seven}}
}
function fromCode(code:string):Template {
  const v=Number(code.slice(1)),kind=code[0]==='q'?'pair':code[0]==='S'?'sequence':'triplet'
  return {kind,logical:kind==='sequence'?[v-1,v,v+1]:Array(kind==='pair'?2:3).fill(v)}
}
export function* duocaiShapes(hand:number[],melds:DuocaiMeld[]=[],variant:Variant='guobiao'):Generator<DuocaiShape> {
  if(hand.length+melds.length*3!==14||melds.length>4||!validPhysical([...hand,...melds.flatMap(m=>m.physical)]))return
  const external=melds.map(meldOptions)
  if(external.some(rows=>!rows.length))return
  const seen=new Set<string>()
  function make(kind:string,closed:DuocaiGroup[],open:DuocaiGroup[]):DuocaiShape|null {
    const groups=[...open,...closed],assignment=Array(hand.length).fill(0)
    let winning_group=-1
    groups.forEach((g,gi)=>g.indices.forEach((id,pos)=>{assignment[id]=g.logical[pos];if(id===hand.length-1)winning_group=gi}))
    const winning_face=assignment[hand.length-1]
    if(kind==='knitted'||kind==='unconnected'){
      const key=kind+'|'+groups.map((g,i)=>[g.kind,g.logical.join(','),g.external,g.concealed,i===winning_group].join(':')).sort().join('|')+'|'+[...assignment].sort((a,b)=>a-b).join(',')+'|'+winning_face
      if(seen.has(key))return null
      seen.add(key)
    }
    return {kind,groups,winning_group,winning_face,assignment,physical:[...hand]}
  }
  for(const base of templates(templateHand(hand,melds.length))){
    if(base.kind==='seven_pairs'&&variant==='riichi'&&base.logical.some(n=>n>2))continue
    const logical=base.logical.flatMap((n,i)=>Array(n).fill(TILES[i]))
    const template=base.kind==='standard'?base.closed.map(fromCode):base.kind==='seven_pairs'?TILES.flatMap((v,i)=>Array.from({length:base.logical[i]/2},()=>({kind:'seven_pair',logical:[v,v]}))):[{kind:'orphan',logical}]
    for(const closed of matched(hand,template))for(const open of product(external)){
      const shape=make(base.kind,closed,open)
      if(shape){if(base.kind==='thirteen_orphans'){shape.groups=[];shape.winning_group=-1}yield shape}
    }
  }
  if(variant!=='guobiao')return
  const natural=hand.filter(t=>!joker(t)).map(face)
  if(!melds.length&&new Set(natural).size===natural.length)for(const pattern of KNITTED){
    const domain=[...pattern,41,42,43,44,45,46,47].sort((a,b)=>a-b)
    if(natural.some(v=>!domain.includes(v)))continue
    for(const extra of choose(domain.filter(v=>!natural.includes(v)),14-natural.length)){
      for(const closed of matched(hand,[{kind:'unconnected',logical:[...natural,...extra].sort((a,b)=>a-b)}])){
        const shape=make('unconnected',closed,[])
        if(shape){shape.groups=[];shape.winning_group=-1;yield shape}
      }
    }
  }
  if(melds.length<=1)for(const pattern of KNITTED){
    const choices=pattern.map(value=>hand.flatMap((t,i)=>allows(t,value,'knitted')?[i]:[]))
    if(choices.some(c=>!c.length))continue
    const order=pattern.map((_,i)=>i).sort((a,b)=>choices[a].length-choices[b].length),assigned=Array(9).fill(-1),seenMasks=new Set<string>()
    function* fill(depth:number,mask:number):Generator<number[]> {
      if(depth===9){
        const win=assigned.indexOf(hand.length-1),key=mask+','+(win<0?0:pattern[win])
        if(!seenMasks.has(key)){seenMasks.add(key);yield [...assigned]}
        return
      }
      const slot=order[depth]
      for(const i of choices[slot])if(!(mask&(1<<i))){assigned[slot]=i;yield*fill(depth+1,mask|(1<<i))}
    }
    for(const indices of fill(0,0)){
      const remaining=hand.map((_,i)=>i).filter(i=>!indices.includes(i)),rest=remaining.map(i=>hand[i])
      for(const base of templates(templateHand(rest,3+melds.length,false))){
        const template=base.closed.map(fromCode)
        const candidates=remaining.includes(hand.length-1)?matched(rest,template):[match(rest,template)]
        for(const tail of candidates){
          if(!tail)continue
          const closed=tail.map(g=>({...g,indices:g.indices.map(i=>remaining[i])}))
          closed.unshift({kind:'knitted',logical:pattern,indices,physical:indices.map(i=>hand[i]),external:false,concealed:true})
          for(const open of product(external)){const shape=make('knitted',closed,open);if(shape)yield shape}
        }
      }
    }
  }
}
