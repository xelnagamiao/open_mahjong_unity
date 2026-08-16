/**
 * 麻雀图书馆规则目录
 * categories:
 *   mahjong  - 麻将（谱系 + 平台可对局）
 *   mil      - MIL 国际麻将联盟
 *   local    - 地方麻将
 *   custom   - 自制规则
 */
export const LIBRARY_SECTIONS = [
  {
    key: 'mahjong',
    title: '麻将',
    hint: '本站可对局的规则。',
  },
  {
    key: 'mil',
    title: 'MIL 国际麻将联盟',
    hint: 'MIL 竞赛与联盟规则书',
  },
  {
    key: 'categorized',
    title: '归类规则',
    hint: '地方麻将、前史、子规则与尚未实装的玩法。',
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
  {
    key: 'lineage',
    title: '麻将谱系',
    hint: '年代表与关系表',
  },
]

export const LIBRARY_RULES = [
  {
    key: 'guobiao',
    label: '国标麻将',
    short: '国标',
    categories: ['platform', 'mil'],
    description:
      '国标麻将源于国家体育总局于1998年11月出台的《中国竞技麻将比赛规则(试行)》、是中国唯一由官方确立的竞技麻将规则；本平台参照Natsuki编著的新编MCR撰写运行逻辑，已通过所有牌例验证，如发现测试过程中出现了不符合国标麻将规则预期的行为，请向Q群906497522反馈。',
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
    description: '立直麻将参照天凤/雀魂规则进行设计，无双倍役满',
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
      '青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照；如在测试中发现设计问题或有任何建议，可以联系规则制定人莫莫柴Q1107574，提交bug可在群906497522提交',
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
    description:
      '虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群497685219一同交流。',
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
      '本规则为根据《绘图麻雀牌谱》《想定宁波规则》等书籍文献资料汇总而成的，试图还原1920年代左右或以前的早期麻将样貌的麻将规则。相比现代规则，古典麻雀有番种体系简单、重刻杠幺九、未和牌家计分等特点，具有独特风味。',
    accent: '#a16207',
    resources: [
      {
        title: '古典麻将规则书',
        desc: '平台现行古典麻将版本。',
        url: '/rulebooks/classical-rulebook.pdf',
        filename: '古典麻将规则.pdf',
      },
      {
        title: '绘图麻雀牌谱',
        desc: '沈一帆 1914 年牌谱。古典麻将按这本书和同层文献还原。',
        url: '/rulebooks/drawing-mahjong.pdf',
        filename: '绘图麻雀牌谱.pdf',
      },
      {
        title: '想定宁波规则（榛原 1952）',
        desc: '榛原茂树据五种民初麻将书想定的宁波打法。',
        url: '/rulebooks/shinbara-ningbo.html',
        filename: '想定宁波规则.html',
      },
    ],
  },
  {
    key: 'sichuan',
    label: '四川麻将（SBR）',
    short: '川麻',
    categories: ['platform', 'mil'],
    description: '四川麻将（血战到底）',
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
    description:
      '长沙麻将经典双鸟规则：108张数牌，可吃上家牌，258将小胡，大胡可叠加，和牌后翻两只鸟并按座位中鸟加倍。',
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
    description:
      '台湾麻将：使用144张牌与16张手牌，按台计分，支持公开报听、食替限制与八仙过海等规则。具体流程与台表可在馆规设置中选择。',
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
    description:
      '南雀规则由南瓜饼编写，是一个正在测试的规则，目标是在新手易上手与竞技策略深度之间取得平衡。无起和限制。当前版本固定采用一人和牌即止。标准规则将采用三人和牌（血战到底），正在开发中。',
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
    description:
      '小林改版国标麻将，对国标麻将进行了番数平衡，还处于测试版，取消了8番起胡和底分，改为点和得分x2，自摸番三。非竞技规则，只为娱乐。',
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
    description:
      'K神改版国标麻将，新增镜同、四连刻等番种，复合番100封顶，默认8番起和。小牌点炮无责：点和12分以下三家各付n；12分以上两家各付12，放铳者付3n-24。自摸三家各付n。可开启错和、可自定义起和番。出现计分bug可在群里向q975653345反馈',
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
  {
    key: 'guobiao-lanshi',
    label: '国标蓝十改',
    short: '蓝十改',
    categories: ['platform', 'custom'],
    description:
      '蓝十改版的国标麻将规则，对国标麻将的番种表进行了全面的修改，并根据番种的难度调整了评分，5分起和，授受制为半全铳半分付。如在测试中发现设计问题或有任何建议，可以联系规则制定人蓝十QQ1002094810。',
    accent: '#0d9488',
    resources: [
      {
        title: '蓝十魔改规则第4版',
        desc: '蓝十改规则说明。',
        url: '/rulebooks/guobiao-lanshi.pdf',
        filename: '蓝十魔改规则第4版.pdf',
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
  if (sectionKey === 'mahjong') {
    return LIBRARY_RULES.filter((r) => r.categories.includes('platform'))
  }
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
  if (sectionKey === 'materials' || sectionKey === 'submit' || sectionKey === 'lineage') return []
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
        desc: '原文档案：谱系史料簿、麻将通论与书志、香港资料。',
        to: '/rule-research',
      },
      {
        title: '古典麻将文献',
        desc: '绘图麻雀牌谱、想定宁波规则，以及平台古典麻将规则书。',
        to: '/library/classical',
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

export const LIBRARY_LINEAGE = {
  key: 'lineage',
  title: '麻将谱系',
  short: '年代表 · 关系表',
  description: '纸牌、牌九接到宁波麻将再分化。',
  to: '/library/lineage',
  accent: '#1f6b52',
}

// 图书馆讨论区主题（非规则条目的板块）：key -> 显示名
export const LIBRARY_TOPIC_LABELS = {
  materials: '其他资料',
  submit: '提交资料',
  lineage: '麻将谱系',
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
