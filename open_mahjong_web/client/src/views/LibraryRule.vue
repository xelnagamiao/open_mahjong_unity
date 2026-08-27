<!-- 麻雀图书馆 · 规则条目（谱系 + 规则书 + 资料 + 讨论） -->
<template>
  <div v-if="entry" class="rule" :style="{ '--ink': entry.accent || '#1f6b52' }">
    <div class="rule-bg" aria-hidden="true">
      <div class="grain" />
    </div>

    <header class="top">
      <router-link class="back" to="/library">← 馆藏目录</router-link>
      <div class="title-block">
        <p class="eyebrow">{{ isSpecial ? 'LIBRARY TAG' : 'RULE ENTRY' }}</p>
        <h1>{{ entry.title || entry.label }}</h1>
        <p v-if="entry.description || row?.blurb" class="desc">{{ entry.description || row.blurb }}</p>
        <p v-if="row?.names?.length" class="aka">也叫 {{ row.names.join('、') }}</p>
      </div>
    </header>

    <main class="body">
      <template v-if="isSpecial">
        <section class="block resources">
          <div class="block-head">
            <h2>资源存放处</h2>
            <span>{{ (entry.resources?.length || entry.links?.length || 0) }} 项</span>
          </div>
          <div v-if="entry.links?.length" class="res-grid">
            <article v-for="link in entry.links" :key="link.to" class="res-card">
              <h3>{{ link.title }}</h3>
              <p v-if="link.desc">{{ link.desc }}</p>
              <div class="res-actions">
                <router-link class="btn primary" :to="link.to">前往</router-link>
              </div>
            </article>
          </div>
        </section>
      </template>
      <RuleIdentityPanel
        v-else
        :slug="catalogSlug"
        :stub="row"
        :docs="docs"
        :sources="sources"
        :parents="parents"
        :children="children"
        :families="families"
        :era-info="eraInfo"
        :appeared="appeared"
        :playable="playable"
        :href-for="ruleHref"
        :name-for="ruleName"
      />

      <section class="block forum">
        <LibraryDiscussion :topic-key="topicKey" title="讨论区" />
      </section>
    </main>
  </div>

  <div v-else-if="!loaded" class="missing">
    <p>{{ loadError || '加载中…' }}</p>
  </div>

  <div v-else class="missing">
    <p>{{ loadError || '未找到该条目。' }}</p>
    <router-link to="/library">返回馆藏目录</router-link>
  </div>
</template>

<script setup>
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import LibraryDiscussion from '@/components/LibraryDiscussion.vue'
import RuleIdentityPanel from '@/components/RuleIdentityPanel.vue'
import { useMahjongCatalog } from '@/composables/useMahjongCatalog'
import {
  getLibraryRule,
  LIBRARY_MATERIALS,
  LIBRARY_SUBMISSION,
} from '@/constants/libraryRules'

const route = useRoute()
const router = useRouter()
const {
  load,
  loaded,
  loadError,
  catalogRow,
  libraryEntry,
  ruleHref,
  ruleName,
  parentsOf,
  childrenOf,
  familiesOf,
  eraOf,
  appearedOf,
  sourcesFor,
  isPlayable,
  isArchiveOnly,
} = useMahjongCatalog()

const ruleKey = computed(() => String(route.params.rule || ''))
const specialTag = computed(() => {
  if (ruleKey.value === 'materials') return LIBRARY_MATERIALS[0] || null
  if (ruleKey.value === 'submit') return LIBRARY_SUBMISSION[0] || null
  return null
})
const isSpecial = computed(() => !!specialTag.value)
const lib = computed(() => libraryEntry(ruleKey.value) || getLibraryRule(ruleKey.value))
const row = computed(() => catalogRow(ruleKey.value))
const catalogSlug = computed(() => row.value?.slug || ruleKey.value)
const topicKey = computed(() => lib.value?.key || row.value?.library_key || catalogSlug.value)
const docs = computed(() => {
  const fromLib = lib.value?.resources || []
  if (fromLib.length) return fromLib
  return sources.value
    .filter((s) => s.type === 'rulebook')
    .map((s) => ({
      title: s.title,
      desc: s.excerpt || '',
      url: s.url,
      filename: '',
    }))
})
const sources = computed(() => (isSpecial.value ? [] : sourcesFor(ruleKey.value)))
const parents = computed(() => parentsOf(catalogSlug.value))
const children = computed(() => childrenOf(catalogSlug.value))
const families = computed(() => familiesOf(catalogSlug.value))
const eraInfo = computed(() => eraOf(catalogSlug.value))
const appeared = computed(() => appearedOf(catalogSlug.value))
const playable = computed(() => isPlayable(ruleKey.value))

