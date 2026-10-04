import { hongKongRulebooks } from './hongKongRulebooks.js'

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
    key: 'wenzhou', label: '温州麻将', short: '温州', categories: ['platform', 'local', 'mil'], accent: '#297b74',
    description: 'MIL 温州2024：136张、十六张手牌，每局翻财；白板固定代财神本牌，可吃碰杠和点和，八对加单张、三财、软硬和及连庄。',
    resources: [{ title: 'MIL 温州麻将2024原文', url: '/rulebooks/mil/温州麻将（试点）竞赛规则（试行2024版）.pdf' }, { title: '温州2024平台补则', url: '/rulebooks/wenzhou/MIL2024-platform-supplement.txt' }],
  },
  {
    key: 'changchun', label: '长春麻将', short: '长春', categories: ['platform', 'mil'], accent: '#5c779a',
    description: 'MIL 长春2024：136张、十三张手牌，三门带幺九，可吃碰杠、报听看宝。一条仅在特殊杠中代牌；六番封顶，流局保留杠分。',
    resources: [{ title: 'MIL 长春2024原文', url: '/rulebooks/mil/长春麻将（推广）竞赛规则（试行2024版）.pdf' }, { title: '平台补则', url: '/rulebooks/changchun.html' }],
  },
  {
    key: 'hongzhong', label: '红中麻将', short: '红中', categories: ['platform', 'mil'], accent: '#b74b46',
    description: 'MIL 红中麻将（推广）2024：112张、十三张手牌，红中为万能牌；仅自摸，可碰杠，不吃。最高四番，和后扎两鸟，流局退杠。',
    resources: [{ title: 'MIL 红中麻将2024原文', url: '/rulebooks/mil/红中麻将（推广）竞赛规则（试行2024版）.pdf' }],
  },
  {
    key: 'hangzhou', label: '杭州麻将', short: '杭州', categories: ['platform', 'mil'], accent: '#1f8a6a',
    description: 'MIL 杭州麻将（推广）2025：136张、白板财神，仅自摸；爆头、财飘、七对、十风，4番封顶，老庄2/4/8倍、三吃承包，墙尾20张流局。',
    resources: [{ title: 'MIL 杭州麻将2025原文', url: '/rulebooks/mil/杭州麻将（推广）竞赛规则（试行2025版）.pdf' }],
  },
  {
    key: 'yixing', label: '宜兴麻将', short: '宜兴', categories: ['platform', 'local'], accent: '#537c69',
    description: "宜兴麻将是江苏宜兴本地的特色玩法，由144张牌组成，其中万条筒各36张，东南西北中发白各4张，花牌8张，2花自摸，3花放冲，一花独吊，最先将手牌全部组成顺子和刻子的玩家赢得一局，起手花牌数能决定你当前牌局打法规划，牌局种类门清，碰碰胡，混一色，清一色等常见大牌，还包括独吊翻倍，杠开翻倍，海底翻倍，抢杠翻3倍等特殊机制，游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加Q541784531一同交流",
    resources: [
      { title: '宜兴规则书与平台补则', url: '/rulebooks/yixing.html' },
      { title: '宜兴麻将规则书（最新 Word）', url: '/rulebooks/yixing-rulebook.docx', filename: '宜兴麻将规则.docx' },
      { title: '宜兴麻将规则书（PDF）', url: '/rulebooks/yixing-rulebook.pdf' },
      { title: '宜兴麻将规则书（DOC）', url: '/rulebooks/yixing-rulebook.doc', filename: '宜兴麻将规则.doc' },
    ],
  },
  {
    key: 'guizhou', label: '贵州麻将', short: '贵州', categories: ['platform', 'mil'],
    description: 'MIL 贵州麻将（推广）2023：108张、十三张手牌，不吃，无花无癞子；原报、软报、捉鸡，鸡杠局终结算。基本分39分封顶，流局按理论最大听牌分查叫。',
    accent: '#92752f',
    resources: [
      { title: 'MIL 贵州麻将2023原文', url: '/rulebooks/mil/贵州麻将（推广）竞赛规则（试行2023版）.pdf' },
    ],
  },
  {
    key: 'guobiao',
    label: '国标麻将',
    short: '国标',
    categories: ['platform', 'mil'],
    description:
      '国标麻将规则资料，包含 Natsuki 编著的《新编 MCR》及各改编版本规则书。',
    accent: '#3b82f6',
    resources: [
      {
        title: '国标麻将（新编 MCR）',
        desc: 'Natsuki 编著的《新编 MCR》。',
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
    description: '四人立直麻将，支持多种规则预设及役种、计分和流局设置',
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
        desc: '青雀第十四版一页纸，来自 mmcr.online。',
        url: '/rulebooks/qingque-onepage.pdf',
        filename: '青雀一页纸 14.pdf',
      },
      {
        title: '青雀牌例',
        desc: '青雀牌例第三版第一次修订，适用于第十四版，来自 mmcr.online。',
        url: '/rulebooks/qingque-paili.pdf',
        filename: '青雀牌例 3.1.pdf',
      },
      {
        title: '青雀规则文档',
        desc: '青雀第十四版第一次修订，来自 mmcr.online。',
        url: '/rulebooks/qingque-rulebook.pdf',
        filename: '青雀 14.1.pdf',
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
        desc: '现存扫描为上海游艺社 1924 年三月初版；与 1914 沈一帆本的版次关系仍待对校。',
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
    key: 'guangdong', label: '广东麻将', short: '广东',
    categories: ['platform', 'mil'], accent: '#0f766e',
    description: 'MIL 推倒和2024无癞子标准本。136张，可吃碰杠，报听可选，头跳；23番种，32番封顶另加2底分。',
    resources: [
      { title: '推倒和 MIL 2024 原书', url: '/rulebooks/mil/推倒和麻将（推广）竞赛规则（试行2024版）.pdf', filename: '推倒和麻将（推广）竞赛规则（试行2024版）.pdf' },
    ],
  },
  {
    key: 'hongkong',
    label: '香港麻将',
    short: '港麻',
    categories: ['platform', 'local'],
    description: '清章十三、新章十三（Wiki）、新章十三（恋绘色）、新章十三（恋绘色魔改）、新章十六，各有独立规则书及来源说明。',
    accent: '#c0904b',
    resources: hongKongRulebooks,
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
    key: 'zhongyong',
    label: '中庸麻将',
    short: '中庸',
    categories: ['platform'],
    description: '标准中庸采用关兆豪的中庸 v3.3 计分法，136张牌，无起和限制，同系列取最高和种，不同系列相加。南雀作为子规则，采用独立计分表与三人和牌的血战到底流程。',
    accent: '#64748b',
    resources: [{
        title: '中庸麻将与南雀规则说明',
        desc: '标准中庸的计分与支付方法，以及南雀血战到底的差异。',
        url: '/rulebooks/zhongyong.html',
        filename: '中庸麻将与南雀规则说明.html',
      }],
  },
  {
    key: 'jiandan',
    label: '南雀',
    short: '南雀',
    categories: [],
    description:
      '南雀规则由南瓜饼编写，现为中庸麻将的子规则。无起和限制，默认血战到底，和牌者退场，三家和牌或牌墙耗尽后统一结算。',
    accent: '#64748b',
    resources: [],
  },
  {
    key: 'shanxi', label: '山西麻将', short: '山西', categories: ['platform', 'local', 'mil'],
    description: 'MIL试行2023版，136张、十三张手牌。无癞子、不吃牌；暗扣报听、三点自摸、六点点和、有和必和，和牌时结杠。',
    accent: '#b7791f', resources: [
      { title: 'MIL山西麻将试行2023版', url: '/rulebooks/mil/山西麻将（推广）竞赛规则（试行2023版）.pdf', filename: '山西麻将（推广）竞赛规则（试行2023版）.pdf' },
    ],
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
        desc: '第4版。平台采用四人赛制，136张无花，5分起和、100分封顶。',
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
    const platformMilFiles = new Set(['mil-sichuan', 'mil-mcr', 'mil-riichi', 'mil-tuidao', 'mil-shanxi', 'mil-red-center', 'mil-changchun'])
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
  description: '从宁波早期见证核查规则变化，区分已证改法、结构比较与待证传承。',
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
