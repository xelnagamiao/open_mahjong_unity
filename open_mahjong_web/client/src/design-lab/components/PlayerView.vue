<script setup>
import {computed,ref,watch,onMounted,onBeforeUnmount} from 'vue'
import {getJSON} from '../useStation.js'
import {demoPlayer} from '../demoData.js'
import {playerMetrics,count,percent,mean,fanEntries,normalizeRecords} from '../dataModel.js'
import {statisticsSelection} from '../../utils/statisticsCatalog.js'
import MetricStrip from './MetricStrip.vue'
import FanView from './FanView.vue'
import RecordTable from './RecordTable.vue'
import Icon from './Icon.vue'
const props=defineProps({rule:String,source:String,initialQuery:String})
defineEmits(['select-record'])
const query=ref(props.initialQuery||''),room=ref(''),start=ref(''),end=ref(''),busy=ref(false),error=ref(''),result=ref(null)
let controller
const metrics=computed(()=>playerMetrics(result.value?.stats))
const playerCount=computed(()=>statisticsSelection(props.rule)?.player_count||4)
const placements=computed(()=>['first','second','third','fourth'].slice(0,playerCount.value).map((v,i)=>({label:(i+1)+' 位',count:Number(result.value?.stats?.[v+'_place_count'])||0})))
const placementTotal=computed(()=>placements.value.reduce((s,p)=>s+p.count,0))
const rankRecords=computed(()=>result.value?.records.slice(0,20).reverse().map(r=>({id:r.game_id,rank:r.players.find(p=>String(p.user_id)===String(result.value.info.user_id))?.rank}))||[])
const fans=computed(()=>{
  const dict=result.value?.info?.fan_dict||{}
  const definition=statisticsSelection(props.rule)
  return fanEntries(result.value?.stats?.fan_stats,dict[definition?.sub_rule]||dict[definition?.rule]||{},result.value?.stats?.win_count)
})
const stats=computed(()=>result.value?.stats)
const ruleName=computed(()=>statisticsSelection(props.rule)?.stats_name||props.rule)
async function search(){
  if(!query.value.trim()){error.value='请输入玩家昵称或 ID';return}
  if(start.value&&end.value&&start.value>end.value){error.value='开始日期不能晚于结束日期';return}
  controller?.abort();controller=new AbortController();const current=controller
  busy.value=true;error.value='';result.value=null
  try{
    if(props.source==='demo'){result.value=demoPlayer(query.value,props.rule);return}
    const info=(await getJSON('/api/player/info/'+encodeURIComponent(query.value.trim()),current.signal)).data
    if(!info?.user_id)throw new Error('未找到该玩家')
    const params=new URLSearchParams({rule:props.rule})
    if(room.value)params.set('room_type',room.value)
    if(start.value)params.set('date_from',start.value+'T00:00:00+08:00')
    if(end.value){const next=new Date(end.value+'T00:00:00Z');next.setUTCDate(next.getUTCDate()+1);params.set('date_to',next.toISOString().slice(0,10)+'T00:00:00+08:00')}
    const [statResponse,recordResponse]=await Promise.all([getJSON('/api/player/rank-stats/'+encodeURIComponent(info.user_id)+'?'+params,current.signal),getJSON('/api/player/records/'+encodeURIComponent(info.user_id)+'?'+params+'&limit=20',current.signal)])
    if(!current.signal.aborted)result.value={info,stats:statResponse.data,records:normalizeRecords(recordResponse.data.items)}
  }catch(e){if(e.name!=='AbortError')error.value=e.message}finally{if(controller===current)busy.value=false}
}
watch(()=>[props.rule,props.source],()=>{if(result.value)search()})
watch(()=>props.initialQuery,v=>{if(v){query.value=v;search()}})
onMounted(()=>{if(props.initialQuery)search()})
onBeforeUnmount(()=>controller?.abort())
</script>
<template><div class="player-view"><section class="panel player-query"><div class="panel-heading"><div><h2>查询玩家</h2><span class="subtle">查看公开对局表现 · {{ruleName}}</span></div><Icon name="person"/></div><form @submit.prevent="search" class="query-form"><label class="query-name"><span>昵称或 ID</span><input v-model="query" placeholder="输入完整昵称或玩家 ID" autocomplete="off" aria-label="玩家昵称或 ID"></label><label><span>对局类型</span><select v-model="room"><option value="">全部类型</option><option value="match">匹配场</option><option value="custom">自定义</option></select></label><label><span>开始日期</span><input type="date" v-model="start" aria-label="玩家统计开始日期"></label><label><span>结束日期</span><input type="date" v-model="end" aria-label="玩家统计结束日期"></label><button type="submit" class="primary-button" :disabled="busy"><Icon name="search" :size="17"/>{{busy?'查询中':'查询'}}</button></form><p class="panel-footnote">日期按北京时间自然日查询，同时作用于玩家统计和牌谱。<button class="text-button" @click="query=source==='demo'?'青空':'GAlianyuan';search()">试查一位{{source==='demo'?'示例':'公开'}}玩家 →</button></p></section><div v-if="error" class="state-card error-state" role="alert">{{error}}</div><div v-else-if="busy" class="loading-block" role="status"><i/><i/><i/><span>正在整理玩家的公开数据…</span></div><template v-else-if="result"><div class="profile-line"><span class="avatar-letter">{{(result.info.user_settings?.username||query).slice(0,1)}}</span><div><h2>{{result.info.user_settings?.username||query}}</h2><span class="subtle">ID {{result.info.user_id}} · {{ruleName}}</span></div><span class="tag">{{source==='demo'?'虚构示例':'公开档案'}}</span></div><MetricStrip :metrics="metrics"/><div class="two-column"><section class="panel"><div class="panel-heading"><div><h2>顺位分布</h2><span class="subtle">个人对局结算 · {{count(placementTotal)}} 场</span></div></div><div class="placement-stack"><span v-for="(p,i) in placements" :key="p.label" :style="{flex:p.count||.001,opacity:1-i*.2}" :title="`${p.label} ${p.count} 场`">{{p.count?percent(p.count,placementTotal,0):''}}</span></div><div class="placement-legend" :style="{gridTemplateColumns:`repeat(${playerCount},1fr)`}"><div v-for="(p,i) in placements" :key="p.label"><i :style="{opacity:1-i*.2}"/><span>{{p.label}}</span><strong>{{count(p.count)}}</strong><small>{{percent(p.count,placementTotal)}}</small></div></div><p class="panel-footnote">只描述该玩家自己的对局结果，不比较其他玩家。</p></section><section class="panel"><div class="panel-heading"><div><h2>最近对局轨迹</h2><span class="subtle">从左至右由早到晚 · 最近 {{rankRecords.length}} 场</span></div></div><div v-if="rankRecords.length" class="rank-chart"><div class="rank-axis"><span v-for="i in playerCount" :key="i">{{i}} 位</span></div><svg viewBox="0 0 400 145" role="img" aria-label="玩家最近对局顺位变化"><line v-for="i in playerCount" :key="i" x1="6" x2="390" :y1="12+(i-1)*120/(playerCount-1)" :y2="12+(i-1)*120/(playerCount-1)" class="rank-grid"/><polyline :points="rankRecords.filter(r=>r.rank).map((r,i)=>`${10+i/Math.max(1,rankRecords.length-1)*376},${12+(r.rank-1)*120/(playerCount-1)}`).join(' ')" class="chart-line"/><circle v-for="(r,i) in rankRecords.filter(r=>r.rank)" :key="r.id" :cx="10+i/Math.max(1,rankRecords.length-1)*376" :cy="12+(r.rank-1)*120/(playerCount-1)" r="4" class="rank-point"><title>{{r.id}} · {{r.rank}} 位</title></circle></svg></div><div v-else class="inline-empty">暂无结算轨迹</div></section></div><section class="panel"><div class="panel-heading"><div><h2>对局习惯</h2><span class="subtle">{{stats.details_available?'明细已统计':'当前范围尚无完整明细'}}</span></div></div><div class="detail-metrics"><div><span>自摸占和牌</span><strong>{{stats.details_available?percent(stats.self_draw_count,stats.win_count):'—'}}</strong></div><div><span>副露率</span><strong>{{stats.details_available?percent(stats.fulu_round_count,stats.total_rounds):'—'}}</strong></div><div><span>平均和牌巡目</span><strong>{{stats.details_available?mean(stats.total_win_turn,stats.win_count,1):'—'}}</strong></div><div><span>已分析对局</span><strong>{{count(stats.analyzed_games)}}</strong></div></div></section><FanView v-if="stats.supports_fan_stats&&fans.length" :fans="fans" compact/><RecordTable :records="result.records" :total="result.records.length" :pagination="false" title="玩家最近牌谱" subtitle="当前查询范围内最近 20 场" @select="$emit('select-record',$event)"/></template><section v-else class="player-start"><span class="large-outline">P</span><h2>从一位玩家开始。</h2><p>把对局样本、顺位变化和攻守数据放在同一份档案中。<br>输入昵称或 ID，即可查看当前规则下的公开数据。</p><div class="start-features"><span>01 / 表现概览</span><span>02 / 顺位轨迹</span><span>03 / 对局回看</span></div></section></div></template>
