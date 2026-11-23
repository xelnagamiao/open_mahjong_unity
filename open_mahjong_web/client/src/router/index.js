import { createRouter, createWebHistory } from 'vue-router'
import { useAdminAuthStore } from '@/stores/adminAuth'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { watch } from 'vue'
import { locale, tr, setLocale } from '@/i18n'
import { requestedGameLocale } from '@/i18n/locale-utils.js'
import { mobileDownloadRedirect } from '@/utils/iosDownload.js'
import { seoEntryFor, noindexEntryFor } from '@/seo'
import { STATIC_PAGES } from '@/seo/pages.js'
import { applyRouteHead } from '@/seo/head.js'
const DefaultLayout = () => import('@/layouts/DefaultLayout.vue')
const PlayerDataLayout = () => import('@/layouts/PlayerDataLayout.vue')
const AccountLayout = () => import('@/layouts/AccountLayout.vue')
const Home = () => import('@/views/Home.vue')
const Login = () => import('@/views/Login.vue')
const Register = () => import('@/views/Register.vue')
const ForgotPassword = () => import('@/views/ForgotPassword.vue')
const Account = () => import('@/views/Account.vue')
const EventsList = () => import('@/views/EventsList.vue')
const EventDetail = () => import('@/views/EventDetail.vue')
const CalculatorView = () => import('@/views/CalculatorView.vue')
const PailiView = () => import('@/views/PailiView.vue')
const PlayerData = () => import('@/views/PlayerData.vue')
const PlatformData = () => import('@/views/PlatformData.vue')
const RecordAnalysis = () => import('@/views/RecordAnalysis.vue')
const FunData = () => import('@/views/FunData.vue')
const UnityGame = () => import('@/views/UnityGame.vue')
const Rulebook = () => import('@/views/Rulebook.vue')
const Library = () => import('@/views/Library.vue')
const LibraryRule = () => import('@/views/LibraryRule.vue')
const LibraryLineage = () => import('@/views/LibraryLineage.vue')
const LibraryLayout = () => import('@/layouts/LibraryLayout.vue')
const RuleResearch = () => import('@/views/RuleResearch.vue')
const SeedVerify = () => import('@/views/SeedVerify.vue')
const MobileDownload = () => import('@/views/MobileDownload.vue')
const UsageGuide = () => import('@/views/UsageGuide.vue')
const Tiles = () => import('@/views/Tiles.vue')
const GuessFanApp = () => import('@/views/guess-fan/GuessFanApp.vue')
const RecordConvert = () => import('@/views/RecordConvert.vue')
const AdminLayout = () => import('@/layouts/AdminLayout.vue')
const AdminLogin = () => import('@/views/admin/Login.vue')
const AdminDashboard = () => import('@/views/admin/Dashboard.vue')
const AdminUsers = () => import('@/views/admin/Users.vue')
const AdminSponsors = () => import('@/views/admin/Sponsors.vue')
const AdminAccountBans = () => import('@/views/admin/AccountBans.vue')
const AdminChatHistory = () => import('@/views/admin/ChatHistory.vue')
const AdminTitles = () => import('@/views/admin/Titles.vue')
const AdminRedemption = () => import('@/views/admin/Redemption.vue')
const AdminInventory = () => import('@/views/admin/Inventory.vue')
const AdminUserDetail = () => import('@/views/admin/UserDetail.vue')
const AdminRank = () => import('@/views/admin/Rank.vue')
const AdminGames = () => import('@/views/admin/Games.vue')
const AdminGameControl = () => import('@/views/admin/GameControl.vue')
const AdminAudit = () => import('@/views/admin/Audit.vue')
const AdminMessages = () => import('@/views/admin/Messages.vue')
const AdminSendEmail = () => import('@/views/admin/SendEmail.vue')
const AdminMailbox = () => import('@/views/admin/Mailbox.vue')
const AdminIpBans = () => import('@/views/admin/IpBans.vue')
const AdminStats = () => import('@/views/admin/Stats.vue')
const AdminEvents = () => import('@/views/admin/Events.vue')
const AdminEventDetail = () => import('@/views/admin/EventDetail.vue')
const AdminActivities = () => import('@/views/admin/Activities.vue')
const AdminClassicRecords = () => import('@/views/admin/ClassicRecords.vue')
const AdminTileContent = () => import('@/views/admin/TileContent.vue')
const EventAdminLayout = () => import('@/layouts/EventAdminLayout.vue')
const EventAdminLogin = () => import('@/views/event-admin/Login.vue')
const EventAdminEvents = () => import('@/views/event-admin/Events.vue')
const EventAdminEventDetail = () => import('@/views/event-admin/EventDetail.vue')

