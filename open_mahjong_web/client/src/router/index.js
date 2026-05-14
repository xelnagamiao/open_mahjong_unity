import { createRouter, createWebHistory } from 'vue-router'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import Home from '@/views/Home.vue'
import ShantenAnalysis from '@/views/ShantenAnalysis.vue'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import PlayerData from '@/views/PlayerData.vue'
import UnityGame from '@/views/UnityGame.vue'
import Rulebook from '@/views/Rulebook.vue'
import Paili from '@/views/Paili.vue'

const routes = [
  // 含布局（顶部导航 + 底部）
  {
    path: '/',
    component: DefaultLayout,
    children: [
      {
        path: '',
        name: 'Home',
        component: Home,
        meta: { title: '欢迎访问 salasasa.cn' }
      },
      {
        path: 'shanten',
        name: 'ShantenAnalysis',
        component: ShantenAnalysis,
        meta: { title: '听牌待牌判断 - salasasa.cn' }
      },
      {
        path: 'paili',
        name: 'Paili',
        component: Paili,
        meta: { title: '牌理 - salasasa.cn' }
      },
      {
        path: 'chinese',
        name: 'ChineseMahjong',
        component: ChineseMahjong,
        meta: { title: '国标麻将牌型解算 - salasasa.cn' }
      },
      {
        path: 'player-data',
        name: 'PlayerData',
        component: PlayerData,
        meta: { title: '玩家数据统计 - salasasa.cn' }
      },
      {
        path: 'rulebook/:rule?',
        name: 'Rulebook',
        component: Rulebook,
        meta: { title: '规则书 - salasasa.cn' }
      }
    ]
  },
  {
    path: '/game-unity',
    name: 'UnityGame',
    component: UnityGame,
    meta: { title: '麻将对战平台 - salasasa.cn' }
  },
  {
    path: '/docs',
    name: 'Docs',
    beforeEnter: () => {
      window.open('https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc# 《open》', '_blank')
      return false
    },
    meta: { title: '开发手册 - salasasa.cn' }
  },
  {
    path: '/github',
    name: 'GitHub',
    beforeEnter: () => {
      window.open('https://github.com/xelnagamiao/open_mahjong_unity', '_blank')
      return false
    },
    meta: { title: 'GitHub 项目 - salasasa.cn' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, from, next) => {
  if (to.meta.title) {
    document.title = to.meta.title
  }
  next()
})

export default router
