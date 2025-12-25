<!-- 玩家数据统计页面 -->
<template>
  <div class="player-data">
    <div class="data-container">
      <h1 class="data-title">玩家数据统计</h1>
      <p class="data-description">通过玩家ID查询统计数据和对局记录</p>
      
      <!-- 搜索区域 -->
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
      
      <!-- 数据展示区域 -->
      <div v-if="playerInfo" class="data-display">
        <!-- 用户基本信息 -->
        <el-card class="info-card">
          <template #header>
            <span>用户信息</span>
          </template>
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

        <!-- 规则切换按钮 -->
        <div class="rule-switch">
          <el-button-group>
            <el-button 
              :type="currentRule === 'GB' ? 'primary' : 'default'"
              @click="switchRule('GB')"
            >
              国标麻将 (GB)
            </el-button>
            <el-button 
              :type="currentRule === 'JP' ? 'primary' : 'default'"
              @click="switchRule('JP')"
            >
              立直麻将 (JP)
            </el-button>
          </el-button-group>
        </div>

        <!-- 统计数据展示 -->
        <div v-if="currentStats.length > 0" class="stats-section">
          <!-- 汇总统计 -->
          <el-card class="stats-card">
            <template #header>
              <span>总计</span>
            </template>
            <div class="stats-content">
              <div class="stats-grid">
                <div class="stat-item" v-for="item in totalStatsDisplay" :key="item.label">
                  <div class="stat-label">{{ item.label }}</div>
                  <div class="stat-value">{{ item.value }}</div>
                </div>
              </div>
            </div>
          </el-card>

          <!-- 各模式统计 -->
          <el-card 
            v-for="(stat, index) in currentStats" 
            :key="`${stat.rule}-${stat.mode}-${index}`"
            class="stats-card"
          >
            <template #header>
              <span>{{ stat.mode || '未知模式' }}</span>
            </template>
            <div class="stats-content">
              <div class="stats-grid">
                <div class="stat-item" v-for="item in getModeStatsDisplay(stat)" :key="item.label">
                  <div class="stat-label">{{ item.label }}</div>
                  <div class="stat-value">{{ item.value }}</div>
                </div>
              </div>
            </div>
          </el-card>

          <!-- 番种统计 -->
          <el-card class="stats-card">
            <template #header>
              <span>番种统计</span>
            </template>
            <div class="fan-stats-content">
              <div class="fan-stats-grid">
                <div 
                  v-for="(name, key) in fanTranslationDict" 
                  :key="key"
                  class="fan-stat-item"
                >
                  <span class="fan-name">{{ name }}：</span>
                  <span class="fan-value">{{ getFanValue(key) }}</span>
                </div>
              </div>
            </div>
          </el-card>
        </div>

        <div v-else class="no-stats">
          <el-empty description="该规则暂无统计数据" />
        </div>

        <!-- 对局记录列表 -->
        <el-card class="records-card" v-if="gameRecords.length > 0">
          <template #header>
            <span>对局记录（最近 {{ gameRecords.length }} 局）</span>
          </template>
          <el-table :data="gameRecords" stripe style="width: 100%">
            <el-table-column prop="game_id" label="对局ID" width="120" />
            <el-table-column prop="rule" label="规则" width="80" />
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
      
      <!-- 无数据提示 -->
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

