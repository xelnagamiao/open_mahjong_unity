<template>
  <div class="home">
    <div class="hero-section">
      <h1 class="hero-title">欢迎访问 salasasa.cn</h1>
      <p class="hero-subtitle">
        一个简易的麻将服务网站。欢迎加入玩家 QQ 群 906497522，寻找雀友与交流。
      </p>
    </div>

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
  VideoPlay,
  User,
  Histogram,
  Operation,
  Reading,
  Document,
  Link,
  Key,
  Iphone,
} from '@element-plus/icons-vue'

const router = useRouter()

const features = ref([
  {
    id: 1,
    title: '对战平台',
    description: 'open_mahjong_unity是基于Unity开发的麻将对战平台,目前支持国标/日麻/青雀/古典和一些子规则。',
    icon: VideoPlay,
    color: '#007bff',
    type: 'route',
    route: '/game-unity',
  },
  {
    id: 10,
    title: '手机版下载',
    description: '下载测试版 Android APK，可能会经常更新，可注意群906497522动态',
    icon: Iphone,
    color: '#67c23a',
    type: 'route',
    route: '/mobile-download',
  },
  {
    id: 8,
    title: 'GitHub 项目',
    // 青灰色
    description: '转至GitHub项目页面。',
    icon: Link,
    color: '#6699cc',
    type: 'route',
    route: '/github',
  },
  {
    id: 2,
    title: '玩家数据',
    description: '查询玩家的胜率、得分、对战次数等数据。',
    icon: User,
    // 大红色
    color: '#ff0000',
    type: 'route',
    route: '/player-data',
  },
  {
    id: 3,
    title: '牌理',
    description: '分析手牌是否听牌，以及听牌的向听数或待牌。',
    icon: Histogram,
    color: '#9b59b6',
    type: 'route',
    route: '/paili',
  },
  {
    id: 5,
    title: '国标计算器',
    description: '根据手牌、副露、花牌、和牌方式计算番种、得分与全部和牌拆解形态。',
    icon: Operation,
    color: '#45B7D1',
    type: 'route',
    route: '/chinese',
  },
  {
    id: 6,
    title: '规则书',
    description: '查询国标/立直/青雀/古典等规则的PDF说明书。',
    icon: Reading,
    color: '#a78bfa',
    type: 'route',
    route: '/rulebook',
  },
  {
    id: 9,
    title: '随机种子验证',
    description: '输入对局公布的主种子与盐值，在本地复现随机座位与每局配牌，验证服务器未更换种子。',
    icon: Key,
    color: '#e6a23c',
    type: 'route',
    route: '/seed-verify',
  },
  {
    id: 7,
    title: '开发手册',
    description: '查看开发文档,设计自定义的麻将规则。',
    icon: Document,
    color: '#00b300',
    type: 'route',
    route: '/docs',
  },
])

const handleFeatureClick = (feature) => {
  if (feature.type === 'route') {
    router.push(feature.route)
  } else if (feature.type === 'external') {
    if (feature.url.startsWith('http')) {
      window.open(feature.url, '_blank')
    } else {
      router.push(feature.url)
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
  margin-bottom: 50px;
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
  opacity: 0.95;
  max-width: 720px;
  margin: 0 auto;
  line-height: 1.6;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 28px;
  margin-bottom: 40px;
}

.feature-card {
  cursor: pointer;
  transition: transform 0.3s ease, box-shadow 0.3s ease;
  border: none;
  border-radius: 15px;
  overflow: hidden;
}

.feature-card:hover {
  transform: translateY(-8px);
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.2);
}

.card-content {
  padding: 36px;
  color: white;
  text-align: center;
  min-height: 200px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
}

.card-icon {
  margin-bottom: 18px;
}

.card-text h3 {
  font-size: 1.5rem;
  margin-bottom: 12px;
  font-weight: bold;
}

.card-text p {
  font-size: 1rem;
  line-height: 1.6;
  opacity: 0.95;
}

@media (max-width: 768px) {
  .hero-title {
    font-size: 2rem;
  }

  .features-grid {
    grid-template-columns: 1fr;
  }

  .card-content {
    padding: 28px;
    min-height: 160px;
  }
}
</style>
