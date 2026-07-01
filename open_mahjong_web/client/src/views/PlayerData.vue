<!-- 玩家数据统计：雀魂牌谱屋式紧凑表格风格（独立全屏布局） -->
<template>
  <div class="player-data">
    <!-- 顶部快捷：热点查询 + 排行榜前 10 -->
    <div class="quick-bar">
      <div class="quick-group">
        <span class="quick-title">热点查询</span>
        <template v-if="hotList.length">
          <button
            v-for="h in hotList"
            :key="`hot-${h.key}`"
            class="chip"
            @click="quickSearch(hotSearchKey(h))"
          >{{ hotDisplayLabel(h) }}<span class="chip-count">{{ h.count }}</span></button>
        </template>
        <span v-else class="quick-empty">暂无</span>
      </div>
      <div class="quick-group">
        <span class="quick-title">排行榜</span>
        <template v-if="leaderList.length">
          <button
            v-for="l in leaderList"
            :key="`lb-${l.user_id}`"
            class="chip"
            @click="quickSearch(String(l.user_id))"
          >
            <span class="chip-pos" :class="posClass(l.rank_position)">{{ l.rank_position }}</span>{{ l.username || l.user_id }}
            <span class="chip-rank">{{ l.guobiao_rank }}</span>
          </button>
        </template>
        <span v-else class="quick-empty">暂无</span>
      </div>
    </div>

    <!-- 搜索条 -->
    <div class="search-bar">
      <span class="search-label">玩家</span>
      <el-input
        v-model="searchForm.key"
        class="search-input"
        placeholder="输入 ID 或用户名"
        clearable
        @keyup.enter="searchPlayer"
      />
      <el-button type="primary" size="small" @click="searchPlayer" :loading="loading">查询</el-button>
      <el-button size="small" @click="resetForm">重置</el-button>
    </div>

    <div v-if="playerInfo" class="data-display">
      <!-- 用户信息条 -->
      <div class="user-bar">
        <span class="u-name">{{ playerInfo.user_settings?.username || '未知' }}</span>
        <span class="u-sep">·</span>
        <span class="u-id">ID {{ playerInfo.user_id }}</span>
      </div>

      <!-- 第一行：规则 -->
      <div class="filter-row">
        <button
          v-for="rule in availableRules"
          :key="rule.key"
          class="chip"
          :class="{ selected: currentRule === rule.key }"
          @click="switchRule(rule.key)"
        >{{ rule.label }}<span class="chip-count">{{ rule.count }}</span></button>
      </div>

      <!-- 第二行：范围 总局数 / 天梯 / 自定义 -->
      <div class="filter-row">
        <button
          v-for="s in SCOPE_OPTIONS"
          :key="s.value"
          class="chip"
          :class="{ selected: scope === s.value }"
          @click="selectScope(s.value)"
        >{{ s.label }}<span class="chip-count">{{ scopeCount(s.value) }}</span></button>
      </div>

      <!-- 第三行：局制 全庄/东西/半庄/东风 -->
      <div class="filter-row">
        <button
          class="chip"
          :class="{ selected: length === null }"
          @click="selectLength(null)"
        >全部局制</button>
        <button
          v-for="l in LENGTH_OPTIONS"
          :key="l.value"
          class="chip"
          :class="{ selected: length === l.value }"
          @click="selectLength(l.value)"
        >{{ l.label }}</button>
      </div>

      <!-- 第四行：场次等级 + 时间（自定义范围时自动亮起「自定义」） -->
      <div class="filter-row with-date">
        <div class="tier-group">
          <button
            v-for="t in TIER_ROW_OPTIONS"
            :key="t.value"
            class="chip"
            :class="{ selected: tier === t.value }"
            @click="selectTier(t.value)"
          >{{ t.label }}</button>
        </div>
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          size="small"
          range-separator="—"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          value-format="YYYY-MM-DD"
          class="filter-date"
          @change="onFilterChange"
        />
      </div>

      <!-- 数据统计 -->
      <div class="stats-area">
        <template v-if="activeStats">
          <div class="stats-table">
            <div class="stats-row" v-for="item in statsDisplay" :key="item.label">
              <span class="stats-label">{{ item.label }}</span>
              <span class="stats-value">{{ item.value }}</span>
            </div>
          </div>
          <div v-if="!prestoredAvailable" class="stats-source-tag">本地分析</div>
        </template>
        <template v-else>
          <div class="no-prestored">
            <span>没有预存数据</span>
            <el-button
              size="small"
              type="primary"
              :loading="analyzing"
              @click="runDailyAnalysis"
            >每日分析</el-button>
          </div>
        </template>
      </div>

      <!-- 图表面板：最近 20 局顺位折线图 + 1234 位比例饼图 -->
      <div class="charts-panel" v-if="activeStats">
        <div class="chart-box chart-line">
          <div class="chart-title">最近 20 局顺位</div>
          <svg viewBox="0 0 400 74" class="line-svg" v-if="recentRanks.length">
            <line x1="8" y1="10" x2="392" y2="10" class="grid-dash" />
            <line x1="8" y1="28" x2="392" y2="28" class="grid-dash" />
            <line x1="8" y1="46" x2="392" y2="46" class="grid-dash" />
            <line x1="8" y1="64" x2="392" y2="64" class="grid-dash" />
            <polyline :points="linePoints" class="line" />
            <circle
              v-for="(p, i) in linePointsArr" :key="i"
              :cx="p.x" :cy="p.y" r="2.2"
              fill="#fff"
              :stroke="rankDotColor(p.rk)"
              stroke-width="1.4"
              class="dot"
            />
          </svg>
          <div v-else class="chart-empty">暂无最近对局</div>
        </div>
        <div class="chart-box chart-pie">
          <div class="chart-title">顺位分布</div>
          <div class="pie-wrap">
            <svg viewBox="0 0 100 100" class="pie-svg">
              <circle cx="50" cy="50" r="40" fill="none" stroke="#eef0f3" stroke-width="18" />
              <circle
                v-for="seg in pieSegments" :key="seg.key"
                cx="50" cy="50" r="40" fill="none"
                :stroke="seg.color" stroke-width="18"
                :stroke-dasharray="seg.dash"
                :stroke-dashoffset="seg.offset"
                transform="rotate(-90 50 50)"
              />
              <text x="50" y="54" class="pie-center" text-anchor="middle">{{ pieTotal }}</text>
            </svg>
            <div class="pie-legend">
              <div v-for="seg in pieSegments" :key="seg.key" class="legend-item">
                <span class="legend-dot" :style="{ background: seg.color }"></span>
                <span class="legend-label">{{ seg.label }}</span>
                <span class="legend-value">{{ seg.value }}{{ seg.pct }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 番种统计 -->
      <el-collapse v-if="currentRuleDef?.fanField && fanEntries.length" class="fan-collapse">
        <el-collapse-item :title="`番种统计（${fanEntries.length}）`" name="fan">
          <div class="fan-grid">
            <div v-for="[fanKey, fanName] in fanEntries" :key="fanKey" class="fan-item">
              <span class="fan-name">{{ fanName }}</span>
              <span class="fan-value">{{ getFanValue(fanKey) }}</span>
            </div>
          </div>
        </el-collapse-item>
      </el-collapse>

      <!-- 对局记录 -->
      <div class="records-section">
        <div class="records-head">
          <span class="section-title">对局记录</span>
          <span class="records-total">共 {{ recordsTotal }} 局</span>
        </div>

        <!-- 表格 -->
        <el-table
          :data="gameRecords"
          size="small"
          class="records-table"
          @selection-change="onSelectionChange"
          empty-text="暂无对局记录"
        >
          <el-table-column type="selection" width="36" />
          <el-table-column label="时间" width="150">
            <template #default="{ row }">
              <span class="cell-time">{{ formatDate(row.created_at) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="场次" min-width="120">
            <template #default="{ row }">
              <span class="cell-scene">{{ sceneLabel(row) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="局制" width="80">
            <template #default="{ row }">
              <span class="cell-mode">{{ gameTypeLabel(row.match_type) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="顺位" width="64">
            <template #default="{ row }">
              <span class="rank-badge" :class="`rank-${myRank(row)}`">{{ myRank(row) || '-' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="得分" width="80">
            <template #default="{ row }">
              <span class="cell-score" :class="scoreClass(myScore(row))">
                {{ formatScore(myScore(row)) }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="同桌" min-width="180">
            <template #default="{ row }">
              <el-tooltip effect="dark" placement="top">
                <template #content>
                  <div v-for="p in row.players" :key="p.user_id" class="tip-player">
                    <span class="rank-badge" :class="`rank-${p.rank}`">{{ p.rank }}</span>
                    {{ p.username }}
                    <span :class="p.score > 0 ? 'pos' : (p.score < 0 ? 'neg' : '')">{{ p.score > 0 ? '+' : '' }}{{ p.score }}</span>
                  </div>
                </template>
                <span class="cell-players">{{ playersSummary(row) }}</span>
              </el-tooltip>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="86" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" size="small" @click="downloadOne(row.game_id)">JSON</el-button>
            </template>
          </el-table-column>
        </el-table>

        <!-- 分页 + 下载 -->
        <div class="records-foot">
          <el-pagination
            v-model:current-page="page.current"
            v-model:page-size="page.size"
            :total="recordsTotal"
            :page-sizes="[20, 50]"
            layout="prev, pager, next, sizes, total"
            small
            background
            @current-change="loadRecords"
            @size-change="onSizeChange"
          />
          <div class="download-actions">
            <span class="quota-tip">每 IP 每日限 10 次下载</span>
            <el-button
              size="small"
              :disabled="selectedIds.length === 0"
              @click="downloadSelected"
            >下载选中({{ selectedIds.length }})</el-button>
            <el-button
              size="small"
              type="primary"
              :loading="downloading"
              :disabled="recordsTotal === 0"
              @click="downloadFiltered"
            >下载筛选结果(ZIP)</el-button>
          </div>
        </div>
      </div>
    </div>

    <div v-else-if="searched && !playerInfo" class="no-data">
      <el-empty description="未找到该玩家的数据" />
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import axios from 'axios'
import { analyzeRecords } from '../utils/recordAnalyzer'

const RULE_DEFS = [
  { key: 'guobiao', label: '国标', statsField: 'guobiao_stats', fanField: 'guobiao' },
  { key: 'riichi', label: '立直', statsField: 'riichi_stats', fanField: null },
  { key: 'qingque', label: '青雀', statsField: 'qingque_stats', fanField: 'qingque' },
  { key: 'classical', label: '古典', statsField: 'classical_stats', fanField: 'classical' },
  { key: 'sichuan', label: '川麻', statsField: 'sichuan_stats', fanField: null }
]

const SCOPE_OPTIONS = [
  { value: 'all', label: '总局数' },
  { value: 'rank', label: '天梯' },
  { value: 'custom', label: '自定义' }
]

const LENGTH_OPTIONS = [
  { value: '4', label: '全庄战' },
  { value: '3', label: '东西战' },
  { value: '2', label: '半庄战' },
  { value: '1', label: '东风战' }
]

const TIER_ROW_OPTIONS = [
  { value: 'beginner', label: '初级' },
  { value: 'intermediate', label: '中级' },
  { value: 'advanced', label: '高级' },
  { value: 'mcrpl', label: 'mcrpl' },
  { value: 'events', label: '比赛场' }
]

const LENGTH_TO_GAME_TYPE = { '4': 'quanzhuang', '3': 'xifeng', '2': 'banzhuang', '1': 'dongfeng' }
const MODE_LABELS = { '4/4': '全庄战', '3/4': '东西战', '2/4': '半庄战', '1/4': '东风战' }

const gameTypeLabel = (matchType) => {
  if (!matchType) return '-'
  const base = matchType.replace(/_rank$/, '')
  return MODE_LABELS[base] || matchType
}

const sceneLabel = (row) => {
  if (row.room_type === 'custom') return '自定义'
  if (row.room_type === 'events') return '比赛场'
  if (row.room_type === 'match') {
    if (row.match_tier) {
      const t = TIER_ROW_OPTIONS.find(x => x.value === row.match_tier)
      return t ? t.label : row.match_tier
    }
    return '天梯'
  }
  return row.room_type || row.rule || '-'
}

const EMPTY_TOTAL = {
  total_games: 0, total_rounds: 0, win_count: 0, self_draw_count: 0,
  deal_in_count: 0, total_fan_score: 0, total_win_turn: 0,
  total_fangchong_score: 0, total_round_score: 0, cuohe_count: 0,
  first_place_count: 0, second_place_count: 0,
  third_place_count: 0, fourth_place_count: 0, fulu_round_count: 0, fan_stats: {}
}

const searchForm = reactive({ key: '' })
const playerInfo = ref(null)
const gameRecords = ref([])
const recentRecords = ref([])  // 玩家最近 20 局（独立于筛选，用于折线图）
const recordsTotal = ref(0)
const searched = ref(false)
const loading = ref(false)
const downloading = ref(false)
const analyzing = ref(false)

const currentRule = ref('guobiao')
const scope = ref('all')
const length = ref(null)
const tier = ref(null)
const dateRange = ref(null)

// 每日分析结果：仅当当前筛选与已分析筛选一致时采用
const analyzedResult = ref(null)

const page = reactive({ current: 1, size: 20 })
const selectedIds = ref([])

const hotList = ref([])
const leaderList = ref([])

const availableRules = computed(() =>
  RULE_DEFS.map(def => ({
    key: def.key,
    label: def.label,
    count: sumGames(playerInfo.value?.[def.statsField] || [])
  }))
)

const currentRuleDef = computed(() => RULE_DEFS.find(d => d.key === currentRule.value))

const currentStats = computed(() => {
  if (!playerInfo.value || !currentRuleDef.value) return []
  return playerInfo.value[currentRuleDef.value.statsField] || []
})

const sumGames = (stats) => (stats || []).reduce((s, x) => s + (x.total_games || 0), 0)

// 按范围统计对局数（用于第二行括号）
const scopeCount = (s) => {
  const stats = currentStats.value
  if (s === 'all') return sumGames(stats)
  if (s === 'rank') return sumGames(stats.filter(x => String(x.mode).endsWith('_rank')))
  if (s === 'custom') return sumGames(stats.filter(x => !String(x.mode).endsWith('_rank')))
  return 0
}

// 预存统计：按 scope + length 过滤 history_stats
const filteredPrestoredStats = computed(() => {
  let rows = currentStats.value
  if (scope.value === 'rank') rows = rows.filter(x => String(x.mode).endsWith('_rank'))
  else if (scope.value === 'custom') rows = rows.filter(x => !String(x.mode).endsWith('_rank'))
  if (length.value) {
    const base = `${length.value}/4`
    rows = rows.filter(x => String(x.mode).replace(/_rank$/, '') === base)
  }
  return rows
})

// 是否可用预存数据：tier 为空或为 custom（=自定义范围，已在预存内）
const prestoredAvailable = computed(() => tier.value === null)

const filterKey = computed(() => JSON.stringify({
  rule: currentRule.value, scope: scope.value, length: length.value,
  tier: tier.value, date: dateRange.value
}))

const analyzedStats = computed(() => {
  if (!analyzedResult.value) return null
  if (analyzedResult.value.filterKey !== filterKey.value) return null
  return analyzedResult.value.stats
})

const mergedPrestored = computed(() => {
  const total = { ...EMPTY_TOTAL, fan_stats: {} }
  for (const stat of filteredPrestoredStats.value) {
    total.total_games += stat.total_games || 0
    total.total_rounds += stat.total_rounds || 0
    total.win_count += stat.win_count || 0
    total.self_draw_count += stat.self_draw_count || 0
    total.deal_in_count += stat.deal_in_count || 0
    total.total_fan_score += stat.total_fan_score || 0
    total.total_win_turn += stat.total_win_turn || 0
    total.total_fangchong_score += stat.total_fangchong_score || 0
    total.total_round_score += stat.total_round_score || 0
    total.first_place_count += stat.first_place_count || 0
    total.second_place_count += stat.second_place_count || 0
    total.third_place_count += stat.third_place_count || 0
    total.fourth_place_count += stat.fourth_place_count || 0
    total.fulu_round_count += stat.fulu_round_count || 0
    total.cuohe_count += stat.cuohe_count || 0
    if (stat.fan_stats) {
      for (const [k, v] of Object.entries(stat.fan_stats)) {
        total.fan_stats[k] = (total.fan_stats[k] || 0) + v
      }
    }
  }
  return total
})

const activeStats = computed(() => {
  if (prestoredAvailable.value) return mergedPrestored.value
  return analyzedStats.value
})

const statsDisplay = computed(() => activeStats.value ? buildStatsRows(activeStats.value) : [])

// ===== 图表数据 =====
// 最近 20 局顺位：独立拉取玩家最近 20 局（不带筛选），按时间正序
const recentRanks = computed(() => {
  const rows = recentRecords.value || []
  return rows.map(r => myRank(r)).filter(r => r >= 1 && r <= 4).slice(0, 20).reverse()
})

const linePointsArr = computed(() => {
  const rs = recentRanks.value
  if (!rs.length) return []
  const x0 = 8, x1 = 392, y0 = 10, y1 = 64
  // 固定间隔：每局占一个等宽槽位（20 局铺满），不足 20 局时左对齐，间隔不变
  const step = (x1 - x0) / 19
  const pts = rs.map((rk, i) => {
    const x = x0 + i * step
    const y = y0 + ((rk - 1) / 3) * (y1 - y0)
    return { x: +x.toFixed(1), y: +y.toFixed(1), rk }
  })
  return pts
})

const linePoints = computed(() => linePointsArr.value.map(p => `${p.x},${p.y}`).join(' '))

// 排行榜名次配色：1 金 / 2 银 / 3 铜 / 4-5 蓝 / 6-10 灰
const posClass = (pos) => {
  if (pos === 1) return 'pos-1'
  if (pos === 2) return 'pos-2'
  if (pos === 3) return 'pos-3'
  if (pos <= 5) return 'pos-4'
  return 'pos-rest'
}

// 顺位配色：1 绿 / 2 蓝 / 3 灰 / 4 红
const RANK_COLORS = { 1: '#6fd86f', 2: '#5dadff', 3: '#aab4c2', 4: '#ff7a7a' }
const rankDotColor = (rk) => RANK_COLORS[rk] || '#94a3b8'

// 顺位分布饼图
const PIE_COLORS = RANK_COLORS
const PIE_LABELS = { 1: '一位', 2: '二位', 3: '三位', 4: '四位' }
const pieSegments = computed(() => {
  const s = activeStats.value
  if (!s) return []
  const counts = [
    { key: 1, value: s.first_place_count || 0 },
    { key: 2, value: s.second_place_count || 0 },
    { key: 3, value: s.third_place_count || 0 },
    { key: 4, value: s.fourth_place_count || 0 }
  ]
  const total = counts.reduce((a, c) => a + c.value, 0)
  const C = 2 * Math.PI * 40
  let acc = 0
  return counts.map(c => {
    const frac = total > 0 ? c.value / total : 0
    const len = frac * C
    const seg = {
      key: c.key,
      label: PIE_LABELS[c.key],
      value: c.value,
      color: PIE_COLORS[c.key],
      dash: `${len} ${C - len}`,
      offset: -acc,
      pct: total > 0 ? ` ${(frac * 100).toFixed(0)}%` : ''
    }
    acc += len
    return seg
  })
})
const pieTotal = computed(() => {
  const s = activeStats.value
  if (!s) return 0
  return (s.first_place_count || 0) + (s.second_place_count || 0) + (s.third_place_count || 0) + (s.fourth_place_count || 0)
})

const currentFanDict = computed(() => {
  if (!playerInfo.value || !currentRuleDef.value?.fanField) return null
  return playerInfo.value.fan_dict?.[currentRuleDef.value.fanField] || null
})

const fanEntries = computed(() => {
  const d = currentFanDict.value
  if (!d || typeof d !== 'object') return []
  return Object.entries(d)
})

const getFanValue = (fanKey) => activeStats.value?.fan_stats?.[fanKey] || 0

const ratio = (n, d, suffix = '%') => (!d || d <= 0 ? '0.00' + suffix : ((n / d) * 100).toFixed(2) + suffix)
const avg = (n, d) => (d === undefined || !d || d <= 0 ? '0.00' : (n / d).toFixed(2))

const avgRank = (s) => {
  const games = s.total_games || 0
  if (!games) return '0.00'
  const weighted = (s.first_place_count || 0) * 1
    + (s.second_place_count || 0) * 2
    + (s.third_place_count || 0) * 3
    + (s.fourth_place_count || 0) * 4
  return (weighted / games).toFixed(2)
}

const buildStatsRows = (s) => [
  { label: '总对局', value: String(s.total_games || 0) },
  { label: '总回合', value: String(s.total_rounds || 0) },
  { label: '平均顺位', value: avgRank(s) },
  { label: '局均点', value: avg(s.total_round_score, s.total_games) },
  { label: '一位率', value: ratio(s.first_place_count, s.total_games) },
  { label: '二位率', value: ratio(s.second_place_count, s.total_games) },
  { label: '三位率', value: ratio(s.third_place_count, s.total_games) },
  { label: '四位率', value: ratio(s.fourth_place_count, s.total_games) },
  { label: '和牌率', value: ratio(s.win_count, s.total_rounds) },
  { label: '自摸率', value: ratio(s.self_draw_count, s.win_count) },
  { label: '放铳率', value: ratio(s.deal_in_count, s.total_rounds) },
  { label: '错和率', value: ratio(s.cuohe_count, s.total_rounds) },
  { label: '副露率', value: ratio(s.fulu_round_count, s.total_rounds) },
  { label: '平均和番', value: avg(s.total_fan_score, s.win_count) },
  { label: '平均和巡', value: avg(s.total_win_turn, s.win_count) },
  { label: '平均铳番', value: avg(s.total_fangchong_score, s.deal_in_count) }
]

const switchRule = (rule) => {
  currentRule.value = rule
  analyzedResult.value = null
  onFilterChange()
}

const selectScope = (s) => {
  scope.value = s
  // 范围切换时清掉场次等级（自定义范围本身即自定义房间，场次行不再含「自定义」选项）
  tier.value = null
  analyzedResult.value = null
  onFilterChange()
}

const selectLength = (l) => {
  length.value = l
  analyzedResult.value = null
  onFilterChange()
}

const selectTier = (t) => {
  if (t === 'events') {
    scope.value = 'all'
    tier.value = 'events'
  } else {
    // 初级/中级/高级/mcrpl 均属天梯
    scope.value = 'rank'
    tier.value = t
  }
  analyzedResult.value = null
  onFilterChange()
}

// ===== 记录列表筛选 =====
const filterPayload = () => {
  const payload = {}
  payload.rule = currentRule.value
  if (length.value) payload.game_type = LENGTH_TO_GAME_TYPE[length.value]
  if (dateRange.value && dateRange.value.length === 2) {
    payload.date_from = dateRange.value[0] + 'T00:00:00'
    const end = new Date(dateRange.value[1])
    end.setDate(end.getDate() + 1)
    payload.date_to = end.toISOString().slice(0, 19)
  }
  // tier 优先；范围兜底
  if (tier.value) payload.tier = tier.value
  else if (scope.value === 'custom') payload.tier = 'custom'
  else if (scope.value === 'rank') payload.tier = 'rank'
  return payload
}

const loadRecords = async () => {
  const userId = playerInfo.value?.user_id
  if (!userId) return
  const params = {
    limit: page.size,
    offset: (page.current - 1) * page.size,
    ...filterPayload()
  }
  try {
    const resp = await axios.get(`/api/player/records/${userId}`, { params })
    if (resp.data.success) {
      const d = resp.data.data
      gameRecords.value = d.items || []
      recordsTotal.value = d.total || 0
    } else {
      ElMessage.warning(resp.data.message || '获取对局记录失败')
      gameRecords.value = []
      recordsTotal.value = 0
    }
  } catch (e) {
    handleAxiosError(e, '获取对局记录失败')
    gameRecords.value = []
    recordsTotal.value = 0
  }
}

// 拉取玩家最近 20 局（不带任何筛选），用于折线图
const loadRecentRanks = async () => {
  const userId = playerInfo.value?.user_id
  if (!userId) return
  try {
    const resp = await axios.get(`/api/player/records/${userId}`, { params: { limit: 20, offset: 0 } })
    if (resp.data.success) {
      recentRecords.value = resp.data.data?.items || []
    } else {
      recentRecords.value = []
    }
  } catch (e) {
    // 折线图失败不阻塞主流程，仅在控制台留痕
    recentRecords.value = []
  }
}

// 统一错误提示：429 显式提示「过于频繁」，避免误报网络问题
const handleAxiosError = (e, fallback) => {
  const status = e?.response?.status
  if (status === 429) {
    ElMessage.error('搜索/请求过于频繁，请稍后再试')
  } else if (status === 404) {
    ElMessage.error('未找到该玩家')
  } else {
    ElMessage.error(fallback || '请求失败')
  }
}

const onFilterChange = () => {
  page.current = 1
  selectedIds.value = []
  if (playerInfo.value) loadRecords()
}

const onSizeChange = (size) => {
  page.size = size
  page.current = 1
  loadRecords()
}

const onSelectionChange = (rows) => {
  selectedIds.value = rows.map(r => r.game_id)
}

// ===== 行内展示 =====
const myPlayer = (rec) => {
  const uid = playerInfo.value?.user_id
  return rec.players?.find(p => Number(p.user_id) === Number(uid))
}
const myRank = (rec) => myPlayer(rec)?.rank
const myScore = (rec) => myPlayer(rec)?.score
const scoreClass = (s) => (s > 0 ? 'pos' : s < 0 ? 'neg' : '')
const formatScore = (s) => (s === undefined || s === null ? '-' : (s > 0 ? '+' : '') + s)
const playersSummary = (rec) =>
  (rec.players || []).map(p => p.username || '?').join(' / ')

const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN', {
    month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit'
  })
}

// ===== 每日分析：客户端下载并本地分析未预存数据 =====
const runDailyAnalysis = async () => {
  const userId = playerInfo.value?.user_id
  if (!userId) return
  try {
    await ElMessageBox.confirm(
      '将下载当前筛选下的牌谱并在本地分析，请确保已保存好需要的数据。每次分析机会每日 4 点刷新，单次最多 500 局。',
      '每日分析',
      { confirmButtonText: '开始分析', cancelButtonText: '取消', type: 'warning' }
    )
  } catch (_) {
    return
  }

  // 先查总数判断是否超 500
  try {
    const countResp = await axios.get(`/api/player/records/${userId}`, {
      params: { limit: 1, offset: 0, ...filterPayload() }
    })
    const total = countResp.data.success ? (countResp.data.data?.total || 0) : 0
    if (total > 500) {
      ElMessage.warning('计算数据超出 500 局，请联系网站管理员')
      return
    }
    if (total === 0) {
      ElMessage.info('当前筛选下没有对局数据')
      return
    }
  } catch (e) {
    handleAxiosError(e, '获取对局数量失败')
    return
  }

  analyzing.value = true
  try {
    const resp = await fetch('/api/player/records/batch-json', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user_id: userId, ...filterPayload() })
    })
    if (resp.status === 429) {
      ElMessage.error('今日分析次数已达上限，每日 4 点刷新')
      analyzing.value = false
      return
    }
    if (!resp.ok) {
      ElMessage.error('拉取牌谱失败')
      analyzing.value = false
      return
    }
    const json = await resp.json()
    const items = json.data?.items || []
    if (!items.length) {
      ElMessage.info('没有可分析的牌谱')
      analyzing.value = false
      return
    }
    const records = items.map(x => x.record)
    const stats = analyzeRecords(records, userId)
    analyzedResult.value = { filterKey: filterKey.value, stats }
    ElMessage.success(`已本地分析 ${items.length} 局牌谱`)
  } catch (e) {
    ElMessage.error('本地分析失败')
  } finally {
    analyzing.value = false
  }
}

// ===== 下载 =====
const triggerBlob = (blob, filename) => {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

const downloadOne = (gameId) => {
  const a = document.createElement('a')
  a.href = `/api/player/record/${gameId}`
  a.download = `${gameId}.json`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}

const handleDownloadResponse = async (resp) => {
  if (resp.status === 429) {
    ElMessage.error('今日下载次数已达上限，请明天再试')
    return false
  }
  if (resp.status === 400) {
    try { const j = await resp.json(); ElMessage.error(j.message || '下载失败') } catch (_) { ElMessage.error('下载失败') }
    return false
  }
  if (resp.status === 404) {
    ElMessage.warning('没有匹配的牌谱')
    return false
  }
  if (!resp.ok) {
    ElMessage.error('下载失败')
    return false
  }
  const blob = await resp.blob()
  const cd = resp.headers.get('content-disposition') || ''
  const m = /filename="?([^";]+)"?/.exec(cd)
  triggerBlob(blob, m ? m[1] : 'records.zip')
  return true
}

const downloadSelected = async () => {
  if (selectedIds.value.length === 0) return
  downloading.value = true
  try {
    const resp = await fetch('/api/player/records/download', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user_id: playerInfo.value.user_id, game_ids: selectedIds.value })
    })
    await handleDownloadResponse(resp)
  } catch (e) {
    ElMessage.error('下载失败')
  } finally {
    downloading.value = false
  }
}

