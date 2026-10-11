export function parseNanchangFan(label) {
  const fields=String(label || '').split('|')
  return fields.length>=4 && fields[0]==='NC' ? {name:fields[3],value:fields[1]+fields[2]} : null
}
const object=value=>{if(typeof value==='string'){try{return JSON.parse(value)}catch{return null}}return value && typeof value==='object' && !Array.isArray(value)?value:null}
export function nanchangInfoAt(round,node) {
  let info=object(round?.nanchang)
  for(const tick of (round?.action_ticks || []).slice(0,Math.max(0,node))) {
    if(tick[0]!=='nanchang')continue
    if(tick[1]==='state')info=object(tick[2]) || info
    if(tick[1]==='win_source')info=object(tick[6]) || info
    if(tick[1]==='jingdiao' && info){const declared=[...(info.declared_jingdiao || [false,false,false,false])];declared[Number(tick[2])]=true;info={...info,declared_jingdiao:declared}}
  }
  return info
}
export function nanchangWaitsAt(round,node,seat,hand) {
  let cache=object(round?.nanchang_hints?.[seat]) || {}
  for(const tick of (round?.action_ticks || []).slice(0,Math.max(0,node)))
    if(tick[0]==='nanchang' && tick[1]==='waits' && Number(tick[2])===seat)cache=object(tick[3]) || {}
  return cache[[...hand].sort((a,b)=>a-b).join(',')] || []
}
export function nanchangHint(wait) {
  if(!wait || wait.status==='unmet')return {label:'未满足',kind:'wuyi'}
  if(wait.status==='below_minimum')return {label:'未起和',kind:'wuyi'}
  if(wait.status==='self_draw_only')return {label:'仅自摸',kind:'zimo'}
  if(wait.status!=='allowed' || !(wait.points>0))return {label:'未满足',kind:'wuyi'}
  const values=[...new Set(Object.values(wait.ron_points || {}).filter(p=>p>0))].sort((a,b)=>a-b)
  const low=wait.points_min>0?wait.points_min:values[0]??wait.points
  const high=wait.points_max>0?wait.points_max:values.at(-1)??wait.points
  return {label:high>low?`${low}～${high}分`:`${wait.points}分`,kind:'dianhe'}
}
export function nanchangWaitData(round,node,seat,hand,snapshot,toTile) {
  const counts=new Map(),add=t=>counts.set(t,(counts.get(t)||0)+1)
  hand.forEach(add)
  for(const player of snapshot.seats) {
    player.discard_pile.forEach(t=>add(toTile(t)))
    for(const meld of player.melds)(meld.physical_mask || []).forEach((t,i)=>{if(i%2)add(toTile(t))})
  }
  const details=tiles=>nanchangWaitsAt(round,node,seat,tiles).map(wait=>({...nanchangHint(wait),tile:wait.tile,base_f:wait.status==='allowed'?wait.points:0,selfdrawn_f:wait.self_draw?wait.self_draw_points:0,unit:'分',remaining_count:Math.max(0,4-(counts.get(wait.tile)||0))}))
  if(hand.length%3===1){const waits=details(hand);return waits.length?{type:'waits',details:waits}:null}
  if(hand.length%3!==2)return null
  const cuts=[...new Set(hand)].map(tile=>{const rest=[...hand];rest.splice(rest.indexOf(tile),1);return {discard_tile:tile,adds:details(rest)}}).filter(row=>row.adds.length)
  return cuts.length?{type:'waits_all',details:cuts}:null
}
export function nanchangLedgerRows(info) {
  return info?.ledger?Array.from({length:4},(_,seat)=>({seat,original:info.seat_to_original?.[seat]??seat,jing:info.ledger.jing_changes?.[seat]??0,kong:info.ledger.kong_changes?.[seat]??0,win:info.ledger.win_changes?.[seat]??0,total:info.ledger.changes?.[seat]??0})):[]
}
