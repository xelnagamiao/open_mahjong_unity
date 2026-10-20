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
  const ron=sixteenScore(q.hand_tiles,q.tiles_combination,q.ron_way,q.get_tile)
  const self=sixteenScore(q.hand_tiles,q.tiles_combination,q.self_way,q.get_tile)
  const canRon=ron.score>0&&ron.qualifying>=q.hepai_limit,canSelf=self.score>0&&self.qualifying>=q.hepai_limit
  if(canRon)return {kind:canSelf?'dianhe':'ron_only',label:canSelf?`${ron.score}番`:'仅点和',score:ron.score}
  return canSelf?{kind:'zimo',label:`自摸${self.score}番`,score:self.score}
    :{kind:'wuyi',label:'未起和',score:Math.max(ron.score,self.score)}
}
