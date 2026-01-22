<template>
  <!-- 首页 欢迎词  -->
  <div class="home">
    <div class="hero-section">
      <h1 class="hero-title">欢迎访问salasasa.cn</h1>
      <p class="hero-subtitle">一个简易的麻将服务网站 欢迎加入玩家qq群906497522寻找雀友与交流</p>
    </div>
    <!-- 功能 -->
    <div class="features-grid">
      <el-card 
        v-for="feature in features" 
        :key="feature.id"
        class="feature-card"
        :body-style="{ padding: '0px' }"
        @click="handleFeatureClick(feature)"
      >
        <div class="card-content" :style="{ backgroundColor: feature.color }">
          <div class="card-icon">
            <el-icon :size="48">
              <component :is="feature.icon" />
            </el-icon>
          </div>
             <div class="card-text">
             <h3 v-html="feature.title.replace('\n', '<br>')"></h3>
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
  Star,
  VideoPlay,
  Document,
  Link
} from '@element-plus/icons-vue'

const router = useRouter()

const features = ref([
  {
    id: 1,
    title: '对战平台',
    description: 'open_mahjong_unity是基于Unity开发的麻将对战平台,目前仅支持国标规则',
    icon: 'VideoPlay',
    color: '#007bff', /* 蓝色 */
    type: 'route',
    route: '/game-unity'
  },
  {
    id: 2,
    title: '开发手册',
    description: '查看开发文档,设计自定义的麻将规则',
    icon: 'Document',
    color: '#00b300', /* 绿色 */
    type: 'route',
    route: '/docs'
  },
  {
    id: 3,
    title: '玩家数据统计',
    description: '查询玩家的胜率、得分、对战次数等数据',
    icon: 'Link',
    color: '#ff0000', /* 红色 */ 
    type: 'route',
    route: '/player-data'
  },
  {
    id: 4,
    title: 'GitHub项目',
    description: '转至GitHub页面',
    icon: 'Link',
    color: '#6699CC', /* 灰色 */ 
    type: 'route',
    route: '/github'
  },
  {
    id: 5,
    title: '听牌待牌判断(未启用)',
    description: '分析手牌是否听牌，以及听牌手牌的待牌',
    icon: 'DataAnalysis',
    color: '#4ECDC4',
    type: 'route',
    route: '/shanten'
  },
  {
    id: 6,
    title: '国标麻将牌型解算(未启用)',
    description: '根据您输入的手牌、副露、花牌、和牌方式计算出可能的和牌构成与他家支付的点数',
    icon: 'Trophy',
    color: '#45B7D1',
    type: 'route',
    route: '/chinese'
  },
  {
    id: 7,
    title: '立直麻将牌型解算(未启用)',
    description: '根据您输入的手牌、副露、宝牌、和牌方式计算出可能的和牌构成与他家支付的点数',
    icon: 'Star',
    color: '#FF8C42',
    type: 'route',
    route: '/riichi'
  }
])

const handleFeatureClick = (feature) => {
  if (feature.type === 'route') {
    // 检查是否为外部链接路由
    if (feature.route === '/game-unity' || feature.route === '/docs' || feature.route === '/github') {
      // 这些路由会在beforeEnter中处理，在新窗口打开
      router.push(feature.route)
    } else {
      // 普通路由正常跳转
      router.push(feature.route)
    }
  } else if (feature.type === 'external') {
    if (feature.url.startsWith('http')) {
      // 外部链接，在新窗口打开
      window.open(feature.url, '_blank')
    } else {
      // 内部静态页面 - 在新窗口打开
      window.open(feature.url, '_blank')
    }
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