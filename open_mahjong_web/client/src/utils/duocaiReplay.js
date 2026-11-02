export function duocaiInfoAt(round,node){
  let info=round?.duocai??null
  for(const tick of (round?.action_ticks??[]).slice(0,node))if(tick[0]==='duocai'&&tick[1]==='state')info=tick[2]
  return info
}
export function duocaiWallAt(round,node){
  const info=duocaiInfoAt(round,node),initial=round?.duocai
  const positions=(initial?.wall??[]).filter(t=>!t.removed&&!t.dead).sort((a,b)=>(a.position-initial.opening+144)%144-(b.position-initial.opening+144)%144)
  const physical=new Map(positions.map((t,i)=>[t.position,round.tiles_list?.[i]??0]))
  const wall=(info?.wall??[]).map(t=>({...t,tile:t.tile??physical.get(t.position)??0,consumed:t.removed||t.dead}))
  return {info,wall,remaining:wall.filter(t=>!t.consumed).map(t=>t.tile),playable:wall.filter(t=>!t.consumed).length}
}
export function applyDuocaiAction(states,event,toTile){
  const actor=Number(event.player),state=states[actor],action=event.action
  if(!state)return {}
  if(action==='riichi'){state.riichi=true;return {actor}}
  if(action==='declare_kan')return {actor,robTile:event.tile}
  const remove=value=>{
    if(state.drawn===value){state.drawn=null;return}
    const i=state.hand.indexOf(value);if(i>=0)state.hand.splice(i,1)
  }
  if(action==='rob_kan'){remove(event.tile);return {actor,robTile:event.tile}}
  const mask=event.combination_mask
  if(!Array.isArray(mask))return {}
  const index=Number(event.meld_index??state.melds.length),previous=state.melds[index]
  const take=mask.filter((_,i)=>i%2)
  for(const value of previous?.duocai_physical??[]){const i=take.indexOf(value);if(i>=0)take.splice(i,1)}
  if(event.cut_from_player!=null){
    const i=take.indexOf(event.tile);if(i>=0)take.splice(i,1)
    const source=states[event.cut_from_player];source.river.pop();source.riverDrawn.pop()
  }
  for(const value of take)remove(value)
  if(state.drawn!=null){state.hand.push(state.drawn);state.drawn=null}
  const code=String(event.meld_code??''),prefix=code[0]
  state.melds[index]={tile:toTile(Number(code.slice(1))),type:prefix==='s'?'sequence':prefix==='k'?'triplet':'kong',
    concealed:prefix==='G',chow_mode:0,meld_from_rel:event.cut_from_player==null?0:(actor-event.cut_from_player+4)%4,
    physical_mask:mask.map((v,i)=>i%2?toTile(v):v),concealed_face_down:mask.filter((_,i)=>i%2===0).map(v=>v===2),
    duocai_physical:mask.filter((_,i)=>i%2),duocai_code:code}
  return {actor}
}
