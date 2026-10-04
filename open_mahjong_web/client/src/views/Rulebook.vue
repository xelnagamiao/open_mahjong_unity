<!-- 规则书：按规则分页签展示，可外链 PDF / 在线阅读 -->
<template>
  <div class="rulebook">
    <header class="page-banner">
      <h1>规则书</h1>
      <p>查阅各类麻将规则的说明书、牌例或文档</p>
    </header>

    <div class="panel">
      <div class="tab-bar">
        <button
          v-for="rule in rules"
          :key="rule.key"
          :class="['tab-pill', { 'is-active': activeKey === rule.key }]"
          @click="setActive(rule.key)"
        >
          {{ rule.label }}
        </button>
      </div>

      <transition name="fade-slide" mode="out-in">
        <section :key="active.key" class="rule-section">
          <div class="rule-intro">
            <h2>{{ active.label }}</h2>
            <p>{{ active.description }}</p>
          </div>

          <div v-if="active.key === 'hongkong'" class="tab-bar" aria-label="香港麻将子规则">
            <button v-for="profile in hongKongRulebooks" :key="profile.subRule"
              :class="['tab-pill', { 'is-active': selectedHongKong.subRule === profile.subRule }]"
              :aria-pressed="selectedHongKong.subRule === profile.subRule"
              @click="setHongKongProfile(profile.subRule)">{{ profile.label }}</button>
          </div>

          <div class="docs-grid">
            <div
              v-for="doc in activeDocs"
              :key="doc.url"
              class="doc-card"
            >
              <div class="doc-card-header">
                <h3>{{ doc.title }}</h3>
              </div>
              <p v-if="doc.desc" class="doc-desc">{{ doc.desc }}</p>
              <p v-if="doc.readHint" class="doc-hint">{{ doc.readHint }}</p>
              <div class="doc-actions">
                <el-button tag="a" type="primary" size="small" :href="doc.url" target="_blank" rel="noopener noreferrer">
                  {{ doc.readLabel || (doc.filename?.endsWith('.docx') ? '获取规则文档' : '在新标签页阅读') }}
                </el-button>
                <el-button v-if="doc.filename" size="small" @click="downloadDoc(doc.url, doc.filename)">
                  {{ doc.filename.endsWith('.docx') ? '下载 Word' : doc.filename.endsWith('.html') ? '下载规则' : '下载 PDF' }}
                </el-button>
              </div>
            </div>
          </div>
        </section>
      </transition>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { hongKongRulebooks, hongKongRulebook } from '../constants/hongKongRulebooks.js'

const route = useRoute()
const router = useRouter()

