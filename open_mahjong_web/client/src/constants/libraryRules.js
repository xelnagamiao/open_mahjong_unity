/**
 * 麻雀图书馆规则目录
 * categories:
 *   platform - 平台支持
 *   mil      - MIL 国际麻将联盟
 *   local    - 地方麻将
 *   custom   - 自制规则
 */
export const LIBRARY_SECTIONS = [
  {
    key: 'platform',
    title: '平台支持规则',
    hint: '本站已实装或可对局的规则',
  },
  {
    key: 'mil',
    title: 'MIL 国际麻将联盟',
    hint: 'MIL 竞赛与联盟规则书',
  },
  {
    key: 'categorized',
    title: '归类规则',
    hint: '地方麻将、自制规则与平台尚未实装的玩法',
  },
  {
    key: 'materials',
    title: '其他资料',
    hint: '规则研究、牌例与来自 other 文件夹的资料索引',
  },
  {
    key: 'submit',
    title: '提交资料',
    hint: '提交新的规则书、牌例或规则研究资料',
  },
]

export const LIBRARY_RULES = [
  {
    key: 'guobiao',
    label: '国标麻将',
    short: '国标',
    categories: ['platform', 'mil'],
    description:
      '国标麻将指国家体育总局在1998出台的《中国竞技麻将比赛规则(试行)》中确立的麻将规则。本平台以新编 MCR 作为主规则书。',
    accent: '#3b82f6',
    resources: [
      {
        title: '国标麻将（新编 MCR）',
        desc: '本平台使用 Natsuki 编著的新编 MCR 规则书设计国标运行逻辑。',
        url: '/rulebooks/guobiao-mcr.pdf',
        filename: '新编MCR.pdf',
      },
    ],
  },
  {
    key: 'riichi',
    label: '立直麻将',
    short: '立直',
    categories: ['platform'],
    description: '立直麻将一般指日本麻将，是麻将规则的一个分支。',
    accent: '#ef4444',
    resources: [
      {
        title: 'GGHK 立直麻将规则书',
        desc: '香港麻将协会发布的立直麻将规则书。',
        url: '/rulebooks/riichi-rulebook.pdf',
        filename: 'GGHK-Riichi-Mahjong-Rulebook-CN.pdf',
      },
    ],
  },
  {
    key: 'qingque',
    label: '青雀',
    short: '青雀',
    categories: ['platform', 'custom'],
    description:
      '青雀由莫莫柴编写，旨在传统行牌框架内平衡做大、抢和与防守，并为各类和牌提供基于美感与难度的赋分参照。',
    accent: '#10b981',
    resources: [
      {
        title: '青雀一页纸',
        desc: '一页纸番种速记。',
        url: '/rulebooks/qingque-onepage.pdf',
        filename: '青雀一页纸.pdf',
      },
      {
        title: '青雀牌例',
        desc: '番种详解与牌例。',
        url: '/rulebooks/qingque-paili.pdf',
        filename: '青雀牌例.pdf',
      },
      {
        title: '青雀规则文档',
        desc: '行牌逻辑与概念解释。',
        url: '/rulebooks/qingque-rulebook.pdf',
        filename: '青雀规则文档.pdf',
      },
    ],
  },
  {
    key: 'mil-collection',
    label: 'MIL 竞赛规则资料集',
    short: 'MIL 资料集',
    categories: ['mil'],
    description: '来自 other/rule/MIL_rule 的 MIL 竞赛规则与补充细则，涵盖国标、四川、立直及各地方推广规则。',
    accent: '#b7791f',
    resources: [
      { title: '四川麻将（SBR）竞赛规则（试行2025版）', url: '/rulebooks/mil/四川麻将（SBR）竞赛规则（试行2025版） (1).pdf' },
      { title: '国标麻将（MCR）竞赛规则', url: '/rulebooks/mil/国标麻将（MCR）竞赛规则Chinese_mahjong_rules_try (1).pdf' },
      { title: '国标麻将（MCR）规则补充细则（2025）', url: '/rulebooks/mil/国标麻将（MCR）规则补充细则（试行，2025） (1).pdf' },
      { title: '山西麻将（推广）竞赛规则', url: '/rulebooks/mil/山西麻将（推广）竞赛规则（试行2023版）.pdf' },
      { title: '广东麻将（推广）竞赛规则', url: '/rulebooks/mil/广东麻将（推广）竞赛规则（试行2023版）.pdf' },
      { title: '推倒和麻将（推广）竞赛规则', url: '/rulebooks/mil/推倒和麻将（推广）竞赛规则（试行2024版）.pdf' },
      { title: '杭州麻将（推广）竞赛规则', url: '/rulebooks/mil/杭州麻将（推广）竞赛规则（试行2025版）.pdf' },
      { title: '温州麻将（试点）竞赛规则', url: '/rulebooks/mil/温州麻将（试点）竞赛规则（试行2024版）.pdf' },
      { title: '立直麻将竞赛规则（RCR）', url: '/rulebooks/mil/立直麻将竞赛规则riichirules2016.pdf' },
      { title: '立直麻将（RCR）竞赛规则补充细则', url: '/rulebooks/mil/立直麻将（RCR）竞赛规则补充细则（2024版）.pdf' },
      { title: '红中麻将（推广）竞赛规则', url: '/rulebooks/mil/红中麻将（推广）竞赛规则（试行2024版）.pdf' },
      { title: '贵州麻将（推广）竞赛规则', url: '/rulebooks/mil/贵州麻将（推广）竞赛规则（试行2023版）.pdf' },
      { title: '长春麻将（推广）竞赛规则', url: '/rulebooks/mil/长春麻将（推广）竞赛规则（试行2024版）.pdf' },
    ],
  },
  {
    key: 'hongque',
    label: '虹雀²',
    short: '虹雀²',
    categories: ['platform', 'custom'],
    description: '虹雀² v1.6 规则书，介绍牌面、行牌流程、吃碰杠和、补牌及计分规则。',
    accent: '#f97316',
    resources: [
      {
        title: '虹雀² v1.6 规则书',
        desc: '虹雀² v1.6 完整规则说明。',
        url: '/rulebooks/hongque-v1.6.pdf',
        filename: '虹雀² v1.6.pdf',
      },
    ],
  },
  {
    key: 'classical',
    label: '古典麻将',
    short: '古典',
    categories: ['platform'],
    description:
      '根据《绘图麻雀牌谱》《想定宁波规则》等文献汇总，试图还原 1920 年代前后早期麻将样貌：番种简洁、重刻杠幺九、未和牌家计分。',
    accent: '#a16207',
    resources: [
      {
        title: '古典麻将规则书',
        desc: '平台现行古典麻将版本。',
        url: '/rulebooks/classical-rulebook.pdf',
        filename: '古典麻将规则.pdf',
      },
    ],
  },
  {
    key: 'sichuan',
    label: '四川麻将（SBR）',
    short: '川麻',
    categories: ['platform', 'mil'],
    description: 'MIL 比赛规则下的四川麻将（血战到底 / SBR 竞赛规则）。',
    accent: '#f59e0b',
    resources: [
      {
        title: '四川麻将（SBR）竞赛规则',
        desc: '四川麻将（SBR）竞赛规则（试行 2025 版）。',
        url: '/rulebooks/sichuan-sbr.pdf',
        filename: '四川麻将（SBR）竞赛规则（试行2025版）.pdf',
      },
    ],
  },
  {
    key: 'changsha',
    label: '长沙麻将',
    short: '长沙',
    categories: ['platform', 'local'],
    description: '湖南地区流行的长沙麻将玩法；本平台实现双鸟变体。',
    accent: '#ec4899',
    resources: [
      {
        title: '长沙麻将（双鸟）规则书',
        desc: '本平台长沙麻将规则说明。',
        url: '/rulebooks/changsha-classic-double-bird-rulebook.pdf',
        filename: '长沙麻将规则书.pdf',
      },
    ],
  },
  {
    key: 'taiwan',
    label: '台湾麻将',
    short: '台麻',
    categories: ['platform', 'local'],
    description: '采用 144 张牌与 16 张手牌，并以台数结算的台湾麻将规则。',
    accent: '#14b8a6',
    resources: [
      {
        title: '台湾麻将台数表',
        desc: '本平台台湾麻将采用的台数参考表。',
        url: '/rulebooks/taiwan-yaku-table.pdf',
        filename: '台湾麻将台数表.pdf',
      },
    ],
  },
  {
    key: 'jiandan',
    label: '南雀',
    short: '南雀',
    categories: ['platform'],
    description: '平台轻量入门向规则，便于快速上手对局。',
    accent: '#64748b',
    resources: [],
  },
  {
    key: 'shiyangjin',
    label: '十样锦麻将',
    short: '十样锦',
    categories: ['local', 'custom'],
    description: '地方特色玩法规则书（平台尚未实装对局，仅提供查阅）。',
    accent: '#8b5cf6',
    resources: [
      {
        title: '十样锦麻将规则书',
        desc: '十样锦麻将规则说明。',
        url: '/rulebooks/shiyangjin.pdf',
        filename: '十样锦麻将规则书.pdf',
      },
    ],
  },
  {
    key: 'guobiao-kobayashi',
    label: '国标小林改',
    short: '小林改',
    categories: ['platform', 'custom'],
    description: '社区修改的中国麻将规则，整体沿用国标番种体系并调整部分条款。',
    accent: '#0ea5e9',
    resources: [
      {
        title: '中国麻将（小林改版）规则书',
        desc: '小林改版修订条款说明。',
        url: '/rulebooks/guobiao-kobayashi.pdf',
        filename: '中国麻将（小林改版）规则书.pdf',
      },
    ],
  },
  {
    key: 'guobiao-kshen',
    label: 'K神麻将',
    short: 'K神',
    categories: ['platform', 'custom'],
    description: '国标 K 神改版规则。',
    accent: '#6366f1',
    resources: [
      {
        title: 'K神麻雀规则说明书',
        desc: 'K 神改版规范说明书。',
        url: '/rulebooks/guobiao-kshen.pdf',
        filename: 'K神麻雀规则说明书.pdf',
      },
    ],
  },
]

