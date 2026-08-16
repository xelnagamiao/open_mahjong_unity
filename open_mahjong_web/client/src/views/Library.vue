<!-- 麻雀图书馆 · 独立馆页索引（底部：主讨论区 + 近期讨论） -->
<template>
  <div class="lib">
    <div class="lib-bg" aria-hidden="true">
      <div class="grain" />
      <div class="rail" />
    </div>

    <header class="hero">
      <p class="eyebrow">OPEN ARCHIVE</p>
      <h1 class="brand">麻雀图书馆</h1>
      <p class="lede">规则书、牌例与讨论。</p>
      <nav class="hero-nav">
        <a
          v-for="sec in sections"
          :key="sec.key"
          class="jump"
          :href="`#sec-${sec.key}`"
          @click.prevent="scrollTo(sec.key)"
        >{{ sec.nav }}</a>
      </nav>
    </header>

    <main class="catalog">
      <section
        v-for="(sec, idx) in sections"
        :id="`sec-${sec.key}`"
        :key="sec.key"
        class="shelf"
        :style="{ '--delay': `${idx * 60}ms` }"
      >
        <div class="shelf-meta">
          <span class="idx">{{ String(idx + 1).padStart(2, '0') }}</span>
          <div>
            <h2>{{ sec.title }}</h2>
            <p>{{ sec.hint }}</p>
          </div>
        </div>
        <div v-if="sec.key === 'materials'" class="tags">
          <router-link v-for="item in LIBRARY_MATERIALS" :key="item.key" class="tag" :to="item.to" :title="item.description" :style="{ '--ink': item.accent }">
            <span class="tag-mark" />
            <span class="tag-text"><strong>{{ item.title }}</strong><em>{{ item.short }}</em></span>
            <span class="tag-go" aria-hidden="true">→</span>
          </router-link>
        </div>
        <div v-else-if="sec.key === 'submit'" class="tags">
          <router-link v-for="item in LIBRARY_SUBMISSION" :key="item.key" class="tag" :to="item.to" :title="item.description" :style="{ '--ink': item.accent }">
            <span class="tag-mark" />
            <span class="tag-text"><strong>{{ item.title }}</strong><em>{{ item.short }}</em></span>
            <span class="tag-go" aria-hidden="true">→</span>
          </router-link>
        </div>
        <div v-else-if="sec.key === 'lineage'" class="lineage-wrap">
          <div class="lineage-btn lineage-split">
            <span class="lineage-mark" />
            <span class="lineage-text">
              <strong>{{ LIBRARY_LINEAGE.title }}</strong>
              <span>{{ LIBRARY_LINEAGE.description }}</span>
            </span>
            <nav class="lineage-links">
              <router-link to="/library/lineage">年代表</router-link>
              <router-link to="/library/lineage/related">关系表</router-link>
            </nav>
          </div>
        </div>
        <div v-else-if="sec.key === 'categorized'" class="family-stack">
          <p v-if="!loaded" class="fam-empty">加载规则目录…</p>
          <div v-for="fam in familyGroups" :key="fam.id" class="fam-block">
            <h3>
              {{ fam.label }}
              <em v-if="fam.region">{{ fam.region }}</em>
            </h3>
            <p
              v-if="fam.relatedness"
              class="clampable"
              :class="{ open: expandedFam.has(fam.id) }"
            >{{ fam.relatedness }}</p>
            <button
              v-if="fam.relatedness && fam.relatedness.length > 60"
              type="button"
              class="fam-toggle"
              @click="toggleFam(fam.id)"
            >{{ expandedFam.has(fam.id) ? '收起' : '展开' }}</button>
            <div class="tags">
              <router-link
                v-for="rule in fam.tags"
                :key="`${fam.id}-${rule.key}`"
                class="tag"
                :to="`/library/${rule.key}`"
                :title="rule.note"
                :style="{ '--ink': rule.accent }"
              >
                <span class="tag-mark" />
                <span class="tag-text">
                  <strong>{{ rule.label }}</strong>
                  <em v-if="rule.short && rule.short !== rule.label">{{ rule.short }}</em>
                </span>
                <span class="tag-go" aria-hidden="true">→</span>
              </router-link>
            </div>
          </div>
        </div>
        <div v-else class="tags">
          <router-link
            v-for="rule in rulesForSection(sec.key)"
            :key="`${sec.key}-${rule.key}`"
            class="tag"
            :to="`/library/${rule.key}`"
            :style="{ '--ink': rule.accent }"
          >
            <span class="tag-mark" />
            <span class="tag-text">
              <strong>{{ rule.label }}</strong>
              <em v-if="rule.short !== rule.label">{{ rule.short }}</em>
            </span>
            <span class="tag-go" aria-hidden="true">→</span>
          </router-link>
        </div>
      </section>
    </main>

    <section id="sec-public" class="bottom">
      <div class="main-disc">
        <LibraryDiscussion topic-key="public" title="主讨论区" />
      </div>

      <aside class="recent">
        <div class="block-head">
          <h2>近期讨论</h2>
        </div>
        <div v-if="recentLoading" class="recent-empty">加载中…</div>
        <div v-else-if="!recent.length" class="recent-empty">还没有帖子。</div>
        <ul v-else class="recent-list">
          <li v-for="item in recent" :key="item.post_id">
            <router-link :to="libraryTopicPath(item.rule_key, item.post_id)">
              <span class="recent-topic">{{ libraryTopicLabel(item.rule_key) }}</span>
              <span class="recent-title">{{ item.title }}</span>
              <span class="recent-meta">
                {{ displayAuthor(item) }} · {{ item.reply_count }} 回复 · {{ shortDate(item.updated_at) }}
              </span>
            </router-link>
          </li>
        </ul>
      </aside>
    </section>

    <footer class="foot">
      <span>Salasasa · Mahjong Library</span>
      <router-link to="/rule-research">规则搜集归档</router-link>
      <router-link to="/">返回主站</router-link>
    </footer>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import LibraryDiscussion from '@/components/LibraryDiscussion.vue'
