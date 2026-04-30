<!-- 玩家数据统计页面 -->
<template>
  <div class="player-data">
    <div class="data-container">
      <h1 class="data-title">玩家数据统计</h1>
      <p class="data-description">通过玩家 ID 查询各规则下的统计数据和最近对局记录</p>

      <div class="search-section">
        <el-card class="search-card">
          <el-form :model="searchForm" label-width="100px">
            <el-form-item label="玩家ID">
              <el-input
                v-model="searchForm.playerId"
                placeholder="请输入玩家ID（例如：10000001）"
                clearable
                @keyup.enter="searchPlayer"
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="searchPlayer" :loading="loading">查询数据</el-button>
              <el-button @click="resetForm">重置</el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </div>

      <div v-if="playerInfo" class="data-display">
        <el-card class="info-card">
          <template #header><span>用户信息</span></template>
          <div class="user-info">
            <div class="info-item">
              <span class="label">用户名：</span>
              <span class="value">{{ playerInfo.user_settings?.username || '未知' }}</span>
            </div>
            <div class="info-item">
              <span class="label">用户ID：</span>
              <span class="value">{{ playerInfo.user_id }}</span>
            </div>
          </div>
        </el-card>

        <div class="rule-switch">
          <el-button-group>
            <el-button
              v-for="rule in availableRules"
              :key="rule.key"
              :type="currentRule === rule.key ? 'primary' : 'default'"
              @click="switchRule(rule.key)"
            >
              {{ rule.label }}（{{ rule.count }}）
            </el-button>
          </el-button-group>
        </div>

        <div v-if="currentStats.length > 0" class="stats-section">
          <el-card class="stats-card">
            <template #header><span>总计</span></template>
            <div class="stats-grid">
              <div class="stat-item" v-for="item in totalStatsDisplay" :key="item.label">
                <div class="stat-label">{{ item.label }}</div>
                <div class="stat-value">{{ item.value }}</div>
              </div>
            </div>
          </el-card>

          <el-card
            v-for="(stat, index) in currentStats"
            :key="`${stat.rule}-${stat.mode}-${index}`"
            class="stats-card"
          >
            <template #header>
              <span>{{ stat.mode || '未知模式' }}</span>
              <span class="rule-tag" v-if="stat.rule"> ／ {{ stat.rule }}</span>
            </template>
            <div class="stats-grid">
              <div class="stat-item" v-for="item in getModeStatsDisplay(stat)" :key="item.label">
                <div class="stat-label">{{ item.label }}</div>
                <div class="stat-value">{{ item.value }}</div>
              </div>
            </div>
          </el-card>

          <el-card v-if="currentFanDict && Object.keys(currentFanDict).length > 0" class="stats-card">
            <template #header><span>番种统计</span></template>
            <div class="fan-stats-grid">
              <div
                v-for="(name, key) in currentFanDict"
                :key="key"
                class="fan-stat-item"
              >
                <span class="fan-name">{{ name }}：</span>
                <span class="fan-value">{{ getFanValue(key) }}</span>
              </div>
            </div>
          </el-card>
        </div>

        <div v-else class="no-stats">
          <el-empty description="该规则暂无统计数据" />
        </div>

        <el-card class="records-card" v-if="gameRecords.length > 0">
          <template #header>
            <span>对局记录（最近 {{ gameRecords.length }} 局）</span>
          </template>
          <el-table :data="gameRecords" stripe style="width: 100%">
            <el-table-column prop="game_id" label="对局ID" width="120" />
            <el-table-column prop="rule" label="规则" width="100" />
            <el-table-column prop="match_type" label="模式" width="120">
              <template #default="scope">
                {{ scope.row.match_type || scope.row.sub_rule || '-' }}
              </template>
            </el-table-column>
            <el-table-column prop="created_at" label="时间" width="180">
              <template #default="scope">
                {{ formatDate(scope.row.created_at) }}
              </template>
            </el-table-column>
            <el-table-column label="玩家信息" min-width="400">
              <template #default="scope">
                <div class="players-info">
                  <div
                    v-for="player in scope.row.players"
                    :key="player.user_id"
                    class="player-item"
                    :class="{ 'current-player': player.user_id === playerInfo.user_id }"
                  >
                    <span class="rank-badge" :class="`rank-${player.rank}`">{{ player.rank }}位</span>
                    <span class="player-name">{{ player.username }}</span>
                    <span class="player-score" :class="{ 'positive': player.score > 0, 'negative': player.score < 0 }">
                      {{ player.score > 0 ? '+' : '' }}{{ player.score }}
                    </span>
                  </div>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </el-card>

        <div v-else-if="searched && gameRecords.length === 0" class="no-records">
          <el-empty description="暂无对局记录" />
        </div>
      </div>

      <div v-else-if="searched && !playerInfo" class="no-data">
        <el-empty description="未找到该玩家的数据" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import axios from 'axios'