const MIL_RULES = [
  ['mil-sichuan', '四川麻将（SBR）', '四川麻将（SBR）竞赛规则（试行2025版） (1).pdf'],
  ['mil-mcr', '国标麻将（MCR）', '国标麻将（MCR）竞赛规则Chinese_mahjong_rules_try (1).pdf'],
  ['mil-mcr-supplement', '国标 MCR 补充细则', '国标麻将（MCR）规则补充细则（试行，2025） (1).pdf'],
  ['mil-shanxi', '山西麻将（推广）', '山西麻将（推广）竞赛规则（试行2023版）.pdf'],
  ['mil-guangdong', '广东麻将（推广）', '广东麻将（推广）竞赛规则（试行2023版）.pdf'],
  ['mil-tuidao', '推倒和麻将（推广）', '推倒和麻将（推广）竞赛规则（试行2024版）.pdf'],
  ['mil-hangzhou', '杭州麻将（推广）', '杭州麻将（推广）竞赛规则（试行2025版）.pdf'],
  ['mil-wenzhou', '温州麻将（试点）', '温州麻将（试点）竞赛规则（试行2024版）.pdf'],
  ['mil-riichi', '立直麻将（RCR）', '立直麻将竞赛规则riichirules2016.pdf'],
  ['mil-riichi-supplement', '立直 RCR 补充细则', '立直麻将（RCR）竞赛规则补充细则（2024版）.pdf'],
  ['mil-red-center', '红中麻将（推广）', '红中麻将（推广）竞赛规则（试行2024版）.pdf'],
  ['mil-guizhou', '贵州麻将（推广）', '贵州麻将（推广）竞赛规则（试行2023版）.pdf'],
  ['mil-changchun', '长春麻将（推广）', '长春麻将（推广）竞赛规则（试行2024版）.pdf'],
].map(([key, label, filename]) => ({
  key,
  label,
  short: label.replace('麻将', ''),
  categories: ['mil'],
  description: `MIL 规则资料：${label}。`,
  accent: '#b7791f',
  resources: [{ title: label, url: `/rulebooks/mil/${filename}`, filename }],
}))

