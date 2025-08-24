<template>
  <div class="home">
    <div class="hero-section">
      <h1 class="hero-title">欢迎来到 Mahjong.fit</h1>
      <p class="hero-subtitle">专业的麻将分析工具，助您提升麻将技巧</p>
    </div>
    
    <div class="features-grid">
      <el-card 
        v-for="feature in features" 
        :key="feature.id"
        class="feature-card"
        :body-style="{ padding: '0px' }"
        @click="navigateTo(feature.route)"
      >
        <div class="card-content" :style="{ backgroundColor: feature.color }">
          <div class="card-icon">
            <el-icon :size="48">
              <component :is="feature.icon" />
            </el-icon>
          </div>
          <div class="card-text">
            <h3>{{ feature.title }}</h3>
            <p>{{ feature.description }}</p>
          </div>
        </div>
      </el-card>
    </div>
    
    <div class="stats-section">
      <el-row :gutter="20">
        <el-col :span="8">
          <el-card class="stat-card">
            <div class="stat-content">
              <el-icon :size="32" color="#409EFF">
                <DataAnalysis />
              </el-icon>
              <div class="stat-info">
                <h3>{{ stats.analyses }}</h3>
                <p>累计分析次数</p>
              </div>
            </div>
          </el-card>
        </el-col>
        <el-col :span="8">
          <el-card class="stat-card">
            <div class="stat-content">
              <el-icon :size="32" color="#67C23A">
                <User />
              </el-icon>
              <div class="stat-info">
                <h3>{{ stats.users }}</h3>
                <p>注册用户数</p>
              </div>
            </div>
          </el-card>
        </el-col>
        <el-col :span="8">
          <el-card class="stat-card">
            <div class="stat-content">
              <el-icon :size="32" color="#E6A23C">
                <Clock />
              </el-icon>
              <div class="stat-info">
                <h3>{{ stats.uptime }}</h3>
                <p>服务运行时间</p>
              </div>
            </div>
          </el-card>
        </el-col>
      </el-row>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  DataAnalysis,
  Trophy,
  Star,
  User,
  Clock
} from '@element-plus/icons-vue'

const router = useRouter()

const features = ref([
  {
    id: 1,
    title: '听牌待牌判断',
    description: '分析手牌是否听牌，以及听牌手牌的待牌',
    icon: 'DataAnalysis',
    color: '#4ECDC4',
    route: '/shanten'
  },
  {
    id: 2,
    title: '国标麻将牌型解算',
    description: '根据您输入的手牌、副露、花牌、和牌方式计算出可能的和牌构成与他家支付的点数',
    icon: 'Trophy',
    color: '#45B7D1',
    route: '/chinese'
  },
  {
    id: 3,
    title: '立直麻将牌型解算',
    description: '根据您输入的手牌、副露、宝牌、和牌方式计算出可能的和牌构成与他家支付的点数',
    icon: 'Star',
    color: '#FF8C42',
    route: '/riichi'
  }
])

const stats = ref({
  analyses: '10,000+',
  users: '1,000+',
  uptime: '99.9%'
})

const navigateTo = (route) => {
  router.push(route)
}
</script>

<style scoped>
.home {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.hero-section {
  text-align: center;
  margin-bottom: 60px;
  color: white;
}

.hero-title {
  font-size: 3rem;
  margin-bottom: 20px;
  font-weight: bold;
  text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
}

.hero-subtitle {
  font-size: 1.2rem;
  opacity: 0.9;
  max-width: 600px;
  margin: 0 auto;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
  gap: 30px;
  margin-bottom: 60px;
}

.feature-card {
  cursor: pointer;
  transition: transform 0.3s ease, box-shadow 0.3s ease;
  border: none;
  border-radius: 15px;
  overflow: hidden;
}

.feature-card:hover {
  transform: translateY(-10px);
  box-shadow: 0 20px 40px rgba(0,0,0,0.2);
}

.card-content {
  padding: 40px;
  color: white;
  text-align: center;
  min-height: 200px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
}

.card-icon {
  margin-bottom: 20px;
}

.card-text h3 {
  font-size: 1.5rem;
  margin-bottom: 15px;
  font-weight: bold;
}

.card-text p {
  font-size: 1rem;
  line-height: 1.6;
  opacity: 0.9;
}

.stats-section {
  margin-top: 60px;
}

.stat-card {
  text-align: center;
  border: none;
  border-radius: 15px;
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(10px);
  color: white;
}

.stat-content {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
}

.stat-info {
  margin-left: 15px;
  text-align: left;
}

.stat-info h3 {
  font-size: 2rem;
  margin: 0;
  font-weight: bold;
  color: #409EFF;
}

.stat-info p {
  margin: 5px 0 0 0;
  opacity: 0.8;
}

@media (max-width: 768px) {
  .hero-title {
    font-size: 2rem;
  }
  
  .features-grid {
    grid-template-columns: 1fr;
  }
  
  .card-content {
    padding: 30px;
    min-height: 150px;
  }
}
</style> 