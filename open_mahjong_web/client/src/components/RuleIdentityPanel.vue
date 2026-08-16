<!-- 单条规则：沿革、父系/子系、种类、规则书、资料 -->
<template>
  <div class="rip">
    <section class="block lineage">
      <div class="block-head">
        <h2>谱系沿革</h2>
      </div>
      <p v-if="eraInfo" class="era-line">
        <time>{{ eraInfo.era.years }}</time>
        <strong>{{ eraInfo.era.title }}</strong>
        <span v-if="eraInfo.track">{{ eraInfo.track.label }}</span>
        <span v-if="appeared">{{ appeared }}</span>
      </p>
      <p v-if="blurb" class="blurb">{{ blurb }}</p>
      <dl v-if="featureRows.length" class="feats">
        <div v-for="row in featureRows" :key="row.k">
          <dt>{{ row.label }}</dt>
          <dd>{{ row.text }}</dd>
        </div>
      </dl>
    </section>

    <section class="block kin-block">
      <dl class="kin">
        <div>
          <dt>父系</dt>
          <dd>
            <template v-if="parents.length">
              <router-link v-for="id in parents" :key="id" :to="href(id)">{{ name(id) }}</router-link>
            </template>
            <span v-else>无</span>
          </dd>
        </div>
        <div>
          <dt>子系</dt>
          <dd>
            <template v-if="children.length">
              <router-link v-for="id in children" :key="id" :to="href(id)">{{ name(id) }}</router-link>
            </template>
            <span v-else>无</span>
          </dd>
        </div>
        <div>
          <dt>种类</dt>
          <dd class="kinds">
            <template v-if="families.length">
              <div v-for="fam in families" :key="fam.id" class="kind-row">
                <span class="kind">
                  {{ fam.label }}
                  <em v-if="fam.region">（{{ fam.region }}）</em>
                </span>
                <span v-if="otherMembers(fam).length" class="kind-subs">
                  同组规则：
                  <router-link
                    v-for="m in otherMembers(fam)"
                    :key="m.id"
                    :to="href(m.id)"
                    :title="m.note || ''"
                  >{{ name(m.id) }}</router-link>
                </span>
              </div>
            </template>
            <span v-else>无</span>
          </dd>
        </div>
        <div v-if="playable">
          <dt>平台</dt>
          <dd>本站可对局</dd>
        </div>
      </dl>
    </section>

    <section class="block docs">
      <div class="block-head">
        <h2>规则书</h2>
        <span>{{ docs.length }} 项</span>
      </div>
      <div v-if="docs.length" class="res-grid">
        <article v-for="doc in docs" :key="doc.url" class="res-card">
          <h3>{{ doc.title }}</h3>
          <p v-if="doc.desc">{{ doc.desc }}</p>
          <div class="res-actions">
            <a class="btn primary" :href="doc.url" target="_blank" rel="noopener noreferrer">阅读</a>
            <a class="btn" :href="doc.url" :download="doc.filename || ''">下载</a>
          </div>
        </article>
      </div>
      <p v-else class="empty">暂无规则书</p>
    </section>

    <section class="block sources">
      <div class="block-head">
        <h2>资料</h2>
        <span>{{ filtered.length }} / {{ sources.length }}</span>
      </div>
      <div v-if="sources.length" class="toolbar">
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
            <option v-for="r in ruleOptions" :key="r" :value="r">{{ name(r) }}</option>
          </select>
        </label>
      </div>
      <article v-for="s in filtered" :id="s.id" :key="s.id" class="src-card">
        <div class="src-top">
          <span class="type" :data-type="s.type">{{ typeLabel(s.type) }}</span>
          <span class="lang">{{ s.lang }}</span>
          <span v-if="s.accessed" class="date">{{ s.accessed }}</span>
          <span v-for="rid in s.rules || []" :key="rid" class="rule-tag">
            <router-link :to="href(rid)">{{ name(rid) }}</router-link>
          </span>
        </div>
        <h3>{{ s.title }}</h3>
        <p v-if="s.excerpt" class="excerpt">{{ s.excerpt }}</p>
        <div class="res-actions">
          <a class="btn" :href="s.url" target="_blank" rel="noopener noreferrer">打开源链接</a>
          <a
            v-if="localView(s)?.kind === 'snapshot'"
            class="btn ghost"
            :href="localView(s).href"
            target="_blank"
            rel="noopener noreferrer"
          >查看快照</a>
          <a
            v-if="localView(s)?.kind === 'file'"
            class="btn ghost"
            :href="localView(s).href"
            target="_blank"
            rel="noopener noreferrer"
          >查看文件</a>
        </div>
      </article>
      <p v-if="!sources.length" class="empty">暂无资料</p>
    </section>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  slug: { type: String, required: true },
  stub: { type: Object, default: null },
  docs: { type: Array, default: () => [] },
  sources: { type: Array, default: () => [] },
  parents: { type: Array, default: () => [] },
  children: { type: Array, default: () => [] },
  families: { type: Array, default: () => [] },
  eraInfo: { type: Object, default: null },
  appeared: { type: String, default: '' },
  playable: { type: Boolean, default: false },
  hrefFor: { type: Function, required: true },
  nameFor: { type: Function, required: true },
})

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

