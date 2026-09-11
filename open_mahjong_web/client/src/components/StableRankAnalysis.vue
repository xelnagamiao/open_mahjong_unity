<template>
  <section class="stable-rank" aria-label="安定段位分析">
    <div class="stable-heading">
      <div>
        <h3>安定段位分析</h3>
        <p>保持所选场次的顺位表现，推算各段位的长期平均 PT 收支。</p>
      </div>
      <span class="sample-count">有效 {{ analysis.sampleCount }} 局</span>
    </div>
    <p class="sample-note">
      当前筛选 {{ selectedCount }} 局，已读取 {{ sampleInfo.samples.length + sampleInfo.excludedCount }} 局牌谱。
      <span v-if="sampleInfo.excludedCount">排除 {{ sampleInfo.excludedCount }} 局：{{ exclusionText }}。</span>
      <span v-if="selectedCount > sampleInfo.samples.length + sampleInfo.excludedCount">尚未下载的牌谱不计入。</span>
    </p>
    <div v-if="!analysis.sampleCount" class="empty-state">
      暂无可用于安定段位的国标天梯对局。请筛选并下载初级、中级、高级或 MCRPL 的完整牌谱。
    </div>
    <template v-else>
      <div class="stable-controls">
        <label>分析场次
          <select v-model="selectedKey" aria-label="安定段位分析场次">
            <option v-if="analysis.groups.length > 1" value="overall">原场次配比（计分推演）</option>
            <option v-for="group in analysis.groups" :key="group.key" :value="group.key">{{ group.label }} · {{ group.sampleCount }} 局</option>
          </select>
        </label>
        <label>对照段位
          <select v-model="rankOverride" aria-label="安定段位对照段位">
            <option value="">{{ currentRank ? `当前段位 · ${currentRank}` : '当前段位未读取，请选择' }}</option>
            <option v-for="rank in RANK_NAMES" :key="rank" :value="rank">{{ rank }}</option>
          </select>
        </label>
      </div>
      <div v-if="selected" class="stable-body">
        <div class="summary-grid">
          <div class="summary-cell equilibrium">
            <span>{{ selectedKey === 'overall' ? '原场次配比推演' : '安定段位估计' }}</span>
            <strong>{{ selected.estimate.label }}</strong>
            <small>{{ estimateNote }}</small>
          </div>
          <div class="summary-cell">
            <span>{{ comparisonRank ? `${comparisonRank}预计每局收支` : '对照段位每局收支' }}</span>
            <strong :class="tone(selected.currentExpectation?.expectedPt)">{{ pt(selected.currentExpectation?.expectedPt) }} <small>PT / 局</small></strong>
            <small v-if="comparisonRank === TOP_RANK_NAME">PT 固定为 100 / 100，不再升降段。</small>
            <small v-else-if="selected.currentExpectation">{{ trend(selected.currentExpectation.expectedPt) }} · 95% 均值区间 {{ interval(selected.currentExpectation) }}</small>
            <small v-else>读取当前段位后显示，也可手动选择对照段位。</small>
          </div>
          <div class="summary-cell">
            <span>本组顺位分布 · {{ selected.sampleCount }} 局</span>
            <div class="rank-rates">
              <div v-for="(rate, index) in selected.rankRates" :key="index">
                <small>{{ index + 1 }} 位</small><b>{{ (rate * 100).toFixed(1) }}%</b>
              </div>
            </div>
            <small>采用实际结算名次；同分时 PT 均分所占名次。</small>
          </div>
        </div>
        <p v-if="selectedKey === 'overall'" class="model-note">各场次、局制分别按其实际 PT 规则重算，再按样本局数加权。此项假设以后仍维持原配比及各组表现；换场后的表现需要对应场次的数据。</p>
        <p v-if="selected.smallSample || selected.noLossSamples" class="sample-warning">
          <template v-if="selected.smallSample">本组不足 100 局，样本较少，结果容易波动。</template>
          <template v-if="selected.noLossSamples">尚未出现三、四位，无法估出有限的收支平衡段位。</template>
        </p>
        <p v-if="selected.admissionNotes.length" class="model-note">{{ selected.admissionNotes.join('；') }}</p>
        <p v-if="selected.currentExpectation?.admissionLimited" class="sample-warning">{{ selected.currentExpectation.admissionNotes.join('；') }}。此段位的收支仅作计分对照。</p>

        <div class="drift-chart" aria-label="各段位预计每局 PT 收支">
          <div class="chart-heading"><b>各段位预计每局收支</b><span>负值倾向降段 · 正值倾向升段</span></div>
          <div v-for="row in danRows" :key="row.rankName" class="drift-row" :class="{ current: row.rankName === comparisonRank }">
            <span>{{ row.rankName }}</span>
            <div class="bar-track"><i class="zero-line"></i><i class="drift-bar" :class="tone(row.expectedPt)" :style="barStyle(row.expectedPt)"></i></div>
            <b :class="tone(row.expectedPt)">{{ pt(row.expectedPt) }}</b>
          </div>
          <p class="sample-note">单位：PT / 局；九段满 7000 PT 升十段，十段 PT 固定为 100 / 100。正收益表示样本期望为正，不代表保证升段。</p>
        </div>

        <div v-if="analysis.groups.length > 1" class="table-scroll">
          <table class="comparison-table">
            <caption>分场次、分局制结果</caption>
            <thead><tr><th>场次 / 局制</th><th>样本</th><th>安定段位估计</th><th>{{ comparisonRank || '对照段位' }} PT / 局</th></tr></thead>
            <tbody><tr v-for="group in analysis.groups" :key="group.key" :class="{ selected: selectedKey === group.key }">
              <td><button type="button" @click="selectedKey = group.key">{{ group.label }}</button></td>
              <td>{{ group.sampleCount }} 局</td><td>{{ group.estimate.label }}</td>
              <td :class="tone(group.currentExpectation?.expectedPt)">{{ pt(group.currentExpectation?.expectedPt) }}</td>
            </tr></tbody>
          </table>
        </div>
        <details class="method-details">
          <summary>计分明细与估算方法</summary>
          <p>单一场次的每局期望约为：局制系数 × [场次基础分 × (0.8 × 一位率 + 0.2 × 二位率) − 目标段位均失 PT × (0.3 × 三位率 + 0.7 × 四位率)]。实际计算先将每局结算保留两位小数，再取平均。</p>
          <p>有并列时，公式中的顺位权重按占据名次均分，逐局重算并取整；上方顺位分布保留实际结算名次。例如两人并列二位，显示均为二位，PT 则各取第二、第三位 PT 的平均。</p>
          <p>初级 / 中级 / 高级 / MCRPL 基础分分别为 30 / 65 / 105 / 135；东风 / 半庄 / 全庄系数为 0.49 / 0.7 / 1。寻找期望收支为零的位置，相邻段位之间作线性插值；十段不再增减 PT、升降段，其固定零收益不参与安定零点估计。</p>
          <p>高段玩家的低场牌谱仍按原场次收益计算，扣分则逐一代入目标段位；不平均历史升降段 PT，也不把低场胜率换算成高场实力。中级场七段及以上不可进入，高级场通常四段起，特许资格另计；MCRPL 需要资格。</p>
          <p>95% 区间是按独立、表现不变假设得到的每局平均 PT 的正态近似区间，不是升段概率或最终段位的置信区间。它不涵盖换场、水平变化和筛选偏差。少量样本和顺位高度单一时尤其不稳定。</p>
          <div class="table-scroll">
            <table class="comparison-table">
              <thead><tr><th>目标段位</th><th>均失 PT</th><th>预计 PT / 局</th><th>95% 均值区间</th><th>场次限制</th></tr></thead>
              <tbody><tr v-for="row in selected.expectations" :key="row.rankName">
                <td>{{ row.rankName }}</td><td>{{ row.lossPt }}</td><td :class="tone(row.expectedPt)">{{ pt(row.expectedPt) }}</td>
                <td>{{ interval(row) }}</td><td>{{ row.admissionLimited ? row.admissionNotes.join('；') : '—' }}</td>
              </tr></tbody>
            </table>
          </div>
          <p>算法参考：<a href="https://github.com/SAPikachu/amae-koromo/blob/master/src/data/types/metadata.ts" target="_blank" rel="noopener noreferrer">雀魂牌谱屋开源实现</a>、<a href="https://tenhou.net/man/" target="_blank" rel="noopener noreferrer">天凤官方手册</a>、<a href="https://nodocchi.moe/tenhoulog/" target="_blank" rel="noopener noreferrer">天凤水表网</a>。这里使用本站国标计分表，估计的是收支平衡点，未模拟升降段重置后的长期段位分布。</p>
        </details>
      </div>
    </template>
  </section>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { RANK_NAMES, TOP_RANK_NAME } from '../constants/rankTable.js'
