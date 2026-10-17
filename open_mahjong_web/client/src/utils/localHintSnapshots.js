const values=new Map(),pending=new Map()
export function localHintRows(request,hand){
  const value=resolveLocalHintSnapshot({local_request:request});if(!value)return []
  const key=tiles=>[...tiles].sort((a,b)=>a-b).join(',')
  if(key(hand)===key(value.source_hand_tiles))return Object.values(value.waiting_details)
  if(value.source_hand_tiles.length===hand.length+1)for(const cut of Object.keys(value.waiting_by_discard)){
    const rest=[...value.source_hand_tiles];rest.splice(rest.indexOf(Number(cut)),1)
    if(key(rest)===key(hand))return Object.values(value.waiting_details_by_discard[cut])
  }
  return []
}
/** Resolve saved own-hand inputs on the local worker; old saved rows remain readable. */
export function resolveLocalHintSnapshot(snapshot) {
  if(!snapshot?.local_request)return snapshot
  const request=snapshot.local_request,key=JSON.stringify(request)
  if(values.has(key))return {...snapshot,...values.get(key),local_request:undefined}
  if(!pending.has(key)&&pending.size<64){
    const input=JSON.parse(key)
    const job=import('../game2d/calc/localHintJobs').then(m=>m.localHintJob('regional.snapshot',input)).then(result=>{
      if(values.size>=64)values.delete(values.keys().next().value)
      values.set(key,result);pending.delete(key)
      if(typeof window!=='undefined')window.dispatchEvent(new Event('four-jokers-waits-ready'))
      return result
    }).catch(error=>{pending.delete(key);console.error('Local hint calculation failed',error);return null})
    pending.set(key,job)
  }
  return null
}
