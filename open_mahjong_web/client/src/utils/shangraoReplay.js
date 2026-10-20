export const parseShangraoFan = label => {
  const p=String(label??'').split('|')
  return p.length>=4&&p[0]==='SR'?{name:p[3],value:p[1]+p[2]}:null
}
export function shangraoInfoAt(round,node) {
  let info=structuredClone(round?.shangrao??null)
  for(const tick of (round?.action_ticks??[]).slice(0,Math.max(0,node))) {
    if(tick[0]!=='shangrao')continue
    if(tick[1]==='state')info=structuredClone(tick[2])
    if(tick[1]==='win_source')info=structuredClone(tick[6]??info)
    if(tick[1]==='burnt'&&info)info.burnt_tiles=[...(info.burnt_tiles??[]),Number(tick[2])]
  }
  return info
}
export function shangraoWallAt(round,node) {
  const original=round.tiles_list??[],remaining=original.map((tile,index)=>({tile,index})),indicator=round.shangrao?.indicator_index
  for(const tick of (round.action_ticks??[]).slice(0,Math.max(0,node))) {
    if(tick[0]!=='d'&&tick[0]!=='gd')continue
    const index=tick[0]==='d'?remaining.findIndex(t=>t.index!==indicator):remaining.findLastIndex(t=>t.index!==indicator)
    if(index<0)continue
    const [drawn]=remaining.splice(index,1)
    if(tick[0]==='gd'){
      const upper=remaining.findIndex(t=>t.index===drawn.index-1&&t.index!==indicator)
      if(upper>=0)remaining.splice(upper,1)
    }
  }
  const kept=new Set(remaining.map(t=>t.index))
  return {remaining:remaining.map(t=>t.tile),playable:remaining.filter(t=>t.index!==indicator).length,
    wall:original.map((tile,index)=>({tile,consumed:!kept.has(index)})),normalIndices:remaining.filter(t=>t.index!==indicator).map(t=>t.index)}
}
export function shangraoVisibleIndicators(info) {
  return info ? [info.indicator, ...(info.burnt_tiles ?? []).filter(tile => tile === info.bao)] : []
}