import { useMahjongCatalog } from '@/composables/useMahjongCatalog'
import {
  LIBRARY_LINEAGE,
  LIBRARY_MATERIALS,
  LIBRARY_SECTIONS,
  LIBRARY_SUBMISSION,
  libraryTopicLabel,
  libraryTopicPath,
  rulesForSection,
} from '@/constants/libraryRules'

const route = useRoute()
const { load, loaded, catalogFamilies } = useMahjongCatalog()
const familyGroups = computed(() => catalogFamilies())
const expandedFam = ref(new Set())

function toggleFam(id) {
  const next = new Set(expandedFam.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expandedFam.value = next
}

const sections = LIBRARY_SECTIONS.map((s) => ({
  ...s,
  nav: ({
    mahjong: '麻将',
    mil: 'MIL',
    categorized: '归类',
    materials: '资料',
    submit: '提交',
    lineage: '谱系',
  })[s.key] || s.title,
}))

const recent = ref([])
const recentLoading = ref(false)

function displayAuthor(row) {
  return row.author_username || (row.author_user_id != null ? `用户${row.author_user_id}` : '匿名')
}

function shortDate(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  const now = new Date()
  return d.toDateString() === now.toDateString()
    ? d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false })
    : d.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' })
}

async function loadRecent() {
  recentLoading.value = true
  try {
    const res = await axios.get('/api/library/posts/recent', { params: { limit: 30 } })
    recent.value = res.data?.data?.items || []
  } catch {
    recent.value = []
  } finally {
    recentLoading.value = false
  }
}

