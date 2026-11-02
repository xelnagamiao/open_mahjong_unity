<script setup>
import {ref,watch,onMounted,onBeforeUnmount} from 'vue'
import {getJSON} from '../useStation.js'
import {demoWalls,demoRecords} from '../demoData.js'
import {dateLabel,count,normalizeRecords} from '../dataModel.js'
import Icon from './Icon.vue'
import RecordTable from './RecordTable.vue'
import {statisticsSelection} from '../../utils/statisticsCatalog.js'
const props=defineProps({source:String})
defineEmits(['select-record'])
const query=ref(''),items=ref([]),busy=ref(false),error=ref(''),selected=ref(null),detail=ref(null),detailBusy=ref(false),detailError=ref('')
let controller,detailController
async function load(){
  controller?.abort();controller=new AbortController();const current=controller
  busy.value=true;error.value='';selected.value=null;detail.value=null;detailController?.abort()
  try{items.value=props.source==='demo'?demoWalls.filter(w=>w.key.includes(query.value.trim())||w.name.includes(query.value.trim())):(await getJSON('/api/duplicate-walls/public?'+new URLSearchParams({key:query.value.trim()}),current.signal)).data.items||[]}
  catch(e){if(e.name!=='AbortError'){error.value=e.message;items.value=[]}}finally{if(controller===current)busy.value=false}
}
async function open(wall){
  detailController?.abort();detailController=new AbortController();const current=detailController
  selected.value=wall;detail.value=null;detailError.value='';detailBusy.value=true
  try{const data=props.source==='demo'?{wall,games:demoRecords('guobiao').items.slice(0,3)}:(await getJSON('/api/duplicate-walls/public/'+encodeURIComponent(wall.key),current.signal)).data;if(!current.signal.aborted)detail.value={...data,games:normalizeRecords(data.games||[])}}
  catch(e){if(e.name!=='AbortError')detailError.value=e.message}finally{if(detailController===current)detailBusy.value=false}
}
watch(()=>props.source,load);onMounted(load);onBeforeUnmount(()=>{controller?.abort();detailController?.abort()})
</script>
<template><div class="duplicate-view"><section class="panel"><div class="panel-heading"><div><h2>公开复式资料</h2><span class="subtle">已解锁的牌山与关联对局 · 所有规则</span></div><Icon name="key"/></div><form class="duplicate-search" @submit.prevent="load"><input v-model="query" placeholder="输入复式密钥检索" aria-label="复式密钥"><button class="primary-button" :disabled="busy" type="submit">{{busy?'查询中':'查询'}}</button><button type="button" @click="query='';load()">重置</button></form></section><div v-if="error" class="state-card error-state">{{error}}<button @click="load">重试</button></div><div v-else-if="busy" class="state-card" role="status">正在查找公开资料…</div><div v-else-if="!items.length" class="state-card"><h2>没有匹配的公开资料</h2><p>请检查密钥。尚未解锁的资料不会显示在此处。</p></div><div v-else class="duplicate-grid"><button v-for="wall in items" :key="wall.id" class="panel wall-card" :class="{active:selected?.id===wall.id}" @click="open(wall)"><div class="wall-top"><Icon name="key"/><span class="tag">已公开</span></div><h3>{{wall.name||'未命名牌山'}}</h3><p>{{wall.event_name||wall.owner_username||'公开复式资料'}}</p><dl><div><dt>规则</dt><dd>{{statisticsSelection(wall.rule)?.stats_name||wall.rule}}</dd></div><div><dt>小局数</dt><dd>{{count(wall.round_count)}}</dd></div><div><dt>公开时间</dt><dd>{{dateLabel(wall.unlocked_at)}}</dd></div></dl><span class="wall-key mono">{{wall.key}}</span><span class="record-open">查看关联对局 <Icon name="arrow" :size="16"/></span></button></div><section v-if="selected" class="panel wall-detail"><div class="panel-heading"><div><h2>{{selected.name}}</h2><span class="subtle">同牌山的关联对局</span></div><button class="icon-button" aria-label="关闭复式详情" @click="selected=null;detailController?.abort()"><Icon name="close"/></button></div><p v-if="detailBusy" role="status" class="inline-empty">正在载入关联对局…</p><p v-else-if="detailError" class="inline-empty error-state">{{detailError}}</p><template v-else-if="detail"><div class="wall-summary"><span>建立 {{dateLabel(detail.wall?.created_at||selected.created_at)}}</span><span>{{detail.wall?.tile_count||selected.tile_count||'—'}} 张 / 局</span><span>{{detail.wall?.is_deleted?'原资料已删除，保留关联信息':'资料可查询'}}</span></div><RecordTable :records="detail.games" :total="detail.games.length" :pagination="false" title="关联牌谱" subtitle="同一牌山已公开的对局" @select="$emit('select-record',$event)"/></template></section></div></template>