export function getLibraryRule(key) {
  return [...LIBRARY_RULES, ...MIL_RULES].find((r) => r.key === key) || null
}

export function rulesForSection(sectionKey) {
  if (sectionKey === 'categorized') {
    return LIBRARY_RULES.filter(
      (r) => !r.categories.includes('platform') && !r.categories.includes('mil'),
    )
  }
  if (sectionKey === 'mil') {
    const platformRules = LIBRARY_RULES.filter(
      (r) => r.categories.includes('mil') && !r.categories.includes('platform') && r.key !== 'mil-collection',
    )
    const platformMilFiles = new Set(['mil-sichuan', 'mil-mcr', 'mil-riichi'])
    return platformRules.concat(MIL_RULES.filter((r) => !platformMilFiles.has(r.key)))
  }
  if (sectionKey === 'materials' || sectionKey === 'submit') return []
  return LIBRARY_RULES.filter((r) => r.categories.includes(sectionKey))
}

export const LIBRARY_MATERIALS = [
  {
    key: 'materials',
    title: '其他资料',
    short: '资料索引',
    description: '规则资料搜集归档、牌谱与历史资料索引：规则研究、绘图麻雀牌谱与 MIL 资料整理。',
    to: '/library/materials',
    accent: '#2f6f5e',
    links: [
      {
        title: '规则资料搜集',
        desc: '浏览规则研究、牌例与历史资料索引。',
        to: '/rule-research',
      },
      {
        title: '绘图麻雀牌谱',
        desc: '传统麻将牌谱与牌例资料。',
        to: '/rule-research/drawing-mahjong',
      },
      {
        title: 'MIL 资料整理',
        desc: 'other/rule 中的 MIL 规则书索引。',
        to: '/library#sec-mil',
      },
    ],
  },
]

export const LIBRARY_SUBMISSION = [
  {
    key: 'submit',
    title: '提交规则资料',
    short: '规则研究',
    description: '提交规则书、牌例、来源链接或校订建议，进入规则资料搜集归档。',
    to: '/library/submit',
    accent: '#9f1239',
    links: [
      {
        title: '前往提交入口',
        desc: '在规则资料搜集页提交新的规则书、牌例或规则研究资料。',
        to: '/rule-research',
      },
    ],
  },
]

// 图书馆讨论区主题（非规则条目的板块）：key -> 显示名
export const LIBRARY_TOPIC_LABELS = {
  materials: '其他资料',
  submit: '提交资料',
  public: '主讨论区',
}

export function libraryTopicLabel(key) {
  const rule = getLibraryRule(key)
  if (rule) return rule.label
  return LIBRARY_TOPIC_LABELS[key] || String(key || '')
}

export function libraryTopicPath(key, postId) {
  if (key === 'public') {
    return `/library?topic=public${postId ? `&post=${postId}` : ''}`
  }
  return `/library/${key}${postId ? `?post=${postId}` : ''}`
}