const rules = [
  {
    key: 'guobiao',
    label: '国标麻将',
    description: '国标麻将规则资料，包含 Natsuki 编著的《新编 MCR》及各改编版本规则书。',
    docs: [
      {
        title: '国标麻将（新编MCR）',
        desc: 'Natsuki 编著的《新编 MCR》。',
        url: '/rulebooks/guobiao-mcr.pdf',
        filename: '新编MCR.pdf'
      },
      {
        title: '国标麻将（小林改）',
        desc: '小林改版国标麻将（2026）：取消 8 番起胡和底分，点和×2，自摸番三。',
        url: '/rulebooks/guobiao-kobayashi.pdf',
        filename: '中国麻将（小林改版）规则书.pdf'
      },
      {
        title: 'K神麻将',
        desc: '国标K神改。',
        url: '/rulebooks/guobiao-kshen.pdf',
        filename: 'K神麻雀规则_v2.63版_规范说明书.pdf'
      }
    ]
  },
  {
    key: 'guobiao-lanshi',
    label: '蓝十改',
    description: '采用《蓝十魔改规则第4版》的四人赛制：136张无花，5分起和、100分封顶，半全铳半分付。PDF中的三人赛制仅供阅读，本平台当前只提供四人房。',
    docs: [{title: '蓝十魔改规则第4版', desc: '四人规则与番种牌例；线下赛事编排、裁判与纪律条款供参考。', url: '/rulebooks/guobiao-lanshi.pdf', filename: '蓝十魔改规则第4版.pdf'}]
  },
  {
    key: 'riichi',
    label: '立直麻将',
    description: '四人立直麻将，支持多种规则预设及役种、计分和流局设置',

    docs: [
      {
        title: 'GGHK 立直麻将规则书',
        desc: '香港麻将协会发布的立直麻将规则书',
        url: '/rulebooks/riichi-rulebook.pdf',
        filename: 'GGHK-Riichi-Mahjong-Rulebook-CN.pdf'
      }
    ]
  },
  {
    key: 'qingque',
    label: '青雀',
    description: '青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照；如在测试中发现设计问题或有任何建议，可以联系规则制定人莫莫柴Q1107574，提交bug可在群906497522提交',

    docs: [
      {
        title: '青雀一页纸',
        desc: '青雀第十四版一页纸，来自 mmcr.online。',
        url: '/rulebooks/qingque-onepage.pdf',
        filename: '青雀一页纸 14.pdf'
      },
      {
        title: '青雀牌例',
        desc: '青雀牌例第三版第一次修订，适用于第十四版，来自 mmcr.online。',
        url: '/rulebooks/qingque-paili.pdf',
        filename: '青雀牌例 3.1.pdf'
      },
      {
        title: '青雀规则文档',
        desc: '青雀第十四版第一次修订，来自 mmcr.online。',
        url: '/rulebooks/qingque-rulebook.pdf',
        filename: '青雀 14.1.pdf'
      }
    ]
  },
  {
    key: 'sichuan',
    label: '四川麻将',
    description: '四川麻将包含血战到底，以及血流成河的弃三张、换三张玩法。',

    docs: [
      {
        title: '四川麻将（SBR）竞赛规则',
        desc: '四川麻将（SBR）竞赛规则（试行 2025 版）。',
        url: '/rulebooks/sichuan-sbr.pdf',
        filename: '四川麻将（SBR）竞赛规则（试行2025版）.pdf'
      },
      {
        title: '血流成河：弃三张与换三张',
        desc: '开局选牌、连续和牌、花区展示、当前番表与局终查叫。',
        url: '/rulebooks/xueliu.html',
        filename: '血流成河规则.html'
      }
    ]
  },
  {
    key: 'changsha',
    label: '长沙麻将',
    description: '长沙麻将经典双鸟规则：108张数牌，可吃上家牌，258将小胡，大胡可叠加，和牌后翻两只鸟并按座位中鸟加倍。',

    docs: [
      {
        title: '长沙麻将（双鸟）规则书',
        desc: '本平台长沙麻将的规则说明（0709 更新）。',
        url: '/rulebooks/changsha-classic-double-bird-rulebook.pdf',
        filename: '长沙麻将规则书-0709更新.pdf'
      }
    ]
  },
  {
    key: 'guangdong', label: '广东麻将', short: '广东',
    categories: ['platform', 'mil'], accent: '#0f766e',
    description: 'MIL 推倒和2024无癞子标准本。136张，可吃碰杠，报听可选，头跳；23番种，32番封顶另加2底分。',
    docs: [
      { title: '推倒和 MIL 2024 原书', url: '/rulebooks/mil/推倒和麻将（推广）竞赛规则（试行2024版）.pdf', filename: '推倒和麻将（推广）竞赛规则（试行2024版）.pdf' },
    ],
  },
  {
    key: 'changchun', label: '长春麻将', short: '长春', categories: ['platform', 'mil'], accent: '#5c779a',
    description: 'MIL 长春2024：136张、十三张手牌，三门带幺九，可吃碰杠、报听看宝。一条仅在特殊杠中代牌；六番封顶，流局保留杠分。',
    docs: [{ title: 'MIL 长春2024原文', url: '/rulebooks/mil/长春麻将（推广）竞赛规则（试行2024版）.pdf' }, { title: '平台补则', url: '/rulebooks/changchun.html' }],
  },
  {
    key: 'hongzhong', label: '红中麻将', short: '红中', categories: ['platform', 'mil'], accent: '#b74b46',
    description: 'MIL 红中麻将（推广）2024：112张、十三张手牌，红中为万能牌；仅自摸，可碰杠，不吃。最高四番，和后扎两鸟，流局退杠。',
    resources: [{ title: 'MIL 红中麻将2024原文', url: '/rulebooks/mil/红中麻将（推广）竞赛规则（试行2024版）.pdf' }],
  },
  {
    key: 'hangzhou', label: '杭州麻将', short: '杭州', categories: ['platform', 'mil'], accent: '#1f8a6a',
    description: 'MIL 杭州麻将（推广）2025：136张、白板财神，仅自摸；爆头、财飘、七对、十风，4番封顶，老庄2/4/8倍、三吃承包，墙尾20张流局。',
    docs: [{ title: 'MIL 杭州麻将2025原文', url: '/rulebooks/mil/杭州麻将（推广）竞赛规则（试行2025版）.pdf' }],
  },
  {
    key: 'wenzhou', label: '温州麻将', short: '温州', categories: ['platform', 'local', 'mil'], accent: '#297b74',
    description: 'MIL 温州2024：136张、十六张手牌，每局翻财；白板固定代财神本牌，可吃碰杠和点和，八对加单张、三财、软硬和及连庄。',
    docs: [{ title: 'MIL 温州麻将2024原文', url: '/rulebooks/mil/温州麻将（试点）竞赛规则（试行2024版）.pdf' }, { title: '温州2024平台补则', url: '/rulebooks/wenzhou/MIL2024-platform-supplement.txt' }],
  },
  {
    key: 'yixing', label: '宜兴麻将', short: '宜兴', categories: ['platform', 'local'], accent: '#537c69',
    description: "宜兴麻将是江苏宜兴本地的特色玩法，由144张牌组成，其中万条筒各36张，东南西北中发白各4张，花牌8张，2花自摸，3花放冲，一花独吊，最先将手牌全部组成顺子和刻子的玩家赢得一局，起手花牌数能决定你当前牌局打法规划，牌局种类门清，碰碰胡，混一色，清一色等常见大牌，还包括独吊翻倍，杠开翻倍，海底翻倍，抢杠翻3倍等特殊机制，游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加Q541784531一同交流",
    docs: [
      { title: '宜兴规则书与平台补则', url: '/rulebooks/yixing.html' },
      { title: '宜兴麻将规则书（最新 Word）', url: '/rulebooks/yixing-rulebook.docx', filename: '宜兴麻将规则.docx' },
      { title: '宜兴麻将规则书（PDF）', url: '/rulebooks/yixing-rulebook.pdf' },
      { title: '宜兴麻将规则书（DOC）', url: '/rulebooks/yixing-rulebook.doc', filename: '宜兴麻将规则.doc' },
    ],
  },
  {
    key: 'guizhou', label: '贵州麻将',
    description: 'MIL 贵州麻将（推广）2023：无花无癞子、不吃、开局报听、捉鸡和局终鸡杠结算。',
    docs: [
      { title: 'MIL 贵州麻将2023原文', url: '/rulebooks/mil/贵州麻将（推广）竞赛规则（试行2023版）.pdf' },
    ],
  },
  {
    key: 'hongkong',
    label: '香港麻将',
    description: '请选择与房间一致的子规则，阅读对应的规则书原文。',
    docs: hongKongRulebooks,
  },
  {
    key: 'taiwan',
    label: '台湾麻将',
    description: '台湾麻将：使用144张牌与16张手牌，按台计分，支持公开报听、食替限制与八仙过海等规则。具体流程与台表可在馆规设置中选择。',

    docs: [
      {
        title: '台湾麻将台数表',
        desc: '本平台台湾麻将采用的台数参考表。',
        url: '/rulebooks/taiwan-yaku-table.pdf',
        filename: '台湾麻将台数表.pdf'
      }
    ]
  },
  {
    key: 'zhongyong',
    label: '中庸麻将',
    description: '标准中庸采用关兆豪的中庸 v3.3 计分法，136张牌，无起和限制，同系列取最高和种，不同系列相加。南雀作为子规则，采用独立计分表与三人和牌的血战到底流程。',
    docs: [{
        title: '中庸麻将与南雀规则说明',
        desc: '标准中庸的计分与支付方法，以及南雀血战到底的差异。',
        url: '/rulebooks/zhongyong.html',
        filename: '中庸麻将与南雀规则说明.html',
      }]
  },
  {
    key: 'shanghai',
    label: '上海麻将',
    description: '上海敲麻：上海特色麻将规则，使用144张麻将牌，有着中发白当花、可以垃圾和、听牌后要敲牌报听、番种简单等特点，节奏快且易上手。上海清混碰：上海传统麻将规则，使用144张麻将牌，以必须做出清、混一色或碰碰和才能和牌为特色，与快节奏的上海敲麻有着鲜明对比，独具特色。',
    docs: [{
      title: 'MIL 上海麻将（推广）竞赛规则（试行2024版）',
      desc: '国际麻将联盟（MIL）规则委员会审定的上海敲麻规则书。',
      url: '/rulebooks/shanghai-qiaoma-2024.pdf',
      filename: '上海麻将（推广）竞赛规则（试行2024版）.pdf'
    }, {
      title: '上海清混碰规则',
      desc: '上海清混碰：上海传统麻将规则，使用144张麻将牌，以必须做出清、混一色或碰碰和才能和牌为特色，与快节奏的上海敲麻有着鲜明对比，独具特色。',
      url: '/rulebooks/shanghai-qinghunpeng.docx',
      filename: '上海清混碰规则.docx'
    }]
  },
  {
    key: 'shiyangjin',
    label: '十样锦麻将',
    description: '尚未实装该规则，此处仅提供规则书查阅。',

    docs: [
      {
        title: '十样锦麻将规则书',
        desc: '十样锦麻将规则说明。',
        url: '/rulebooks/shiyangjin.pdf',
        filename: '十样锦麻将规则书.pdf'
      }
    ]
  },
  {
    key: 'classical',
    label: '古典麻将',
    description: '本规则为根据《绘图麻雀牌谱》《想定宁波规则》等书籍文献资料汇总而成的，试图还原1920年代左右或以前的早期麻将样貌的麻将规则。相比现代规则，古典麻雀有番种体系简单、重刻杠幺九、未和牌家计分等特点，具有独特风味。',

    docs: [
      {
        title: '古典麻将',
        desc: '平台现行的古典麻将版本。',
        url: '/rulebooks/classical-rulebook.pdf',
        filename: '古典麻将规则.pdf'
      }
    ]
  },
  {
    key: 'hongque',
    label: '虹雀²',
    description: '虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群497685219一同交流。',

    docs: [
      {
        title: '虹雀² v1.6 规则书',
        desc: '虹雀² v1.6 完整规则说明。',
        url: '/rulebooks/hongque-v1.6.pdf',
        filename: '虹雀² v1.6.pdf'
      }
    ]
  }
]

