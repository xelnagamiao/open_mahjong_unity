<!-- 规则资料搜集：史料簿索引。各规则条目已并入麻雀图书馆「麻将」栏。 -->
<template>
  <div class="rr">
    <div class="bg" aria-hidden="true" />

    <header class="head">
      <router-link class="back" to="/library">← 麻雀图书馆</router-link>
      <p class="eyebrow">RULE RESEARCH ARCHIVE</p>
      <template v-if="!slug">
        <h1>规则资料搜集</h1>
        <p class="lede">
          这里只放史料簿原文。谱系图见
          <router-link to="/library/lineage">年代表</router-link>
          与
          <router-link to="/library/lineage/related">关系表</router-link>，
          各规则条目见
          <router-link to="/library#sec-mahjong">麻雀图书馆「麻将」栏</router-link>
          与
          <router-link to="/library#sec-categorized">「归类规则」</router-link>。
        </p>
      </template>
      <template v-else>
        <h1>{{ data?.label || slug }}</h1>
        <p v-if="data?.notes" class="notes">{{ data.notes }}</p>
        <p v-if="data" class="meta">
          <span>{{ data.sources?.length || 0 }} 条原文</span>
          <span v-if="data.collected_at">搜集于 {{ data.collected_at }}</span>
        </p>
      </template>
    </header>

    <main v-if="!slug" class="main">
      <div v-if="indexError" class="empty">{{ indexError }}</div>
      <div v-else-if="!indexRules.length" class="empty">加载中…</div>
      <ul v-else class="index-list">
        <li v-for="r in indexRules" :key="r.slug">
          <router-link :to="archiveHref(r.slug)">
            <strong>{{ r.label }}</strong>
            <span>{{ r.count }} 条 · {{ r.collected_at }}</span>
          </router-link>
        </li>
      </ul>
    </main>

    <main v-else class="main">
      <div v-if="loadError" class="empty">{{ loadError }}</div>
      <div v-else-if="!data" class="empty">加载中…</div>
      <template v-else>
        <template v-if="data.sources?.length">
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
            <label v-if="ruleOptions.length">
              规则标签
              <select v-model="ruleFilter">
                <option value="">全部</option>
                <option v-for="r in ruleOptions" :key="r" :value="r">{{ ruleLabel(r) }}</option>
              </select>
            </label>
            <span class="count">原文 {{ filtered.length }} / {{ data.sources.length }}</span>
          </div>

          <article v-for="s in filtered" :id="s.id" :key="s.id" class="card">
            <div class="card-top">
              <span class="type" :data-type="s.type">{{ typeLabel(s.type) }}</span>
              <span class="lang">{{ s.lang }}</span>
              <span class="date">{{ s.accessed }}</span>
              <router-link
                v-for="rid in s.rules || []"
                :key="rid"
                class="rule-tag"
                :to="`/library/${pageKey(rid)}`"
              >{{ ruleLabel(rid) }}</router-link>
            </div>
            <h2>{{ s.title }}</h2>
            <p class="excerpt">{{ s.excerpt }}</p>
            <div class="card-actions">
              <a class="btn" :href="s.url" target="_blank" rel="noopener noreferrer">打开源链接</a>
              <a
                v-if="localView(s)?.kind === 'snapshot'"
                class="btn btn-side"
                :href="localView(s).href"
                target="_blank"
                rel="noopener noreferrer"
              >查看快照</a>
              <a
                v-if="localView(s)?.kind === 'file'"
                class="btn btn-side"
                :href="localView(s).href"
                target="_blank"
                rel="noopener noreferrer"
              >查看文件</a>
            </div>
          </article>
        </template>
        <p v-else class="notes">还没有收到单独的原文档案。</p>
        <p class="card-actions back-row">
          <router-link class="btn btn-side" to="/library/lineage">年代表</router-link>
          <router-link class="btn btn-side" to="/library/lineage/related">关系表</router-link>
        </p>
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
import { useRoute, useRouter } from 'vue-router'
import { tr } from '@/i18n'
import { useMahjongCatalog } from '@/composables/useMahjongCatalog'

const ARCHIVE_ONLY = new Set([
  'mahjong-phylogeny',
  'mahjong-studies',
  'ningbo-classical',
  'chuanyu',
  'yuegang',
  'shiliuzhang',
  'jiangnan-caishen',
  'xiang',
  'dongbei-sanchen',
  'hongzhong-laizi',
  'lizhi',
  'nanyang',
  'american-mj',
  'jingjin',
  'jinshan',
  'qian',
  'qiaoma',
  'suhu-huama',
  'huazhong',
])

