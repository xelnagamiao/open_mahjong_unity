<!-- 规则资料搜集归档展示 -->
<template>
  <div class="rr">
    <div class="bg" aria-hidden="true" />

    <header class="head">
      <router-link class="back" to="/library">← 麻雀图书馆</router-link>
      <p class="eyebrow">RULE RESEARCH ARCHIVE</p>
      <template v-if="!slug">
        <h1>规则资料搜集</h1>
        <p class="lede">按源链接与原文摘录归档，不做分析。</p>
      </template>
      <template v-else>
        <h1>{{ data?.label || slug }}</h1>
        <p v-if="data" class="meta">
          <span>{{ data.sources?.length || 0 }} 条</span>
          <span>搜集于 {{ data.collected_at }}</span>
          <span v-if="data.aliases?.length">别名：{{ data.aliases.join('、') }}</span>
        </p>
        <p v-if="data?.notes" class="notes">{{ data.notes }}</p>
      </template>
    </header>

    <!-- 索引 -->
    <main v-if="!slug" class="main">
      <div v-if="indexError" class="empty">{{ indexError }}</div>
      <div v-else-if="!indexRules.length" class="empty">加载中…</div>
      <ul v-else class="index-list">
        <li v-for="r in indexRules" :key="r.slug">
          <router-link :to="`/rule-research/${r.slug}`">
            <strong>{{ r.label }}</strong>
            <span>{{ r.count }} 条 · {{ r.collected_at }}</span>
          </router-link>
        </li>
      </ul>
    </main>

    <!-- 单规则 -->
    <main v-else class="main">
      <div v-if="loadError" class="empty">{{ loadError }}</div>
      <div v-else-if="!data" class="empty">加载中…</div>
      <template v-else>
        <div class="toolbar">
          <label>
            类型
            <select v-model="typeFilter">
              <option value="">全部</option>
              <option v-for="t in typeOptions" :key="t" :value="t">{{ typeLabel(t) }}</option>
            </select>
          </label>
          <label>
            语言
            <select v-model="langFilter">
              <option value="">全部</option>
              <option v-for="l in langOptions" :key="l" :value="l">{{ l }}</option>
            </select>
          </label>
          <span class="count">显示 {{ filtered.length }} / {{ data.sources.length }}</span>
        </div>

        <article
          v-for="s in filtered"
          :id="s.id"
          :key="s.id"
          class="card"
        >
          <div class="card-top">
            <span class="type" :data-type="s.type">{{ typeLabel(s.type) }}</span>
            <span class="lang">{{ s.lang }}</span>
            <span class="date">{{ s.accessed }}</span>
          </div>
          <h2>{{ s.title }}</h2>
          <p class="excerpt">{{ s.excerpt }}</p>
          <div class="card-actions">
            <a class="btn" :href="s.url" target="_blank" rel="noopener noreferrer">打开源链接</a>
            <span v-if="s.local_path" class="local">本地：{{ s.local_path }}</span>
          </div>
        </article>
      </template>
    </main>

    <footer class="foot">
      <router-link to="/rule-research">搜集索引</router-link>
      <span>salasasa · rule research</span>
    </footer>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const slug = computed(() => route.params.slug || '')

const data = ref(null)
const loadError = ref('')
const indexRules = ref([])
const indexError = ref('')
const typeFilter = ref('')
const langFilter = ref('')

const TYPE_LABELS = {
  rulebook: '规则书',
  official: '官方',
  history: '历史',
  discussion: '讨论/整理',
  variant: '变体',
  scoring: '计番',
  media: '媒体',
}

function typeLabel(t) {
  return TYPE_LABELS[t] || t
}

const typeOptions = computed(() => {
  if (!data.value?.sources) return []
  return [...new Set(data.value.sources.map((s) => s.type))].sort()
})

const langOptions = computed(() => {
  if (!data.value?.sources) return []
  return [...new Set(data.value.sources.map((s) => s.lang))].sort()
})

const filtered = computed(() => {
  if (!data.value?.sources) return []
  return data.value.sources.filter((s) => {
    if (typeFilter.value && s.type !== typeFilter.value) return false
    if (langFilter.value && s.lang !== langFilter.value) return false
    return true
  })
})

async function loadIndex() {
  indexError.value = ''
  try {
    const res = await fetch('/rule-research/index.json')
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const json = await res.json()
    indexRules.value = json.rules || []
  } catch (e) {
    indexError.value = `索引加载失败：${e.message}`
  }
}

async function loadSlug(s) {
  data.value = null
  loadError.value = ''
  typeFilter.value = ''
  langFilter.value = ''
  try {
    const res = await fetch(`/rule-research/${s}.json`)
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    data.value = await res.json()
  } catch (e) {
    loadError.value = `资料加载失败：${e.message}`
  }
}