const initialKey = (() => {
  const k = route.params.rule
  if (k && rules.some(r => r.key === k)) return k
  return 'guobiao'
})()

const activeKey = ref(initialKey)
const active = computed(() => rules.find(r => r.key === activeKey.value) || rules[0])
const selectedHongKong = computed(() => hongKongRulebook({
  sub_rule: route.query.sub_rule,
  hk_new13_version: route.query.new13_version,
}))
const activeDocs = computed(() => active.value.key === 'hongkong' ? [selectedHongKong.value] : active.value.docs)
const setHongKongProfile = (subRule) => {
  router.replace({ name: 'Rulebook', params: { rule: 'hongkong' }, query: { sub_rule: subRule } })
}

const setActive = (key) => {
  if (activeKey.value === key) return
  activeKey.value = key
  router.replace({ name: 'Rulebook', params: { rule: key } })
}

watch(() => route.params.rule, (rule) => {
  if (!rule) return
  if (rule !== activeKey.value && rules.some(r => r.key === rule)) {
    activeKey.value = rule
  }
})

const downloadDoc = (url, filename) => {
  const a = document.createElement('a')
  a.href = url
  a.download = filename || ''
  a.target = '_blank'
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}
</script>

<style scoped>
.rulebook {
  --accent: #a78bfa;
  --accent-deep: #7c5fd4;
  color: #333;
}

