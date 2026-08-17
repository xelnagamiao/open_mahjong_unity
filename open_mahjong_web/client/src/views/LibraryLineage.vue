<!-- 麻雀图书馆 · 麻将谱系 / 关系表 -->
<template>
  <div class="lin">
    <div class="lin-bg" aria-hidden="true">
      <div class="grain" />
    </div>

    <header class="top">
      <router-link class="back" to="/library">← 馆藏目录</router-link>
      <p class="eyebrow">LINEAGE</p>
      <h1>{{ mapTab === 'rel' ? '麻将关系表' : '麻将年代表' }}</h1>
      <p class="lede">{{ mapTab === 'rel' ? relLede : yearLede }}</p>
    </header>

    <main class="body">
      <p v-if="loadError" class="err">{{ loadError }}</p>
      <RuleLineageMap
        :phy="phy"
        :areal="areal"
        :catalog="catalog"
        :tab="mapTab"
        embedded
      />
    </main>
  </div>
</template>

<script setup>
import { computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import RuleLineageMap from '@/components/RuleLineageMap.vue'
import { useMahjongCatalog } from '@/composables/useMahjongCatalog'

const route = useRoute()
const { catalog, phy, areal, loadError, load } = useMahjongCatalog()
const mapTab = computed(() => (route.meta.lineageTab === 'rel' ? 'rel' : 'year'))
const yearLede = '从纸牌、骨牌到各地现行打法，按出现年代排列。点规则名可打开对应条目。'
const relLede = '按张数、吃、字牌、计分、癞子这类结构特征分组，不是按省名；同组不代表玩法完全一样。'

function syncTitle() {
  document.title = route.meta.title || '麻将谱系 · 麻雀图书馆'
}

onMounted(() => {
  syncTitle()
  load()
})
watch(() => route.meta.title, syncTitle)
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=Noto+Serif+SC:wght@600;700&display=swap');

.lin {
  --paper: #dfeae4;
  --ink: #102820;
  --ink-soft: #3d5a4c;
  --muted: var(--ink-soft);
  --line: rgba(16, 40, 32, 0.14);
  --accent: #1f6b52;
  position: relative;
  height: 100vh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  color: var(--ink);
  background: var(--paper);
  font-family: 'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', sans-serif;
}

.lin-bg {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background:
    radial-gradient(ellipse 80% 50% at 10% -10%, rgba(31, 107, 82, 0.18), transparent 55%),
    radial-gradient(ellipse 60% 40% at 100% 20%, rgba(196, 92, 58, 0.1), transparent 50%),
    linear-gradient(180deg, #e8f1eb 0%, var(--paper) 35%, #d8e6de 100%);
}

.grain {
  position: absolute;
  inset: 0;
  opacity: 0.35;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='0.45'/%3E%3C/svg%3E");
  mix-blend-mode: multiply;
}

.top,
.body {
  position: relative;
  z-index: 1;
  width: 100%;
  padding-left: clamp(14px, 2.4vw, 26px);
  padding-right: clamp(14px, 2.4vw, 26px);
}

.top {
  flex: 0 0 auto;
  padding-top: 16px;
  padding-bottom: 6px;
}

.back {
  display: inline-block;
  color: var(--ink-soft);
  text-decoration: none;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  margin-bottom: 8px;
}

.back:hover {
  color: var(--accent);
}

.eyebrow {
  margin: 0 0 4px;
  font-size: 11px;
  letter-spacing: 0.24em;
  color: var(--accent);
  font-weight: 600;
}

h1 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: clamp(1.5rem, 3vw, 1.9rem);
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.15;
}

.lede {
  margin: 6px 0 0;
  max-width: 62em;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--ink-soft);
}

.body {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding-bottom: 10px;
}

.err {
  flex: 0 0 auto;
  margin: 0 0 8px;
  font-size: 13px;
  color: #c45c3a;
}

.body :deep(.map) {
  flex: 1 1 auto;
  min-height: 0;
  margin-top: 0.35rem;
  padding-top: 0;
  border-top: 0;
}
</style>
