<template>
  <section class="stable-rank" aria-label="安定段位分析">
    <div class="stable-heading">
      <h3>安定段位分析</h3>
      <div v-if="analysis.sampleCount" class="stable-controls">
        <label>分析场次
          <select v-model="selectedKey" aria-label="分析场次">
            <option v-for="group in analysis.groups" :key="group.key" :value="group.key">{{ group.label }} · {{ group.sampleCount }} 局</option>
          </select>
        </label>
        <label>对照段位
          <select v-model="rankOverride" aria-label="对照段位">
            <option value="">{{ currentRank ? `当前段位 · ${currentRank}` : '当前段位未读取，请选择' }}</option>
            <option v-for="rank in RANK_NAMES" :key="rank" :value="rank">{{ rank }}</option>
          </select>
        </label>
      </div>
    </div>
    <div v-if="!analysis.sampleCount" class="empty-state">
      暂无可用牌谱
      <p v-if="exclusionText">{{ exclusionText }}</p>
    </div>
    <template v-else-if="selected">
      <div class="summary-grid">
        <div class="summary-cell equilibrium">
          <span>安定段位</span>
          <strong>{{ selected.estimate.label }}<template v-if="selected.smallSample">?</template></strong>
        </div>
        <div class="summary-cell">
          <span>当前段位预计每局收支</span>
          <strong :class="tone(selected.currentExpectation?.expectedPt)">{{ pt(selected.currentExpectation?.expectedPt) }}</strong>
        </div>
        <div class="summary-cell">
          <span>筛选场次顺位分布</span>
          <div class="rank-rates">
            <div v-for="(rate, index) in selected.rankRates" :key="index">
              <small>{{ index + 1 }} 位</small><b>{{ (rate * 100).toFixed(1) }}%</b>
            </div>
          </div>
        </div>
      </div>

      <div class="drift-chart" aria-label="各段位预计每局 PT 收支">
        <div class="chart-heading"><b>各段位预计每局收支</b></div>
        <div v-for="row in danRows" :key="row.rankName" class="drift-row" :class="{ current: row.rankName === comparisonRank }">
          <span>{{ row.rankName }}</span>
          <div class="bar-track"><i class="zero-line"></i><i class="drift-bar" :class="tone(row.expectedPt)" :style="barStyle(row.expectedPt)"></i></div>
          <b :class="tone(row.expectedPt)">{{ pt(row.expectedPt) }}</b>
        </div>
      </div>

      <div v-if="formatRows.length > 1" class="table-scroll">
        <table class="comparison-table">
          <thead><tr><th>局制</th><th>样本</th><th>安定段位</th><th>{{ comparisonRank || '对照段位' }} PT / 局</th></tr></thead>
          <tbody><tr v-for="group in formatRows" :key="group.key" :class="{ selected: formatKey === group.key }">
            <td><button type="button" @click="toggleFormat(group.key)">{{ formatLabel(group) }}</button></td>
            <td>{{ group.sampleCount }}</td><td>{{ group.estimate.label }}<template v-if="group.smallSample">?</template></td>
            <td :class="tone(group.currentExpectation?.expectedPt)">{{ pt(group.currentExpectation?.expectedPt) }}</td>
          </tr></tbody>
        </table>
      </div>

      <div class="score-sheet">
        <div class="chart-heading">
          <b>计分明细</b>
          <span>{{ selected.label }} · {{ scoreRank }} · 均失 {{ scoreLoss }}</span>
        </div>
        <div class="table-scroll">
          <table class="comparison-table">
            <thead><tr><th>局制</th><th>一位</th><th>二位</th><th>三位</th><th>四位</th></tr></thead>
            <tbody>
              <tr v-for="row in scoreRows" :key="row.key" :class="{ selected: formatKey === row.key }">
                <td>{{ row.label }}</td>
                <td v-for="(value, index) in row.places" :key="index" :class="tone(value)">{{ pt(value) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <details class="method-details">
        <summary>PT 预测</summary>
        <div class="table-scroll">
          <table class="comparison-table">
            <thead><tr><th>目标段位</th><th>均失 PT</th><th>预计 PT / 局</th><th>95% 区间</th></tr></thead>
            <tbody><tr v-for="row in predictionRows" :key="row.rankName">
              <td>{{ row.rankName }}</td><td>{{ row.lossPt }}</td><td :class="tone(row.expectedPt)">{{ pt(row.expectedPt) }}</td>
              <td>{{ interval(row) }}</td>
            </tr></tbody>
          </table>
        </div>
      </details>
    </template>
  </section>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { RANK_NAMES } from '../constants/rankTable.js'
import { calculateStableRank, scoringRows } from '../utils/stableRank.js'

const props = defineProps({
  sampleInfo: { type: Object, required: true },
  currentRank: { type: String, default: '' },
})
const rankOverride = ref('')
const comparisonRank = computed(() => rankOverride.value || props.currentRank)
const analysis = computed(() => calculateStableRank(props.sampleInfo.samples, { currentRank: comparisonRank.value }))
const exclusionText = computed(() => (props.sampleInfo?.exclusions || [])
  .filter((row) => row?.count && row.label)
  .map((row) => `${row.label} ${row.count} 局`)
  .join('；'))
const selectedKey = ref('')
const formatKey = ref('')
watch(() => props.sampleInfo, () => {
  rankOverride.value = ''
  selectedKey.value = analysis.value.defaultKey
  formatKey.value = ''
}, { immediate: true })
watch(selectedKey, () => { formatKey.value = '' })
const selected = computed(() => analysis.value.formatGroups.find((group) => group.key === formatKey.value)
  || analysis.value.groups.find((group) => group.key === selectedKey.value)
  || analysis.value.overall)
const formatRows = computed(() => (analysis.value.formatGroups || []).filter((group) => group.tier === selectedKey.value))
const formatLabel = (group) => group.label.includes(' · ') ? group.label.split(' · ').pop() : group.label
const toggleFormat = (key) => { formatKey.value = formatKey.value === key ? '' : key }
const scoreRank = computed(() => {
  if (comparisonRank.value) return comparisonRank.value
  const first = (selected.value?.expectations || []).find((row) => Number.isFinite(row.expectedPt) && row.dan >= 1)
    || (selected.value?.expectations || []).find((row) => Number.isFinite(row.expectedPt))
  return first?.rankName || '初段'
})
const scoreRows = computed(() => {
  const rows = scoringRows(selected.value?.tier, scoreRank.value)
  if (!formatKey.value) return rows
  return rows.filter((row) => row.key === formatKey.value)
})
const scoreLoss = computed(() => {
  const row = selected.value?.expectations?.find((item) => item.rankName === scoreRank.value)
  return row?.lossPt ?? '—'
})
const danRows = computed(() => (selected.value?.expectations || [])
  .filter((row) => row.dan >= 1 && Number.isFinite(row.expectedPt)))
const predictionRows = computed(() => (selected.value?.expectations || [])
  .filter((row) => Number.isFinite(row.expectedPt)))
const maxMagnitude = computed(() => Math.max(1, ...danRows.value.map((row) => Math.abs(row.expectedPt))))
const tone = (value) => value > 0.00001 ? 'positive' : value < -0.00001 ? 'negative' : ''
const pt = (value) => value == null || !Number.isFinite(value) ? '—' : `${value >= 0.005 ? '+' : ''}${Math.abs(value) < 0.005 ? '0.00' : value.toFixed(2)}`
const interval = (row) => row.confidenceLow == null || row.confidenceHigh == null ? '—' : `${pt(row.confidenceLow)} ～ ${pt(row.confidenceHigh)}`
const barStyle = (value) => {
  const width = Math.abs(value) / maxMagnitude.value * 48
  return { left: `${value < 0 ? 50 - width : 50}%`, width: `${width}%` }
}
</script>

<style scoped>
.stable-rank { margin: 0 0 22px; padding: 18px; border: 1px solid #dbe5ee; background: #fbfdff; color: #334155; }
.stable-heading { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
h3 { margin: 0; color: #1e293b; font-size: 17px; }
.empty-state { padding: 20px 0; font-size: 13px; line-height: 1.7; }
.empty-state p { margin: 8px 0 0; color: #64748b; }
.stable-controls { display: flex; flex-wrap: wrap; gap: 16px; }
.stable-controls label { display: flex; align-items: center; gap: 8px; font-size: 12px; }
select { padding: 6px 8px; color: #334155; border: 1px solid #cbd5e1; background: white; max-width: 100%; font: inherit; }
.summary-grid { display: grid; grid-template-columns: 1fr 1fr 1.2fr; gap: 1px; border: 1px solid #e2e8f0; background: #e2e8f0; margin-top: 16px; }
.summary-cell { background: white; padding: 16px; display: flex; flex-direction: column; gap: 10px; min-width: 0; }
.summary-cell > span { color: #64748b; font-size: 12px; }
.summary-cell > strong { color: #1e293b; font-size: 23px; font-weight: 650; line-height: 1.4; }
.summary-cell.equilibrium { background: #f0f7ff; }
.rank-rates { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
.rank-rates div { display: flex; flex-direction: column; gap: 4px; }
.rank-rates b { font-size: 16px; }
.drift-chart, .score-sheet { margin: 20px 0; padding: 14px; background: white; border: 1px solid #e2e8f0; }
.chart-heading { margin-bottom: 12px; font-size: 13px; display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.chart-heading span { color: #64748b; font-size: 12px; }
.drift-row { display: grid; grid-template-columns: 42px 1fr 66px; gap: 10px; align-items: center; min-height: 29px; font-size: 12px; }
.drift-row.current { background: #f1f7ff; font-weight: 700; }
.drift-row b { text-align: right; font-variant-numeric: tabular-nums; }
.bar-track { height: 20px; position: relative; }
.zero-line { position: absolute; left: 50%; top: -4px; bottom: -4px; border-left: 1px solid #cbd5e1; }
.drift-bar { position: absolute; top: 4px; height: 12px; background: #b8c4d1; }
.positive { color: #16805e !important; }.negative { color: #c74753 !important; }
.drift-bar.positive { background: #5fb699; }.drift-bar.negative { background: #e2868d; }
.table-scroll { width: 100%; overflow-x: auto; margin: 14px 0 0; }
.score-sheet .table-scroll { margin: 0; }
.comparison-table { width: 100%; border-collapse: collapse; font-size: 12px; text-align: left; }
th { background: #f1f5f9; color: #64748b; font-weight: 500; }
th, td { padding: 10px; border-bottom: 1px solid #e2e8f0; white-space: nowrap; }
tr.selected { background: #edf5ff; }
button { border: none; padding: 0; background: none; color: #2563a7; cursor: pointer; font: inherit; text-decoration: underline; text-underline-offset: 3px; }
.method-details { margin-top: 18px; font-size: 12px; }.method-details summary { cursor: pointer; color: #2563a7; padding: 6px 0; }
@media (max-width: 760px) {
  .stable-rank { padding: 12px; }.summary-grid { grid-template-columns: 1fr; }
  .summary-cell { padding: 12px; }.stable-controls { flex-direction: column; gap: 10px; }
  .stable-controls label { flex-wrap: wrap; }
  .drift-chart, .score-sheet { padding: 10px; }.summary-cell > strong { font-size: 21px; }
}
</style>
