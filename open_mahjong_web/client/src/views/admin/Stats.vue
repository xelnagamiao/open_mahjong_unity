<template>
  <div>
    <h2 class="page-title">全站每日统计</h2>

    <div class="filter-bar">
      <el-date-picker
        v-model="dateRange"
        type="daterange"
        range-separator="至"
        start-placeholder="开始"
        end-placeholder="结束"
        size="small"
        value-format="YYYY-MM-DD"
        :shortcuts="dateShortcuts"
        class="filter-daterange filter-daterange--compact"
        @change="loadDailyStats"
      />
      <el-radio-group v-model="granularity" size="large" class="granularity-group" @change="loadDailyStats">
        <el-radio-button value="day">日</el-radio-button>
        <el-radio-button value="week">周</el-radio-button>
        <el-radio-button value="month">月</el-radio-button>
      </el-radio-group>
      <el-button size="small" @click="setQuickRange(7)">近7天</el-button>
      <el-button size="small" @click="setQuickRange(30)">近30天</el-button>
      <el-button size="small" @click="setQuickRange(90)">近90天</el-button>
      <span class="hint">统计日按北京时间 04:00 切日。</span>
    </div>

    <el-card shadow="never" class="block-card">
      <template #header>每日总览</template>
      <div v-loading="dailyLoading" class="charts-wrap">
        <div ref="lineChartRef" class="chart-box"></div>
        <div ref="barChartRef" class="chart-box"></div>
      </div>
      <el-table :data="dailyStatsDisplay" size="small" empty-text="暂无数据" max-height="320" class="detail-table">
        <el-table-column label="日期" prop="stat_date" width="130">
          <template #default="{ row }">
            <el-tooltip content="北京时间统计日（04:00 执行聚合）" placement="top">
              <span>{{ row.stat_date }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="对局数" prop="game_count" width="110" />
        <el-table-column label="日活" width="100">
          <template #default="{ row }">
            <el-tooltip content="注册用户当日成功登录的去重人数（不含游客）" placement="top">
              <span>{{ row.dau }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="活跃用户" width="120">
          <template #default="{ row }">
            <el-tooltip content="注册用户当日参与至少一局对局的去重人数（含游客对局）" placement="top">
              <span>{{ row.active_users }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="最大在线" width="120">
          <template #default="{ row }">
            {{ row.max_online > 0 ? row.max_online : '未采样' }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="block-card">
      <template #header>天梯场次历史总计（全站累计，口径同 PlayerData）</template>
      <div v-loading="totalsLoading">
        <el-tabs v-model="totalsTierTab" class="totals-tabs">
          <el-tab-pane
            v-for="t in TOTALS_TIER_OPTIONS"
            :key="t.value"
            :label="t.label"
            :name="t.value"
          >
            <div v-if="activeTierTotals" class="stats-grid">
              <div v-for="row in buildPlatformStatsRows(activeTierTotals)" :key="row.label" class="stats-cell">
                <span class="stats-label">{{ row.label }}</span>
                <span class="stats-value">{{ row.value }}</span>
              </div>
            </div>
            <p v-else class="empty-hint">暂无该场次累计数据</p>
            <el-collapse class="fan-collapse">
              <el-collapse-item name="fan">
                <template #title>
                  <span class="fan-collapse-title">番种出现次数（{{ fanDictSize }}）</span>
                </template>
                <div class="fan-toolbar">
                  <el-radio-group v-model="fanTier" size="small" class="fan-toolbar-group">
                    <el-radio-button value="all">全部</el-radio-button>
                    <el-radio-button value="low">1-3番</el-radio-button>
                    <el-radio-button value="mid">4-24番</el-radio-button>
                    <el-radio-button value="high">25番以上</el-radio-button>
                  </el-radio-group>
                  <el-radio-group v-model="fanView" size="small" class="fan-toolbar-group">
                    <el-radio-button value="table">表格</el-radio-button>
                    <el-radio-button value="bar">柱状图</el-radio-button>
                  </el-radio-group>
                  <el-radio-group v-model="fanSort" size="small" class="fan-toolbar-group">
                    <el-radio-button value="default">默认顺序</el-radio-button>
                    <el-radio-button value="count">从多到少</el-radio-button>
                  </el-radio-group>
                  <el-button
                    class="fan-percent-toggle"
                    size="small"
                    link
                    type="primary"
                    @click="showFanPercent = !showFanPercent"
                  >
                    {{ showFanPercent ? '隐藏百分比' : '显示百分比' }}
                  </el-button>
                </div>
                <div v-show="fanView === 'table'" class="fan-grid">
                  <div
                    v-for="item in tierFanEntries"
                    :key="item.key"
                    class="fan-item"
                    :class="{ 'fan-item--zero': item.count === 0 }"
                  >
                    <span class="fan-name">{{ item.label }}</span>
                    <span class="fan-count">
                      {{ item.count }}<span v-if="showFanPercent && item.percent !== undefined" class="fan-percent">（{{ item.percent }}）</span>
                    </span>
                  </div>
                </div>
                <div v-show="fanView === 'bar'" ref="fanChartRef" class="fan-chart"></div>
              </el-collapse-item>
            </el-collapse>
          </el-tab-pane>
        </el-tabs>
      </div>
    </el-card>

    <div class="filter-bar scene-filter">
      <el-select v-model="statsTier" placeholder="全部场次" size="small" clearable class="filter-tier" @change="loadSceneDaily">
        <el-option v-for="t in TIER_OPTIONS" :key="t.value" :label="t.label" :value="t.value" />
      </el-select>
      <el-date-picker
        v-model="sceneDateRange"
        type="daterange"
        range-separator="至"
        start-placeholder="开始"
        end-placeholder="结束"
        size="small"
        value-format="YYYY-MM-DD"
        class="filter-daterange filter-daterange--compact"
        @change="loadSceneDaily"
      />
      <el-button size="small" @click="setSceneQuickRange(7)">近7天</el-button>
      <el-button size="small" @click="setSceneQuickRange(30)">近30天</el-button>
      <el-button size="small" @click="setSceneQuickRange(90)">近90天</el-button>
    </div>

    <el-card shadow="never" class="block-card">
      <template #header>天梯场次每日对局数（初级 / 中级 / 高级 / mcrpl）</template>
      <div v-loading="sceneLoading" class="scene-chart-wrap">
        <div ref="sceneLineChartRef" class="chart-box chart-box--wide"></div>
      </div>
      <el-table :data="sceneDailyTable" size="small" empty-text="暂无数据" max-height="360" class="detail-table">
        <el-table-column label="日期" prop="stat_date" width="120" />
        <el-table-column
          v-for="t in visibleTierColumns"
          :key="t.value"
          :label="t.label"
          :prop="t.value"
          width="100"
        />
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import * as echarts from 'echarts'
import adminApi from '@/api/adminClient'
import { GUOBIAO_FAN_DICT, GUOBIAO_FAN_VALUES } from '@/constants/guobiaoFanDict'
import {
  buildPlatformStatsRows,
  buildAllFanEntries,
  buildSceneDailyChartOption,
  buildSceneDailyTable,
  sumSceneTotals,
  sumTierFans,
} from '@/utils/statsDisplay'

const TIER_OPTIONS = [
  { value: 'beginner', label: '初级场' },
  { value: 'intermediate', label: '中级场' },
  { value: 'advanced', label: '高级场' },
  { value: 'mcrpl', label: 'mcrpl' },
]
const TOTALS_TIER_OPTIONS = [...TIER_OPTIONS, { value: 'total', label: '总计' }]
const TIER_LABEL = Object.fromEntries(TOTALS_TIER_OPTIONS.map(t => [t.value, t.label]))
const fanDictSize = Object.keys(GUOBIAO_FAN_DICT).length
const showFanPercent = ref(false)
const fanTier = ref('all')
const fanView = ref('table')
const fanSort = ref('count')
const fanChartRef = ref(null)
let fanChart = null

/** 番种柱状图容器：同一模板在 5 个场次页签中各有一份，ref 是数组，取当前可见的那个 */
const getFanChartEl = () => {
  const v = fanChartRef.value
  if (!v) return null
  const list = Array.isArray(v) ? v : [v]
  return list.find((el) => el && el.clientHeight > 0) || list[0] || null
}

const formatLocalDate = (d) => {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const makeRange = (days) => {
  const to = new Date()
  const from = new Date()
  from.setDate(from.getDate() - (days - 1))
  return [formatLocalDate(from), formatLocalDate(to)]
}

const dateRange = ref(makeRange(30))
const sceneDateRange = ref(makeRange(30))
const granularity = ref('day')
const dailyStats = ref([])
const sceneDaily = ref([])
const sceneTotals = ref([])
const sceneTierFans = ref({})
const dailyLoading = ref(false)
const sceneLoading = ref(false)
const totalsLoading = ref(false)
const totalsTierTab = ref('beginner')
const statsTier = ref(null)

const lineChartRef = ref(null)
const barChartRef = ref(null)
const sceneLineChartRef = ref(null)
let lineChart = null
let barChart = null
let sceneLineChart = null

const dateShortcuts = [
  { text: '近7天', value: () => { const [a, b] = makeRange(7); return [a, b] } },
  { text: '近30天', value: () => { const [a, b] = makeRange(30); return [a, b] } },
  { text: '近90天', value: () => { const [a, b] = makeRange(90); return [a, b] } },
]

const dailyStatsDisplay = computed(() =>
  [...dailyStats.value].sort((a, b) => (a.stat_date < b.stat_date ? 1 : -1))
)

const activeTierTotals = computed(() => {
  if (totalsTierTab.value === 'total') {
    const row = sceneTotals.value.find(r => r.match_tier === 'total')
    if (row) return row
    const rows = sceneTotals.value.filter(r => TIER_OPTIONS.some(t => t.value === r.match_tier))
    return rows.length ? sumSceneTotals(rows) : null
  }
  return sceneTotals.value.find(r => r.match_tier === totalsTierTab.value) || null
})

const tierFanEntries = computed(() => {
  const fans = totalsTierTab.value === 'total'
    ? (sceneTierFans.value.total
      || sumTierFans(sceneTierFans.value, TIER_OPTIONS.map(t => t.value)))
    : sceneTierFans.value[totalsTierTab.value]
  const entries = buildAllFanEntries(
    fans,
    GUOBIAO_FAN_DICT,
    activeTierTotals.value?.win_count,
    GUOBIAO_FAN_VALUES,
    fanSort.value,
  )
  const tier = fanTier.value
  if (tier === 'all') return entries
  return entries.filter(e => {
    const v = Number(e.value) || 0
    if (tier === 'low') return v >= 1 && v <= 3
    if (tier === 'mid') return v >= 4 && v <= 24
    return v >= 25
  })
})

const visibleTierColumns = computed(() =>
  statsTier.value
    ? TIER_OPTIONS.filter(t => t.value === statsTier.value)
    : TIER_OPTIONS
)

const sceneDailyTable = computed(() =>
  buildSceneDailyTable(sceneDaily.value, visibleTierColumns.value, TIER_LABEL)
)

const setQuickRange = (days) => {
  dateRange.value = makeRange(days)
  loadDailyStats()
}

const setSceneQuickRange = (days) => {
  sceneDateRange.value = makeRange(days)
  loadSceneDaily()
}

const buildChartOptions = (rows) => {
  const sorted = [...rows].sort((a, b) => (a.stat_date > b.stat_date ? 1 : -1))
  const dates = sorted.map(r => r.stat_date)
  const games = sorted.map(r => Number(r.game_count) || 0)
  const dau = sorted.map(r => Number(r.dau) || 0)
  const users = sorted.map(r => Number(r.active_users) || 0)
  const online = sorted.map(r => Number(r.max_online) || 0)
  const chartBottom = dates.length > 14 ? 88 : 72

  const lineOpt = {
    tooltip: {
      trigger: 'axis',
      formatter(params) {
        const lines = [params[0]?.axisValue]
        params.forEach(p => {
          let val = p.value
          if (p.seriesName === '最大在线' && val === 0) val = '未采样'
          lines.push(`${p.marker}${p.seriesName}: ${val}`)
        })
        return lines.join('<br/>')
      }
    },
    legend: { data: ['对局数', '日活', '活跃用户', '最大在线'], bottom: 4, itemGap: 20, padding: [0, 0, 8, 0] },
    grid: { left: 48, right: 24, top: 28, bottom: chartBottom, containLabel: true },
    xAxis: {
      type: 'category',
      data: dates,
      axisLabel: { rotate: dates.length > 14 ? 35 : 0, margin: 16 },
      axisTick: { alignWithLabel: true },
    },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: '对局数', type: 'line', smooth: true, data: games, itemStyle: { color: '#409eff' } },
      { name: '日活', type: 'line', smooth: true, data: dau, itemStyle: { color: '#909399' } },
      { name: '活跃用户', type: 'line', smooth: true, data: users, itemStyle: { color: '#67c23a' } },
      { name: '最大在线', type: 'line', smooth: true, data: online, itemStyle: { color: '#e6a23c' } },
    ],
  }

  const barOpt = {
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    legend: { data: ['对局数', '日活', '活跃用户'], bottom: 4, itemGap: 20, padding: [0, 0, 8, 0] },
    grid: { left: 48, right: 24, top: 28, bottom: chartBottom, containLabel: true },
    xAxis: {
      type: 'category',
      data: dates,
      axisLabel: { rotate: dates.length > 14 ? 35 : 0, margin: 16 },
      axisTick: { alignWithLabel: true },
    },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: '对局数', type: 'bar', data: games, itemStyle: { color: '#409eff' } },
      { name: '日活', type: 'bar', data: dau, itemStyle: { color: '#909399' } },
      { name: '活跃用户', type: 'bar', data: users, itemStyle: { color: '#67c23a' } },
    ],
  }

  return { lineOpt, barOpt }
}

const renderCharts = () => {
  if (!lineChartRef.value || !barChartRef.value) return
  const { lineOpt, barOpt } = buildChartOptions(dailyStats.value)
  if (!lineChart) lineChart = echarts.init(lineChartRef.value)
  if (!barChart) barChart = echarts.init(barChartRef.value)
  lineChart.setOption(lineOpt, true)
  barChart.setOption(barOpt, true)
}

const renderSceneChart = () => {
  if (!sceneLineChartRef.value) return
  const opt = buildSceneDailyChartOption(sceneDaily.value, {
    tierOptions: visibleTierColumns.value,
    tierLabel: TIER_LABEL,
    selectedTier: statsTier.value,
  })
  if (!sceneLineChart) sceneLineChart = echarts.init(sceneLineChartRef.value)
  sceneLineChart.setOption(opt, true)
}

const renderFanChart = () => {
  if (fanView.value !== 'bar') return
  const el = getFanChartEl()
  if (!el) return
  const entries = tierFanEntries.value
  const opt = {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      formatter(params) {
        const p = Array.isArray(params) ? params[0] : params
        const e = entries[p?.dataIndex]
        if (!e) return ''
        const lines = [e.label, `达成次数：${e.count}`]
        if (showFanPercent.value && e.percent !== undefined) lines.push(`占比：${e.percent}`)
        return lines.join('<br/>')
      },
    },
    grid: { left: 56, right: 24, top: 28, bottom: 104, containLabel: true },
    xAxis: {
      type: 'category',
      data: entries.map(e => e.label),
      axisLabel: { rotate: 60, interval: 0, fontSize: 10, margin: 12, width: 72, overflow: 'truncate' },
      axisTick: { alignWithLabel: true },
    },
    yAxis: { type: 'value', minInterval: 1, name: '次数' },
    dataZoom: [{
      type: 'slider',
      height: 34,
      bottom: 6,
      borderColor: '#c0c4cc',
      backgroundColor: '#f5f7fa',
      fillerColor: 'rgba(64, 158, 255, 0.3)',
      handleStyle: { color: '#409eff', borderColor: '#409eff' },
      moveHandleStyle: { color: '#409eff' },
      textStyle: { color: '#606266' },
      showDetail: false,
    }],
    series: [{
      type: 'bar',
      data: entries.map(e => ({
        value: e.count,
        itemStyle: { color: e.value >= 25 ? '#e6a23c' : e.value >= 4 ? '#67c23a' : '#409eff' },
      })),
      barMaxWidth: 26,
    }],
  }
  if (!fanChart || fanChart.getDom() !== el) {
    fanChart?.dispose()
    fanChart = echarts.init(el)
  }
  fanChart.setOption(opt, true)
  fanChart.resize()
}

