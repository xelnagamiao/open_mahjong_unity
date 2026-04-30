<!-- 首页 -->
<template>
  <div class="home">
    <div class="hero-section">
      <h1 class="hero-title">欢迎访问 salasasa.cn</h1>
      <p class="hero-subtitle">一个简易的麻将服务网站，欢迎加入玩家 QQ 群 <strong>906497522</strong> 寻找雀友与交流</p>
    </div>

    <div class="features-grid">
      <el-card
        v-for="feature in features"
        :key="feature.id"
        class="feature-card"
        :body-style="{ padding: '0px' }"
        @click="handleFeatureClick(feature)"
      >
        <div class="card-content" :style="{ background: feature.gradient || feature.color }">
          <div class="card-icon">
            <el-icon :size="48">
              <component :is="feature.icon" />
            </el-icon>
          </div>
          <div class="card-text">
            <h3 v-html="feature.title"></h3>
            <p>{{ feature.description }}</p>
          </div>
        </div>
      </el-card>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  DataAnalysis,
  Trophy,
  VideoPlay,
  Document,
  Link,
  User
} from '@element-plus/icons-vue'

const router = useRouter()

const features = ref([
  {
    id: 1,
    title: '麻将对战平台',
    description: 'open_mahjong_unity 是基于 Unity 开发的麻将对战平台，目前支持国标、青雀和古典麻将。',
    icon: VideoPlay,
    gradient: 'linear-gradient(135deg, #007bff 0%, #1e3c72 100%)',
    type: 'route',
    route: '/game-unity'
  },
  {
    id: 2,
    title: '玩家数据统计',
    description: '查询玩家在国标、立直、青雀、古典等规则下的胜率、得分、对战次数等数据。',
    icon: User,
    gradient: 'linear-gradient(135deg, #ff5858 0%, #f09819 100%)',
    type: 'route',
    route: '/player-data'
  },
  {
    id: 3,
    title: '听牌待牌判断',
    description: '输入手牌与副露，自动给出听牌状态及全部待牌（基于国标麻将听牌算法）。',
    icon: DataAnalysis,
    gradient: 'linear-gradient(135deg, #11998e 0%, #38ef7d 100%)',
    type: 'route',
    route: '/shanten'
  },
  {
    id: 4,
    title: '国标麻将牌型解算',
    description: '输入手牌、副露、和牌张和和牌方式，计算番种、得分以及全部和牌拆解形态。',
    icon: Trophy,
    gradient: 'linear-gradient(135deg, #f7971e 0%, #ffd200 100%)',
    type: 'route',
    route: '/chinese'
  },
  {
    id: 5,
    title: '开发手册',
    description: '查看开发文档，了解协议结构与如何设计自定义麻将规则。',
    icon: Document,
    gradient: 'linear-gradient(135deg, #2c3e50 0%, #4ca1af 100%)',
    type: 'route',
    route: '/docs'
  },
  {
    id: 6,
    title: 'GitHub 项目',
    description: '前往 GitHub 仓库查看源码、提交 issue 或参与共建。',
    icon: Link,
    gradient: 'linear-gradient(135deg, #6699cc 0%, #2c5364 100%)',
    type: 'route',
    route: '/github'
  }
])

const handleFeatureClick = (feature) => {
  if (feature.type === 'route') {
    router.push(feature.route)
  } else if (feature.type === 'external') {
    window.open(feature.url, '_blank')
  }
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
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
}

.hero-subtitle {
  font-size: 1.2rem;
  opacity: 0.9;
  max-width: 720px;
  margin: 0 auto;
}
.hero-subtitle strong { color: #ffd04b; }

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
  gap: 30px;
  margin-bottom: 60px;
}

.feature-card {
  cursor: pointer;
  transition: transform 0.3s ease, box-shadow 0.3s ease;
  border: none;
  border-radius: 16px;
  overflow: hidden;
}

.feature-card:hover {
  transform: translateY(-8px);
  box-shadow: 0 16px 36px rgba(0, 0, 0, 0.25);
}

.card-content {
  padding: 40px 32px;
  color: white;
  text-align: center;
  min-height: 220px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
}

.card-icon { margin-bottom: 18px; }

.card-text h3 {
  font-size: 1.5rem;
  margin-bottom: 14px;
  font-weight: bold;
}

.card-text p {
  font-size: 1rem;
  line-height: 1.6;
  opacity: 0.92;
}

@media (max-width: 768px) {
  .hero-title { font-size: 2rem; }
  .features-grid { grid-template-columns: 1fr; }
  .card-content { padding: 28px 20px; min-height: 180px; }
}
</style>