const RULE_DEFS = [
  { key: 'guobiao', label: '国标', statsField: 'guobiao_stats', fanField: 'guobiao' },
  { key: 'riichi', label: '立直', statsField: 'riichi_stats', fanField: null },
  { key: 'qingque', label: '青雀', statsField: 'qingque_stats', fanField: 'qingque' },
  { key: 'classical', label: '古典', statsField: 'classical_stats', fanField: 'classical' }
]

const searchForm = reactive({ playerId: '' })
const playerInfo = ref(null)
const gameRecords = ref([])
const searched = ref(false)
const loading = ref(false)
const currentRule = ref('guobiao')

const availableRules = computed(() => {
  return RULE_DEFS.map(def => ({
    key: def.key,
    label: def.label,
    count: playerInfo.value?.[def.statsField]?.length || 0
  }))
})

const currentRuleDef = computed(() => RULE_DEFS.find(d => d.key === currentRule.value))

const currentStats = computed(() => {
  if (!playerInfo.value || !currentRuleDef.value) return []
  return playerInfo.value[currentRuleDef.value.statsField] || []
})

const currentFanDict = computed(() => {
  if (!playerInfo.value || !currentRuleDef.value?.fanField) return null
  return playerInfo.value.fan_dict?.[currentRuleDef.value.fanField] || null
})

// 汇总当前规则下所有模式的数据
const totalStats = computed(() => {
  if (currentStats.value.length === 0) return null
  const total = {
    total_games: 0, total_rounds: 0, win_count: 0, self_draw_count: 0,
    deal_in_count: 0, total_fan_score: 0, total_win_turn: 0,
    total_fangchong_score: 0, first_place_count: 0, second_place_count: 0,
    third_place_count: 0, fourth_place_count: 0, fan_stats: {}
  }
  for (const stat of currentStats.value) {
    total.total_games += stat.total_games || 0
    total.total_rounds += stat.total_rounds || 0
    total.win_count += stat.win_count || 0
    total.self_draw_count += stat.self_draw_count || 0
    total.deal_in_count += stat.deal_in_count || 0
    total.total_fan_score += stat.total_fan_score || 0
    total.total_win_turn += stat.total_win_turn || 0
    total.total_fangchong_score += stat.total_fangchong_score || 0
    total.first_place_count += stat.first_place_count || 0
    total.second_place_count += stat.second_place_count || 0
    total.third_place_count += stat.third_place_count || 0
    total.fourth_place_count += stat.fourth_place_count || 0
    if (stat.fan_stats) {
      for (const [k, v] of Object.entries(stat.fan_stats)) {
        total.fan_stats[k] = (total.fan_stats[k] || 0) + v
      }
    }
  }
  return total
})

const ratio = (numerator, denominator, suffix = '%') => {
  if (!denominator || denominator <= 0) return '0.00' + suffix
  return ((numerator / denominator) * 100).toFixed(2) + suffix
}

const avg = (numerator, denominator) => {
  if (!denominator || denominator <= 0) return '0.00'
  return (numerator / denominator).toFixed(2)
}

const buildStatsRows = (s) => [
  { label: '总对局数', value: s.total_games || 0 },
  { label: '累计回合数', value: s.total_rounds || 0 },
  { label: '和牌率', value: ratio(s.win_count, s.total_rounds) },
  { label: '自摸率', value: ratio(s.self_draw_count, s.total_rounds) },
  { label: '放铳率', value: ratio(s.deal_in_count, s.total_rounds) },
  { label: '平均和番', value: avg(s.total_fan_score, s.win_count) },
  { label: '平均和巡', value: avg(s.total_win_turn, s.win_count) },
  { label: '平均铳番', value: avg(s.total_fangchong_score, s.deal_in_count) },
  { label: '一位率', value: ratio(s.first_place_count, s.total_games) },
  { label: '二位率', value: ratio(s.second_place_count, s.total_games) },
  { label: '三位率', value: ratio(s.third_place_count, s.total_games) },
  { label: '四位率', value: ratio(s.fourth_place_count, s.total_games) }
]

const totalStatsDisplay = computed(() => totalStats.value ? buildStatsRows(totalStats.value) : [])
const getModeStatsDisplay = (stat) => buildStatsRows(stat)

const getFanValue = (fanKey) => {
  if (!totalStats.value || !totalStats.value.fan_stats) return 0
  return totalStats.value.fan_stats[fanKey] || 0
}

const switchRule = (rule) => { currentRule.value = rule }

