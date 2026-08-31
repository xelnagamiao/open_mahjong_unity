/**
 * 站点 SEO（TDK）配置——单一数据源。
 * 被 router（运行时 head 更新）与 scripts/prerender-seo.mjs（构建期预渲染）共用。
 */

export const SITE = {
  name: 'Salasasa',
  domain: 'https://salasasa.cn',
}

/**
 * 各页面的 Title / Description / Keywords。
 * path 与 vue-router 的路由路径一致；带参数的动态路由使用模式（如 '/library/:rule'）。
 */
export const SEO_PAGES = [
  {
    path: '/',
    title: 'Salasasa国标麻将对战平台-萨拉飒飒',
    description:
      '支持国标麻将、立直麻将、长沙麻将、四川麻将、台湾麻将等一系列规则的开源麻将平台，提供2D、3D网页对战、数据站、牌理分析等多种功能；Salasasa平台也是open_mahjong_unity项目的示例服务器。',
    keywords: '国标麻将,立直麻将,长沙麻将,四川麻将,台湾麻将,在线麻将,麻将平台,麻将计算器,麻将规则,Salasasa',
  },
  {
    path: '/rulebook',
    title: '麻将规则书大全：国标、立直、青雀、虹雀² PDF - Salasasa',
    description: '汇集国标麻将（新编 MCR）、立直麻将、青雀、虹雀² 等规则书 PDF，可在线阅读或下载，附规则介绍与牌例。',
    keywords: '麻将规则,国标麻将规则,立直麻将规则,青雀规则,虹雀²,规则书PDF',
  },
  {
    path: '/rulebook/:rule',
    title: '麻将规则书 - Salasasa',
    description: '查阅各麻将规则的规则书 PDF、牌例与文档，可在线阅读或下载。',
    keywords: '麻将规则书,麻将规则,规则书PDF',
  },
  {
    path: '/library',
    title: '麻雀图书馆：麻将规则书、牌例与资料归档 - Salasasa',
    description: '开放收录的麻将规则与资料馆：麻将谱系、可对局规则、MIL 竞赛规则、地方与自制规则，附讨论区。',
    keywords: '麻雀图书馆,麻将规则资料,麻将规则书,麻将牌例',
  },
  {
    path: '/library/:rule',
    title: '麻将规则书与牌例 - Salasasa 麻雀图书馆',
    description: '查阅各麻将规则的规则书 PDF、牌例与讨论，可在线阅读或下载。',
    keywords: '麻将规则书,麻将规则,麻将牌例,麻将资料',
  },
  {
    path: '/library/lineage',
    title: '麻将年代表 - Salasasa 麻雀图书馆',
    description: '麻将年代表：纸牌、牌九接到宁波麻将再分化。',
    keywords: '麻将谱系,麻将分类,麻将年代表,国标麻将,四川麻将,东北麻将',
  },
  {
    path: '/library/lineage/related',
    title: '麻将关系表 - Salasasa 麻雀图书馆',
    description: '麻将关系表：按后起改动把相近打法放在一起。',
    keywords: '麻将谱系,麻将分类,麻将关系表,国标麻将,四川麻将,东北麻将',
  },
  {
    path: '/rule-research',
    title: '规则资料搜集：麻将规则原文归档 - Salasasa',
    description: '按源链接与原文摘录归档的麻将规则研究资料，保留出处，便于查证与对比。',
    keywords: '麻将规则资料,规则研究,麻将规则原文,规则归档',
  },
  {
    path: '/rule-research/:slug',
    title: '规则资料搜集：麻将规则原文 - Salasasa',
    description: '按源链接与原文摘录归档的麻将规则研究资料，保留出处，便于查证与对比。',
    keywords: '麻将规则资料,规则研究,麻将规则原文,规则归档',
  },
  {
    path: '/calc/chinese',
    title: '国标麻将算番计算器：番种与得分 - Salasasa',
    description: '在线国标麻将算番工具：输入手牌、副露与和牌方式，自动计算番种、得分及全部和牌拆解。',
    keywords: '国标麻将算番,国标麻将计算器,麻将番数计算,国标麻将和牌',
  },
  {
    path: '/calc/hongque',
    title: '虹雀² 和牌计算器：番数与牌组拆解 - Salasasa',
    description: '按虹雀² v1.6 规则在线计算和牌拆解与番数得分，支持手牌与副露输入。',
    keywords: '虹雀²,虹雀²计算器,麻将和牌计算器,麻将算番',
  },
  {
    path: '/paili',
    title: '麻将牌理分析：听牌、进张与切牌建议 - Salasasa',
    description: '输入 13 张手牌分析听牌与进张，14 张分析最佳切牌，支持七对、十三幺、全不靠等特殊牌型。',
    keywords: '麻将牌理,听牌分析,进张,切牌建议,向听数',
  },
  {
    path: '/seed-verify',
    title: '麻将对局随机种子验证：复现配牌与座位 - Salasasa',
    description: '用对局公布的主种子在浏览器内复现随机座位与每局配牌、牌山，验证服务器未中途更换随机种子。',
    keywords: '麻将随机种子,配牌验证,对局公平性,种子验证',
  },
  {
    path: '/guess-fan',
    title: '猜番对抗：麻将番数猜谜与训练 - Salasasa',
    description: '单人训练或联机对战，根据手牌与牌谱猜测和牌番种，立直 BO5 限时对抗计入排行。',
    keywords: '猜番,麻将番数,麻将猜谜,番数训练',
  },
  {
    path: '/tools/record-convert',
    title: '麻将牌谱格式转换工具 - Salasasa',
    description: '在多种麻将牌谱格式之间转换，附数据影响说明与转换示例，方便归档与迁移。',
    keywords: '麻将牌谱,牌谱格式,牌谱转换,麻将记录转换',
  },
  {
    path: '/events',
    title: '麻将比赛与赛事列表 - Salasasa',
    description: '查看 Salasasa 平台近期与过往麻将赛事，支持申请办赛与赛事管理。',
    keywords: '麻将比赛,麻将赛事,在线麻将比赛,赛事报名',
  },
  {
    path: '/events/:eventId',
    title: '麻将赛事详情 - Salasasa',
    description: '查看赛事介绍、报名与参赛信息；登录后可申请办赛或管理自己的赛事。',
    keywords: '麻将赛事,赛事详情,麻将比赛报名',
  },
  {
    path: '/mobile-download',
    title: 'Salasasa 手机版下载：安卓/iOS 客户端 - Salasasa',
    description: '下载 Salasasa 麻将手机客户端，与 PC、网页端互通，加入 QQ 群获取支持。',
    keywords: '麻将手机版,麻将APK下载,Salasasa下载,麻将客户端',
  },
  {
    path: '/guide',
    title: '使用说明：平台简介、对局机制与办赛指南 - Salasasa',
    description: '了解 Salasasa 平台的对局机制、猜番对抗、办赛申请与常见问题；规则细则见规则书。',
    keywords: '麻将平台使用,麻将怎么玩,Salasasa指南,对局机制',
  },
  {
    path: '/2d',
    title: 'Salasasa 2D 国标麻将：网页在线对战 - Salasasa',
    description: '免下载网页直接开局：2D 国标麻将在线对战、牌谱阅览与玩家数据查询，支持繁中、英文、日文。',
    keywords: '2D麻将,在线麻将,国标麻将网页版,麻将网页游戏',
  },
  {
    path: '/player-data',
    title: '麻将玩家数据统计与排行 - Salasasa',
    description: '查询平台玩家对局数据、段位排行与统计指标，支持按局制与场景筛选分析。',
    keywords: '麻将玩家数据,麻将段位,麻将统计,玩家排行',
  },
  {
    path: '/player-data/platform',
    title: '麻将平台数据统计 - Salasasa',
    description: '查看 Salasasa 平台整体对局数据与统计指标，支持按局制与场景筛选。',
    keywords: '麻将平台数据,麻将统计,平台对局',
  },
  {
    path: '/player-data/analysis',
    title: '麻将牌谱分析 - Salasasa',
    description: '下载牌谱到本地，用标准分析工具统计顺位、和牌率与副露等指标。',
    keywords: '麻将牌谱分析,牌谱下载,对局统计',
  },
  {
    path: '/game-unity',
    title: '麻将在线对战平台：国标、立直、青雀、川麻 - Salasasa',
    description: '进入 Salasasa 对战平台，支持国标、立直、青雀、川麻、长沙麻将，网页/PC/手机三端互通。',
    keywords: '麻将对战,在线麻将,麻将平台,3D麻将',
  },
]