import { calculateStableRank } from '../utils/stableRank.js'

const props = defineProps({
  sampleInfo: { type: Object, required: true },
  currentRank: { type: String, default: '' },
  selectedCount: { type: Number, default: 0 },
})
const rankOverride = ref('')
const comparisonRank = computed(() => rankOverride.value || props.currentRank)
const analysis = computed(() => calculateStableRank(props.sampleInfo.samples, { currentRank: comparisonRank.value }))
const selectedKey = ref('overall')
watch(() => props.sampleInfo, () => {
  rankOverride.value = ''
  selectedKey.value = analysis.value.groups.length === 1 ? analysis.value.groups[0].key : 'overall'
}, { immediate: true })
const selected = computed(() => selectedKey.value === 'overall'
  ? analysis.value.overall
  : analysis.value.groups.find((group) => group.key === selectedKey.value))
const exclusionText = computed(() => props.sampleInfo.exclusions.map((item) => `${item.label} ${item.count} 局`).join('、'))
const danRows = computed(() => (selected.value?.expectations || []).filter((row) => row.dan >= 1))
const maxMagnitude = computed(() => Math.max(1, ...danRows.value.map((row) => Math.abs(row.expectedPt))))
const tone = (value) => value > 0.00001 ? 'positive' : value < -0.00001 ? 'negative' : ''
const pt = (value) => value == null || !Number.isFinite(value) ? '—' : `${value >= 0.005 ? '+' : ''}${Math.abs(value) < 0.005 ? '0.00' : value.toFixed(2)}`
const trend = (value) => value > 0.00001 ? '平均收益为正' : value < -0.00001 ? '平均收益为负' : '平均收支平衡'
const interval = (row) => row.confidenceLow == null || row.confidenceHigh == null ? '样本不足' : `${pt(row.confidenceLow)} ～ ${pt(row.confidenceHigh)}`
const barStyle = (value) => {
  const width = Math.abs(value) / maxMagnitude.value * 48
  return { left: `${value < 0 ? 50 - width : 50}%`, width: `${width}%` }
}
const estimateNote = computed(() => {
  const estimate = selected.value?.estimate
  if (estimate?.status === 'top') return '九段仍有非负期望；达到当前计分表上限。'
  if (estimate?.status === 'no_losses') return '缺少负位样本，不能给出有限安定段位。'
  if (estimate?.status === 'balanced') return '相邻段位间的收支平衡位置，非升段保证。'
  return '按本组样本的期望收支估算。'
})
</script>

