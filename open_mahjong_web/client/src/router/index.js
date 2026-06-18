import { createRouter, createWebHistory } from 'vue-router'
import { useAdminAuthStore } from '@/stores/adminAuth'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import Home from '@/views/Home.vue'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import PlayerData from '@/views/PlayerData.vue'
import UnityGame from '@/views/UnityGame.vue'
import Rulebook from '@/views/Rulebook.vue'
import Paili from '@/views/Paili.vue'
import SeedVerify from '@/views/SeedVerify.vue'
import MobileDownload from '@/views/MobileDownload.vue'
import AdminLayout from '@/layouts/AdminLayout.vue'
import AdminLogin from '@/views/admin/Login.vue'
import AdminDashboard from '@/views/admin/Dashboard.vue'
import AdminUsers from '@/views/admin/Users.vue'
import AdminUserDetail from '@/views/admin/UserDetail.vue'
import AdminRank from '@/views/admin/Rank.vue'
import AdminGames from '@/views/admin/Games.vue'
import AdminAudit from '@/views/admin/Audit.vue'
import AdminMessages from '@/views/admin/Messages.vue'

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
        redirect: '/paili'
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
        meta: { title: '国标计算器 - salasasa.cn' }
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
      },
      {
        path: 'seed-verify',
        name: 'SeedVerify',
        component: SeedVerify,
        meta: { title: '随机种子验证 - salasasa.cn' }
      },
      {
        path: 'mobile-download',
        name: 'MobileDownload',
        component: MobileDownload,
        meta: { title: '手机版下载 - salasasa.cn' }
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
  },
  {
    path: '/admin/login',
    name: 'AdminLogin',
    component: AdminLogin,
    meta: { title: '管理后台登录', publicAdmin: true }
  },
  {
    path: '/admin',
    component: AdminLayout,
    meta: { requiresAdmin: true },
    children: [
      { path: '', name: 'AdminDashboard', component: AdminDashboard, meta: { title: '管理仪表盘' } },
      { path: 'users', name: 'AdminUsers', component: AdminUsers, meta: { title: '用户管理' } },
      {
        path: 'users/:userId',
        name: 'AdminUserDetail',
        component: AdminUserDetail,
        meta: { title: '用户详情' }
      },
      { path: 'rank', name: 'AdminRank', component: AdminRank, meta: { title: '段位管理' } },
      { path: 'games', name: 'AdminGames', component: AdminGames, meta: { title: '对局管理' } },
      { path: 'audit', name: 'AdminAudit', component: AdminAudit, meta: { title: '操作审计' } },
      { path: 'messages', name: 'AdminMessages', component: AdminMessages, meta: { title: '消息推送' } }
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach(async (to, from, next) => {
  if (to.meta.title) {
    document.title = to.meta.title
  }

  if (to.path.startsWith('/admin')) {
    const auth = useAdminAuthStore()
    if (to.meta.publicAdmin) {
      if (!auth.loaded) await auth.fetchMe()
      if (auth.isLoggedIn) return next('/admin')
      return next()
    }
    if (!auth.loaded) await auth.fetchMe()
    if (!auth.isLoggedIn) return next({ path: '/admin/login', query: { redirect: to.fullPath } })
  }

  next()
})

export default router