const downloadFiltered = async () => {
  downloading.value = true
  try {
    const resp = await fetch('/api/player/records/download', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user_id: playerInfo.value.user_id, ...filterPayload() })
    })
    await handleDownloadResponse(resp)
  } catch (e) {
    ElMessage.error('下载失败')
  } finally {
    downloading.value = false
  }
}

// ===== 查询入口（ID 或用户名） =====
const logSearch = async (key, info) => {
  try {
    await axios.post('/api/player/search-log', {
      key,
      user_id: info?.user_id || null,
      username: info?.user_settings?.username || null
    })
  } catch (_) { /* 静默 */ }
}

const searchPlayer = async (rawKey, isManual = true) => {
  // rawKey 为字符串时（quickSearch）直接用；为 Event/undefined 时回退输入框
  const raw = String((typeof rawKey === 'string' && rawKey) ? rawKey : (searchForm.key ?? '')).trim()
  if (!raw) {
    ElMessage.error('请输入玩家 ID 或用户名')
    return
  }
  searchForm.key = raw
  loading.value = true
  searched.value = true
  try {
    const infoResp = await axios.get(`/api/player/info/${encodeURIComponent(raw)}`)
    if (infoResp.data.success) {
      playerInfo.value = infoResp.data.data
      const defaultRule = RULE_DEFS.find(d => (playerInfo.value[d.statsField] || []).length > 0)
      currentRule.value = defaultRule ? defaultRule.key : 'guobiao'
      scope.value = 'all'
      length.value = null
      tier.value = null
      dateRange.value = null
      analyzedResult.value = null
      page.current = 1
      page.size = 20
      selectedIds.value = []
      await loadRecords()
      loadRecentRanks()
      // 仅手动输入并点查询才计入热点；点击热点/排行榜 chip 不计
      if (isManual) logSearch(raw, playerInfo.value)
      loadQuickLists()
    } else {
      ElMessage.error(infoResp.data.message || '获取玩家信息失败')
      playerInfo.value = null
      gameRecords.value = []
      recentRecords.value = []
      recordsTotal.value = 0
    }
  } catch (error) {
    handleAxiosError(error, '查询失败，请检查网络连接')
    playerInfo.value = null
    gameRecords.value = []
    recentRecords.value = []
    recordsTotal.value = 0
  } finally {
    loading.value = false
  }
}

