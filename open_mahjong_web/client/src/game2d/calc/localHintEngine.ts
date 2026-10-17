import {jokerWaiting,describeJoker} from './guobiao/jokerLocal'
import {sixteenWaiting,sixteenScore} from './guobiao/sixteenLocal'
import {regionalSnapshot} from './regionalLocal'
import type {FourJokersHintRequest,FourJokersSnapshot,FourJokersHint} from './guobiao/fourJokers'

export function calculateLocalHint(kind:string,body:any):FourJokersSnapshot|FourJokersHint|ReturnType<typeof regionalSnapshot> {
  if(kind==='regional.snapshot')return regionalSnapshot(body)
  const sixteen=body.sub_rule==='guobiao/sixteen'
  if(kind==='guobiao.waits'){
    const hand:number[]=[...body.hand_tiles],melds:string[]=[...body.tiles_combination]
    const waiting=(tiles:number[])=>sixteen?sixteenWaiting(tiles,melds):jokerWaiting(tiles,melds,body.sub_rule)
    const result:FourJokersSnapshot={sub_rule:body.sub_rule,hand,melds,waits:[],discard_waits:{}}
    if(hand.length%3===1)result.waits=waiting(hand)
    else if(hand.length%3===2)for(const cut of new Set(hand)){
      if(body.sub_rule==='guobiao/huahun'&&cut>=51&&cut<=54)continue
      const rest=[...hand];rest.splice(rest.indexOf(cut),1);result.discard_waits[cut]=waiting(rest)
    }
    return result
  }
  if(kind!=='guobiao.score')throw new Error('Unknown local hint job: '+kind)
  const q=body as FourJokersHintRequest
  if(!sixteen)return describeJoker(q)
  let maximum=0
  for(const [way,kind,prefix] of [[q.ron_way,'dianhe',''],[q.self_way,'zimo','自摸']] as const){
    const result=sixteenScore(q.hand_tiles,q.tiles_combination,way,q.get_tile)
    maximum=Math.max(maximum,result.score)
    if(result.score>0&&result.qualifying>=q.hepai_limit)return {kind,label:`${prefix}${result.score}番`,score:result.score}
  }
  return {kind:'wuyi',label:'未起和',score:maximum}
}
