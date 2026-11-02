<script setup>
import {computed,ref,watch} from 'vue'
import {count} from '../dataModel.js'
const props=defineProps({series:{type:Array,default:()=>[]},initial:{type:String,default:'line'},compact:Boolean,title:{type:String,default:'对局趋势'}})
const emit=defineEmits(['select-day'])
const mode=ref(props.initial),hover=ref(-1)
watch(()=>props.initial,v=>mode.value=v)
watch(()=>props.series,()=>hover.value=-1)
const max=computed(()=>Math.max(5,...props.series.map(d=>d.value)))
const total=computed(()=>props.series.reduce((s,d)=>s+d.value,0))
const selected=computed(()=>props.series[hover.value])
const point=(d,i)=>({x:42+(props.series.length===1?340:i/(props.series.length-1)*676),y:176-d.value/max.value*138})
const line=computed(()=>props.series.map((d,i)=>`${i?'L':'M'}${point(d,i).x},${point(d,i).y}`).join(' '))
const area=computed(()=>props.series.length?`${line.value} L718,176 L42,176 Z`:'')
const ticks=computed(()=>[0,Math.round((props.series.length-1)/2),props.series.length-1].filter((v,i,a)=>v>=0&&a.indexOf(v)===i))
const weekCells=computed(()=>{
  if(!props.series.length)return[]
  const padding=(new Date(props.series[0].date+'T12:00:00Z').getUTCDay()+6)%7
  return [...Array(padding).fill(null),...props.series]
})
function select(i){hover.value=i;emit('select-day',props.series[i])}
</script>
<template>
  <section class="panel trend-panel" :class="{compact}">
    <div class="panel-heading"><div><h2>{{title}}</h2><span class="subtle">{{series[0]?.date}} — {{series.at(-1)?.date}}</span></div><div class="segmented chart-controls" aria-label="趋势展示方式"><button v-for="item in [['line','曲线'],['bar','柱图'],['heat','日历'],['table','明细']]" :key="item[0]" :aria-pressed="mode===item[0]" :class="{active:mode===item[0]}" @click="mode=item[0]">{{item[1]}}</button></div></div>
    <div class="trend-summary"><strong>{{selected?count(selected.value):count(total)}}</strong><span>{{selected?selected.date+' · 当日对局':'场 · 所选趋势区间'}}<small>仅趋势随区间变化</small></span></div>
    <div v-if="!series.length || !total" class="inline-empty">这段时间暂无对局记录</div>
    <div v-else-if="mode==='line'||mode==='bar'" class="chart-wrap">
      <svg viewBox="0 0 744 212" role="img" :aria-label="`${title}，${series.length} 天，共 ${count(total)} 场`" class="trend-svg">
        <g class="chart-grid"><line v-for="i in [0,1,2]" :key="i" x1="42" x2="718" :y1="38+i*69" :y2="38+i*69"/><text v-for="i in [0,1,2]" :key="'t'+i" x="32" :y="42+i*69" text-anchor="end">{{count(Math.round(max*(1-i/2)))}}</text></g>
        <template v-if="mode==='line'"><path :d="area" class="chart-area"/><path :d="line" class="chart-line"/></template>
        <g v-else><rect v-for="(d,i) in series" :key="d.date" :x="42+i*676/series.length" :y="176-d.value/max*138" :width="Math.max(2,676/series.length-4)" :height="d.value/max*138" class="chart-bar" :class="{selected:hover===i}"/></g>
        <g v-if="hover>=0&&mode==='line'" class="chart-highlight"><line :x1="point(selected,hover).x" :x2="point(selected,hover).x" y1="30" y2="176"/><circle :cx="point(selected,hover).x" :cy="point(selected,hover).y" r="5"/></g>
        <text v-for="i in ticks" :key="'date'+i" :x="42+i/Math.max(1,series.length-1)*676" y="204" :text-anchor="i===0?'start':i===series.length-1?'end':'middle'" class="axis-date">{{series[i].label}}</text>
        <rect v-for="(d,i) in series" :key="'hit'+d.date" :x="Math.max(38,point(d,i).x-338/series.length)" y="20" :width="Math.max(12,676/series.length)" height="160" fill="transparent" tabindex="0" role="button" :aria-label="`${d.date}，${d.value} 场；选择此日`" @mouseenter="hover=i" @focus="hover=i" @click="select(i)" @keydown.enter="select(i)" @keydown.space.prevent="select(i)"/>
      </svg>
    </div>
    <div v-else-if="mode==='heat'" class="calendar-chart"><div class="calendar-weekdays"><span v-for="day in ['一','二','三','四','五','六','日']" :key="day">{{day}}</span></div><div class="calendar-days"><template v-for="(day,i) in weekCells" :key="i"><button v-if="day" :style="{'--intensity':day.value/max}" :class="{selected:selected?.date===day.date}" :aria-label="`${day.date}，${day.value} 场`" @mouseenter="hover=series.indexOf(day)" @focus="hover=series.indexOf(day)" @click="select(series.indexOf(day))"><span>{{day.label}}</span><strong>{{day.value}}</strong></button><span v-else/></template></div><div class="calendar-legend"><span>少</span><i v-for="opacity in [.08,.25,.5,.75,1]" :key="opacity" :style="{opacity}"/><span>多</span></div></div>
    <div v-else class="table-scroll trend-data-table"><table><thead><tr><th>统计日</th><th class="numeric">对局数</th><th>占区间比例</th></tr></thead><tbody><tr v-for="day in [...series].reverse()" :key="day.date"><td>{{day.date}}</td><td class="numeric">{{count(day.value)}}</td><td><span class="mini-bar"><i :style="{width:(day.value/total*100)+'%'}"/></span>{{(day.value/total*100).toFixed(1)}}%</td></tr></tbody></table></div>
    <p class="panel-footnote">统计日按北京时间 04:00 切分；无记录日期补零。点击图表可查看当日数值。</p>
  </section>
</template>