// 番种英文到中文的翻译字典
const fanTranslationDict = {
  "dasixi": "大四喜",
  "dasanyuan": "大三元",
  "lvyise": "绿一色",
  "jiulianbaodeng": "九莲宝灯",
  "sigang": "四杠",
  "sangang": "三杠",
  "lianqidui": "连七对",
  "shisanyao": "十三幺",
  "qingyaojiu": "清幺九",
  "xiaosixi": "小四喜",
  "xiaosanyuan": "小三元",
  "ziyise": "字一色",
  "sianke": "四暗刻",
  "yiseshuanglonghui": "一色双龙会",
  "yisesitongshun": "一色四同顺",
  "yisesijiegao": "一色四节高",
  "yisesibugao": "一色四步高",
  "hunyaojiu": "混幺九",
  "qiduizi": "七对子",
  "qixingbukao": "七星不靠",
  "quanshuangke": "全双刻",
  "qingyise": "清一色",
  "yisesantongshun": "一色三同顺",
  "yisesanjiegao": "一色三节高",
  "quanda": "全大",
  "quanzhong": "全中",
  "quanxiao": "全小",
  "qinglong": "清龙",
  "sanseshuanglonghui": "三色双龙会",
  "yisesanbugao": "一色三步高",
  "quandaiwu": "全带五",
  "santongke": "三同刻",
  "sananke": "三暗刻",
  "quanbukao": "全不靠",
  "zuhelong": "组合龙",
  "dayuwu": "大于五",
  "xiaoyuwu": "小于五",
  "sanfengke": "三风刻",
  "hualong": "花龙",
  "tuibudao": "推不倒",
  "sansesantongshun": "三色三同顺",
  "sansesanjiegao": "三色三节高",
  "wufanhe": "无番和",
  "miaoshouhuichun": "妙手回春",
  "haidilaoyue": "海底捞月",
  "gangshangkaihua": "杠上开花",
  "qiangganghe": "抢杠和",
  "pengpenghe": "碰碰和",
  "hunyise": "混一色",
  "sansesanbugao": "三色三步高",
  "wumenqi": "五门齐",
  "quanqiuren": "全求人",
  "shuangangang": "双暗杠",
  "shuangjianke": "双箭刻",
  "quandaiyao": "全带幺",
  "buqiuren": "不求人",
  "shuangminggang": "双明杠",
  "hejuezhang": "和绝张",
  "jianke": "箭刻",
  "quanfengke": "圈风刻",
  "menfengke": "门风刻",
  "menqianqing": "门前清",
  "pinghe": "平和",
  "siguiyi": "四归一",
  "shuangtongke": "双同刻",
  "shuanganke": "双暗刻",
  "angang": "暗杠",
  "duanyao": "断幺",
  "yibangao": "一般高",
  "xixiangfeng": "喜相逢",
  "lianliu": "连六",
  "laoshaofu": "老少副",
  "yaojiuke": "幺九刻",
  "minggang": "明杠",
  "queyimen": "缺一门",
  "wuzi": "无字",
  "bianzhang": "边张",
  "qianzhang": "嵌张",
  "dandiaojiang": "单钓将",
  "zimo": "自摸",
  "huapai": "花牌",
  "mingangang": "明暗杠"
}

const searchForm = reactive({
  playerId: ''
})

const playerInfo = ref(null)
const gameRecords = ref([])
const searched = ref(false)
const loading = ref(false)
const currentRule = ref('GB')

// 当前规则的统计数据
const currentStats = computed(() => {
  if (!playerInfo.value) return []
  return currentRule.value === 'GB' 
    ? (playerInfo.value.gb_stats || [])
    : (playerInfo.value.jp_stats || [])
})

// 汇总统计数据
const totalStats = computed(() => {
  if (currentStats.value.length === 0) return null
  
  const total = {
    total_games: 0,
    total_rounds: 0,
    win_count: 0,
    self_draw_count: 0,
    deal_in_count: 0,
    total_fan_score: 0,
    total_win_turn: 0,
    total_fangchong_score: 0,
    first_place_count: 0,
    second_place_count: 0,
    third_place_count: 0,
    fourth_place_count: 0,
    fan_stats: {}
  }

  currentStats.value.forEach(stat => {
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
      Object.keys(stat.fan_stats).forEach(key => {
        total.fan_stats[key] = (total.fan_stats[key] || 0) + stat.fan_stats[key]
      })
    }
  })

  return total
})

