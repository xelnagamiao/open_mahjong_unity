import { createRouter, createWebHistory } from 'vue-router'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import Home from '@/views/Home.vue'
import ShantenAnalysis from '@/views/ShantenAnalysis.vue'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import RiichiMahjong from '@/views/RiichiMahjong.vue'
import PlayerData from '@/views/PlayerData.vue'
import UnityGame from '@/views/UnityGame.vue'

// 注册路由
const routes = [
  // 使用默认布局的路由（有头部和尾部）
  {
    path: '/',
    component: DefaultLayout,
    children: [
      {
        path: '',
        name: 'Home',
        component: Home,
        meta: { title: '首页 - salasasa.cn' }
      },
      {
        path: 'shanten',
        name: 'ShantenAnalysis',
        component: ShantenAnalysis,
        meta: { title: '听牌待牌判断 - salasasa.cn' }
      },
      {
        path: 'chinese',
        name: 'ChineseMahjong',
        component: ChineseMahjong,
        meta: { title: '国标麻将牌型解算 - salasasa.cn' }
      },
      {
        path: 'riichi',
        name: 'RiichiMahjong',
        component: RiichiMahjong,
        meta: { title: '立直麻将牌型解算 - salasasa.cn' }
      },
      {
        path: 'player-data',
        name: 'PlayerData',
        component: PlayerData,
        meta: { title: '玩家数据统计 - salasasa.cn' }
      }
    ]
  },
  // 无布局的路由（纯黑全屏页面）
  {
    path: '/game-unity',
    name: 'UnityGame',
    component: UnityGame,
    meta: { title: '麻将对战平台 - salasasa.cn' }
  },
  // 外部链接路由
  {
    path: '/docs',
    name: 'Docs',
    beforeEnter: (to, from, next) => {
      // 在新窗口打开开发手册页面
      window.open('https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc# 《open》', '_blank')
    },
    meta: { title: '开发手册 - salasasa.cn' }
  },
  {
    path: '/github',
    name: 'GitHub',
    beforeEnter: (to, from, next) => {
      // 在新窗口打开GitHub页面
      window.open('https://github.com/xelnagamiao/open_mahjong_unity', '_blank')
    },
    meta: { title: 'GitHub项目 - salasasa.cn' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// 路由守卫 - 设置页面标题
router.beforeEach((to, from, next) => {
  if (to.meta.title) {
    document.title = to.meta.title
  }
  next()
})

export default router 