const FEAT_LABELS = {
  hand_size: '手牌',
  tiles: '牌张',
  honors: '字牌',
  flowers: '花牌',
  jokers: '癞子',
  chi: '吃牌',
  after_first_win: '一家和后',
  dingque: '定缺',
  scoring: '计分',
  non_winner_score: '未和者计分',
  dealer_double: '庄加倍',
  riichi: '立直',
  furiten: '振听',
  dianpao: '点炮',
  min_win: '起和',
  jiang_258: '258 将',
}

const FEAT_VALUES = {
  true: '有',
  false: '无',
  stop: '这局结束',
  xuezhan: '其他人继续',
  xueliu: '和了还能再和',
  'fu-han': '番副',
  fan: '番',
  none: '无',
  one: '一家',
  all: '多家',
  'mcr-fan': '国标番种',
  constructed: '另编',
  additive: '加算',
  'hua-lezi': '花／辣子',
  bao: '宝牌',
  dan: '蛋牌',
  jing: '精',
  animals: '动物牌',
  '8': '八张',
  'pung-or-above': '碰碰和以上',
}

const typeFilter = ref('')
const langFilter = ref('')
const ruleFilter = ref('')

const blurb = computed(() => props.stub?.blurb || props.stub?.features?.note || '')

const featureRows = computed(() => {
  const feat = props.stub?.features || {}
  return Object.keys(FEAT_LABELS)
    .filter((k) => feat[k] !== undefined && feat[k] !== null && feat[k] !== '')
    .map((k) => ({
      k,
      label: FEAT_LABELS[k],
      text: featText(feat[k]),
    }))
})

const typeOptions = computed(() => [...new Set(props.sources.map((s) => s.type).filter(Boolean))].sort())
const langOptions = computed(() => [...new Set(props.sources.map((s) => s.lang).filter(Boolean))].sort())
const ruleOptions = computed(() => [
  ...new Set(props.sources.flatMap((s) => s.rules || []).filter(Boolean)),
])
const filtered = computed(() =>
  props.sources.filter((s) => {
    if (typeFilter.value && s.type !== typeFilter.value) return false
    if (langFilter.value && s.lang !== langFilter.value) return false
    if (ruleFilter.value && !(s.rules || []).includes(ruleFilter.value)) return false
    return true
  }),
)

function featText(v) {
  if (typeof v === 'boolean') return v ? '有' : '无'
  const key = String(v)
  return FEAT_VALUES[key] ?? key
}

function typeLabel(t) {
  return TYPE_LABELS[t] || t
}

function href(id) {
  return props.hrefFor(id)
}

function name(id) {
  return props.nameFor(id)
}

