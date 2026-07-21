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
    key: 'local',
    title: '地方麻将规则',
    hint: '各地流行玩法与地方竞赛规则',
  },
  {
    key: 'custom',
    title: '自制规则',
    hint: '社区改版与原创规则书',
  },
]

export const LIBRARY_RULES = [
  {
    key: 'guobiao',
    label: '国标麻将',
    short: '国标',
    categories: ['platform'],
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
    key: 'jiandan',
    label: '简单麻将',
    short: '简单',
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
    categories: ['custom'],
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
    categories: ['custom'],
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

export function getLibraryRule(key) {
  return LIBRARY_RULES.find((r) => r.key === key) || null
}

export function rulesForSection(sectionKey) {
  return LIBRARY_RULES.filter((r) => r.categories.includes(sectionKey))
}
