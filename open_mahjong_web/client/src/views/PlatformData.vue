<template>
  <div class="platform-data">
    <p v-if="meta.as_of_date" class="as-of-tip">
      数据截止至 <strong>{{ meta.as_of_date }}</strong> 统计日（北京时间 04:00 切日，每日 04:00 更新）
    </p>

    <section class="section-card">
      <div class="totals-toolbar">
        <h3 class="section-title">{{ totalsTitle }}</h3>
        <div class="tier-bar">
          <el-radio-group
            class="tier-group"
            :model-value="selectedEventId ? '' : totalsTierTab"
            @change="onTierClick"
          >
            <el-radio-button
              v-for="t in TOTALS_TIER_OPTIONS"
              :key="t.value"
              :value="t.value"
            >{{ t.label }}</el-radio-button>
          </el-radio-group>
          <el-select
            v-model="selectedEventId"
            clearable
            filterable
            placeholder="阅览比赛"
            class="event-select"
            :class="{ 'is-active': !!selectedEventId }"
            @change="onEventSelect"
          >
            <el-option value="all" label="全部比赛" />
            <el-option
              v-for="ev in eventOptions"
              :key="ev.event_id"
              :label="eventOptionLabel(ev)"
              :value="ev.event_id"
            />
          </el-select>
        </div>
      </div>
      <div v-loading="loading">
            <div v-if="activeTierTotals" class="stats-grid">
              <div v-for="row in buildPlatformStatsRows(activeTierTotals)" :key="row.label" class="stats-cell">
                <span class="stats-label">{{ row.label }}</span>
                <span class="stats-value">{{ row.value }}</span>
              </div>
            </div>
            <p v-else class="empty-hint">{{ selectedEventId ? '暂无该比赛累计数据' : '暂无该场次累计数据' }}</p>
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
      </div>
    </section>

    <section class="section-card">
      <div class="section-head">
        <h3 class="section-title">{{ dailyTitle }}</h3>
        <div class="filter-row">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            range-separator="至"
            start-placeholder="开始"
            end-placeholder="结束"
            size="small"
            value-format="YYYY-MM-DD"
            class="filter-daterange"
            @change="loadStats"
          />
          <el-button size="small" @click="setQuickRange(7)">近7天</el-button>
          <el-button size="small" @click="setQuickRange(30)">近30天</el-button>
          <el-button size="small" @click="setQuickRange(90)">近90天</el-button>
        </div>
      </div>
      <div v-loading="loading" class="chart-wrap">
        <div ref="sceneChartRef" class="chart-box"></div>
      </div>
      <el-table :data="dailyTable" size="small" empty-text="暂无数据" max-height="360" class="detail-table">
        <el-table-column label="日期" prop="stat_date" width="120" />
        <el-table-column
          v-for="t in dailyColumnOptions"
          :key="t.value"
          :label="t.label"
          :prop="t.value"
          min-width="100"
        />
      </el-table>
    </section>

    <section class="section-card">
      <div class="section-head">
        <h3 class="section-title">最近对局牌谱</h3>
        <span class="records-hint">{{ recordsHint }} · 共 {{ recentTotal }} 局</span>
      </div>
      <el-table
        :data="recentRecords"
        size="small"
        class="records-table"
        v-loading="loadingRecords"
        empty-text="暂无对局记录"
      >
        <el-table-column label="牌谱 ID" min-width="140">
          <template #default="{ row }">
            <span class="cell-game-id" :title="row.game_id">{{ row.game_id }}</span>
          </template>
        </el-table-column>
        <el-table-column label="时间" width="150">
          <template #default="{ row }">
            <span class="cell-time">{{ formatRecordDate(row.created_at) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="场次" min-width="100">
          <template #default="{ row }">
            <span class="cell-scene">{{ sceneLabel(row) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="局制" width="80">
          <template #default="{ row }">
            <span class="cell-mode">{{ gameTypeLabel(row.match_type) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="同桌" min-width="200">
          <template #default="{ row }">
            <el-tooltip effect="dark" placement="top">
              <template #content>
                <div v-for="p in row.players" :key="p.user_id" class="tip-player">
                  <span class="rank-badge" :class="`rank-${p.rank}`">{{ p.rank }}</span>
                  {{ p.username }}
                  <span :class="scoreClass(p.score)">{{ formatScore(p.score) }}</span>
                </div>
              </template>
              <span class="cell-players">{{ playersSummary(row) }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="168" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="row.rule === 'guobiao'"
              link
              type="warning"
              size="small"
              tag="a"
              :href="`/2d/record/${encodeURIComponent(row.game_id)}`"
              target="_blank"
              rel="noopener noreferrer"
            >2D</el-button>
            <el-button
              link
              type="success"
              size="small"
              tag="a"
              :href="`/game-unity?recordId=${encodeURIComponent(row.game_id)}`"
              target="_blank"
              rel="noopener noreferrer"
            >3D</el-button>
            <el-button link type="primary" size="small" @click="downloadOne(row.game_id)">JSON</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="records-foot">
        <el-pagination
          v-model:current-page="recordsPage.current"
          v-model:page-size="recordsPage.size"
          :total="recentTotal"
          :page-sizes="[20, 50]"
          layout="prev, pager, next, sizes, total"
          small
          background
          @current-change="loadRecentRecords"
          @size-change="onRecordsSizeChange"
        />
      </div>
    </section>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import axios from 'axios'
import * as echarts from 'echarts'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { getPlayerToken } from '@/api/playerClient'
import { GUOBIAO_FAN_DICT, GUOBIAO_FAN_VALUES } from '@/constants/guobiaoFanDict'
import {
  buildPlatformStatsRows,
  buildAllFanEntries,
  buildSceneDailyChartOption,
  buildSceneDailyTable,
  sumSceneTotals,
  sumTierFans,
} from '@/utils/statsDisplay'

const route = useRoute()
const router = useRouter()
const auth = usePlayerAuthStore()

const TIER_OPTIONS = [
  { value: 'beginner', label: '初级场' },
  { value: 'intermediate', label: '中级场' },
  { value: 'advanced', label: '高级场' },
  { value: 'mcrpl', label: 'mcrpl' },
]
const TOTALS_TIER_OPTIONS = [...TIER_OPTIONS, { value: 'total', label: '总计' }]
const TIER_LABEL = Object.fromEntries(TOTALS_TIER_OPTIONS.map((t) => [t.value, t.label]))
const MODE_LABELS = { '4/4': '全庄战', '3/4': '东西战', '2/4': '半庄战', '1/4': '东风战' }
const fanDictSize = Object.keys(GUOBIAO_FAN_DICT).length
const showFanPercent = ref(false)
const fanTier = ref('all')
const fanView = ref('table')
const fanSort = ref('count')
const fanChartRef = ref(null)
let fanChart = null

/** 番种柱状图容器 */
const getFanChartEl = () => fanChartRef.value

const formatLocalDate = (d) => {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const makeRange = (days, endDateStr) => {
  const to = endDateStr ? new Date(`${endDateStr}T12:00:00`) : new Date()
  const from = new Date(to)
  from.setDate(from.getDate() - (days - 1))
  return [formatLocalDate(from), formatLocalDate(to)]
}

const loading = ref(false)
const loadingRecords = ref(false)
const meta = ref({})
const sceneTotals = ref([])
const sceneTierFans = ref({})
const sceneDaily = ref([])
const totalsTierTab = ref('beginner')
const selectedEventId = ref('')
const eventOptions = ref([])
const dateRange = ref(makeRange(30))
const recentRecords = ref([])
const recentTotal = ref(0)
const recordsPage = reactive({ current: 1, size: 20 })

const sceneChartRef = ref(null)
let sceneChart = null

const eventLabelMap = computed(() => {
  const m = { all: '全部比赛', total: '总计' }
  for (const ev of eventOptions.value) {
    m[ev.event_id] = ev.name
  }
  return m
})

const eventOptionLabel = (ev) => {
  const status = ev.status === 'closed' ? '（已关闭）' : ev.status === 'registered' ? '（已注册）' : ''
  const count = ev.game_count != null ? ` · ${ev.game_count}局` : ''
  return `${ev.name}${status}${count}`
}

const selectedEventName = computed(() => {
  if (!selectedEventId.value) return ''
  if (selectedEventId.value === 'all') return '全部比赛'
  return eventLabelMap.value[selectedEventId.value] || selectedEventId.value
})

const totalsTitle = computed(() =>
  selectedEventId.value ? `${selectedEventName.value} 历史总计` : '天梯场次历史总计'
)
const dailyTitle = computed(() =>
  selectedEventId.value ? `${selectedEventName.value} 每日对局数` : '天梯场次每日对局数'
)
const recordsHint = computed(() =>
  selectedEventId.value ? selectedEventName.value : (TIER_LABEL[totalsTierTab.value] || '天梯')
)

const dailyColumnOptions = computed(() => {
  if (!selectedEventId.value) return TIER_OPTIONS
  if (selectedEventId.value === 'all') {
    const fromDaily = [...new Set(sceneDaily.value.map((r) => r.match_tier).filter(Boolean))]
    const fromEvents = eventOptions.value.filter((ev) => ev.game_count > 0).map((ev) => ev.event_id)
    const ids = fromDaily.length ? fromDaily : fromEvents
    return ids.map((id) => ({ value: id, label: eventLabelMap.value[id] || id }))
  }
  return [{ value: selectedEventId.value, label: selectedEventName.value }]
})

const dailyColumnLabel = computed(() => {
  const m = { ...TIER_LABEL, ...eventLabelMap.value }
  for (const col of dailyColumnOptions.value) m[col.value] = col.label
  return m
})

const activeTierTotals = computed(() => {
  if (selectedEventId.value) {
    const row = sceneTotals.value.find((r) => r.match_tier === 'total')
    if (row) return row
    const rows = sceneTotals.value.filter((r) => r.match_tier && r.match_tier !== 'total')
    return rows.length ? sumSceneTotals(rows) : null
  }
  if (totalsTierTab.value === 'total') {
    const row = sceneTotals.value.find((r) => r.match_tier === 'total')
    if (row) return row
    const rows = sceneTotals.value.filter((r) => TIER_OPTIONS.some((t) => t.value === r.match_tier))
    return rows.length ? sumSceneTotals(rows) : null
  }
  return sceneTotals.value.find((r) => r.match_tier === totalsTierTab.value) || null
})

const tierFanEntries = computed(() => {
  const fans = selectedEventId.value
    ? (sceneTierFans.value.total
      || sumTierFans(sceneTierFans.value, Object.keys(sceneTierFans.value).filter((k) => k !== 'total')))
    : (totalsTierTab.value === 'total'
      ? (sceneTierFans.value.total
        || sumTierFans(sceneTierFans.value, TIER_OPTIONS.map((t) => t.value)))
      : sceneTierFans.value[totalsTierTab.value]);
  const entries = buildAllFanEntries(
    fans,
    GUOBIAO_FAN_DICT,
    activeTierTotals.value?.win_count,
    GUOBIAO_FAN_VALUES,
    fanSort.value,
  )
  const tier = fanTier.value
  if (tier === 'all') return entries
  return entries.filter((e) => {
    const v = Number(e.value) || 0
    if (tier === 'low') return v >= 1 && v <= 3
    if (tier === 'mid') return v >= 4 && v <= 24
    return v >= 25
  })
})

const dailyTable = computed(() =>
  buildSceneDailyTable(sceneDaily.value, dailyColumnOptions.value, dailyColumnLabel.value)
)

const gameTypeLabel = (matchType) => {
  if (!matchType) return '-'
  const base = String(matchType).replace(/_rank$/, '')
  return MODE_LABELS[base] || matchType
}

const sceneLabel = (row) => {
  if (row.room_type === 'events' || selectedEventId.value) {
    if (row.event_name) return row.event_name
    if (row.event_id) return eventLabelMap.value[row.event_id] || row.event_id
    return '比赛场'
  }
  if (row.match_tier) return TIER_LABEL[row.match_tier] || eventLabelMap.value[row.match_tier] || row.match_tier
  if (row.room_type === 'match') return '天梯'
  return row.room_type || row.rule || '-'
}

const formatRecordDate = (dateString) => {
  if (!dateString) return ''
  return new Date(dateString).toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const scoreClass = (s) => (s > 0 ? 'pos' : s < 0 ? 'neg' : '')
const formatScore = (s) => (s === undefined || s === null ? '-' : (s > 0 ? '+' : '') + s)
const playersSummary = (rec) =>
  (rec.players || []).map((p) => p.username || '?').join(' / ')

const downloadOne = async (gameId) => {
  if (!auth.isLoggedIn) {
    ElMessage.warning('请先登录后再下载牌谱')
    router.push({ path: '/login', query: { redirect: route.fullPath } })
    return
  }
  const token = getPlayerToken()
  try {
    const resp = await fetch(`/api/player/record/${encodeURIComponent(gameId)}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    if (resp.status === 401) {
      ElMessage.warning('请先登录后再下载牌谱')
      router.push({ path: '/login', query: { redirect: route.fullPath } })
      return
    }
    if (resp.status === 429) {
      try {
        const j = await resp.json()
        ElMessage.error(j.message || '今日下载局数已达上限')
      } catch (_) {
        ElMessage.error('今日下载局数已达上限')
      }
      return
    }
    if (!resp.ok) {
      ElMessage.error('下载失败')
      return
    }
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${gameId}.json`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  } catch (_) {
    ElMessage.error('下载失败')
  }
}

const renderSceneChart = () => {
  if (!sceneChartRef.value) return
  const opt = buildSceneDailyChartOption(sceneDaily.value, {
    tierOptions: dailyColumnOptions.value,
    tierLabel: dailyColumnLabel.value,
    dateFrom: dateRange.value?.[0],
    dateTo: dateRange.value?.[1],
  })
  if (!sceneChart) sceneChart = echarts.init(sceneChartRef.value)
  sceneChart.setOption(opt, true)
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
      data: entries.map((e) => e.label),
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
      data: entries.map((e) => ({
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
  sceneChart?.resize()
  fanChart?.resize()
}

const setQuickRange = (days) => {
  dateRange.value = makeRange(days, meta.value.as_of_date)
  loadStats()
}

const loadRecentRecords = async () => {
  loadingRecords.value = true
  try {
    const offset = (recordsPage.current - 1) * recordsPage.size
    const res = await axios.get('/api/platform/recent-records', {
      params: {
        ...(selectedEventId.value
          ? { event_id: selectedEventId.value }
          : (totalsTierTab.value === 'total' ? {} : { match_tier: totalsTierTab.value })),
        limit: recordsPage.size,
        offset,
      },
    })
    const data = res.data?.data || {}
    recentRecords.value = data.items || []
    recentTotal.value = data.total || 0
  } catch (_) {
    ElMessage.error('获取最近对局失败')
    recentRecords.value = []
    recentTotal.value = 0
  } finally {
    loadingRecords.value = false
  }
}

const onRecordsSizeChange = () => {
  recordsPage.current = 1
  loadRecentRecords()
}

const loadStats = async () => {
  loading.value = true
  try {
    const params = {}
    if (dateRange.value?.length === 2) {
      params.date_from = dateRange.value[0]
      params.date_to = dateRange.value[1]
    } else {
      params.days = 30
    }
    if (selectedEventId.value) params.event_id = selectedEventId.value
    const res = await axios.get('/api/platform/stats', { params })
    const payload = res.data?.data || {}
    sceneTotals.value = payload.totals || []
    sceneTierFans.value = payload.fans || {}
    sceneDaily.value = payload.daily || []
    eventOptions.value = payload.events || eventOptions.value
    meta.value = res.data?.meta || {}
    if (meta.value.as_of_date && dateRange.value?.[1] > meta.value.as_of_date) {
      dateRange.value = makeRange(30, meta.value.as_of_date)
    }
    await nextTick()
    renderSceneChart()
  } catch (e) {
    ElMessage.error('获取平台数据失败')
    sceneTotals.value = []
    sceneTierFans.value = {}
    sceneDaily.value = []
  } finally {
    loading.value = false
  }
}

watch(sceneDaily, () => nextTick(() => renderSceneChart()))
watch([tierFanEntries, fanView, showFanPercent], () => nextTick(() => renderFanChart()))
watch(fanView, (v) => {
  if (v === 'bar') nextTick(() => renderFanChart())
})
const onTierClick = (tier) => {
  totalsTierTab.value = tier
  selectedEventId.value = ''
  recordsPage.current = 1
  loadStats()
  loadRecentRecords()
}

const onEventSelect = () => {
  recordsPage.current = 1
  loadStats()
  loadRecentRecords()
}

onMounted(() => {
  loadStats()
  loadRecentRecords()
  window.addEventListener('resize', handleResize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleResize)
  sceneChart?.dispose()
  fanChart?.dispose()
})
</script>

<style scoped>
.platform-data { color: #1f2329; }
.as-of-tip {
  margin: 0 0 14px;
  font-size: 13px;
  color: #606266;
  padding: 10px 12px;
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 6px;
}
.section-card {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
}
.section-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
}
.section-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}
.totals-toolbar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
}
.tier-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  border-bottom: 1px solid #dcdfe6;
  padding-bottom: 8px;
}
.tier-group :deep(.el-radio-button__inner) {
  height: 32px;
  line-height: 30px;
  padding: 0 16px;
}
.event-select {
  width: 200px;
  flex-shrink: 0;
}
.event-select :deep(.el-select__wrapper),
.event-select :deep(.el-input__wrapper) {
  min-height: 32px;
  height: 32px;
  box-shadow: 0 0 0 1px #dcdfe6 inset;
}
.event-select.is-active :deep(.el-select__wrapper),
.event-select.is-active :deep(.el-input__wrapper) {
  background: #409eff;
  box-shadow: 0 0 0 1px #409eff inset;
}
.event-select.is-active :deep(.el-select__selected-item),
.event-select.is-active :deep(.el-select__placeholder),
.event-select.is-active :deep(.el-select__caret),
.event-select.is-active :deep(.el-input__inner),
.event-select.is-active :deep(.el-input__suffix) {
  color: #fff;
}
.filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.filter-daterange { width: 240px; }
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
  background: #f4f5f7;
  border-radius: 6px;
}
.stats-label { font-size: 12px; color: #909399; }
.stats-value { font-size: 15px; font-weight: 600; }
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
.chart-wrap { margin-bottom: 12px; }
.chart-box { width: 100%; height: 320px; min-width: 0; }
.detail-table { margin-top: 8px; }
.records-hint { font-size: 12px; color: #94a3b8; }
.records-table { width: 100%; }
:deep(.records-table .el-table__cell) { padding: 4px 0; }
:deep(.records-table th.el-table__cell) {
  background: #f5f7fa;
  color: #475569;
  font-weight: 600;
  border-bottom: 1px solid #dcdfe6;
}
:deep(.records-table td.el-table__cell) { border-color: #eef0f3; }
.cell-game-id { font-size: 12px; font-family: Consolas, Menlo, monospace; color: #303133; }
.cell-time { font-size: 12px; color: #64748b; font-family: Consolas, Menlo, monospace; }
.cell-scene { font-size: 12px; color: #303133; }
.cell-mode { font-size: 12px; color: #64748b; font-family: Consolas, Menlo, monospace; }
.cell-players { font-size: 12px; color: #64748b; cursor: help; }
.tip-player { font-size: 12px; line-height: 1.7; }
.tip-player .pos { color: #ff7a7a; font-weight: 700; }
.tip-player .neg { color: #6ee06e; font-weight: 700; }
.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  font-weight: 700;
  font-size: 11px;
  font-family: Consolas, Menlo, monospace;
}
.rank-1 { background: #6fd86f; color: #1f5e1f; }
.rank-2 { background: #5dadff; color: #fff; }
.rank-3 { background: #aab4c2; color: #2c3848; }
.rank-4 { background: #ff7a7a; color: #fff; }
.records-foot {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}
</style>