const quickSearch = (key) => {
  searchForm.key = key
  searchPlayer(key, false)
}

// 热点查询：展示用户名，点击仍用 user_id 优先
const hotDisplayLabel = (h) => h.username || h.key
const hotSearchKey = (h) => (h.user_id != null ? String(h.user_id) : (h.username || h.key))

const resetForm = () => {
  searchForm.key = ''
  playerInfo.value = null
  gameRecords.value = []
  recentRecords.value = []
  recordsTotal.value = 0
  searched.value = false
  currentRule.value = 'guobiao'
  scope.value = 'all'
  length.value = null
  tier.value = null
  dateRange.value = null
  analyzedResult.value = null
  selectedIds.value = []
}

// ===== 顶部快捷列表 =====
const loadQuickLists = async () => {
  try {
    const [hot, lb] = await Promise.all([
      axios.get('/api/player/hot', { params: { limit: 10 } }),
      axios.get('/api/player/leaderboard', { params: { limit: 10 } })
    ])
    hotList.value = hot.data.success ? (hot.data.data || []) : []
    leaderList.value = lb.data.success ? (lb.data.data || []) : []
  } catch (_) { /* 静默 */ }
}

onMounted(() => {
  loadQuickLists()
})
</script>

<style scoped>
.player-data {
  color: #1f2329;
  text-align: left;
}