const routes = [
  ...STATIC_PAGES.map((page) => ({
    path: page.path,
    name: `Content-${page.family}-${page.locale}`,
    component: () => import('@/views/McrLanding.vue'),
    meta: { title: page.title },
  })),
  // 含布局（顶部导航 + 底部）
  {
    path: '/',
    component: DefaultLayout,
    children: [
      { path: 'community', name: 'Community', component: () => import('@/views/Community.vue'), meta: { title: '社群' } },
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
        path: 'register',
        name: 'Register',
        component: Register,
        meta: { title: '玩家注册 - salasasa.cn' }
      },
      {
        path: 'forgot-password',
        name: 'ForgotPassword',
        component: ForgotPassword,
        meta: { title: '找回密码 - salasasa.cn', noindex: true }
      },
      {
        path: 'events',
        name: 'EventsList',
        component: EventsList,
        meta: { title: '比赛 / 基地 - salasasa.cn' }
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
        component: PailiView
      },
      {
        path: 'paili/hongque',
        name: 'PailiHongque',
        component: PailiView
      },
      { path: 'paili/qingque', name: 'PailiQingque', component: PailiView },
      { path: 'paili/riichi', name: 'PailiRiichi', component: PailiView },
      {
        path: 'calc/chinese',
        name: 'CalculatorChinese',
        component: CalculatorView
      },
      {
        path: 'calc/hongque',
        name: 'CalculatorHongque',
        component: CalculatorView
      },
      { path: 'calc/qingque', name: 'CalculatorQingque', component: CalculatorView },
      { path: 'calc/riichi', name: 'CalculatorRiichi', component: CalculatorView },
      {
        path: 'calc',
        redirect: '/calc/chinese'
      },
      {
        path: 'chinese',
        redirect: '/calc/chinese'
      },
      {
        path: 'rulebook/new_chinese',
        redirect: to => ({ name: 'Rulebook', params: { rule: 'custom' }, query: { ...to.query, sub_rule: 'new_chinese/standard' }, hash: to.hash }),
      },
      {
        path: 'rulebook/:rule?',
        name: 'Rulebook',
        component: Rulebook,
        meta: { title: '规则书 - salasasa.cn' }
      },
      {
        path: 'duplicate',
        name: 'DuplicateGuide',
        component: () => import('@/views/DuplicateGuide.vue'),
        meta: { title: '创建复式牌墙 - salasasa.cn' }
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
        path: 'ios-download',
        name: 'IosDownload',
        component: () => import('@/views/IosDownload.vue'),
        meta: { title: 'iOS下载 - salasasa.cn' }
      },
      {
        path: 'guide',
        name: 'UsageGuide',
        component: UsageGuide,
        meta: { title: '使用说明 - salasasa.cn' }
      },
      {
        path: 'tiles',
        name: 'Tiles',
        component: Tiles,
        meta: { title: '牌面 - salasasa.cn' }
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
        path: 'lineage/related',
        name: 'LibraryRelatedness',
        component: LibraryLineage,
        meta: { title: '麻将关系表 · 麻雀图书馆', lineageTab: 'rel' }
      },
      {
        path: 'lineage',
        name: 'LibraryLineage',
        component: LibraryLineage,
        meta: { title: '麻将年代表 · 麻雀图书馆', lineageTab: 'year' }
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
    path: '/2d/demo',
    name: 'Game2DTableDemo',
    component: () => import('@/views/game2d/TableDemo.vue'),
    meta: { title: '2D 设定牌桌 - Salasasa', noindex: true }
  },
  {
    path: '/2d/game',
    name: 'Game2D',
    component: () => import('@/views/game2d/Game.vue'),
    meta: { title: 'Salasasa 2D 国标对局' }
  },
  {
    path: '/2d/record/:gameId',
    name: 'Game2DRecord',
    component: () => import('@/views/game2d/Replay.vue'),
    meta: { title: 'Salasasa 2D 牌谱阅览' }
  },
  {
    path: '/2d/player/:id',
    name: 'Game2DPlayer',
    component: () => import('@/views/game2d/Player.vue'),
    meta: { title: 'Salasasa 2D 玩家资料' }
  },
  {
    path: '/lab',
    name: 'UnitySimLab',
    component: () => import('@/views/lab/UnitySimLab.vue'),
    meta: { title: '国标 Unity 组件测试台（本地）', noindex: true }
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
      },
      {
        path: 'analysis',
        name: 'RecordAnalysis',
        component: RecordAnalysis,
        meta: { title: '牌谱分析 - salasasa.cn' }
      },
      {
        path: 'duplicate',
        name: 'DuplicateData',
        component: () => import('@/views/DuplicateData.vue'),
        meta: { title: '复式密钥查询 - salasasa.cn' }
      },
      {
        path: 'fun',
        name: 'FunData',
        component: FunData,
        meta: { title: '其他数据 - salasasa.cn' }
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
      { path: 'sponsors', name: 'AdminSponsors', component: AdminSponsors, meta: { title: '赞助玩家和特殊设定' } },
      { path: 'account-bans', name: 'AdminAccountBans', component: AdminAccountBans, meta: { title: '账号封禁名单' } },
      { path: 'chat-history', name: 'AdminChatHistory', component: AdminChatHistory, meta: { title: '聊天记录' } },
      { path: 'titles', name: 'AdminTitles', component: AdminTitles, meta: { title: '头衔管理' } },
      { path: 'inventory', name: 'AdminInventory', component: AdminInventory, meta: { title: '物品管理' } },
      { path: 'shop', name: 'AdminShop', component: () => import('@/views/admin/Shop.vue'), meta: { title: '商店管理' } },
      { path: 'redemption', name: 'AdminRedemption', component: AdminRedemption, meta: { title: '兑换码' } },
      {
        path: 'users/:userId',
        name: 'AdminUserDetail',
        component: AdminUserDetail,
        meta: { title: '用户详情' }
      },
      { path: 'events', name: 'AdminEvents', component: AdminEvents, meta: { title: '赛事管理', venueKind: 'event' } },
      { path: 'bases', name: 'AdminBases', component: AdminEvents, meta: { title: '基地管理', venueKind: 'base' } },
      {
        path: 'events/:eventId',
        name: 'AdminEventDetail',
        component: AdminEventDetail,
        meta: { title: '赛事详情', venueKind: 'event' }
      },
      {
        path: 'bases/:eventId',
        name: 'AdminBaseDetail',
        component: AdminEventDetail,
        meta: { title: '基地详情', venueKind: 'base' }
      },
      {
        path: 'event-applications',
        name: 'AdminEventApplications',
        redirect: (to) => ({ name: 'AdminEvents', query: to.query, hash: '#applications' })
      },
      {
        path: 'base-applications',
        name: 'AdminBaseApplications',
        redirect: (to) => ({ name: 'AdminBases', query: to.query, hash: '#applications' })
      },
      { path: 'tile-content', name: 'AdminTileContent', component: AdminTileContent, meta: { title: '牌面审核' } },
      { path: 'rank', name: 'AdminRank', component: AdminRank, meta: { title: '段位管理' } },
      { path: 'games', name: 'AdminGames', component: AdminGames, meta: { title: '对局记录管理' } },
      { path: 'anti-cheat', name: 'AdminAntiCheat', component: () => import('@/views/admin/AntiCheat.vue'), meta: { title: '反作弊数据中心' } },
      { path: 'room-control', name: 'AdminRoomControl', component: () => import('@/views/admin/RoomControl.vue'), meta: { title: '房间管理' } },
      { path: 'game-control', name: 'AdminGameControl', component: AdminGameControl, meta: { title: '对局管理' } },
      { path: 'audit', name: 'AdminAudit', component: AdminAudit, meta: { title: '操作审计' } },
      { path: 'messages', name: 'AdminMessages', component: AdminMessages, meta: { title: '消息推送' } },
      { path: 'activities', name: 'AdminActivities', component: AdminActivities, meta: { title: '活动设计' } },
      { path: 'classic-records', name: 'AdminClassicRecords', component: AdminClassicRecords, meta: { title: '经典牌谱' } },
      { path: 'platform-content', name: 'AdminPlatformContent', component: () => import('@/views/admin/PlatformContent.vue'), meta: { title: '鸣谢与社群' } },
      { path: 'send-email', name: 'AdminSendEmail', component: AdminSendEmail, meta: { title: '发送邮件' } },
      { path: 'mailbox', name: 'AdminMailbox', component: AdminMailbox, meta: { title: '邮箱 · 游戏内邮件' } },
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

/**
 * 把 seo.js 中的 TDK 配置合并进路由 meta：
 * 具体路径精确匹配，动态路由按模式（如 '/library/:rule'）匹配。
 */
function applySeoRoutes(routes, parentPath = '') {
  for (const route of routes) {
    const fullPath = (parentPath + (route.path || '')).replace(/\/+/g, '/').replace(/\/$/, '') || '/'
    const seo = seoEntryFor(fullPath)
    const noindex = noindexEntryFor(fullPath)
    if (seo || noindex) {
      const merged = { ...(seo || {}) }
      if (noindex) {
        merged.noindex = true
        if (!merged.title) merged.title = noindex.title
        if (!merged.description) merged.description = noindex.description || ''
      }
      route.meta = { ...(route.meta || {}), ...merged }
    }
    if (route.children) {
      applySeoRoutes(route.children, fullPath.endsWith('/') ? fullPath : `${fullPath}/`)
    }
  }
}

applySeoRoutes(routes)

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior(to, _from, savedPosition) {
    if (to.path.startsWith('/2d')) return { top: 0 }
    return savedPosition || false
  },
})

router.beforeEach(async (to, from, next) => {
  // A global guard also handles removing an explicit Android query on this route.
  const downloadRedirect = mobileDownloadRedirect(to, globalThis.navigator)
  if (downloadRedirect) return next(downloadRedirect)
  const gameLocale = requestedGameLocale(to.path, new URLSearchParams(
    typeof to.query.lang === 'string' ? { lang: to.query.lang } : {}
  ).toString())
  if (gameLocale) setLocale(gameLocale)
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
    return next({ path: '/account', hash: '#sec-manage-event' })
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

function applyHead(route) {
  applyRouteHead(route, locale.value, tr)
}

watch(locale, () => applyHead(router.currentRoute.value), { immediate: true })

router.afterEach((to) => {
  applyHead(to)
  // 路由切换后默认回到页面顶部，避免从首页进入时停留在上次滚动位置
  window.scrollTo(0, 0)
})

export default router
