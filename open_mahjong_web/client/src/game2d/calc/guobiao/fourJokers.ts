import type { WaitTipsContext, WaitInfoData, WaitDetail } from './waitTips'
import { buildBaseWay, countShowTilesOnTable, remainingCount } from './waitTips'

export interface FourJokersSnapshot {
  sub_rule?: string; hand: number[]; melds: string[]; waits: number[]; discard_waits: Record<string, number[]>
}
export interface FourJokersHintRequest {
  sub_rule?: string; no_joker_bonus?: string; black_flower_progressive?: boolean
  hand_tiles: number[]; tiles_combination: string[]; get_tile: number
  ron_way: string[]; self_way: string[]; hepai_limit: number; joker_multi_wait?: boolean
}
export interface FourJokersHint { kind: 'dianhe'|'zimo'|'wuyi'; label: string; score: number }
const waits = new Map<string, number[]>()
const hints = new Map<string, Promise<FourJokersHint | null>>()
const fetching = new Set<string>()
const failed = new Map<string, number>()
const key = (hand: number[], melds: string[], sub = "guobiao/four_jokers") => JSON.stringify([sub,[...hand].sort((a,b)=>a-b), [...melds].sort()])
function remember<T>(cache: Map<string,T>, id: string, value: T) {
  if (cache.size >= 128 && !cache.has(id)) cache.delete(cache.keys().next().value!)
  cache.set(id,value)
}
async function post<T>(route: string, body: unknown): Promise<T | null> {
  try {
    const res = await fetch(`/api/mahjong/gb/four-jokers/${route}`, { method:'POST',
      headers:{'Content-Type':'application/json'}, body:JSON.stringify(body), signal:AbortSignal.timeout(15000) })
    return res.ok ? await res.json() : null
  } catch { return null }
}
export function acceptFourJokersSnapshot(snapshot?: FourJokersSnapshot) {
  if (!Array.isArray(snapshot?.hand)) return
  const {hand, melds=[]} = snapshot!
  if (hand.length % 3 === 1) remember(waits,key(hand,melds,snapshot!.sub_rule),[...(snapshot!.waits || [])])
  for (const [tile, values] of Object.entries(snapshot!.discard_waits || {})) {
    const reduced=[...hand],index=reduced.indexOf(Number(tile))
    if(index<0)continue
    reduced.splice(index,1);remember(waits,key(reduced,melds,snapshot!.sub_rule),[...values])
  }
}
function requestWaits(ctx: WaitTipsContext) {
  const id=key(ctx.hand,ctx.combinations,ctx.subRule)
  if(fetching.size>=2 || fetching.has(id) || Date.now()<(failed.get(id)||0))return
  fetching.add(id)
  void post<FourJokersSnapshot>('waits',{sub_rule:ctx.subRule,hand_tiles:ctx.hand,tiles_combination:ctx.combinations}).then(snapshot=>{
    fetching.delete(id)
    if(!snapshot){remember(failed,id,Date.now()+5000);return}
    acceptFourJokersSnapshot(snapshot)
    if(typeof window !== 'undefined')window.dispatchEvent(new Event('four-jokers-waits-ready'))
  })
}
// At most two exact computations in flight. The caller checks that its UI
// entry still exists; only visible hint rows request exact scoring.
let active=0
const queue: Array<()=>void> = []
function drain() { while(active<2 && queue.length)queue.shift()!() }
export function resolveFourJokersHint(body: FourJokersHintRequest): Promise<FourJokersHint|null> {
  const id=JSON.stringify(body)
  if(hints.has(id))return hints.get(id)!
  if(queue.length>=64)return Promise.resolve(null)
  const task=new Promise<FourJokersHint|null>(resolve=>{
    queue.push(()=>{active++;void post<FourJokersHint>('hint',body).then(result=>{
      active--;if(!result)hints.delete(id);resolve(result);drain()
    })});drain()
  })
  remember(hints,id,task);return task
}
export function buildFourJokersWaitData(ctx:WaitTipsContext, includeDiscards:boolean):WaitInfoData {
  let missing=false
  function details(hand:number[],cut:number|null=null):WaitDetail[] {
    const tiles=waits.get(key(hand,ctx.combinations,ctx.subRule))
    if(!tiles){missing=true;return []}
    const way=buildBaseWay(ctx,tiles.length)
    return tiles.map(tile=>{
      const extra=countShowTilesOnTable(tile,ctx.seatDiscards,ctx.seatCombinations,cut)===3?['和绝张']:[]
      const supplyAdjustment=ctx.subRule==='guobiao/huahun' && tile>=51 && tile<=54 ? 3 : 0
      return {tile,base_f:0,selfdrawn_f:0,remaining_count:Math.max(0,remainingCount(hand,ctx.seatDiscards,ctx.seatCombinations,tile,cut)-supplyAdjustment),
        label:'计算中',four_jokers_request:{sub_rule:ctx.subRule,no_joker_bonus:ctx.noJokerBonus || "add8",black_flower_progressive:ctx.blackFlowerProgressive !== false,hand_tiles:[...hand,tile],tiles_combination:[...ctx.combinations],
          get_tile:tile,ron_way:[...way,...extra,'点和'],self_way:[...way,...extra,'自摸'],hepai_limit:ctx.hepaiLimit,joker_multi_wait:!!ctx.jokerMultiWait}}
    })
  }
  let result:WaitInfoData
  if(includeDiscards){
    const rows=[...new Set(ctx.hand)].filter(t=>ctx.subRule!=="guobiao/huahun" || t<51 || t>54).flatMap(tile=>{
      const hand=[...ctx.hand];hand.splice(hand.indexOf(tile),1)
      const adds=details(hand,tile);return adds.length?[{discard_tile:tile,adds}]:[]
    });result=rows.length?{type:'waits_all',details:rows}:null
  } else {const rows=details(ctx.hand);result=rows.length?{type:'waits',details:rows}:null}
  if(missing)requestWaits(ctx)
  return result
}