function otherMembers(fam) {
  const self = props.slug
  return (fam.members || []).filter((m) => m.id && m.id !== self)
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
</script>

<style scoped>
.rip {
  --rip-ink: var(--ink-base, var(--ink, #102820));
  --rip-muted: var(--ink-soft, var(--muted, #3d5a4c));
  --rip-line: var(--line, rgba(16, 40, 32, 0.14));
  --rip-accent: var(--accent, #1f6b52);
  display: flex;
  flex-direction: column;
  gap: 22px;
}

.block-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--rip-line);
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
  color: var(--rip-muted);
}

.era-line {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem 1rem;
  margin: 0 0 0.6rem;
  font-size: 0.95rem;
}

.era-line time,
.era-line strong {
  font-family: 'Noto Serif SC', serif;
}

.era-line span {
  color: var(--rip-muted);
}

.blurb {
  margin: 0;
  max-width: 46em;
  font-size: 14px;
  line-height: 1.65;
  color: var(--rip-muted);
}

.feats {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 0.35rem 1rem;
  margin: 0.85rem 0 0;
}

.feats div {
  display: flex;
  gap: 0.4rem;
  font-size: 13px;
}

.feats dt {
  color: var(--rip-muted);
}

.feats dd {
  margin: 0;
  font-weight: 600;
}

.kin {
  display: grid;
  gap: 0;
  margin: 0;
}

.kin div {
  display: grid;
  grid-template-columns: 4.5rem 1fr;
  gap: 0.75rem;
  padding: 0.7rem 0;
  border-bottom: 1px solid var(--rip-line);
}

.kin dt {
  color: var(--rip-muted);
  font-size: 0.9rem;
  padding-top: 0.15rem;
}

.kin dd {
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem 0.85rem;
  line-height: 1.55;
}

.kin a {
  color: var(--rip-accent);
  text-decoration: none;
  font-weight: 600;
}

.kin a:hover {
  text-decoration: underline;
}

.kind em {
  font-style: normal;
  color: var(--rip-muted);
}

.kinds {
  flex-direction: column;
  align-items: flex-start;
  gap: 0.65rem;
}

.kind-row {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.kind-subs {
  font-size: 13px;
  color: var(--rip-muted);
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem 0.75rem;
}

.kind-subs a {
  color: var(--rip-accent);
  text-decoration: none;
  font-weight: 600;
}

.empty {
  margin: 0;
  font-size: 13px;
  color: var(--rip-muted);
}

.res-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 10px;
}

.res-card,
.src-card {
  background: rgba(255, 255, 255, 0.58);
  border: 1px solid var(--rip-line);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.src-card {
  margin-bottom: 10px;
}

.res-card h3,
.src-card h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 650;
  font-family: 'Noto Serif SC', serif;
}

.res-card p,
.excerpt {
  margin: 0;
  font-size: 12px;
  line-height: 1.55;
  color: var(--rip-muted);
  white-space: pre-wrap;
}

.res-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.btn {
  appearance: none;
  display: inline-block;
  border: 1px solid var(--rip-line);
  background: rgba(255, 255, 255, 0.7);
  color: var(--rip-ink);
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  padding: 6px 12px;
  text-decoration: none;
  cursor: pointer;
}

.btn.primary {
  background: var(--rip-accent);
  border-color: var(--rip-accent);
  color: #fff;
}

.btn.ghost {
  background: transparent;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1.25rem;
  margin-bottom: 12px;
  font-size: 12px;
  color: var(--rip-muted);
}

.toolbar select {
  border: 1px solid var(--rip-line);
  background: #fff;
  padding: 0.2rem 0.35rem;
}

.src-top {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  font-size: 0.75rem;
}

.type {
  background: var(--rip-accent);
  color: #f5faf7;
  padding: 0.12rem 0.45rem;
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
  color: var(--rip-muted);
}

.rule-tag {
  border: 1px solid var(--rip-line);
  padding: 0.08rem 0.35rem;
  color: var(--rip-muted);
}

.rule-tag a {
  color: var(--rip-accent);
  text-decoration: none;
  font-weight: 600;
}

.rule-tag a:hover {
  text-decoration: underline;
}
</style>