/**
 * 登录 / 后台 / 对战局等页面不做 SEO，标记 noindex。
 * prefix: true 表示按路径前缀匹配（如 /admin 及其全部子页面）。
 */
export const NOINDEX_PAGES = [
  { path: '/login', title: '玩家登录 - Salasasa', description: '登录 Salasasa 麻将平台，进入对战、数据站与赛事管理。' },
  { path: '/register', title: '注册 Salasasa 账号 - Salasasa', description: '注册 Salasasa 平台账号，与游戏内账户互通。' },
  { path: '/account', title: '账号面板 - Salasasa' },
  { path: '/admin/login', title: '管理后台登录 - Salasasa' },
  { path: '/event-admin/login', title: '赛事管理后台登录 - Salasasa' },
  { path: '/2d/game', title: 'Salasasa 2D 国标对局' },
  { path: '/admin', prefix: true, title: '管理后台 - Salasasa' },
  { path: '/event-admin', prefix: true, title: '赛事管理后台 - Salasasa' },
  { path: '/2d/record', prefix: true, title: 'Salasasa 2D 牌谱阅览' },
  { path: '/2d/player', prefix: true, title: 'Salasasa 2D 玩家资料' },
]

/** 麻雀图书馆中已知的规则条目（用于 sitemap 与预渲染）。 */
export const LIBRARY_RULE_PATHS = [
  'guobiao',
  'riichi',
  'qingque',
  'mil-collection',
  'hongque',
  'classical',
  'sichuan',
  'changsha',
  'taiwan',
  'jiandan',
  'shiyangjin',
  'guobiao-kobayashi',
  'guobiao-kshen',
]