<style scoped>
.stable-rank { margin: 0 0 22px; padding: 18px; border: 1px solid #dbe5ee; background: #fbfdff; color: #334155; }
.stable-heading { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }
h3 { margin: 0; color: #1e293b; font-size: 17px; }
p { font-size: 13px; line-height: 1.7; margin: 8px 0; }
.stable-heading p { color: #64748b; }
.sample-count { white-space: nowrap; background: #e8f2ff; color: #2563a7; padding: 5px 9px; font-size: 12px; }
.sample-note { color: #64748b; font-size: 12px; }
.empty-state { padding: 20px 0; font-size: 13px; line-height: 1.7; }
.stable-controls { display: flex; flex-wrap: wrap; gap: 16px; margin: 18px 0; }
.stable-controls label { display: flex; align-items: center; gap: 8px; font-size: 12px; }
select { padding: 6px 8px; color: #334155; border: 1px solid #cbd5e1; background: white; max-width: 100%; font: inherit; }
.summary-grid { display: grid; grid-template-columns: 1fr 1fr 1.2fr; gap: 1px; border: 1px solid #e2e8f0; background: #e2e8f0; }
.summary-cell { background: white; padding: 16px; display: flex; flex-direction: column; gap: 10px; min-width: 0; }
.summary-cell > span { color: #64748b; font-size: 12px; }
.summary-cell > strong { color: #1e293b; font-size: 23px; font-weight: 650; line-height: 1.4; }
.summary-cell small { font-size: 11px; color: #64748b; font-weight: normal; line-height: 1.6; }
.summary-cell.equilibrium { background: #f0f7ff; }
.rank-rates { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
.rank-rates div { display: flex; flex-direction: column; gap: 4px; }
.rank-rates b { font-size: 16px; }
.model-note { color: #58677b; }
.sample-warning { color: #8a5a12; background: #fff8e9; padding: 8px 10px; }
.drift-chart { margin: 20px 0; padding: 14px; background: white; border: 1px solid #e2e8f0; }
.chart-heading { display: flex; align-items: baseline; justify-content: space-between; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; font-size: 13px; }
.chart-heading span { font-size: 11px; color: #64748b; }
.drift-row { display: grid; grid-template-columns: 42px 1fr 66px; gap: 10px; align-items: center; min-height: 29px; font-size: 12px; }
.drift-row.current { background: #f1f7ff; font-weight: 700; }
.drift-row b { text-align: right; font-variant-numeric: tabular-nums; }
.bar-track { height: 20px; position: relative; }
.zero-line { position: absolute; left: 50%; top: -4px; bottom: -4px; border-left: 1px solid #cbd5e1; }
.drift-bar { position: absolute; top: 4px; height: 12px; background: #b8c4d1; }
.positive { color: #16805e !important; }.negative { color: #c74753 !important; }
.drift-bar.positive { background: #5fb699; }.drift-bar.negative { background: #e2868d; }
.table-scroll { width: 100%; overflow-x: auto; margin: 14px 0; }
.comparison-table { width: 100%; border-collapse: collapse; font-size: 12px; text-align: left; }
caption { text-align: left; font-size: 13px; font-weight: 600; padding-bottom: 10px; }
th { background: #f1f5f9; color: #64748b; font-weight: 500; }
th, td { padding: 10px; border-bottom: 1px solid #e2e8f0; white-space: nowrap; }
tr.selected { background: #edf5ff; }
button { border: none; padding: 0; background: none; color: #2563a7; cursor: pointer; font: inherit; text-decoration: underline; text-underline-offset: 3px; }
.method-details { margin-top: 18px; font-size: 12px; }.method-details summary { cursor: pointer; color: #2563a7; padding: 6px 0; }
.method-details p { color: #64748b; font-size: 12px; }a { color: #2563a7; }
@media (max-width: 760px) {
  .stable-rank { padding: 12px; }.summary-grid { grid-template-columns: 1fr; }
  .summary-cell { padding: 12px; }.stable-controls { flex-direction: column; gap: 10px; }
  .stable-controls label { flex-wrap: wrap; }.stable-heading { flex-wrap: wrap; }
  .drift-chart { padding: 10px; }.summary-cell > strong { font-size: 21px; }
}
</style>
