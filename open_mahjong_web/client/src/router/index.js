import { createRouter, createWebHistory } from 'vue-router'
import Home from '@/views/Home.vue'
import ShantenAnalysis from '@/views/ShantenAnalysis.vue'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import RiichiMahjong from '@/views/RiichiMahjong.vue'
import PlayerData from '@/views/PlayerData.vue'
import Login from '@/views/Login.vue'
import Register from '@/views/Register.vue'

const routes = [
  {
    path: '/',
    name: 'Home',
    component: Home,
    meta: { title: '首页 - Mahjong.fit' }
  },
  {
    path: '/shanten',
    name: 'ShantenAnalysis',
    component: ShantenAnalysis,
    meta: { title: '听牌待牌判断 - Mahjong.fit' }
  },
  {
    path: '/chinese',
    name: 'ChineseMahjong',
    component: ChineseMahjong,
    meta: { title: '国标麻将牌型解算 - Mahjong.fit' }
  },
  {
    path: '/riichi',
    name: 'RiichiMahjong',
    component: RiichiMahjong,
    meta: { title: '立直麻将牌型解算 - Mahjong.fit' }
  },
  {
    path: '/player-data',
    name: 'PlayerData',
    component: PlayerData,
    meta: { title: '玩家数据统计 - Mahjong.fit' }
  },
  {
    path: '/login',
    name: 'Login',
    component: Login,
    meta: { title: '登录 - Mahjong.fit' }
  },
  {
    path: '/register',
    name: 'Register',
    component: Register,
    meta: { title: '注册 - Mahjong.fit' }
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