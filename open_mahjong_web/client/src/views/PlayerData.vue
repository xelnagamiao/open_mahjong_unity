<!-- 玩家数据统计：窄版、汇总恒显、对局横向滚动 -->

<template>

  <div class="player-data">

    <div class="data-container">

      <h1 class="data-title">玩家数据统计</h1>

      <p class="data-description">输入玩家 ID 查询各规则下的统计数据与最近对局</p>



      <el-card class="search-card" body-style="padding: 12px 14px;">

        <div class="search-row">

          <span class="search-label">玩家 ID</span>

          <el-input

            v-model="searchForm.playerId"

            class="search-input"

            placeholder="10000001"

            clearable

            @keyup.enter="searchPlayer"

          />

          <el-button type="primary" size="small" @click="searchPlayer" :loading="loading">查询</el-button>

          <el-button size="small" @click="resetForm">重置</el-button>

        </div>

      </el-card>



      <div v-if="playerInfo" class="data-display">

        <el-card class="info-card" shadow="never">

          <template #header><span class="card-h">用户信息</span></template>

          <div class="user-info">

            <div class="info-item">

              <span class="label">用户名</span>

              <span class="value">{{ playerInfo.user_settings?.username || '未知' }}</span>

            </div>

            <div class="info-item">

              <span class="label">用户 ID</span>

              <span class="value">{{ playerInfo.user_id }}</span>

            </div>

          </div>

        </el-card>



        <div class="rule-switch">

          <el-button-group size="small">

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



        <div class="stats-section">

          <el-card class="stats-card" shadow="never">

            <template #header><span class="card-h">当前规则 · 汇总</span></template>

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

            shadow="never"

          >

            <template #header>

              <span class="card-h">{{ stat.mode || '未知模式' }}</span>

              <span class="rule-tag" v-if="stat.rule">／{{ stat.rule }}</span>

            </template>

            <div class="stats-grid stats-grid-dense">

              <div class="stat-item" v-for="item in getModeStatsDisplay(stat)" :key="item.label">

                <div class="stat-label">{{ item.label }}</div>

                <div class="stat-value">{{ item.value }}</div>

              </div>

            </div>

          </el-card>



          <p v-if="currentStats.length === 0" class="hint-inline">该规则下暂无对局细分，上表汇总为 0。</p>



          <el-card v-if="currentRuleDef?.fanField" class="stats-card" shadow="never">

            <template #header><span class="card-h">番种统计</span></template>

            <div v-if="fanEntries.length" class="fan-stats-grid">

              <div

                v-for="[fanKey, fanName] in fanEntries"

                :key="fanKey"

                class="fan-stat-item"

              >

                <span class="fan-name">{{ fanName }}</span>

                <span class="fan-value">{{ getFanValue(fanKey) }}</span>

              </div>

            </div>

            <div v-else class="fan-empty">暂无番种记录（均为 0）</div>

          </el-card>

        </div>



        <el-card class="records-card" shadow="never">

          <template #header>

            <span class="card-h">对局记录</span>

            <span class="records-meta">最近 {{ gameRecords.length }} 局 · 横向滑动查看</span>

          </template>

          <div class="records-scroll">

            <div

              v-for="rec in gameRecords"

              :key="rec.game_id"

              class="record-tile"

            >

              <div class="record-top">

                <span class="rid">#{{ rec.game_id }}</span>

                <span class="rmeta">{{ rec.rule }} · {{ rec.match_type || rec.sub_rule || '-' }}</span>

              </div>

              <div class="record-time">{{ formatDate(rec.created_at) }}</div>

              <div class="record-players">

                <div

                  v-for="player in rec.players"

                  :key="player.user_id"

                  class="p-row"

                  :class="{ 'is-me': player.user_id === playerInfo.user_id }"

                >

                  <span class="rank-badge" :class="`rank-${player.rank}`">{{ player.rank }}</span>

                  <span class="p-name">{{ player.username }}</span>

                  <span class="p-score" :class="{ positive: player.score > 0, negative: player.score < 0 }">

                    {{ player.score > 0 ? '+' : '' }}{{ player.score }}

                  </span>

                </div>

              </div>

            </div>

            <div v-if="gameRecords.length === 0" class="records-empty">暂无对局记录</div>

          </div>

        </el-card>

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