const route = useRoute()
const router = useRouter()
const slug = computed(() => route.params.slug || '')
const { load: loadCatalog, catalogRow, pageKey, ruleName } = useMahjongCatalog()

const data = ref(null)
const loadError = ref('')
const indexRules = ref([])
const indexError = ref('')
const typeFilter = ref('')
const langFilter = ref('')
const ruleFilter = ref('')

const TYPE_LABELS = {
  rulebook: '规则书',
  official: '官方',
  history: '历史',
  discussion: '讨论/整理',
  variant: '变体',
  scoring: '计番',
  media: '媒体',
  map: '地图/分布',
  book: '书籍',
}

function ruleLabel(id) {
  return ruleName(id) || catalogRow(id)?.name_zh || id
}

function typeLabel(t) {
  return TYPE_LABELS[t] || t
}

function archiveHref(id) {
  if (id === 'drawing-mahjong') return '/library/classical'
  if (ARCHIVE_ONLY.has(id) || !catalogRow(id)) return `/rule-research/${id}`
  return `/library/${pageKey(id)}`
}

function localView(s) {
  if (!s || s.local_status === 'failed' || !s.local_path) return null
  const p = String(s.local_path).replace(/\\/g, '/')
  let href
  if (/^https?:\/\//i.test(p)) href = p
  else if (p.startsWith('/')) href = p
  else if (p.startsWith('rule-research/')) href = `/${p}`
  else href = `/rule-research/${p}`
  const kind =
    s.local_kind || (/\.(pdf|djvu|zip|png|jpe?g|webp)$/i.test(p) ? 'file' : 'snapshot')
  return { href, kind }
}

const typeOptions = computed(() => {
  if (!data.value?.sources) return []
  return [...new Set(data.value.sources.map((s) => s.type))].sort()
})

const langOptions = computed(() => {
  if (!data.value?.sources) return []
  return [...new Set(data.value.sources.map((s) => s.lang))].sort()
})

const ruleOptions = computed(() => {
  if (!data.value?.sources) return []
  return [...new Set(data.value.sources.flatMap((s) => s.rules || []).filter(Boolean))]
})

const filtered = computed(() => {
  if (!data.value?.sources) return []
  return data.value.sources.filter((s) => {
    if (typeFilter.value && s.type !== typeFilter.value) return false
    if (langFilter.value && s.lang !== langFilter.value) return false
    if (ruleFilter.value && !(s.rules || []).includes(ruleFilter.value)) return false
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
  ruleFilter.value = ''
  try {
    const res = await fetch(`/rule-research/${s}.json`)
    const ct = res.headers.get('content-type') || ''
    if (res.ok && ct.includes('json')) {
      data.value = await res.json()
      return
    }
    throw new Error(res.ok ? 'not json' : `HTTP ${res.status}`)
  } catch (e) {
    loadError.value = `资料加载失败：${e.message}`
  }
}

watch(
  slug,
  async (s) => {
    await loadCatalog()
    if (s && !ARCHIVE_ONLY.has(s) && catalogRow(s)) {
      router.replace(`/library/${pageKey(s)}`)
      return
    }
    if (!s && (route.hash === '#sec-map' || route.hash === '#sec-mahjong')) {
      router.replace('/library/lineage')
      return
    }
    if (s) {
      loadSlug(s)
    } else {
      data.value = null
      loadIndex()
      document.title = tr('规则资料搜集 - salasasa.cn')
    }
  },
  { immediate: true },
)

watch(data, (d) => {
  if (d?.label) document.title = `${tr(d.label)} · ${tr('规则搜集 - salasasa.cn')}`
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
  max-width: 920px;
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
  max-width: 42rem;
}

.lede a,
.notes a {
  color: var(--accent);
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1rem;
  color: var(--muted);
  font-size: 0.9rem;
  margin: 0.5rem 0 0;
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

.type[data-type='history'] { background: #8b6914; }
.type[data-type='discussion'] { background: #4a6670; }
.type[data-type='variant'] { background: #6b4f8a; }
.type[data-type='scoring'] { background: #a35a3a; }
.type[data-type='official'] { background: #1f5c4a; }
.type[data-type='map'] { background: #3d5a80; }
.type[data-type='book'] { background: #6b4f2a; }

.lang,
.date {
  color: var(--muted);
}

.rule-tag {
  border: 1px solid var(--line);
  padding: 0.08rem 0.35rem;
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
}

.rule-tag:hover {
  text-decoration: underline;
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

.btn-side {
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--ink);
}

.btn-side:hover {
  background: var(--ink);
  color: #f5f1e8;
}

.empty {
  padding: 2rem 0;
  color: var(--muted);
}

.back-row {
  margin-top: 1.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--line);
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