.page-banner {
  background: var(--accent);
  color: #fff;
  padding: 22px 20px;
  margin-bottom: 0;
}

.page-banner h1 {
  margin: 0 0 6px;
  font-size: 1.45rem;
  font-weight: 700;
}

.page-banner p {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  opacity: 0.95;
}

.panel {
  background: #fff;
  border: 1px solid #e0e0e0;
  border-top: 0;
  padding: 16px;
}

.tab-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 16px;
  padding-bottom: 12px;
  border-bottom: 1px solid #eee;
}

.tab-pill {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  border: 1px solid #e0e0e0;
  background: #fafafa;
  color: #555;
  font-size: 13px;
  cursor: pointer;
  font-family: inherit;
}

.tab-pill:hover {
  border-color: var(--accent);
  color: var(--accent-deep);
}

.tab-pill.is-active {
  background: var(--accent);
  border-color: var(--accent);
  color: #fff;
  font-weight: 600;
}

.rule-section {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.rule-intro h2 {
  margin: 0 0 6px;
  font-size: 1.15rem;
  font-weight: 700;
  color: #222;
}

.rule-intro p {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: #666;
}

.docs-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 12px;
}

.doc-card {
  background: #fafafa;
  border: 1px solid #eee;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.doc-card:hover {
  border-color: #d4c4ff;
  background: #f8f5ff;
}

.doc-card-header h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: #222;
}

.doc-desc {
  margin: 0;
  color: #666;
  font-size: 13px;
  line-height: 1.55;
  flex: 1;
}

.doc-actions {
  display: flex;
  gap: 8px;
  margin-top: auto;
  flex-wrap: wrap;
}

.doc-hint { margin: 0; color: #666; font-size: 13px; line-height: 1.55; }

.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.fade-slide-enter-from {
  opacity: 0;
  transform: translateY(6px);
}
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