const EMPTY_TOTAL = {

  total_games: 0, total_rounds: 0, win_count: 0, self_draw_count: 0,

  deal_in_count: 0, total_fan_score: 0, total_win_turn: 0,

  total_fangchong_score: 0, first_place_count: 0, second_place_count: 0,

  third_place_count: 0, fourth_place_count: 0, fan_stats: {}

}



const searchForm = reactive({ playerId: '10000001' })

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



const fanEntries = computed(() => {

  const d = currentFanDict.value

  if (!d || typeof d !== 'object') return []

  return Object.entries(d)

})



const totalStats = computed(() => {

  if (!playerInfo.value || !currentRuleDef.value) return { ...EMPTY_TOTAL }

  if (currentStats.value.length === 0) return { ...EMPTY_TOTAL }

  const total = { ...EMPTY_TOTAL, fan_stats: {} }

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

  { label: '总对局数', value: String(s.total_games || 0) },

  { label: '累计回合数', value: String(s.total_rounds || 0) },

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



const totalStatsDisplay = computed(() => buildStatsRows(totalStats.value))

const getModeStatsDisplay = (stat) => buildStatsRows(stat)



const getFanValue = (fanKey) => {

  if (!totalStats.value?.fan_stats) return 0

  return totalStats.value.fan_stats[fanKey] || 0

}



const switchRule = (rule) => { currentRule.value = rule }



const searchPlayer = async () => {

  const raw = String(searchForm.playerId ?? '').trim() || '10000001'

  searchForm.playerId = raw

  const userId = parseInt(raw, 10)

  if (isNaN(userId)) {

    ElMessage.error('玩家 ID 必须是数字')

    return

  }



  loading.value = true

  searched.value = true



  try {

    const infoResp = await axios.get(`/api/player/info/${userId}`)

    if (infoResp.data.success) {

      playerInfo.value = infoResp.data.data

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

    ElMessage.error('查询失败，请检查网络连接或玩家 ID')

    playerInfo.value = null

    gameRecords.value = []

  } finally {

    loading.value = false

  }

}



const resetForm = () => {

  searchForm.playerId = '10000001'

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

  max-width: 860px;

  margin: 0 auto;

  padding: 12px 12px 24px;

  color: white;

  min-height: calc(100vh - 200px);

}



.data-container { text-align: center; }



.data-title {

  font-size: 1.5rem;

  margin: 0 0 6px;

  font-weight: bold;

  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);

  color: white;

}



.data-description {

  font-size: 0.88rem;

  margin: 0 0 14px;

  opacity: 0.92;

  color: rgba(255, 255, 255, 0.95);

}



.search-card {

  background: rgba(255, 255, 255, 0.96);

  border-radius: 10px;

  color: #303133;

  text-align: left;

}



.search-row {

  display: flex;

  flex-wrap: wrap;

  align-items: center;

  gap: 8px;

}



.search-label {

  font-size: 13px;

  font-weight: 600;

  color: var(--omu-text-soft, #475569);

  flex-shrink: 0;

}



.search-input {

  width: 160px;

  max-width: 100%;

}



.data-display {

  margin-top: 14px;

  text-align: left;

  color: #303133;

}



.info-card,

.stats-card,

.records-card {

  background: rgba(255, 255, 255, 0.96);

  border-radius: 10px;

  margin-bottom: 10px;

  color: #303133;

}



.card-h {

  font-size: 13px;

  font-weight: 600;

}



.user-info {

  display: flex;

  gap: 16px 24px;

  flex-wrap: wrap;

  font-size: 13px;

}

.info-item { display: flex; align-items: center; gap: 6px; }

.info-item .label { font-weight: 600; color: var(--omu-text-soft, #475569); }

.info-item .value { color: var(--omu-accent, #409eff); font-family: var(--omu-mono, 'Consolas', monospace); }



.rule-switch {

  margin: 8px 0 10px;

}



.rule-tag { color: var(--omu-text-muted, #94a3b8); font-size: 12px; margin-left: 4px; }



.stats-grid {

  display: grid;

  grid-template-columns: repeat(4, minmax(0, 1fr));

  gap: 6px;

}



.stats-grid-dense {

  grid-template-columns: repeat(3, minmax(0, 1fr));

}



.stat-item {

  padding: 6px 8px;

  background: var(--omu-surface-soft, #f5f7fa);

  border-radius: 6px;

  border: 1px solid var(--omu-border, #ebeef5);

  text-align: center;

}

.stat-label { font-size: 11px; color: var(--omu-text-soft, #475569); margin-bottom: 2px; line-height: 1.2; }

.stat-value { font-size: 0.95rem; font-weight: 700; color: var(--omu-accent, #409eff); font-family: var(--omu-mono, 'Consolas', monospace); }



.hint-inline {

  margin: 0 0 8px;

  font-size: 12px;

  color: var(--omu-text-muted, #94a3b8);

  text-align: center;

}



.fan-stats-grid {

  display: grid;

  grid-template-columns: repeat(2, minmax(0, 1fr));

  gap: 6px;

  max-height: 280px;

  overflow-y: auto;

}

.fan-stat-item {

  padding: 5px 8px;

  background: var(--omu-surface-soft, #f5f7fa);

  border: 1px solid var(--omu-border, #ebeef5);

  border-radius: 6px;

  display: flex;

  justify-content: space-between;

  align-items: center;

  font-size: 12px;

  gap: 8px;

}

.fan-name { color: var(--omu-text-soft, #475569); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.fan-value { font-weight: 700; color: var(--omu-accent, #409eff); font-family: var(--omu-mono, 'Consolas', monospace); flex-shrink: 0; }



.fan-empty {

  font-size: 12px;

  color: var(--omu-text-muted, #94a3b8);

  text-align: center;

  padding: 8px;

}



.records-meta {

  float: right;

  font-size: 11px;

  font-weight: 400;

  color: var(--omu-text-muted, #94a3b8);

}



.records-scroll {

  display: flex;

  flex-direction: row;

  gap: 10px;

  overflow-x: auto;

  padding-bottom: 4px;

  -webkit-overflow-scrolling: touch;

}



.record-tile {

  flex: 0 0 200px;

  border: 1px solid var(--omu-border, #ebeef5);

  border-radius: 8px;

  padding: 8px;

  background: var(--omu-surface-soft, #f8fafc);

  font-size: 11px;

}



.record-top {

  display: flex;

  flex-direction: column;

  gap: 2px;

  margin-bottom: 4px;

}

.rid { font-weight: 700; color: var(--omu-text, #1e293b); font-family: var(--omu-mono, 'Consolas', monospace); }

.rmeta { color: var(--omu-text-soft, #64748b); line-height: 1.3; }



.record-time {

  color: var(--omu-text-muted, #94a3b8);

  margin-bottom: 6px;

  font-size: 10px;

}



.record-players {

  display: flex;

  flex-direction: column;

  gap: 4px;

}



.p-row {

  display: flex;

  align-items: center;

  gap: 6px;

  padding: 2px 0;

  border-radius: 4px;

}

.p-row.is-me {

  background: rgba(64, 158, 255, 0.1);

  font-weight: 600;

}



.rank-badge {

  flex-shrink: 0;

  min-width: 20px;

  text-align: center;

  padding: 1px 4px;

  border-radius: 4px;

  font-weight: 700;

  font-size: 10px;

}

.rank-1 { background: #fde68a; color: #78350f; }

.rank-2 { background: #e2e8f0; color: #1e293b; }

.rank-3 { background: #fed7aa; color: #7c2d12; }

.rank-4 { background: #f1f5f9; color: #475569; }



.p-name {

  flex: 1;

  min-width: 0;

  overflow: hidden;

  text-overflow: ellipsis;

  white-space: nowrap;

}

.p-score {

  font-weight: 700;

  font-family: var(--omu-mono, 'Consolas', monospace);

  font-size: 11px;

}

.p-score.positive { color: var(--omu-success, #16a34a); }

.p-score.negative { color: var(--omu-danger, #dc2626); }



.records-empty {

  flex: 1;

  min-height: 100px;

  display: flex;

  align-items: center;

  justify-content: center;

  color: var(--omu-text-muted, #94a3b8);

  font-size: 12px;

  border: 1px dashed var(--omu-border, #e4e7ed);

  border-radius: 8px;

}



.no-data { margin-top: 24px; }



@media (max-width: 520px) {

  .stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }

  .stats-grid-dense { grid-template-columns: repeat(2, minmax(0, 1fr)); }

  .record-tile { flex-basis: 180px; }

}

</style>

