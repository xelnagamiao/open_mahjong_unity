<template>
  <div>
    <h2 class="page-title">全站每日统计</h2>

    <div class="filter-bar">
      <el-select v-model="statsTier" placeholder="全部场次" size="small" clearable class="filter-tier" @change="loadSceneStats">
        <el-option v-for="t in TIER_OPTIONS" :key="t.value" :label="t.label" :value="t.value" />
      </el-select>
      <el-select v-model="statsGameType" placeholder="全部局制" size="small" clearable class="filter-gametype" @change="loadSceneStats">
        <el-option v-for="g in GAME_TYPE_OPTIONS" :key="g.value" :label="g.label" :value="g.value" />
      </el-select>
      <el-select v-model="statsRule" placeholder="全部规则" size="small" clearable class="filter-rule" @change="loadSceneStats">
        <el-option label="国标" value="guobiao" />
        <el-option label="立直" value="riichi" />
        <el-option label="青雀" value="qingque" />
        <el-option label="古典" value="classical" />
      </el-select>
      <span class="hint">每日 04:00 聚合；停机重启后启动时自动补齐缺失日期。</span>
    </div>

    <el-card shadow="never" class="block-card">
      <template #header>每日总览（近 30 天）</template>
      <el-table :data="dailyStats" size="small" empty-text="暂无数据" max-height="360" v-loading="dailyLoading">
        <el-table-column label="日期" prop="stat_date" width="130" />
        <el-table-column label="对局数" prop="game_count" width="110" />
        <el-table-column label="活跃用户" prop="active_users" width="120" />
        <el-table-column label="最大在线" prop="max_online" width="120" />
      </el-table>
    </el-card>

    <el-card shadow="never" class="block-card">
      <template #header>各场次指标（按筛选聚合）</template>
      <el-table :data="sceneStats" size="small" empty-text="暂无数据" max-height="420" v-loading="sceneLoading">
        <el-table-column label="日期" prop="stat_date" width="120" />
        <el-table-column label="场次" min-width="110">
          <template #default="{ row }">{{ sceneDimLabel(row) }}</template>
        </el-table-column>
        <el-table-column label="局制" width="90">
          <template #default="{ row }">{{ gameTypeLabel(row.game_type) }}</template>
        </el-table-column>
        <el-table-column label="规则" prop="rule" width="80" />
        <el-table-column label="对局" prop="total_games" width="70" />
        <el-table-column label="回合" prop="total_rounds" width="80" />
        <el-table-column label="和牌" prop="win_count" width="70" />
        <el-table-column label="自摸" prop="self_draw_count" width="70" />
        <el-table-column label="放铳" prop="deal_in_count" width="70" />
        <el-table-column label="一位" prop="first_place_count" width="60" />
        <el-table-column label="二位" prop="second_place_count" width="60" />
        <el-table-column label="三位" prop="third_place_count" width="60" />
        <el-table-column label="四位" prop="fourth_place_count" width="60" />
        <el-table-column label="局均点" width="90">
          <template #default="{ row }">
            {{ avgRoundScore(row) }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'

const TIER_OPTIONS = [
  { value: 'custom', label: '自定义' },
  { value: 'beginner', label: '初级场' },
  { value: 'intermediate', label: '中级场' },
  { value: 'advanced', label: '高级场' },
  { value: 'mcrpl', label: 'mcrpl' },
  { value: 'events', label: '比赛场' },
  { value: 'legacy_match', label: '历史排位' }
]
const GAME_TYPE_OPTIONS = [
  { value: 'dongfeng', label: '东风战' },
  { value: 'banzhuang', label: '东南战' },
  { value: 'xifeng', label: '西风战' },
  { value: 'quanzhuang', label: '全庄战' }
]
const TIER_LABEL = Object.fromEntries(TIER_OPTIONS.map(t => [t.value, t.label]))

const dailyStats = ref([])
const sceneStats = ref([])
const dailyLoading = ref(false)
const sceneLoading = ref(false)
const statsTier = ref(null)
const statsGameType = ref(null)
const statsRule = ref(null)

const sceneDimLabel = (row) => {
  if (row.room_type === 'custom') return '自定义'
  if (row.room_type === 'events') return '比赛场'
  if (row.room_type === 'match') {
    if (row.match_tier && TIER_LABEL[row.match_tier]) return TIER_LABEL[row.match_tier]
    return '历史排位'
  }
  return row.room_type || '-'
}
const gameTypeLabel = (gt) => {
  const m = { dongfeng: '东风战', banzhuang: '东南战', xifeng: '西风战', quanzhuang: '全庄战' }
  return m[gt] || gt || '-'
}
const avgRoundScore = (row) => {
  const g = Number(row.total_games) || 0
  if (!g) return '0.00'
  return (Number(row.total_round_score) / g).toFixed(2)
}

const loadDailyStats = async () => {
  dailyLoading.value = true
  try {
    const res = await adminApi.get('/stats/daily', { params: { days: 30 } })
    dailyStats.value = res.data.data || []
  } catch (e) {
    ElMessage.error('获取每日统计失败')
    dailyStats.value = []
  } finally {
    dailyLoading.value = false
  }
}

const loadSceneStats = async () => {
  sceneLoading.value = true
  try {
    const params = {}
    if (statsTier.value) params.tier = statsTier.value
    if (statsGameType.value) params.game_type = statsGameType.value
    if (statsRule.value) params.rule = statsRule.value
    const res = await adminApi.get('/stats/scene', { params })
    sceneStats.value = res.data.data || []
  } catch (e) {
    ElMessage.error('获取场次统计失败')
    sceneStats.value = []
  } finally {
    sceneLoading.value = false
  }
}

onMounted(() => {
  loadDailyStats()
  loadSceneStats()
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
.filter-tier { width: 140px; }
.filter-gametype { width: 130px; }
.filter-rule { width: 110px; }
.hint { font-size: 12px; color: #909399; }
.block-card { margin-bottom: 16px; }
</style>