// 汇总统计显示数据
const totalStatsDisplay = computed(() => {
  if (!totalStats.value) return []
  const stats = totalStats.value
  
  const winRate = stats.total_rounds > 0 
    ? ((stats.win_count / stats.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const selfDrawRate = stats.total_rounds > 0
    ? ((stats.self_draw_count / stats.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const dealInRate = stats.total_rounds > 0
    ? ((stats.deal_in_count / stats.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const avgFanScore = stats.win_count > 0
    ? (stats.total_fan_score / stats.win_count).toFixed(2)
    : '0.00'
  
  const avgWinTurn = stats.win_count > 0
    ? (stats.total_win_turn / stats.win_count).toFixed(2)
    : '0.00'
  
  const avgFangchongScore = stats.deal_in_count > 0
    ? (stats.total_fangchong_score / stats.deal_in_count).toFixed(2)
    : '0.00'
  
  const firstPlaceRate = stats.total_games > 0
    ? ((stats.first_place_count / stats.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const secondPlaceRate = stats.total_games > 0
    ? ((stats.second_place_count / stats.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const thirdPlaceRate = stats.total_games > 0
    ? ((stats.third_place_count / stats.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const fourthPlaceRate = stats.total_games > 0
    ? ((stats.fourth_place_count / stats.total_games) * 100).toFixed(2) + '%'
    : '0.00%'

  return [
    { label: '总对局数', value: stats.total_games },
    { label: '累计回合数', value: stats.total_rounds },
    { label: '和牌率', value: winRate },
    { label: '自摸率', value: selfDrawRate },
    { label: '放铳率', value: dealInRate },
    { label: '平均和番', value: avgFanScore },
    { label: '平均和巡', value: avgWinTurn },
    { label: '平均铳番', value: avgFangchongScore },
    { label: '一位率', value: firstPlaceRate },
    { label: '二位率', value: secondPlaceRate },
    { label: '三位率', value: thirdPlaceRate },
    { label: '四位率', value: fourthPlaceRate }
  ]
})

// 获取模式统计显示数据
const getModeStatsDisplay = (stat) => {
  const winRate = stat.total_rounds > 0 
    ? ((stat.win_count / stat.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const selfDrawRate = stat.total_rounds > 0
    ? ((stat.self_draw_count / stat.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const dealInRate = stat.total_rounds > 0
    ? ((stat.deal_in_count / stat.total_rounds) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const avgFanScore = stat.win_count > 0
    ? (stat.total_fan_score / stat.win_count).toFixed(2)
    : '0.00'
  
  const avgWinTurn = stat.win_count > 0
    ? (stat.total_win_turn / stat.win_count).toFixed(2)
    : '0.00'
  
  const avgFangchongScore = stat.deal_in_count > 0
    ? (stat.total_fangchong_score / stat.deal_in_count).toFixed(2)
    : '0.00'
  
  const firstPlaceRate = stat.total_games > 0
    ? ((stat.first_place_count / stat.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const secondPlaceRate = stat.total_games > 0
    ? ((stat.second_place_count / stat.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const thirdPlaceRate = stat.total_games > 0
    ? ((stat.third_place_count / stat.total_games) * 100).toFixed(2) + '%'
    : '0.00%'
  
  const fourthPlaceRate = stat.total_games > 0
    ? ((stat.fourth_place_count / stat.total_games) * 100).toFixed(2) + '%'
    : '0.00%'

  return [
    { label: '总对局数', value: stat.total_games || 0 },
    { label: '累计回合数', value: stat.total_rounds || 0 },
    { label: '和牌率', value: winRate },
    { label: '自摸率', value: selfDrawRate },
    { label: '放铳率', value: dealInRate },
    { label: '平均和番', value: avgFanScore },
    { label: '平均和巡', value: avgWinTurn },
    { label: '平均铳番', value: avgFangchongScore },
    { label: '一位率', value: firstPlaceRate },
    { label: '二位率', value: secondPlaceRate },
    { label: '三位率', value: thirdPlaceRate },
    { label: '四位率', value: fourthPlaceRate }
  ]
}

// 获取番种值
const getFanValue = (fanKey) => {
  if (!totalStats.value || !totalStats.value.fan_stats) return 0
  return totalStats.value.fan_stats[fanKey] || 0
}

// 切换规则
const switchRule = (rule) => {
  currentRule.value = rule
}

// 查询玩家数据
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
    // 获取玩家信息
    const infoResponse = await axios.get(`/api/player/info/${userId}`)
    if (infoResponse.data.success) {
      playerInfo.value = infoResponse.data.data
    } else {
      ElMessage.error(infoResponse.data.message || '获取玩家信息失败')
      playerInfo.value = null
    }

    // 获取对局记录
    const recordsResponse = await axios.get(`/api/player/records/${userId}`, {
      params: { limit: 20 }
    })
    if (recordsResponse.data.success) {
      gameRecords.value = recordsResponse.data.data || []
    } else {
      ElMessage.warning(recordsResponse.data.message || '获取对局记录失败')
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

// 重置表单
const resetForm = () => {
  searchForm.playerId = ''
  playerInfo.value = null
  gameRecords.value = []
  searched.value = false
  currentRule.value = 'GB'
}

// 格式化日期
const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
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

.data-container {
  text-align: center;
}

.data-title {
  font-size: 2.5rem;
  margin-bottom: 10px;
  font-weight: bold;
  text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
}

.data-description {
  font-size: 1.1rem;
  margin-bottom: 30px;
  opacity: 0.9;
}

.search-section {
  margin-bottom: 30px;
}

.search-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
}

.search-card :deep(.el-card__body) {
  background: transparent;
}

.search-card :deep(.el-form-item__label) {
  color: white;
}

.search-card :deep(.el-input__wrapper) {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
}

.search-card :deep(.el-input__inner) {
  color: white;
}

.data-display {
  margin-top: 30px;
  text-align: left;
}

.info-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
  margin-bottom: 20px;
}

.info-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
  color: white;
}

.info-card :deep(.el-card__body) {
  background: transparent;
}

.user-info {
  display: flex;
  gap: 30px;
  flex-wrap: wrap;
}

.info-item {
  display: flex;
  align-items: center;
}

.info-item .label {
  font-weight: bold;
  margin-right: 10px;
}

.info-item .value {
  color: #ffd04b;
}

.rule-switch {
  margin: 20px 0;
  text-align: center;
}

.stats-section {
  margin-top: 20px;
}

.stats-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
  margin-bottom: 20px;
}

.stats-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
  color: white;
  font-weight: bold;
}

.stats-card :deep(.el-card__body) {
  background: transparent;
}

.stats-content {
  padding: 10px 0;
}

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

.stat-label {
  font-size: 0.9rem;
  opacity: 0.8;
  margin-bottom: 5px;
}

.stat-value {
  font-size: 1.2rem;
  font-weight: bold;
  color: #ffd04b;
}

.fan-stats-content {
  padding: 10px 0;
  max-height: 500px;
  overflow-y: auto;
}

.fan-stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 10px;
}

.fan-stat-item {
  padding: 8px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 6px;
  display: flex;
  justify-content: space-between;
  font-size: 0.9rem;
}

.fan-name {
  opacity: 0.9;
}

.fan-value {
  font-weight: bold;
  color: #ffd04b;
}

.records-card {
  margin-top: 30px;
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
}

.records-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
  color: white;
}

.records-card :deep(.el-card__body) {
  background: transparent;
}

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

.rank-1 {
  background: #ffd700;
  color: #000;
}

.rank-2 {
  background: #c0c0c0;
  color: #000;
}

.rank-3 {
  background: #cd7f32;
  color: #fff;
}

.rank-4 {
  background: #666;
  color: #fff;
}

.player-name {
  flex: 1;
}

.player-score {
  font-weight: bold;
  min-width: 60px;
  text-align: right;
}

.player-score.positive {
  color: #67c23a;
}

.player-score.negative {
  color: #f56c6c;
}

.no-data,
.no-stats,
.no-records {
  margin-top: 50px;
}

.no-data :deep(.el-empty__description),
.no-stats :deep(.el-empty__description),
.no-records :deep(.el-empty__description) {
  color: white;
}

@media (max-width: 768px) {
  .data-title {
    font-size: 2rem;
  }
  
  .stats-grid {
    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  }
  
  .fan-stats-grid {
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  }
}
</style>