watch(
  slug,
  (s) => {
    if (s) loadSlug(s)
    else {
      data.value = null
      loadIndex()
      document.title = '规则资料搜集 - salasasa.cn'
    }
  },
  { immediate: true },
)

watch(data, (d) => {
  if (d?.label) document.title = `${d.label} · 规则搜集 - salasasa.cn`
})
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=Noto+Serif+SC:wght@500;700&family=DM+Sans:wght@400;500;600&display=swap');

.rr {
  --ink: #1a2e28;
  --paper: #f3efe6;
  --line: #c9bfae;
  --accent: #2f6f5e;
  --muted: #5c6b64;
  min-height: 100vh;
  position: relative;
  color: var(--ink);
  font-family: 'DM Sans', system-ui, sans-serif;
  background: var(--paper);
}

.bg {
  position: fixed;
  inset: 0;
  pointer-events: none;
  background:
    radial-gradient(ellipse 80% 50% at 10% -10%, #d8e8e0 0%, transparent 55%),
    radial-gradient(ellipse 60% 40% at 100% 0%, #e8dcc8 0%, transparent 50%),
    linear-gradient(180deg, #f7f3ea 0%, #ebe4d6 100%);
  z-index: 0;
}

.head,
.main,
.foot {
  position: relative;
  z-index: 1;
  max-width: 820px;
  margin: 0 auto;
  padding: 0 1.25rem;
}

.head {
  padding-top: 2rem;
  padding-bottom: 1.5rem;
}

.back {
  display: inline-block;
  color: var(--accent);
  text-decoration: none;
  font-size: 0.9rem;
  margin-bottom: 1rem;
}

.eyebrow {
  letter-spacing: 0.14em;
  font-size: 0.72rem;
  color: var(--muted);
  margin: 0 0 0.4rem;
}

h1 {
  font-family: 'Noto Serif SC', serif;
  font-weight: 700;
  font-size: clamp(1.8rem, 4vw, 2.4rem);
  margin: 0 0 0.6rem;
  line-height: 1.25;
}

.lede,
.notes {
  color: var(--muted);
  margin: 0;
  line-height: 1.55;
  max-width: 40rem;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1rem;
  color: var(--muted);
  font-size: 0.9rem;
  margin: 0 0 0.5rem;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1.25rem;
  align-items: center;
  margin-bottom: 1.25rem;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--line);
}

.toolbar label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
  color: var(--muted);
}

.toolbar select {
  border: 1px solid var(--line);
  background: #fffef9;
  border-radius: 4px;
  padding: 0.25rem 0.4rem;
  color: var(--ink);
}

.count {
  margin-left: auto;
  font-size: 0.85rem;
  color: var(--muted);
}

.index-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.index-list a {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 0;
  border-bottom: 1px solid var(--line);
  text-decoration: none;
  color: inherit;
}

.index-list a:hover strong {
  color: var(--accent);
}

.index-list span {
  color: var(--muted);
  font-size: 0.9rem;
  white-space: nowrap;
}

.card {
  padding: 1.25rem 0;
  border-bottom: 1px solid var(--line);
  animation: rise 0.45s ease both;
}

.card:nth-child(n + 2) {
  animation-delay: 0.04s;
}

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

.card-top {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 0.45rem;
  font-size: 0.75rem;
}

.type {
  background: var(--accent);
  color: #f5faf7;
  padding: 0.12rem 0.45rem;
  border-radius: 3px;
  font-weight: 600;
}

.type[data-type='history'] {
  background: #8b6914;
}
.type[data-type='discussion'] {
  background: #4a6670;
}
.type[data-type='variant'] {
  background: #6b4f8a;
}
.type[data-type='scoring'] {
  background: #a35a3a;
}
.type[data-type='official'] {
  background: #1f5c4a;
}

.lang,
.date {
  color: var(--muted);
}

.card h2 {
  font-family: 'Noto Serif SC', serif;
  font-size: 1.15rem;
  font-weight: 700;
  margin: 0 0 0.55rem;
  line-height: 1.35;
}

.excerpt {
  margin: 0 0 0.85rem;
  line-height: 1.65;
  color: #2a3833;
  white-space: pre-wrap;
}

.card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
}

.btn {
  display: inline-block;
  padding: 0.35rem 0.75rem;
  background: var(--ink);
  color: #f5f1e8;
  text-decoration: none;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 500;
}

.btn:hover {
  background: var(--accent);
}

.local {
  font-size: 0.8rem;
  color: var(--muted);
}

.empty {
  padding: 2rem 0;
  color: var(--muted);
}

.foot {
  display: flex;
  justify-content: space-between;
  padding: 2.5rem 1.25rem 2rem;
  font-size: 0.85rem;
  color: var(--muted);
}

.foot a {
  color: var(--accent);
  text-decoration: none;
}

@media (max-width: 560px) {
  .index-list a {
    flex-direction: column;
    gap: 0.25rem;
  }
  .count {
    margin-left: 0;
    width: 100%;
  }
}
</style>
