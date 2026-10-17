import {resolveLocalHintSnapshot} from './localHintSnapshots.js'
/** The rule's physical gold indicator is saved, never regenerated during replay. */
export function fuzhouInfoAt(round,node) {
  let info=round?.fuzhou || null
  for(const tick of (round?.action_ticks || []).slice(0,Math.max(0,node))) {
    if(tick[0]!=='fuzhou') continue
    if(tick[1]==='state') info=tick[2] || info
    else if(tick[1]==='win_source') info=tick[6] || info
    else if(['kaijin','kaijin_flower','qiangjin_take','youjin','tingjin'].includes(tick[1])) info=tick[4] || info
  }
  return info
}

export function parseFuzhouFan(label) {
  const fields=String(label || '').split('|')
  return fields.length>=4 && fields[0]==='FZ' ? {name:fields[3],value:`${fields[2]}分`} : null
}

export function fuzhouActionLabel(tick) {
  return ({kaijin:'开金',kaijin_flower:'开金补花',qiangjin_take:'抢金',youjin:'游金',tingjin:'听金坎',win_source:'和牌',hints:'听牌提示',state:'福州结算'})[tick?.[1]] || '福州状态'
}

/** Mirrors physical transfers only; the authoritative server supplies each winning hand. */
export function applyFuzhouPhysical(states,tick) {
  const kind=tick[1],actor=Number(tick[2]),tile=Number(tick[3]),state=states[actor]
  if(kind==='kaijin_flower' && state) state.flowers.push(tile)
  if(kind==='qiangjin_take' && state) {
    if(state.drawn!=null) state.hand.push(state.drawn)
    state.drawn=tile
  }
  if(kind==='win_source' && state) {
    const source=states[Number(tick[3])],winning=Number(tick[5])
    if(source && tick[4]==='rob_kong') {
      if(source.drawn===winning) source.drawn=null
      else {const i=source.hand.indexOf(winning);if(i>=0)source.hand.splice(i,1)}
      if(source.drawn!=null) {source.hand.push(source.drawn);source.drawn=null}
    } else if(source && tick[4]==='discard' && tick[8] && source.river.at(-1)===winning) {
      source.river.pop();source.riverDrawn.pop()
    }
    const hand=tick[6]?.final_hands?.[actor]
    if(Array.isArray(hand)) {
      state.hand=[...hand];state.drawn=null
      if(!tick[7] && hand.length%3===2) {
        const i=state.hand.lastIndexOf(winning)
        if(i>=0) state.drawn=state.hand.splice(i,1)[0]
      }
    }
  }
  return kind==='kaijin'||kind==='kaijin_flower' ? 1 : 0
}

// Missing versions are legacy records. Never reinterpret their tail indicator.
export const isFuzhouV2 = version => version === 'fuzhou-user-20261015-v2'

export function fuzhouReserve(states, version) {
  let total=18
  for(const state of states) {
    for(const meld of state.melds) if(meld.type==='kong') total+=meld.concealed?2:1
    const flowers=state.flowers || []
    for(let t=41;t<=47;t++) if(flowers.filter(v=>v===t).length===4) total+=2
    if(isFuzhouV2(version)) for(const group of [[51,52,53,54],[55,56,57,58]])
      if(group.every(t=>flowers.includes(t))) total+=2
  }
  return total
}

const same=(a,b)=>Array.isArray(a)&&Array.isArray(b)&&a.length===b.length&&[...a].sort().every((t,i)=>t===[...b].sort()[i])
export function fuzhouHintsAt(round,node,seat,hand,melds) {
  let hints=null
  for(const tick of (round?.action_ticks || []).slice(0,Math.max(0,node)))
    if(tick[0]==='fuzhou'&&tick[1]==='hints') hints=tick[2]?.[seat] || null
  hints=resolveLocalHintSnapshot(hints)
  if(!hints||!same(hints.source_melds || [],melds)) return null
  if(same(hints.source_hand_tiles,hand)) return hints
  if(hints.source_hand_tiles?.length===hand.length+1)
    for(const [discard,waits] of Object.entries(hints.waiting_by_discard || {})) {
      const source=[...hints.source_hand_tiles],i=source.indexOf(Number(discard))
      if(i<0)continue
      source.splice(i,1)
      if(same(source,hand))return {...hints,source_hand_tiles:[...hand],waiting_tiles:waits,waiting_details:hints.waiting_details_by_discard?.[discard] || {},waiting_by_discard:{},waiting_details_by_discard:{}}
    }
  return null
}

export function fuzhouWaitDataAt(round,node,seat,hand,melds,knownTiles) {
  const hints=fuzhouHintsAt(round,node,seat,hand,melds)
  if(!hints)return null
  const info=fuzhouInfoAt(round,node),known=[...knownTiles]
  if(info?.gold_tile&&!info.indicator_claimed)known.push(info.gold_tile)
  const details=(waits,scores)=>(waits || []).map(tile=>{
    const ruleKind=scores?.[tile]?.kind,kind=ruleKind==='ron'?'dianhe':ruleKind==='self_only'?'zimo':'wuyi'
    return {tile,base_f:kind==='dianhe'?1:0,selfdrawn_f:kind==='zimo'?1:0,kind,
      label:kind==='dianhe'?'可和':kind==='zimo'?'仅自摸':'未满足',
      remaining_count:Math.max(0,4-known.filter(t=>t===tile).length)}
  })
  if(hand.length%3===2) {
    const rows=Object.entries(hints.waiting_by_discard || {}).map(([tile,waits])=>({discard_tile:Number(tile),adds:details(waits,hints.waiting_details_by_discard?.[tile])})).filter(r=>r.adds.length)
    return rows.length?{type:'waits_all',details:rows}:null
  }
  const rows=details(hints.waiting_tiles,hints.waiting_details)
  return rows.length?{type:'waits',details:rows}:null
}