const handleResize = () => {
  lineChart?.resize()
  barChart?.resize()
  sceneLineChart?.resize()
  fanChart?.resize()
}

const loadDailyStats = async () => {
  dailyLoading.value = true
  try {
    const params = { granularity: granularity.value }
    if (dateRange.value?.length === 2) {
      params.date_from = dateRange.value[0]
      params.date_to = dateRange.value[1]
    } else {
      params.days = 30
    }
    const res = await adminApi.get('/stats/daily', { params })
    dailyStats.value = res.data.data || []
    await nextTick()
    renderCharts()
  } catch (e) {
    ElMessage.error('获取每日统计失败')
    dailyStats.value = []
  } finally {
    dailyLoading.value = false
  }
}

const loadSceneTotals = async () => {
  totalsLoading.value = true
  try {
    const [totalsRes, fansRes] = await Promise.all([
      adminApi.get('/stats/scene/totals'),
      adminApi.get('/stats/scene/totals/fans'),
    ])
    sceneTotals.value = totalsRes.data.data || []
    sceneTierFans.value = fansRes.data.data || {}
  } catch (e) {
    ElMessage.error('获取场次总计失败')
    sceneTotals.value = []
    sceneTierFans.value = {}
  } finally {
    totalsLoading.value = false
  }
}