function scrollTo(key) {
  const el = document.getElementById(`sec-${key}`)
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function scrollToTopic() {
  const topic = route.query.topic
  if (topic) {
    scrollTo(topic)
    return
  }
  const hash = route.hash
  if (hash) {
    document.querySelector(hash)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
}

onMounted(async () => {
  load()
  loadRecent()
  await nextTick()
  scrollToTopic()
})

watch(
  () => [route.query.topic, route.hash],
  () => {
    nextTick(scrollToTopic)
  },
)
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=Noto+Serif+SC:wght@600;700&display=swap');

.lib {
  --paper: #dfeae4;
  --paper-deep: #d2e0d8;
  --ink: #102820;
  --ink-soft: #3d5a4c;
  --line: rgba(16, 40, 32, 0.14);
  --accent: #1f6b52;
  --mark: #c45c3a;
  position: relative;
  min-height: 100vh;
  color: var(--ink);
  background: var(--paper);
  font-family: 'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', sans-serif;
  overflow-x: hidden;
}

.lib-bg {
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

.rail {
  position: absolute;
  top: 0;
  bottom: 0;
  left: clamp(12px, 4vw, 40px);
  width: 1px;
  background: linear-gradient(180deg, transparent, var(--line) 12%, var(--line) 88%, transparent);
}

.hero,
.catalog,
.bottom,
.foot {
  position: relative;
  z-index: 1;
  max-width: 920px;
  margin: 0 auto;
  padding-left: clamp(28px, 6vw, 56px);
  padding-right: clamp(18px, 4vw, 32px);
}

.hero {
  padding-top: clamp(48px, 10vh, 96px);
  padding-bottom: 28px;
  animation: rise 0.7s ease both;
}

.eyebrow {
  margin: 0 0 10px;
  font-size: 11px;
  letter-spacing: 0.28em;
  text-transform: uppercase;
  color: var(--accent);
  font-weight: 600;
}

.brand {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: clamp(2.4rem, 7vw, 3.6rem);
  font-weight: 700;
  line-height: 1.05;
  letter-spacing: 0.04em;
  color: var(--ink);
}

.lede {
  margin: 14px 0 0;
  max-width: 28em;
  font-size: 15px;
  line-height: 1.65;
  color: var(--ink-soft);
}

.hero-nav {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 22px;
}

.jump {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  border: 1px solid var(--line);
  background: rgba(255, 255, 255, 0.35);
  color: var(--ink);
  text-decoration: none;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.06em;
  transition: background 0.15s ease, border-color 0.15s ease, transform 0.15s ease;
}

.jump:hover {
  background: rgba(255, 255, 255, 0.7);
  border-color: var(--accent);
  transform: translateY(-1px);
}

.catalog {
  padding-bottom: 8px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.shelf {
  display: grid;
  grid-template-columns: minmax(140px, 200px) 1fr;
  gap: 14px 20px;
  padding: 16px 0 18px;
  border-top: 1px solid var(--line);
  animation: rise 0.65s ease both;
  animation-delay: var(--delay);
}

.shelf-meta {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}

.idx {
  font-family: 'IBM Plex Sans', monospace;
  font-size: 12px;
  font-weight: 600;
  color: var(--mark);
  letter-spacing: 0.08em;
  padding-top: 3px;
}

.shelf-meta h2 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: 0.06em;
}

.shelf-meta p {
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--ink-soft);
}

.family-stack {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.fam-block h3 {
  margin: 0;
  font-family: 'Noto Serif SC', serif;
  font-size: 0.95rem;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.fam-block h3 em {
  font-style: normal;
  font-size: 12px;
  font-weight: 500;
  color: var(--ink-soft);
  margin-left: 0.5rem;
}

.fam-block p {
  margin: 4px 0 8px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ink-soft);
}

.fam-block p.clampable {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  overflow: hidden;
}

.fam-block p.clampable.open {
  display: block;
  -webkit-line-clamp: unset;
  line-clamp: unset;
  overflow: visible;
}

.fam-toggle {
  display: inline-block;
  margin: -4px 0 8px;
  border: 0;
  background: none;
  padding: 0;
  font-size: 11px;
  font-weight: 600;
  color: var(--accent);
  cursor: pointer;
}

.fam-empty {
  margin: 0;
  font-size: 13px;
  color: var(--ink-soft);
}

.tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-content: flex-start;
}

.tag {
  --ink: var(--accent);
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 38px;
  padding: 6px 10px 6px 8px;
  text-decoration: none;
  color: inherit;
  background: rgba(255, 255, 255, 0.55);
  border: 1px solid var(--line);
  border-left: 3px solid var(--ink);
  transition: transform 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}

.tag:hover {
  background: #fff;
  transform: translateY(-2px);
  box-shadow: 0 8px 18px rgba(16, 40, 32, 0.08);
}

.tag-mark {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--ink);
  flex-shrink: 0;
}

.tag-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.tag-text strong {
  font-size: 13px;
  font-weight: 600;
  line-height: 1.2;
}

.tag-text em {
  font-style: normal;
  font-size: 10px;
  color: var(--ink-soft);
  letter-spacing: 0.04em;
}

.tag-go {
  margin-left: 4px;
  font-size: 12px;
  color: var(--ink-soft);
  opacity: 0;
  transform: translateX(-4px);
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.tag:hover .tag-go {
  opacity: 1;
  transform: translateX(0);
}

.lineage-wrap {
  min-width: 0;
}

.lineage-btn {
  display: flex;
  align-items: center;
  gap: 14px;
  width: 100%;
  min-height: 88px;
  padding: 16px 18px;
  text-decoration: none;
  color: inherit;
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid var(--line);
  border-left: 5px solid var(--accent);
  box-sizing: border-box;
  transition: transform 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}

.lineage-btn:hover,
.lineage-split:hover {
  background: #fff;
  transform: translateY(-2px);
  box-shadow: 0 10px 22px rgba(16, 40, 32, 0.1);
}

.lineage-links {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-left: auto;
  flex-shrink: 0;
}

.lineage-links a {
  color: var(--accent);
  text-decoration: none;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.04em;
  padding: 0.28rem 0.7rem;
  border: 1px solid var(--line);
  background: #fff;
}

.lineage-links a:hover {
  background: var(--ink);
  color: #f5f1e8;
  border-color: var(--ink);
}

.lineage-mark {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--accent);
  flex-shrink: 0;
}

.lineage-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  flex: 1;
}