const searchPlayer = async () => {
  if (!searchForm.playerId) {
    ElMessage.warning('请输入玩家ID')
    return
  }
  const userId = parseInt(searchForm.playerId)
  if (isNaN(userId)) {
    ElMessage.error('玩家ID必须是数字')
    return
  }

  loading.value = true
  searched.value = true

  try {
    const infoResp = await axios.get(`/api/player/info/${userId}`)
    if (infoResp.data.success) {
      playerInfo.value = infoResp.data.data
      // 默认显示有数据的第一个规则
      const defaultRule = RULE_DEFS.find(d => (playerInfo.value[d.statsField] || []).length > 0)
      if (defaultRule) currentRule.value = defaultRule.key
    } else {
      ElMessage.error(infoResp.data.message || '获取玩家信息失败')
      playerInfo.value = null
    }

    const recordsResp = await axios.get(`/api/player/records/${userId}`, { params: { limit: 20 } })
    if (recordsResp.data.success) {
      gameRecords.value = recordsResp.data.data || []
    } else {
      ElMessage.warning(recordsResp.data.message || '获取对局记录失败')
      gameRecords.value = []
    }
  } catch (error) {
    console.error('查询玩家数据错误:', error)
    ElMessage.error('查询失败，请检查网络连接或玩家ID是否正确')
    playerInfo.value = null
    gameRecords.value = []
  } finally {
    loading.value = false
  }
}

const resetForm = () => {
  searchForm.playerId = ''
  playerInfo.value = null
  gameRecords.value = []
  searched.value = false
  currentRule.value = 'guobiao'
}

const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit'
  })
}
</script>

<style scoped>
.player-data {
  max-width: 1400px;
  margin: 0 auto;
  padding: 20px;
  color: white;
  min-height: calc(100vh - 200px);
}

.data-container { text-align: center; }

.data-title {
  font-size: 2.5rem;
  margin-bottom: 10px;
  font-weight: bold;
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
}

.data-description {
  font-size: 1.1rem;
  margin-bottom: 30px;
  opacity: 0.9;
}

.search-section { margin-bottom: 30px; }

.search-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
}
.search-card :deep(.el-card__body) { background: transparent; }
.search-card :deep(.el-form-item__label) { color: white; }
.search-card :deep(.el-input__wrapper) {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
}
.search-card :deep(.el-input__inner) { color: white; }

.data-display { margin-top: 30px; text-align: left; }

.info-card,
.stats-card,
.records-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
  margin-bottom: 20px;
}

.info-card :deep(.el-card__header),
.stats-card :deep(.el-card__header),
.records-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
  color: white;
}
.info-card :deep(.el-card__body),
.stats-card :deep(.el-card__body),
.records-card :deep(.el-card__body) {
  background: transparent;
}

.user-info {
  display: flex;
  gap: 30px;
  flex-wrap: wrap;
}
.info-item { display: flex; align-items: center; }
.info-item .label { font-weight: bold; margin-right: 10px; }
.info-item .value { color: #ffd04b; }

.rule-switch {
  margin: 20px 0;
  text-align: center;
}

.rule-tag { opacity: 0.7; font-size: 0.9em; }

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 15px;
}

.stat-item {
  padding: 10px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  text-align: center;
}
.stat-label { font-size: 0.9rem; opacity: 0.8; margin-bottom: 5px; }
.stat-value { font-size: 1.2rem; font-weight: bold; color: #ffd04b; }

.fan-stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 10px;
  max-height: 500px;
  overflow-y: auto;
}
.fan-stat-item {
  padding: 8px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 6px;
  display: flex;
  justify-content: space-between;
  font-size: 0.9rem;
}
.fan-name { opacity: 0.9; }
.fan-value { font-weight: bold; color: #ffd04b; }

.records-card :deep(.el-table) {
  background: transparent;
  color: white;
}
.records-card :deep(.el-table th) {
  background: rgba(255, 255, 255, 0.1);
  color: white;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
}
.records-card :deep(.el-table td) {
  background: transparent;
  color: white;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}
.records-card :deep(.el-table--striped .el-table__body tr.el-table__row--striped td) {
  background: rgba(255, 255, 255, 0.05);
}

.players-info {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.player-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 5px;
  border-radius: 4px;
}
.player-item.current-player {
  background: rgba(255, 208, 75, 0.2);
  font-weight: bold;
}

.rank-badge {
  display: inline-block;
  width: 40px;
  text-align: center;
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: bold;
  font-size: 0.85rem;
}
.rank-1 { background: #ffd700; color: #000; }
.rank-2 { background: #c0c0c0; color: #000; }
.rank-3 { background: #cd7f32; color: #fff; }
.rank-4 { background: #666; color: #fff; }

.player-name { flex: 1; }
.player-score { font-weight: bold; min-width: 60px; text-align: right; }
.player-score.positive { color: #67c23a; }
.player-score.negative { color: #f56c6c; }

.no-data, .no-stats, .no-records { margin-top: 50px; }
.no-data :deep(.el-empty__description),
.no-stats :deep(.el-empty__description),
.no-records :deep(.el-empty__description) { color: white; }

@media (max-width: 768px) {
  .data-title { font-size: 2rem; }
  .stats-grid { grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
  .fan-stats-grid { grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); }
}
</style>