/* 搜索条 */
.search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  background: #ffffff;
  border: 1px solid #dcdfe6;
  padding: 10px 12px;
  margin-bottom: 12px;
}
.search-label { font-size: 13px; font-weight: 600; color: #475569; flex-shrink: 0; }
.search-input { width: 220px; max-width: 100%; }

/* 顶部快捷 */
.quick-bar {
  display: flex;
  flex-direction: column;
  gap: 6px;
  background: #ffffff;
  border: 1px solid #dcdfe6;
  padding: 8px 12px;
  margin-bottom: 12px;
}
.quick-group { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.quick-title { font-size: 12px; font-weight: 700; color: #64748b; flex-shrink: 0; width: 56px; }
.quick-empty { font-size: 12px; color: #cbd5e1; }

/* 通用 chip */
.chip {
  appearance: none;
  border: 1px solid #dcdfe6;
  background: #fff;
  color: #475569;
  font-size: 13px;
  padding: 4px 10px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  line-height: 1.4;
}
.chip:hover { border-color: #409eff; color: #409eff; }
.chip.selected {
  background: #409eff;
  border-color: #409eff;
  color: #fff;
  font-weight: 700;
}
.chip.selected .chip-count,
.chip.selected .chip-rank { color: #fff; }
.chip-count {
  font-size: 11px;
  color: #94a3b8;
  font-family: 'Consolas', 'Menlo', monospace;
}
.chip-pos {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  font-size: 11px;
  font-weight: 700;
  color: #fff;
}
.chip-pos.pos-1 { background: #ffc832; color: #5a4500; }
.chip-pos.pos-2 { background: #c9d4e0; color: #2c3848; }
.chip-pos.pos-3 { background: #e6a173; color: #4a2f1a; }
.chip-pos.pos-4 { background: #5dadff; color: #fff; }
.chip-pos.pos-rest { background: #aab4c2; color: #fff; }
.chip-rank { font-size: 11px; color: #94a3b8; }

.data-display {
  background: #ffffff;
  border: 1px solid #dcdfe6;
  padding: 14px 16px;
  margin-bottom: 16px;
}

/* 用户信息条 */
.user-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  border-bottom: 1px solid #eef0f3;
  padding-bottom: 10px;
  margin-bottom: 10px;
}
.u-name { font-weight: 700; color: #1e293b; }
.u-sep { color: #cbd5e1; }
.u-id { font-family: 'Consolas', 'Menlo', monospace; color: #64748b; font-size: 13px; }

/* 筛选行 */
.filter-row {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}
.filter-row.with-date {
  justify-content: space-between;
}
.tier-group { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.filter-date { width: 240px !important; }

/* 统计区 */
.stats-area { margin: 4px 0 10px; position: relative; }
.stats-source-tag {
  display: inline-block;
  font-size: 11px;
  color: #fff;
  background: #67c23a;
  padding: 1px 6px;
  margin-top: 6px;
}
.no-prestored {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #94a3b8;
  font-size: 13px;
  padding: 10px 0;
}

/* 汇总指标：紧凑网格 */
.stats-table {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1px;
  background: #dcdfe6;
  border: 1px solid #dcdfe6;
  overflow: hidden;
}
.stats-table.dense { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.stats-row {
  display: flex;
  flex-direction: column;
  background: #fff;
  padding: 6px 9px;
}
.stats-label { font-size: 11px; color: #64748b; line-height: 1.2; }
.stats-value {
  font-size: 0.95rem;
  font-weight: 700;
  color: #1e293b;
  font-family: 'Consolas', 'Menlo', monospace;
}

/* 图表面板 */
.charts-panel {
  display: flex;
  gap: 12px;
  margin: 4px 0 10px;
  flex-wrap: nowrap;
  align-items: stretch;
}
.chart-box {
  background: transparent;
  border: 1px solid #eef0f3;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
}
.chart-line { flex: 1 1 auto; min-width: 0; }
.chart-pie {
  flex: 0 0 228px;
  width: 228px;
}
.chart-title { font-size: 12px; font-weight: 600; color: #475569; margin-bottom: 4px; flex-shrink: 0; }
.chart-empty { font-size: 12px; color: #94a3b8; text-align: center; padding: 30px 0; }
.line-svg { width: 100%; height: auto; display: block; aspect-ratio: 400 / 74; flex: 1; min-height: 74px; }
.line-svg .grid-dash { stroke: #cbd5e1; stroke-width: 1; stroke-dasharray: 4 3; }
.line-svg .line { fill: none; stroke: #475569; stroke-width: 1.2; stroke-linejoin: round; stroke-linecap: round; }
.line-svg .dot { pointer-events: none; }
.pie-wrap {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  min-height: 74px;
}
.pie-svg { width: 80px; height: 80px; flex-shrink: 0; }
.pie-center { font-size: 15px; font-weight: 700; fill: #1e293b; }
.pie-legend { display: flex; flex-direction: column; gap: 3px; flex: 1; min-width: 0; }
.legend-item { display: flex; align-items: center; gap: 5px; font-size: 11px; color: #475569; }
.legend-dot { width: 8px; height: 8px; flex-shrink: 0; }
.legend-label { flex: 1; white-space: nowrap; }
.legend-value { font-weight: 700; color: #1e293b; font-family: 'Consolas', 'Menlo', monospace; white-space: nowrap; }

@media (max-width: 720px) {
  .charts-panel { flex-wrap: wrap; }
  .chart-pie { flex: 1 1 100%; width: auto; }
}


/* 番种 */
.fan-collapse { margin: 6px 0 10px; }
:deep(.fan-collapse .el-collapse-item__header) { font-size: 13px; font-weight: 600; }
.fan-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px;
}
.fan-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 8px;
  background: #f5f7fa;
  border: 1px solid #eef0f3;
  font-size: 12px;
}
.fan-name { color: #475569; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.fan-value { font-weight: 700; color: #409eff; font-family: 'Consolas', 'Menlo', monospace; }

/* 记录区 */
.records-section { margin-top: 14px; border-top: 1px solid #eef0f3; padding-top: 10px; }
.records-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 8px;
}
.section-title { font-size: 14px; font-weight: 700; color: #1e293b; }
.records-total { font-size: 12px; color: #94a3b8; }

/* 表格通用 */
.records-table { width: 100%; }
:deep(.records-table .el-table__cell) { padding: 4px 0; }
:deep(.records-table th.el-table__cell) {
  background: #f5f7fa;
  color: #475569;
  font-weight: 600;
  border-bottom: 1px solid #dcdfe6;
}
:deep(.records-table .el-table__border-left-patch),
:deep(.records-table td.el-table__cell) { border-color: #eef0f3; }
.cell-time { font-size: 12px; color: #64748b; font-family: 'Consolas', 'Menlo', monospace; }
.cell-scene { font-size: 12px; color: #303133; }
.cell-mode { font-size: 12px; color: #64748b; font-family: 'Consolas', 'Menlo', monospace; }
.cell-score { font-weight: 700; font-family: 'Consolas', 'Menlo', monospace; }
.cell-score.pos { color: #c0392b; }
.cell-score.neg { color: #2c7a2c; }
.cell-players { font-size: 12px; color: #64748b; cursor: help; }

.tip-player { font-size: 12px; line-height: 1.7; }

.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  font-weight: 700;
  font-size: 11px;
  font-family: 'Consolas', 'Menlo', monospace;
}
.rank-1 { background: #6fd86f; color: #1f5e1f; }
.rank-2 { background: #5dadff; color: #fff; }
.rank-3 { background: #aab4c2; color: #2c3848; }
.rank-4 { background: #ff7a7a; color: #fff; }
.tip-player .pos { color: #ff7a7a; font-weight: 700; }
.tip-player .neg { color: #6ee06e; font-weight: 700; }

.records-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}
.download-actions { display: flex; align-items: center; gap: 8px; }
.quota-tip { font-size: 11px; color: #94a3b8; }

.no-data { margin-top: 24px; }

@media (max-width: 640px) {
  .stats-table { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .stats-table.dense { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .filter-date { width: 100% !important; }
  .filter-row.with-date { flex-direction: column; align-items: flex-start; }
}
</style>
