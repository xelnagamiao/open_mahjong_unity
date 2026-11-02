import { ref, computed, watch, onMounted } from 'vue'
import { STATISTICS_RULES, statisticsSelection } from '../utils/statisticsCatalog.js'
import { selectTotal, dailySeries, fanEntries, platformMetrics, normalizeRecords } from './dataModel.js'
import { demoDataset, demoRecords } from './demoData.js'

export async function getJSON(url, signal) {
  const timeout=AbortSignal.timeout(20000)
  let response
  try { response=await fetch(url,{signal:signal?AbortSignal.any([signal,timeout]):timeout,cache:'no-store'}) }
  catch(error){if(error.name==='TimeoutError')throw new Error('读取超时，请重新载入');throw error}
  let body
  try { body = await response.json() } catch { throw new Error('服务返回了无法读取的内容') }
  if (!response.ok || body.success === false) throw new Error(response.status === 429 ? '查询过于频繁，请稍后重试' : body.message || '数据加载失败')
  return body
}
export function useStation() {
  const query = new URLSearchParams(location.search)
  const source=ref(query.get('source')==='demo'?'demo':'live')
  const rule=ref(STATISTICS_RULES.some(r=>r.key===query.get('rule'))?query.get('rule'):'guobiao')
  const days=ref(30),tier=ref('total'),eventId=ref(''),loading=ref(false),error=ref('')
  const dataset=ref({totals:[],daily:[],fans:{},fan_dict:{},events:[],meta:{}})
  const records=ref([]),recordTotal=ref(0),recordPage=ref(1),recordLoading=ref(false),recordError=ref('')
  const classics=ref([]),hierarchy=ref([]),extrasError=ref('')
  let controller,recordController,extrasSequence=0
  const definition=computed(()=>statisticsSelection(rule.value))
  const ruleName=computed(()=>definition.value?.stats_name||rule.value)
  const rows=computed(()=>dataset.value.totals.filter(t=>t.match_tier!=='total').map(row=>({...row,label:dataset.value.events.find(e=>(e.event_id||e.id)===row.match_tier)?.name})))
  const total=computed(()=>selectTotal(dataset.value.totals,tier.value))
  const series=computed(()=>dailySeries(dataset.value.daily,dataset.value.meta.date_from,dataset.value.meta.date_to,tier.value))
  const periodGames=computed(()=>series.value.reduce((s,d)=>s+d.value,0))
  const fans=computed(()=>fanEntries(dataset.value.fans?.[tier.value],dataset.value.fan_dict,total.value?.win_count))
  const unit=computed(()=>({fan:'番',fu:'符',points:'点',tai:'台',base_points:'基本分',multiplier:'倍'})[definition.value?.win_stat_unit] || '')
  const metrics=computed(()=>platformMetrics(total.value,unit.value))
  async function loadStats() {
    controller?.abort();controller=new AbortController();const current=controller
    loading.value=true;error.value=''
    try{
      let next
      if(source.value==='demo') next=demoDataset(rule.value,days.value,eventId.value)
      else{
        const params=new URLSearchParams({rule:rule.value,days:String(days.value)})
        if(eventId.value)params.set('event_id',eventId.value)
        const response=await getJSON('/api/platform/stats?'+params,current.signal)
        next={...response.data,meta:response.meta||{}}
      }
      if(current.signal.aborted)return
      dataset.value=next
    }catch(e){if(e.name!=='AbortError')error.value=e.message}
    finally{if(controller===current)loading.value=false}
  }
  async function loadRecords() {
    recordController?.abort();recordController=new AbortController();const current=recordController
    recordLoading.value=true;recordError.value=''
    try{
      let result
      if(source.value==='demo')result=demoRecords(rule.value,tier.value,(recordPage.value-1)*12)
      else{
        const params=new URLSearchParams({rule:rule.value,limit:'12',offset:String((recordPage.value-1)*12)})
        if(eventId.value)params.set('event_id',eventId.value)
        else if(tier.value!=='total')params.set('match_tier',tier.value)
        result=(await getJSON('/api/platform/recent-records?'+params,current.signal)).data
      }
      if(current.signal.aborted)return
      records.value=normalizeRecords(result.items);recordTotal.value=result.total||0
    }catch(e){if(e.name!=='AbortError'){recordError.value=e.message;records.value=[];recordTotal.value=0}}
    finally{if(recordController===current)recordLoading.value=false}
  }
  async function loadExtras() {
    const sequence=++extrasSequence;extrasError.value=''
    if(source.value==='demo'){
      hierarchy.value=[{rule:rule.value,room_type:'match',game_type_label:'全庄战',total_games:11796,details_available:true}]
      classics.value=demoRecords(rule.value).items.slice(0,3).map((r,i)=>({...r,description:['门前的选择','攻守之间','终盘的一手'][i],round:i+1}))
      return
    }
    const results=await Promise.allSettled([getJSON('/api/platform/home-stats'),getJSON('/api/platform/fun-stats')])
    if(sequence!==extrasSequence)return
    hierarchy.value=results[0].status==='fulfilled'?results[0].value.data.rows:[]
    classics.value=results[1].status==='fulfilled'?results[1].value.data.classics:[]
    if(results.some(r=>r.status==='rejected'))extrasError.value='部分资料暂时未能载入'
  }
  function selectTier(value){
    const event=dataset.value.events.find(e=>(e.event_id||e.id)===value)
    if(event){eventId.value=value;tier.value='total'}
    else{eventId.value='';tier.value=value}
    recordPage.value=1
  }
  function refresh(){loadStats();loadRecords();loadExtras()}
  watch([rule,source],()=>{tier.value='total';eventId.value='';recordPage.value=1;refresh()})
  watch([days,eventId],()=>{if(eventId.value)tier.value='total';loadStats()})
  watch(eventId,()=>recordPage.value=1)
  watch([tier,eventId,recordPage],loadRecords)
  onMounted(refresh)
  return {source,rule,days,tier,eventId,loading,error,dataset,records,recordTotal,recordPage,recordLoading,recordError,classics,hierarchy,extrasError,definition,ruleName,rows,total,series,periodGames,fans,metrics,loadStats,loadRecords,refresh,selectTier,rules:STATISTICS_RULES}
}

