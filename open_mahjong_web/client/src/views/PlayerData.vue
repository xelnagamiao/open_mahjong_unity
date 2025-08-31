<template>
  <div class="player-data">
    <div class="data-container">
      <h1 class="data-title">玩家数据统计</h1>
      <p class="data-description">查询玩家的胜率、得分、对战次数等数据</p>
      
      <!-- 搜索区域 -->
      <div class="search-section">
        <el-card class="search-card">
          <el-form :model="searchForm" label-width="100px">
            <el-form-item label="玩家ID">
              <el-input 
                v-model="searchForm.playerId" 
                placeholder="请输入玩家ID"
                clearable
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="searchPlayer">查询数据</el-button>
              <el-button @click="resetForm">重置</el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </div>
      
      <!-- 数据展示区域 -->
      <div v-if="playerData" class="data-display">
        <el-row :gutter="20">
          <el-col :span="6">
            <el-card class="stat-card">
              <div class="stat-content">
                <h3>{{ playerData.totalGames }}</h3>
                <p>总对战次数</p>
              </div>
            </el-card>
          </el-col>
          <el-col :span="6">
            <el-card class="stat-card">
              <div class="stat-content">
                <h3>{{ playerData.winRate }}%</h3>
                <p>胜率</p>
              </div>
            </el-card>
          </el-col>
          <el-col :span="6">
            <el-card class="stat-card">
              <div class="stat-content">
                <h3>{{ playerData.totalScore }}</h3>
                <p>总得分</p>
              </div>
            </el-card>
          </el-col>
          <el-col :span="6">
            <el-card class="stat-card">
              <div class="stat-content">
                <h3>{{ playerData.rank }}</h3>
                <p>当前排名</p>
              </div>
            </el-card>
          </el-col>
        </el-row>
        
        <!-- 详细数据表格 -->
        <el-card class="detail-card">
          <template #header>
            <span>详细对战记录</span>
          </template>
          <el-table :data="playerData.gameHistory" stripe>
            <el-table-column prop="date" label="日期" width="120" />
            <el-table-column prop="opponent" label="对手" width="120" />
            <el-table-column prop="result" label="结果" width="80">
              <template #default="scope">
                <el-tag :type="scope.row.result === '胜' ? 'success' : 'danger'">
                  {{ scope.row.result }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="score" label="得分" width="80" />
            <el-table-column prop="duration" label="时长" width="100" />
          </el-table>
        </el-card>
      </div>
      
      <!-- 无数据提示 -->
      <div v-else-if="searched" class="no-data">
        <el-empty description="未找到该玩家的数据" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue'
import { ElMessage } from 'element-plus'

const searchForm = reactive({
  playerId: ''
})

const playerData = ref(null)
const searched = ref(false)

const searchPlayer = () => {
  if (!searchForm.playerId) {
    ElMessage.warning('请输入玩家ID')
    return
  }
  
  // 模拟API调用
  searched.value = true
  setTimeout(() => {
    // 模拟数据
    playerData.value = {
      playerId: searchForm.playerId,
      totalGames: 156,
      winRate: 68.5,
      totalScore: 2840,
      rank: 15,
      gameHistory: [
        { date: '2024-01-15', opponent: '玩家A', result: '胜', score: 120, duration: '45分钟' },
        { date: '2024-01-14', opponent: '玩家B', result: '负', score: -80, duration: '38分钟' },
        { date: '2024-01-13', opponent: '玩家C', result: '胜', score: 95, duration: '52分钟' },
        { date: '2024-01-12', opponent: '玩家D', result: '胜', score: 150, duration: '41分钟' },
        { date: '2024-01-11', opponent: '玩家E', result: '负', score: -60, duration: '35分钟' }
      ]
    }
  }, 1000)
}

const resetForm = () => {
  searchForm.playerId = ''
  playerData.value = null
  searched.value = false
}
</script>

<style scoped>
.player-data {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
  color: white;
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
}

.stat-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
  text-align: center;
}

.stat-card :deep(.el-card__body) {
  background: transparent;
}

.stat-content h3 {
  font-size: 2rem;
  margin: 0;
  color: #ff0000;
  font-weight: bold;
}

.stat-content p {
  margin: 5px 0 0 0;
  opacity: 0.8;
}

.detail-card {
  margin-top: 30px;
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  border: none;
  border-radius: 15px;
  color: white;
}

.detail-card :deep(.el-card__header) {
  background: transparent;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
  color: white;
}

.detail-card :deep(.el-card__body) {
  background: transparent;
}

.detail-card :deep(.el-table) {
  background: transparent;
  color: white;
}

.detail-card :deep(.el-table th) {
  background: rgba(255, 255, 255, 0.1);
  color: white;
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
}

.detail-card :deep(.el-table td) {
  background: transparent;
  color: white;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.detail-card :deep(.el-table--striped .el-table__body tr.el-table__row--striped td) {
  background: rgba(255, 255, 255, 0.05);
}

.no-data {
  margin-top: 50px;
}

.no-data :deep(.el-empty__description) {
  color: white;
}

@media (max-width: 768px) {
  .data-title {
    font-size: 2rem;
  }
  
  .stat-content h3 {
    font-size: 1.5rem;
  }
}
</style> 