const loadSceneDaily = async () => {
  sceneLoading.value = true
  try {
    const params = {}
    if (statsTier.value) params.tier = statsTier.value
    if (sceneDateRange.value?.length === 2) {
      params.date_from = sceneDateRange.value[0]
      params.date_to = sceneDateRange.value[1]
    }
    const res = await adminApi.get('/stats/scene/daily', { params })
    sceneDaily.value = res.data.data || []
    await nextTick()
    renderSceneChart()
  } catch (e) {
    ElMessage.error('获取场次每日统计失败')
    sceneDaily.value = []
  } finally {
    sceneLoading.value = false
  }
}

watch(dailyStats, () => nextTick(() => renderCharts()))
watch([sceneDaily, statsTier], () => nextTick(() => renderSceneChart()))
watch([tierFanEntries, fanView, showFanPercent], () => nextTick(() => renderFanChart()))
watch(fanView, (v) => {
  if (v === 'bar') nextTick(() => renderFanChart())
})

onMounted(() => {
  loadDailyStats()
  loadSceneTotals()
  loadSceneDaily()
  window.addEventListener('resize', handleResize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleResize)
  lineChart?.dispose()
  barChart?.dispose()
  sceneLineChart?.dispose()
  fanChart?.dispose()
})
</script>

<style scoped>
.page-title { margin: 0 0 16px; }
.filter-bar {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
  margin-bottom: 16px;
}
.scene-filter { margin-top: 4px; }
.filter-daterange--compact { width: 210px; }
.filter-daterange--compact :deep(.el-range-input) {
  font-size: 12px;
}
.granularity-group :deep(.el-radio-button__inner) {
  padding: 10px 18px;
  font-size: 15px;
  font-weight: 600;
}
.filter-tier { width: 140px; }
.hint { font-size: 12px; color: #909399; }
.block-card { margin-bottom: 16px; }
.charts-wrap {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 16px;
}
.scene-chart-wrap { margin-bottom: 12px; }
.chart-box {
  width: 100%;
  height: 320px;
  min-width: 0;
}
.chart-box--wide { height: 360px; }
.detail-table { margin-top: 8px; }
.totals-tabs { margin-top: 4px; }
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 10px 16px;
  margin-bottom: 12px;
}
.stats-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 10px;
  background: #f5f7fa;
  border-radius: 6px;
}
.stats-label { font-size: 12px; color: #909399; }
.stats-value { font-size: 15px; font-weight: 600; color: #303133; }
.empty-hint { font-size: 13px; color: #909399; margin: 8px 0; }
.fan-collapse { margin-top: 8px; }
.fan-collapse-title { flex: 1; min-width: 0; }
.fan-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 10px;
}
.fan-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 6px 12px;
}
.fan-chart {
  width: 100%;
  height: 360px;
  min-width: 0;
}
.fan-item {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  padding: 4px 0;
  border-bottom: 1px dashed #ebeef5;
}
.fan-item--zero .fan-name,
.fan-item--zero .fan-count { color: #c0c4cc; }
.fan-name { color: #606266; }
.fan-count { font-weight: 600; color: #409eff; }
.fan-percent { margin-left: 5px; font-size: 12px; font-weight: 500; color: #909399; }
.fan-item--zero .fan-percent { color: #c0c4cc; }
@media (max-width: 960px) {
  .charts-wrap { grid-template-columns: 1fr 1fr; }
}
@media (max-width: 560px) {
  .charts-wrap { grid-template-columns: 1fr 1fr; }
  .stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .fan-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
