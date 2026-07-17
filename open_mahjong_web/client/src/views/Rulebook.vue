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

          <div class="docs-grid">
            <div
              v-for="doc in active.docs"
              :key="doc.url"
              class="doc-card"
            >
              <div class="doc-card-header">
                <h3>{{ doc.title }}</h3>
              </div>
              <p v-if="doc.desc" class="doc-desc">{{ doc.desc }}</p>
              <div class="doc-actions">
                <el-button type="primary" size="small" @click="openInNewTab(doc.url)">
                  在新标签页阅读
                </el-button>
                <el-button size="small" @click="downloadDoc(doc.url, doc.filename)">
                  下载 PDF
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

const route = useRoute()
const router = useRouter()

const rules = [
  {
    key: 'guobiao',
    label: '国标麻将',
    description: '国标麻将指国家体育总局在1998出台的《中国竞技麻将比赛规则(试行)》中确立的麻将规则',
    docs: [
      {
        title: '国标麻将（新编MCR）',
        desc: '本平台使用Natsuki编著的新编MCR规则书设计国标麻将的运行逻辑。',
        url: '/rulebooks/guobiao-mcr.pdf',
        filename: '新编MCR.pdf'
      },
      {
        title: '国标麻将（小林改）',
        desc: '社区修改的一种麻将规则',
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
    key: 'riichi',
    label: '立直麻将',
    description: '立直麻将一般指日本麻将，是麻将规则的一个分支。',

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
    description: '青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照。',

    docs: [
      {
        title: '青雀一页纸',
        desc: '一页纸番种速记。',
        url: '/rulebooks/qingque-onepage.pdf',
        filename: '青雀一页纸 12.5.pdf'
      },
      {
        title: '青雀牌例',
        desc: '记录规则中所有番种对应的详解及牌例。',
        url: '/rulebooks/qingque-paili.pdf',
        filename: '青雀牌例 2.8.pdf'
      },
      {
        title: '青雀规则文档',
        desc: '包含青雀规则的行牌逻辑和概念解释。',
        url: '/rulebooks/qingque-rulebook.pdf',
        filename: '青雀 12.5-a1.pdf'
      }
    ]
  },
  {
    key: 'sichuan',
    label: '四川麻将',
    description: 'MIL比赛规则。',

    docs: [
      {
        title: '四川麻将（SBR）竞赛规则',
        desc: '四川麻将（SBR）竞赛规则（试行 2025 版）。',
        url: '/rulebooks/sichuan-sbr.pdf',
        filename: '四川麻将（SBR）竞赛规则（试行2025版）.pdf'
      }
    ]
  },
  {
    key: 'changsha',
    label: '长沙麻将',
    description: '长沙麻将是湖南地区流行的麻将玩法。',

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
  }
]

const initialKey = (() => {
  const k = route.params.rule
  if (k && rules.some(r => r.key === k)) return k
  return 'guobiao'
})()

const activeKey = ref(initialKey)
const active = computed(() => rules.find(r => r.key === activeKey.value) || rules[0])

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

const openInNewTab = (url) => {
  window.open(url, '_blank')
}

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
