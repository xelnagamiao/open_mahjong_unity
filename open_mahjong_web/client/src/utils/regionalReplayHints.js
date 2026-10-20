import {resolveLocalHintSnapshot} from './localHintSnapshots.js'

/** These rows are previews only. They never grant an action or change settlement. */
export function regionalReplayHints(rule,round,node,seat,hand,melds,config={}) {
  let value=rule==='shangrao'?round?.shangrao_hints?.[seat]??null:null,ghosts=0
  for(const tick of (round?.action_ticks??[]).slice(0,Math.max(0,node))){
    if(tick?.[0]===rule&&(tick[1]==='hints'||rule==='shangrao'&&tick[1]==='waits')) {
      if(rule==='nanjing')value=tick[2]?.[seat]??value
      else if(Number(tick[2])===seat)value=tick[3]
    }
    if(rule==='shenyang'&&tick?.[0]==='sy'&&tick[1]?.kind==='hints'&&tick[1].player===seat)value=tick[1].hints
    if(rule==='guangdong'&&tick?.[0]==='guangdong'&&tick[1]==='ghost_discard'&&Number(tick[2])===seat)ghosts=Number(tick[3])
  }
  if(rule==='guangdong')value={local_request:{rule,hand:[...hand],melds:[...melds],context:{discarded_ghosts:ghosts,require_minimum_score:config.require_minimum_score!==false}}}
  if(!value)return null
  if(rule==='shenyang'&&!value.local_request)value={...value,waiting_tiles:Object.keys(value.waiting??{}).map(Number),waiting_details:value.waiting??{},waiting_by_discard:Object.fromEntries(Object.entries(value.by_discard??{}).map(([cut,rows])=>[cut,Object.keys(rows).map(Number)])),waiting_details_by_discard:value.by_discard??{}}
  const result=resolveLocalHintSnapshot(value)
  const same=(a,b)=>JSON.stringify([...(a??[])].sort())===JSON.stringify([...(b??[])].sort())
  if(!result||!same(result.source_melds,melds))return null
  if(same(result.source_hand_tiles,hand))return result
  if(result.source_hand_tiles?.length===hand.length+1)for(const cut of Object.keys(result.waiting_by_discard??{})){
    const rest=[...result.source_hand_tiles];const index=rest.indexOf(Number(cut));if(index<0)continue;rest.splice(index,1)
    if(same(rest,hand))return {...result,source_hand_tiles:[...hand],waiting_tiles:result.waiting_by_discard[cut],waiting_details:result.waiting_details_by_discard?.[cut]??{},waiting_by_discard:{},waiting_details_by_discard:{}}
  }
  return null
}
export function regionalReplayWaitData(rule,round,node,seat,hand,melds,known,config={}){
  const hints=regionalReplayHints(rule,round,node,seat,hand,melds,config);if(!hints)return null
  const rows=(tiles,scores)=>[...(tiles??[])].map(tile=>{
    const row=scores?.[tile]??{},status=row.status??row.kind
    const ron=['ron','dianhe','win'].includes(status),self=['zimo','self_only','tsumo_only','self_draw'].includes(status)
    const points=row.points??row.ron_fan??row.ron_min??0,unit=rule==='singapore'?'台':rule==='wuhan'?'点':'分'
    const label=row.label??(ron?`${points}${unit}`:self?'仅自摸':status==='not_ready'||status==='below_minimum'?'未起和':'未满足')
    return {tile,label,kind:ron?'dianhe':self?'zimo':'wuyi',base_f:ron?points:0,selfdrawn_f:self?(row.self_draw_points??points):0,unit,remaining_count:Math.max(0,(rule==='guangdong'&&tile>=55&&tile<=58?1:4)-known.filter(t=>t===tile).length)}
  })
  if(hand.length%3===1){const details=rows(hints.waiting_tiles,hints.waiting_details);return details.length?{type:'waits',details}:null}
  const details=Object.entries(hints.waiting_by_discard??{}).map(([tile,waits])=>({discard_tile:Number(tile),adds:rows(waits,hints.waiting_details_by_discard?.[tile])})).filter(row=>row.adds.length)
  return details.length?{type:'waits_all',details}:null
}
