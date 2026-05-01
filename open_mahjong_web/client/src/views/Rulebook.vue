<!-- 规则书：按规则分页签展示，可外链 PDF / 在线阅读 -->
<template>
  <div class="rulebook">
    <div class="page-header">
      <h1>规则书</h1>
      <p class="subtitle">查阅各类麻将规则的官方文档与详细牌例</p>
    </div>

    <div class="tab-bar">
      <button
        v-for="rule in rules"
        :key="rule.key"
        :class="['tab-pill', { 'is-active': activeKey === rule.key }]"
        :style="activeKey === rule.key ? { background: rule.gradient } : {}"
        @click="setActive(rule.key)"
      >
        <el-icon class="tab-icon" :size="18">
          <component :is="rule.icon" />
        </el-icon>
        <span>{{ rule.label }}</span>
      </button>
    </div>

    <transition name="fade-slide" mode="out-in">
      <section :key="active.key" class="rule-section">
        <div class="rule-hero" :style="{ '--gradient': active.gradient }">
          <div class="rule-hero-text">
            <h2>{{ active.label }}</h2>
            <p>{{ active.description }}</p>
          </div>
          <el-icon class="rule-hero-icon" :size="40">
            <component :is="active.icon" />
          </el-icon>
        </div>

        <div class="docs-grid">
          <div
            v-for="doc in active.docs"
            :key="doc.url"
            class="doc-card"
          >
            <div class="doc-card-header">
              <el-icon :size="20"><Document /></el-icon>
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
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Trophy, Promotion, Star, Coffee, Document } from '@element-plus/icons-vue'

const route = useRoute()
const router = useRouter()

const rules = [
  {
    key: 'guobiao',
    label: '国标麻将',
    description: '中国国家体育总局正式比赛使用的麻将规则。本平台同时提供「小林改版」社区调整方案。',
    gradient: 'linear-gradient(135deg, #f6d365 0%, #fda085 100%)',
    icon: Trophy,
    docs: [
      {
        title: '新编 MCR 规则书',
        desc: '官方修订版规则与番种说明，含完整算番示例。',
        url: '/rulebooks/guobiao-mcr.pdf',
        filename: '新编MCR.pdf'
      },
      {
        title: '中国麻将（小林改版）规则书',
        desc: '社区平衡修订版：对部分番种进行小幅升降，仍以 MCR 番种为基础。',
        url: '/rulebooks/guobiao-kobayashi.pdf',
        filename: '中国麻将（小林改版）规则书.pdf'
      }
    ]
  },
  {
    key: 'riichi',
    label: '立直麻将',
    description: '日式立直麻将规则，包含役种、符算与立直棒等核心机制说明。',
    gradient: 'linear-gradient(135deg, #a1c4fd 0%, #c2e9fb 100%)',
    icon: Promotion,
    docs: [
      {
        title: 'GGHK 立直麻将规则书（中文）',
        desc: '香港麻将协会发布的中文规则书，对役种与计分讲解清晰。',
        url: '/rulebooks/riichi-rulebook.pdf',
        filename: 'GGHK-Riichi-Mahjong-Rulebook-CN.pdf'
      }
    ]
  },
  {
    key: 'qingque',
    label: '青雀',
    description: '社区设计的轻量化麻将规则，通过一页纸 / 牌例 / 完整规则书三层资料快速上手。',
    gradient: 'linear-gradient(135deg, #84fab0 0%, #8fd3f4 100%)',
    icon: Star,
    docs: [
      {
        title: '青雀一页纸（速查版）',
        desc: '一张纸看完核心机制与术语，适合零基础速记。',
        url: '/rulebooks/qingque-onepage.pdf',
        filename: '青雀一页纸 12.5.pdf'
      },
      {
        title: '青雀牌例',
        desc: '通过实战牌例理解番种和特殊和牌形态。',
        url: '/rulebooks/qingque-paili.pdf',
        filename: '青雀牌例 2.8.pdf'
      },
      {
        title: '青雀完整规则书',
        desc: '包含完整规则、判罚、特殊形态与平台扩展规则。',
        url: '/rulebooks/qingque-rulebook.pdf',
        filename: '青雀 12.5-a1.pdf'
      }
    ]
  },
  {
    key: 'classical',
    label: '古典麻将',
    description: '传统古典麻将玩法，副计分体系，含连庄、庄家幺二（庄家收支双倍）等机制。',
    gradient: 'linear-gradient(135deg, #fbc2eb 0%, #a18cd1 100%)',
    icon: Coffee,
    docs: [
      {
        title: '古典麻将规则书',
        desc: '完整规则与计分逻辑，含连庄、庄家幺二等结算细节。',
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
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.page-header {
  text-align: center;
  margin-bottom: 40px;
  color: white;
}

.page-header h1 {
  font-size: 2.5rem;
  margin: 0 0 10px;
  font-weight: bold;
  color: white;
  text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.3);
}

.page-header .subtitle {
  font-size: 1.1rem;
  color: rgba(255, 255, 255, 0.95);
  margin: 0;
  opacity: 0.95;
}

.tab-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 18px;
}

.tab-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 14px;
  border-radius: 7px;
  border: 1px solid var(--omu-border, #ebeef5);
  background: var(--omu-surface, #fff);
  color: var(--omu-text-soft, #475569);
  font-size: 13.5px;
  cursor: pointer;
  transition: background-color 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.tab-pill:hover {
  background: var(--omu-surface-soft, #f5f7fa);
  border-color: #dcdfe6;
  color: var(--omu-text, #1f2933);
}

.tab-pill.is-active {
  background: #409eff;
  border-color: #409eff;
  color: #fff;
  font-weight: 600;
}

.tab-icon { display: inline-flex; }

.rule-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.rule-hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 22px;
  border-radius: 12px;
  background: var(--omu-surface, #fff);
  border: 1px solid var(--omu-border, #ebeef5);
  color: var(--omu-text, #1f2933);
  position: relative;
  overflow: hidden;
}

.rule-hero::before {
  content: '';
  position: absolute;
  inset: 0;
  background: var(--gradient, transparent);
  opacity: 0.18;
  pointer-events: none;
}

.rule-hero-text { position: relative; z-index: 1; }
.rule-hero-text h2 {
  margin: 0 0 4px;
  font-size: 1.3rem;
  font-weight: 700;
}

.rule-hero-text p {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.6;
  max-width: 720px;
  color: var(--omu-text-soft, #475569);
}

.rule-hero-icon {
  color: var(--omu-text-soft, #475569);
  opacity: 0.6;
  position: relative;
  z-index: 1;
}

.docs-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}

.doc-card {
  background: var(--omu-surface, #fff);
  border-radius: 10px;
  padding: 16px;
  border: 1px solid var(--omu-border, #ebeef5);
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.doc-card:hover {
  border-color: #dcdfe6;
  box-shadow: var(--omu-shadow-md, 0 4px 14px rgba(15, 23, 42, 0.08));
}

.doc-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--omu-text, #1f2933);
}

.doc-card-header h3 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
}

.doc-desc {
  margin: 0;
  color: var(--omu-text-soft, #475569);
  font-size: 13px;
  line-height: 1.55;
}

.doc-actions {
  display: flex;
  gap: 8px;
  margin-top: auto;
  flex-wrap: wrap;
}

.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: opacity 0.22s ease, transform 0.22s ease;
}
.fade-slide-enter-from {
  opacity: 0;
  transform: translateY(8px);
}
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