/** 构建期需要生成静态 HTML（含 TDK）的具体 URL。 */
export const PRERENDER_PATHS = [
  '/',
  '/rulebook',
  '/library',
  '/library/lineage',
  '/library/lineage/related',
  ...LIBRARY_RULE_PATHS.map((rule) => `/library/${rule}`),
  '/rule-research',
  '/calc/chinese',
  '/calc/hongque',
  '/paili',
  '/seed-verify',
  '/guess-fan',
  '/tools/record-convert',
  '/events',
  '/mobile-download',
  '/guide',
  '/2d',
  '/player-data',
  '/player-data/platform',
  '/player-data/analysis',
  '/game-unity',
  // 以下为 noindex 壳页，避免搜索引擎通过 SPA fallback 收录到错误的首页 TDK
  '/login',
  '/register',
  '/account',
  '/admin',
  '/admin/login',
  '/event-admin',
  '/event-admin/login',
  '/2d/game',
]

/** 将路由模式（如 '/library/:rule'）转成正则。 */
function patternToRegex(pattern) {
  const escaped = pattern.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/:[^/]+/g, '[^/]+')
  return new RegExp(`^${escaped}$`)
}

/** 按具体路径查找 TDK 配置：先精确匹配，再匹配动态路由模式。 */
export function seoEntryFor(path) {
  const exact = SEO_PAGES.find((p) => p.path === path)
  if (exact) return exact
  return SEO_PAGES.find((p) => p.path.includes(':') && patternToRegex(p.path).test(path)) || null
}

/** 查找 noindex 配置：先精确匹配，再按前缀匹配。 */
export function noindexEntryFor(path) {
  const exact = NOINDEX_PAGES.find((p) => !p.prefix && p.path === path)
  if (exact) return exact
  return NOINDEX_PAGES.find((p) => p.prefix && path.startsWith(p.path)) || null
}
