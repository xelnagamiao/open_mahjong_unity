<template>
  <div class="platform-data">
    <p v-if="meta.as_of_date" class="as-of-tip">
      数据截止至 <strong>{{ meta.as_of_date }}</strong> 统计日（北京时间 04:00 切日，每日 04:00 更新）
    </p>

    <section class="section-card">
      <h3 class="section-title">天梯场次历史总计</h3>
      <div v-loading="loading">
        <el-tabs v-model="totalsTierTab" class="totals-tabs">
          <el-tab-pane
            v-for="t in TIER_OPTIONS"
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
              <el-collapse-item :title="`番种出现次数（${fanDictSize}）`" name="fan">
                <div class="fan-grid">
                  <div
                    v-for="item in tierFanEntries"
                    :key="item.key"
                    class="fan-item"
                    :class="{ 'fan-item--zero': item.count === 0 }"
                  >
                    <span class="fan-name">{{ item.label }}</span>
                    <span class="fan-count">{{ item.count }}</span>
                  </div>
                </div>
              </el-collapse-item>
            </el-collapse>
          </el-tab-pane>
        </el-tabs>
      </div>
    </section>

    <section class="section-card">
      <div class="section-head">
        <h3 class="section-title">天梯场次每日对局数</h3>
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
          v-for="t in TIER_OPTIONS"
          :key="t.value"
          :label="t.label"
          :prop="t.value"
          width="100"
        />
      </el-table>
    </section>

    <section class="section-card">
      <div class="section-head">
        <h3 class="section-title">最近对局牌谱</h3>
        <span class="records-hint">{{ TIER_LABEL[totalsTierTab] }} · 共 {{ recentTotal }} 局</span>
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
        <el-table-column label="操作" width="72" fixed="right">
          <template #default="{ row }">
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
import { ElMessage } from 'element-plus'
import axios from 'axios'
import * as echarts from 'echarts'
import { GUOBIAO_FAN_DICT } from '@/constants/guobiaoFanDict'
import {
  buildPlatformStatsRows,
  buildAllFanEntries,
  buildSceneDailyChartOption,
  buildSceneDailyTable,
} from '@/utils/statsDisplay'

const TIER_OPTIONS = [
  { value: 'beginner', label: '初级场' },
  { value: 'intermediate', label: '中级场' },
  { value: 'advanced', label: '高级场' },
  { value: 'mcrpl', label: 'mcrpl' },
]
const TIER_LABEL = Object.fromEntries(TIER_OPTIONS.map((t) => [t.value, t.label]))
const MODE_LABELS = { '4/4': '全庄战', '3/4': '东西战', '2/4': '半庄战', '1/4': '东风战' }
const fanDictSize = Object.keys(GUOBIAO_FAN_DICT).length

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
const dateRange = ref(makeRange(30))
const recentRecords = ref([])
const recentTotal = ref(0)
const recordsPage = reactive({ current: 1, size: 20 })

const sceneChartRef = ref(null)
let sceneChart = null

const activeTierTotals = computed(() =>
  sceneTotals.value.find((r) => r.match_tier === totalsTierTab.value) || null
)

const tierFanEntries = computed(() =>
  buildAllFanEntries(sceneTierFans.value[totalsTierTab.value], GUOBIAO_FAN_DICT)
)

const dailyTable = computed(() =>
  buildSceneDailyTable(sceneDaily.value, TIER_OPTIONS, TIER_LABEL)
)

const gameTypeLabel = (matchType) => {
  if (!matchType) return '-'
  const base = String(matchType).replace(/_rank$/, '')
  return MODE_LABELS[base] || matchType
}

const sceneLabel = (row) => {
  if (row.match_tier) return TIER_LABEL[row.match_tier] || row.match_tier
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

const downloadOne = (gameId) => {
  const a = document.createElement('a')
  a.href = `/api/player/record/${encodeURIComponent(gameId)}`
  a.download = `${gameId}.json`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}

const renderSceneChart = () => {
  if (!sceneChartRef.value) return
  const opt = buildSceneDailyChartOption(sceneDaily.value, {
    tierOptions: TIER_OPTIONS,
    tierLabel: TIER_LABEL,
  })
  if (!sceneChart) sceneChart = echarts.init(sceneChartRef.value)
  sceneChart.setOption(opt, true)
}

const handleResize = () => sceneChart?.resize()

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
        match_tier: totalsTierTab.value,
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
    const res = await axios.get('/api/platform/stats', { params })
    const payload = res.data?.data || {}
    sceneTotals.value = payload.totals || []
    sceneTierFans.value = payload.fans || {}
    sceneDaily.value = payload.daily || []
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
watch(totalsTierTab, () => {
  recordsPage.current = 1
  loadRecentRecords()
})

onMounted(() => {
  loadStats()
  loadRecentRecords()
  window.addEventListener('resize', handleResize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleResize)
  sceneChart?.dispose()
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
.fan-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 6px 12px;
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