const catalogEntry = computed(() => {
  if (!row.value) return null
  return {
    key: topicKey.value,
    title: lib.value?.label || row.value.name_zh,
    label: lib.value?.label || row.value.name_zh,
    description: lib.value?.description || row.value.blurb || '',
    accent: lib.value?.accent || '#1f6b52',
  }
})

const entry = computed(() => specialTag.value || lib.value || catalogEntry.value)

watch(
  entry,
  (e) => {
    if (e) document.title = `${e.title || e.label} · 麻雀图书馆`
  },
  { immediate: true },
)

watch(
  [ruleKey, loaded, row],
  () => {
    const key = ruleKey.value
    if (!key) return
    if (isArchiveOnly(key)) {
      router.replace(`/rule-research/${key}`)
      return
    }
    if (row.value?.enter_href) {
      router.replace(row.value.enter_href)
      return
    }
    if (row.value?.fold_into && row.value.fold_into !== key) {
      router.replace(`/library/${row.value.fold_into}`)
    }
  },
  { immediate: true },
)

onMounted(() => {
  load()
})
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=Noto+Serif+SC:wght@600;700&display=swap');

.rule {
  --paper: #dfeae4;
  --ink-base: #102820;
  --ink-soft: #3d5a4c;
  --line: rgba(16, 40, 32, 0.14);
  --ink: #1f6b52;
  --accent: var(--ink);
  --muted: var(--ink-soft);
  position: relative;
  min-height: 100vh;
  color: var(--ink-base);
  background: var(--paper);
  font-family: 'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', sans-serif;
}

.rule-bg {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background:
    radial-gradient(ellipse 70% 45% at 90% 0%, color-mix(in srgb, var(--ink) 22%, transparent), transparent 55%),
    linear-gradient(180deg, #e8f1eb 0%, var(--paper) 40%, #d8e6de 100%);
}

.grain {
  position: absolute;
  inset: 0;
  opacity: 0.3;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='0.45'/%3E%3C/svg%3E");
  mix-blend-mode: multiply;
}

.top,
.body {
  position: relative;
  z-index: 1;
  max-width: 920px;
  margin: 0 auto;
  padding-left: clamp(18px, 4vw, 32px);
  padding-right: clamp(18px, 4vw, 32px);
}

.top {
  padding-top: 28px;
  padding-bottom: 8px;
  animation: rise 0.55s ease both;
}

.back {
  display: inline-block;
  color: var(--ink-soft);
  text-decoration: none;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  margin-bottom: 18px;
}

.back:hover {
  color: var(--ink);
}

.eyebrow {
  margin: 0 0 8px;
  font-size: 11px;
  letter-spacing: 0.24em;
  color: var(--ink);
  font-weight: 600;
}

.title-block h1 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: clamp(1.8rem, 5vw, 2.5rem);
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.15;
}

.desc {
  margin: 12px 0 0;
  max-width: 42em;
  font-size: 14px;
  line-height: 1.65;
  color: var(--ink-soft);
}

.aka {
  margin: 8px 0 0;
  font-size: 13px;
  color: var(--ink-soft);
}

.body {
  padding-top: 22px;
  padding-bottom: 48px;
  display: flex;
  flex-direction: column;
  gap: 22px;
}

.block {
  animation: rise 0.6s ease both;
}

.block-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line);
}

.block-head h2 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: 0.08em;
}

.block-head span {
  font-size: 11px;
  color: var(--ink-soft);
  letter-spacing: 0.06em;
}

.res-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 10px;
}

.res-card {
  background: rgba(255, 255, 255, 0.58);
  border: 1px solid var(--line);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: transform 0.15s ease, background 0.15s ease;
}

.res-card:hover {
  background: #fff;
  transform: translateY(-2px);
}

.res-card h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 650;
}

.res-card p {
  margin: 0;
  flex: 1;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ink-soft);
}

.res-actions {
  display: flex;
  gap: 8px;
}

.btn {
  appearance: none;
  border: 1px solid var(--line);
  background: rgba(255, 255, 255, 0.7);
  color: var(--ink-base);
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  padding: 6px 12px;
  cursor: pointer;
  text-decoration: none;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.btn:hover:not(:disabled) {
  border-color: var(--ink);
  color: var(--ink);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: var(--ink);
  border-color: var(--ink);
  color: #fff;
}

.btn.primary:hover:not(:disabled) {
  filter: brightness(1.08);
  color: #fff;
}

.missing {
  min-height: 100vh;
  display: grid;
  place-content: center;
  gap: 10px;
  text-align: center;
  background: #dfeae4;
  font-family: 'IBM Plex Sans', sans-serif;
  color: #102820;
}

.missing a {
  color: #1f6b52;
  font-weight: 600;
}

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@supports not (background: color-mix(in srgb, red 10%, white)) {
  .rule-bg {
    background: linear-gradient(180deg, #e8f1eb 0%, #dfeae4 100%);
  }
}
</style>