.lineage-text strong {
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: 1.15rem;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.lineage-text em {
  font-style: normal;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.08em;
  color: var(--accent);
}

.lineage-text span {
  font-size: 12px;
  line-height: 1.45;
  color: var(--ink-soft);
}

.lineage-go {
  font-size: 22px;
  color: var(--accent);
  flex-shrink: 0;
}

.bottom {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 300px;
  gap: 18px;
  align-items: start;
  padding-top: 18px;
  padding-bottom: 44px;
  border-top: 1px solid var(--line);
  animation: rise 0.65s ease both;
  animation-delay: 0.25s;
}

.main-disc,
.recent {
  background: rgba(255, 255, 255, 0.45);
  border: 1px solid var(--line);
  padding: 14px 16px 16px;
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

.recent-empty {
  padding: 2px 0 4px;
  font-size: 12px;
  color: var(--ink-soft);
}

.recent-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.recent-list li {
  border-top: 1px solid rgba(16, 40, 32, 0.08);
}

.recent-list li:first-child {
  border-top: 0;
}

.recent-list a {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 2px;
  text-decoration: none;
  color: inherit;
}

.recent-list a:hover {
  background: rgba(255, 255, 255, 0.6);
}

.recent-topic {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
  color: var(--accent);
  text-transform: uppercase;
}

.recent-title {
  font-size: 13px;
  font-weight: 600;
  line-height: 1.35;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.recent-meta {
  font-size: 11px;
  color: var(--ink-soft);
}

.foot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding-top: 18px;
  padding-bottom: 28px;
  border-top: 1px solid var(--line);
  font-size: 12px;
  color: var(--ink-soft);
}

.foot a {
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
}

.foot a:hover {
  text-decoration: underline;
}

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@media (max-width: 760px) {
  .bottom {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .shelf {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  .rail {
    left: 14px;
  }
}
</style>
