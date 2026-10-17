import {TILES,prepare,canWin,shapes,meldTiles,type Policy} from '../localJokerHand'
import {Chinese_Hepai_Check as MCR} from './gbHepai'
import {KNITTED} from './jokerLocal'

const policy=(count=5,formal=false):Policy=>({meldCount:count,supply:formal?Object.fromEntries(TILES.map(t=>[t,5])):undefined})
const VALUES:Record<string,number>={tianhe:8,dihe:8,renhe:8,...MCR.count_model_dict_default}
const LABELS:Record<string,string>={tianhe:'天和',dihe:'地和',renhe:'人和',...MCR.eng_to_chinese_dict}
type Group={code:string;tile:number;tiles:number[];kind:string;external:boolean;concealed:boolean}
type Decomposition={groups:Group[];knitted:boolean}
const group=(code:string,external=false):Group=>({code,tile:Number(code.slice(1)),tiles:meldTiles(code)!,kind:code[0]==='q'?'pair':'sS'.includes(code[0])?'sequence':'gG'.includes(code[0])?'kong':'triplet',external,concealed:!external||code[0]==='G'})
function* decompositions(hand:number[],melds:string[],formal=false):Generator<Decomposition>{
  for(const shape of shapes(prepare(hand,melds,policy(5,formal))))yield {groups:[...melds.map(c=>group(c,true)),...shape.closed.map(c=>group(c))],knitted:false}
  if(melds.length>2)return
  for(let i=0;i<KNITTED.length;i++){
    const pattern=KNITTED[i],rest=[...hand];let valid=true
    for(const t of pattern){const index=rest.indexOf(t);if(index<0){valid=false;break}rest.splice(index,1)}
    if(!valid)continue
    for(const shape of shapes(prepare(rest,melds,policy(2,formal))))yield {groups:[{code:'z'+i,tile:0,tiles:pattern,kind:'knitted',external:false,concealed:true},...melds.map(c=>group(c,true)),...shape.closed.map(c=>group(c))],knitted:true}
  }
}
function complete(hand:number[],melds:string[],formal=false){return !!prepare(hand,melds,policy(5,formal))&&!decompositions(hand,melds,formal).next().done}
export function sixteenWaiting(hand:number[],melds:string[],formal=false):number[]{
  if(!prepare(hand,melds,policy(),false))return []
  return TILES.filter(t=>complete([...hand,t],melds,formal))
}
function* combinations<T>(values:T[],count:number,start=0):Generator<T[]>{
  if(!count){yield [];return}for(let i=start;i<=values.length-count;i++)for(const tail of combinations(values,count-1,i+1))yield [values[i],...tail]
}
const binary=(a:number,b:number)=>a===b?'yibangao':a%10===b%10?'xixiangfeng':Math.floor(a/10)===Math.floor(b/10)?Math.abs(a-b)===3?'lianliu':[a%10,b%10].sort((a,b)=>a-b).join(',')==='2,8'?'laoshaofu':null:null
function doubleChows(chows:[number,number][],contracted:number[]):Record<string,number>{
  const parents=Object.fromEntries(chows.map(([i])=>[i,i]));for(const i of contracted.slice(1))parents[i]=contracted[0]
  const edges=[...combinations(chows,2)].flatMap(([[i,a],[j,b]])=>{const key=binary(a,b);return key?[{i,j,key}]:[]})
  const root=(p:Record<number,number>,i:number):number=>p[i]===i?i:root(p,p[i]);let best:Record<string,number>={},maximum=0
  function search(pos:number,p:Record<number,number>,chosen:Record<string,number>,total:number){
    if(total>maximum){maximum=total;best={...chosen}}if(pos===edges.length||total+edges.length-pos<=maximum)return
    const {i,j,key}=edges[pos],a=root(p,i),b=root(p,j)
    if(a!==b&&!(key==='laoshaofu'&&chosen[key]))search(pos+1,{...p,[b]:a},{...chosen,[key]:(chosen[key]??0)+1},total+1)
    search(pos+1,p,chosen,total)
  }
  search(0,parents,{},0);return best
}
function* major(chows:[number,number][],pungs:[number,number][]):Generator<[string|null,number[]]>{
  yield [null,[]]
  for(const n of [3,4]){
    for(const selected of combinations(chows,n)){
      const ids=selected.map(c=>c[0]),values=selected.map(c=>c[1]),suits=new Set(values.map(v=>Math.floor(v/10))),ranks=values.map(v=>v%10).sort((a,b)=>a-b)
      if(suits.size===1){
        if(new Set(ranks).size===1)yield [n===3?'yisesantongshun':'yisesitongshun',ids]
        if(new Set(ranks).size===n&&new Set(ranks.slice(1).map((v,i)=>v-ranks[i])).size===1){
          if([1,2].includes(ranks[1]-ranks[0]))yield [n===3?'yisesanbugao':'yisesibugao',ids]
          if(n===3&&ranks.join(',')==='2,5,8')yield ['qinglong',ids]
        }
      }else if(n===3&&suits.size===3){
        if(new Set(ranks).size===1)yield ['sansesantongshun',ids]
        if(ranks.join(',')==='2,5,8')yield ['hualong',ids]
        if(ranks.every((v,i)=>v===ranks[0]+i))yield ['sansesanbugao',ids]
      }
    }
    for(const selected of combinations(pungs,n)){
      const values=selected.map(c=>c[1]),ranks=values.map(v=>v%10).sort((a,b)=>a-b)
      if(Math.max(...values)<40&&new Set(values.map(v=>Math.floor(v/10))).size===1&&ranks.every((v,i)=>v===ranks[0]+i))yield [n===3?'yisesanjiegao':'yisesijiegao',[]]
    }
  }
}
const EXCLUDES:Record<string,string[]>={dasixi:['pengpenghe','quanfengke','menfengke','yaojiuke'],lvyise:['hunyise'],sigang:['pengpenghe','dandiaojiang'],qingyaojiu:['pengpenghe','quandaiyao','shuangtongke','yaojiuke','wuzi'],ziyise:['pengpenghe','quandaiyao','yaojiuke'],sianke:['pengpenghe'],yisesitongshun:['siguiyi'],yisesijiegao:['pengpenghe'],hunyaojiu:['pengpenghe','quandaiyao','yaojiuke'],quanshuangke:['pengpenghe','duanyao','wuzi'],qingyise:['wuzi'],quanda:['dayuwu','wuzi'],quanzhong:['duanyao','wuzi'],quanxiao:['xiaoyuwu','wuzi'],quandaiwu:['duanyao','wuzi'],santongke:['shuangtongke'],dayuwu:['wuzi'],xiaoyuwu:['wuzi'],tuibudao:['queyimen'],miaoshouhuichun:['zimo'],gangshangkaihua:['zimo'],qiangganghe:['hejuezhang'],quanqiuren:['dandiaojiang'],shuangangang:['shuanganke'],buqiuren:['zimo'],pinghe:['wuzi'],duanyao:['wuzi']}
const terminal=(t:number)=>t>=40||t%10===1||t%10===9
const wind=(t:number)=>t>=41&&t<=44,dragon=(t:number)=>t>=45&&t<=47
function scoreShape(shape:Decomposition,way:string[],win:number,winning:number,single:boolean):{score:number;fan:Record<string,number>}{
  const g=shape.groups,tiles=g.flatMap(g=>g.tiles),domain=[...new Set(tiles)],chows=g.flatMap((g,i)=>g.kind==='sequence'?[[i,g.tile] as [number,number]]:[]),pungs=g.flatMap((g,i)=>['triplet','kong'].includes(g.kind)?[[i,g.tile] as [number,number]]:[]),kongs=g.filter(g=>g.kind==='kong'),pair=g.find(g=>g.kind==='pair')!.tile
  const self=way.some(w=>['自摸','杠上开花','妙手回春','last_deal'].includes(w)),concealed=g.filter((g,i)=>['triplet','kong'].includes(g.kind)&&g.concealed&&(self||g.kind==='kong'||i!==winning)).length,closed=g.every(g=>g.concealed),ordinary=!shape.knitted,fan:Record<string,number>={}
  const add=(key:string,condition=true,count=1)=>{if(condition)fan[key]=count}
  const sets:Record<string,(t:number)=>boolean>={lvyise:t=>[32,33,34,36,38,47].includes(t),qingyaojiu:t=>t<40&&terminal(t),ziyise:t=>t>=40,quanda:t=>t<40&&t%10>=7,quanzhong:t=>t<40&&t%10>=4&&t%10<=6,quanxiao:t=>t<40&&t%10<=3,dayuwu:t=>t<40&&t%10>=6,xiaoyuwu:t=>t<40&&t%10<=4,tuibudao:t=>[21,22,23,24,25,28,29,46,32,34,35,36,38,39].includes(t),duanyao:t=>!terminal(t)}
  for(const [key,predicate] of Object.entries(sets))add(key,domain.every(predicate))
  const suits=new Set(domain.filter(t=>t<40).map(t=>Math.floor(t/10))),honors=domain.some(t=>t>=40)
  add('qingyise',suits.size===1&&!honors);add('hunyise',suits.size===1&&honors);add('hunyaojiu',honors&&suits.size>0&&domain.every(terminal));add('wumenqi',suits.size===3&&domain.some(wind)&&domain.some(dragon));add('queyimen',suits.size===2);add('wuzi',!honors)
  add('quanshuangke',ordinary&&pungs.length===5&&domain.every(t=>t<40&&t%2===0));add('quandaiwu',ordinary&&g.every(g=>g.tiles.some(t=>t<40&&t%10===5)));add('quandaiyao',ordinary&&g.every(g=>g.tiles.some(terminal)));add('pengpenghe',ordinary&&pungs.length===5);add('pinghe',pair<40&&chows.length===(ordinary?5:2));add('zuhelong',shape.knitted)
  add('sianke',concealed>=4);add('sananke',concealed===3);add('shuanganke',concealed===2);add('sigang',kongs.length>=4);add('sangang',kongs.length===3)
  const hiddenKongs=kongs.filter(g=>g.concealed).length
  if(kongs.length===2)add(['shuangminggang','mingangang','shuangangang'][hiddenKongs]);if(kongs.length===1)add(hiddenKongs?'angang':'minggang')
  add('siguiyi',true,domain.filter(t=>tiles.filter(x=>x===t).length===4&&!kongs.some(g=>g.tile===t)).length)
  const winds=new Set(pungs.map(g=>g[1]).filter(wind)),dragons=new Set(pungs.map(g=>g[1]).filter(dragon)),excluded=new Set(dragons)
  add('dasixi',winds.size===4);add('xiaosixi',winds.size===3&&wind(pair)&&!winds.has(pair));add('sanfengke',winds.size===3&&!fan.xiaosixi);add('dasanyuan',dragons.size===3);add('xiaosanyuan',dragons.size===2&&dragon(pair)&&!dragons.has(pair));add('shuangjianke',dragons.size===2&&!fan.xiaosanyuan);add('jianke',dragons.size===1)
  if(winds.size>=3)for(const t of winds)excluded.add(t)
  for(const [prefix,key] of [['场风','quanfengke'],['自风','menfengke']])for(let i=0;i<4;i++)if(way.includes(prefix+'东南西北'[i])&&winds.has(41+i)){add(key);excluded.add(41+i)}
  add('yaojiuke',true,pungs.filter(([,t])=>terminal(t)&&!excluded.has(t)).length)
  add('santongke',Array.from({length:9},(_,r)=>r+1).some(r=>new Set(pungs.filter(([,t])=>t<40&&t%10===r).map(([,t])=>Math.floor(t/10))).size===3))
  add('shuangtongke',true,[...combinations(pungs,2)].filter(([[,a],[,b]])=>a<40&&b<40&&Math.floor(a/10)!==Math.floor(b/10)&&a%10===b%10).length)
  add('sansesanjiegao',[...combinations(pungs,3)].some(c=>{const v=c.map(([,t])=>t),r=v.map(t=>t%10).sort((a,b)=>a-b);return Math.max(...v)<40&&new Set(v.map(t=>Math.floor(t/10))).size===3&&r.every((t,i)=>t===r[0]+i)}))
  add('quanqiuren',ordinary&&!self&&g.filter(g=>g.external&&!g.concealed).length===5);add(self?'buqiuren':'menqianqing',closed);add('zimo',self)
  for(const [label,key] of [['天和','tianhe'],['地和','dihe'],['人和','renhe'],['妙手回春','miaoshouhuichun'],['last_deal','miaoshouhuichun'],['海底捞月','haidilaoyue'],['last_cut','haidilaoyue'],['杠上开花','gangshangkaihua'],['抢杠和','qiangganghe'],['和绝张','hejuezhang']])add(key,way.includes(label))
  add('huapai',true,way.filter(w=>w==='花牌').length)
  if(single&&winning>=0){const target=g[winning];if(target.kind==='pair')add('dandiaojiang');else if(target.kind==='sequence'&&!target.external){if(win===target.tile)add('qianzhang');else if(target.tile%10===2&&win-target.tile===1||target.tile%10===8&&win-target.tile===-1)add('bianzhang')}}
  let best={score:0,fan:{} as Record<string,number>}
  for(const [key,selected] of major(chows,pungs)){
    const current={...fan};if(key)current[key]=1
    if(selected.length!==4)for(const [k,n] of Object.entries(doubleChows(chows,selected)))current[k]=(current[k]??0)+n
    for(const [high,lows] of Object.entries(EXCLUDES))if(current[high])for(const low of lows)delete current[low]
    if(!Object.entries(current).some(([k,n])=>k!=='huapai'&&n>0))current.wufanhe=1
    const score=Object.entries(current).reduce((sum,[k,n])=>sum+(VALUES[k]??0)*n,0)
    if(score>best.score)best={score,fan:current}
  }
  return best
}
export function sixteenScore(hand:number[],melds:string[],way:string[],win:number,allowSingle=true){
  const empty={score:0,qualifying:0,fans:[] as string[]};if(!hand.includes(win)||!complete(hand,melds))return empty
  const before=[...hand];before.splice(before.indexOf(win),1);const waits=allowSingle&&!way.includes('天和')?sixteenWaiting(before,melds,true):[];const single=waits.length===1&&waits[0]===win
  let best={score:0,fan:{} as Record<string,number>}
  for(const shape of decompositions(hand,melds))for(let i=0;i<shape.groups.length;i++)if(!shape.groups[i].external&&shape.groups[i].tiles.includes(win)){const result=scoreShape(shape,way,win,i,single);if(result.score>best.score)best=result}
  const repeat=new Set(['siguiyi','shuangtongke','yibangao','xixiangfeng','lianliu','yaojiuke','huapai'])
  return {score:best.score,qualifying:Math.max(0,best.score-way.filter(w=>w==='花牌').length),fans:Object.keys(VALUES).filter(k=>best.fan[k]>0).map(k=>LABELS[k]+(repeat.has(k)?'*'+best.fan[k]:''))}
}
