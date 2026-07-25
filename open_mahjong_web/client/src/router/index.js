import { createRouter, createWebHistory } from 'vue-router'
import { useAdminAuthStore } from '@/stores/adminAuth'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import PlayerDataLayout from '@/layouts/PlayerDataLayout.vue'
import AccountLayout from '@/layouts/AccountLayout.vue'
import Home from '@/views/Home.vue'
import Login from '@/views/Login.vue'
import Account from '@/views/Account.vue'
import EventsList from '@/views/EventsList.vue'
import EventDetail from '@/views/EventDetail.vue'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import PlayerData from '@/views/PlayerData.vue'
import PlatformData from '@/views/PlatformData.vue'
import UnityGame from '@/views/UnityGame.vue'
import Rulebook from '@/views/Rulebook.vue'
import Library from '@/views/Library.vue'
import LibraryRule from '@/views/LibraryRule.vue'
import LibraryLayout from '@/layouts/LibraryLayout.vue'
import RuleResearch from '@/views/RuleResearch.vue'
import Paili from '@/views/Paili.vue'
import SeedVerify from '@/views/SeedVerify.vue'
import MobileDownload from '@/views/MobileDownload.vue'
import UsageGuide from '@/views/UsageGuide.vue'
import GuessFanApp from '@/views/guess-fan/GuessFanApp.vue'
import RecordConvert from '@/views/RecordConvert.vue'
import AdminLayout from '@/layouts/AdminLayout.vue'
import AdminLogin from '@/views/admin/Login.vue'
import AdminDashboard from '@/views/admin/Dashboard.vue'
import AdminUsers from '@/views/admin/Users.vue'
import AdminUserDetail from '@/views/admin/UserDetail.vue'
import AdminRank from '@/views/admin/Rank.vue'
import AdminGames from '@/views/admin/Games.vue'
import AdminGameControl from '@/views/admin/GameControl.vue'
import AdminAudit from '@/views/admin/Audit.vue'
import AdminMessages from '@/views/admin/Messages.vue'
import AdminSendEmail from '@/views/admin/SendEmail.vue'
import AdminIpBans from '@/views/admin/IpBans.vue'
import AdminStats from '@/views/admin/Stats.vue'
import AdminEvents from '@/views/admin/Events.vue'
import AdminEventDetail from '@/views/admin/EventDetail.vue'
import AdminEventApplications from '@/views/admin/EventApplications.vue'
import EventAdminLayout from '@/layouts/EventAdminLayout.vue'
import EventAdminLogin from '@/views/event-admin/Login.vue'
import EventAdminEvents from '@/views/event-admin/Events.vue'
import EventAdminEventDetail from '@/views/event-admin/EventDetail.vue'

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
        path: 'login',
        name: 'Login',
        component: Login,
        meta: { title: '玩家登录 - salasasa.cn' }
      },
      {
        path: 'events',
        name: 'EventsList',
        component: EventsList,
        meta: { title: '比赛 - salasasa.cn' }
      },
      {
        path: 'events/:eventId',
        name: 'EventDetail',
        component: EventDetail,
        meta: { title: '比赛详情 - salasasa.cn' }
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
      },
      {
        path: 'guide',
        name: 'UsageGuide',
        component: UsageGuide,
        meta: { title: '使用说明 - salasasa.cn' }
      },
      {
        path: 'guess-fan/:playMode(solo|play)?',
        name: 'GuessFan',
        component: GuessFanApp,
        meta: { title: '猜番对抗 - salasasa.cn', requiresPlayer: true }
      },
      {
        path: 'tools/record-convert',
        name: 'RecordConvert',
        component: RecordConvert,
        meta: { title: '牌谱格式转换 - salasasa.cn' }
      }
    ]
  },
  {
    path: '/library',
    component: LibraryLayout,
    children: [
      {
        path: '',
        name: 'Library',
        component: Library,
        meta: { title: '麻雀图书馆' }
      },
      {
        path: ':rule',
        name: 'LibraryRule',
        component: LibraryRule,
        meta: { title: '麻雀图书馆' }
      }
    ]
  },
  {
    path: '/rule-research',
    component: LibraryLayout,
    children: [
      {
        path: '',
        name: 'RuleResearchIndex',
        component: RuleResearch,
        meta: { title: '规则资料搜集 - salasasa.cn' }
      },
      {
        path: ':slug',
        name: 'RuleResearch',
        component: RuleResearch,
        meta: { title: '规则资料搜集 - salasasa.cn' }
      }
    ]
  },
  {
    path: '/account',
    component: AccountLayout,
    children: [
      {
        path: '',
        name: 'Account',
        component: Account,
        meta: { title: '账户面板 - salasasa.cn' }
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
    path: '/2d',
    name: 'Game2DLobby',
    component: () => import('@/views/game2d/Lobby.vue'),
    meta: { title: 'Salasasa 2D 国标麻将' }
  },
  {
    path: '/2d/game',
    name: 'Game2D',
    component: () => import('@/views/game2d/Game.vue'),
    meta: { title: 'Salasasa 2D 国标对局' }
  },
  {
    path: '/2d/player/:id',
    name: 'Game2DPlayer',
    component: () => import('@/views/game2d/Player.vue'),
    meta: { title: 'Salasasa 2D 玩家资料' }
  },
  {
    path: '/player-data',
    component: PlayerDataLayout,
    children: [
      {
        path: '',
        name: 'PlayerData',
        component: PlayerData,
        meta: { title: '玩家数据统计 - salasasa.cn' }
      },
      {
        path: 'platform',
        name: 'PlatformData',
        component: PlatformData,
        meta: { title: '平台数据统计 - salasasa.cn' }
      }
    ]
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
      { path: 'events', name: 'AdminEvents', component: AdminEvents, meta: { title: '赛事管理' } },
      {
        path: 'events/:eventId',
        name: 'AdminEventDetail',
        component: AdminEventDetail,
        meta: { title: '赛事详情' }
      },
      {
        path: 'event-applications',
        name: 'AdminEventApplications',
        component: AdminEventApplications,
        meta: { title: '办赛申请' }
      },
      { path: 'rank', name: 'AdminRank', component: AdminRank, meta: { title: '段位管理' } },
      { path: 'games', name: 'AdminGames', component: AdminGames, meta: { title: '对局记录管理' } },
      { path: 'game-control', name: 'AdminGameControl', component: AdminGameControl, meta: { title: '对局管理' } },
      { path: 'audit', name: 'AdminAudit', component: AdminAudit, meta: { title: '操作审计' } },
      { path: 'messages', name: 'AdminMessages', component: AdminMessages, meta: { title: '消息推送' } },
      { path: 'send-email', name: 'AdminSendEmail', component: AdminSendEmail, meta: { title: '发送邮件' } },
      { path: 'ip-bans', name: 'AdminIpBans', component: AdminIpBans, meta: { title: 'IP 封禁' } },
      { path: 'stats', name: 'AdminStats', component: AdminStats, meta: { title: '全站统计' } }
    ]
  },
  {
    path: '/event-admin/login',
    name: 'EventAdminLogin',
    component: EventAdminLogin,
    meta: { title: '比赛管理后台登录', publicEventAdmin: true }
  },
  {
    path: '/event-admin',
    component: EventAdminLayout,
    meta: { requiresEventAdmin: true },
    children: [
      {
        path: '',
        name: 'EventAdminEvents',
        component: EventAdminEvents,
        meta: { title: '我的赛事' }
      },
      {
        path: 'events/:eventId',
        name: 'EventAdminEventDetail',
        component: EventAdminEventDetail,
        meta: { title: '赛事管理' }
      }
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior(to, _from, savedPosition) {
    if (to.path.startsWith('/2d')) return { top: 0 }
    return savedPosition || false
  },
})

router.beforeEach(async (to, from, next) => {
  if (to.meta.title) {
    document.title = to.meta.title
  }

  if (to.path.startsWith('/admin')) {
    const auth = useAdminAuthStore()
    if (to.meta.publicAdmin) {
      if (!auth.loaded) await auth.fetchMe()
      if (auth.isLoggedIn) return next({ path: '/admin', replace: true })
      return next()
    }
    if (!auth.loaded) await auth.fetchMe()
    if (!auth.isLoggedIn) {
      const redirect =
        to.path === '/admin/login' || to.fullPath.startsWith('/admin/login')
          ? '/admin'
          : to.fullPath
      return next({ path: '/admin/login', query: { redirect }, replace: true })
    }
    return next()
  }

  // 取消独立赛事后台入口：列表/登录并入账户页；保留赛事详情管理路由
  if (to.path === '/event-admin' || to.path === '/event-admin/' || to.path === '/event-admin/login') {
    return next({ path: '/account', hash: '#sec-manage' })
  }

  if (to.path.startsWith('/event-admin')) {
    const auth = useEventAdminAuthStore()
    if (!auth.loaded) await auth.fetchMe()
    if (!auth.isLoggedIn) {
      return next({
        path: '/login',
        query: { redirect: to.fullPath },
      })
    }
    return next()
  }

  if (to.meta.requiresPlayer) {
    const auth = usePlayerAuthStore()
    if (!auth.loaded) await auth.fetchMe()
    if (!auth.isLoggedIn) {
      return next({
        path: '/login',
        query: { redirect: to.fullPath },
      })
    }
  }

  next()
})

export default router
