<template>
  <section id="sec-map" class="map" :class="{ embedded }">
    <template v-if="!embedded">
      <p class="eyebrow">谱系</p>
      <h2>麻将谱系</h2>
      <p v-if="ledeText" class="lede">{{ ledeText }}</p>
      <ul v-if="tab === 'year' && axes.length" class="axes">
        <li v-for="a in axes" :key="a.id">
          <b>{{ a.label }}</b>
          <span class="src">{{ a.source }}</span>
          {{ a.note }}
        </li>
      </ul>
    </template>

    <div class="tabs" role="tablist">
      <router-link
        role="tab"
        :aria-selected="tab === 'year'"
        :class="{ on: tab === 'year' }"
        :to="{ name: 'LibraryLineage' }"
        replace
      >年代表</router-link>
      <router-link
        role="tab"
        :aria-selected="tab === 'rel'"
        :class="{ on: tab === 'rel' }"
        :to="{ name: 'LibraryRelatedness' }"
        replace
      >关系表</router-link>
    </div>

    <div class="stage">
      <aside
        class="side left"
        :class="{ collapsed: leftPanel.collapsed.value }"
        :style="leftPanel.collapsed.value ? {} : { width: leftPanel.width.value + 'px', flexBasis: leftPanel.width.value + 'px' }"
      >
        <button type="button" class="side-toggle" :title="leftPanel.collapsed.value ? '展开图例' : '收起图例'" @click="leftPanel.toggleCollapsed()">
          {{ leftPanel.collapsed.value ? '›' : '‹' }}
        </button>
        <div v-show="!leftPanel.collapsed.value" class="side-body">
          <p class="side-h">图例</p>
          <p class="legend">
            <span data-k="play">平台支持</span>
            <span data-k="salasasa">salasasa 进入平台</span>
          </p>
          <button
            v-if="tab === 'year'"
            type="button"
            class="continuity-legend"
            :class="{ on: continuityHighlight }"
            :title="continuityHighlight ? '点击恢复默认亮度' : '点击让这些线段亮起来'"
            @click="continuityHighlight = !continuityHighlight"
          >
            <svg class="continuity-swatch" width="34" height="14" viewBox="0 0 34 14" aria-hidden="true">
              <line x1="2" y1="7" x2="32" y2="7" />
              <circle cx="32" cy="7" r="3" />
            </svg>
            <span>没再分出新规则、但现在仍在打的老规则，续到表底</span>
          </button>

          <template v-if="tab === 'year' && phy">
            <template v-if="originStories.length">
              <p class="side-h">起源故事</p>
              <div class="nodes legends-nodes">
                <span
                  v-for="lg in originStories"
                  :key="lg.id"
                  class="node legend"
                  :title="lg.desc || ''"
                >{{ lg.label }}</span>
              </div>
            </template>
            <template v-if="glossaryTags.length">
              <p class="side-h">常见术语</p>
              <div class="nodes legends-nodes">
                <span
                  v-for="g in glossaryTags"
                  :key="g.id"
                  class="node legend glossary"
                  :title="g.desc"
                >{{ g.label }}</span>
              </div>
            </template>
          </template>

          <template v-else-if="tab === 'rel' && areal">
            <p class="side-h">结构特征（点击筛选）</p>
            <div class="track-chips">
              <button
                v-for="iso in areal.isoglosses"
                :key="iso.id"
                type="button"
                class="track-chip"
                :class="{ on: isoFilter === iso.id }"
                @click="isoFilter = isoFilter === iso.id ? '' : iso.id"
              >{{ iso.label }}</button>
            </div>
          </template>
        </div>
        <div class="drag-handle" @mousedown="leftPanel.startDrag" />
      </aside>

      <div class="stage-main">
        <template v-if="tab === 'year' && phy">
          <div
            ref="chartScrollEl"
            class="chart-scroll"
            :class="{ dragging }"
            @mousedown="onPanDown"
            @touchstart="onPanDown"
            @wheel="onChartWheel"
            @click.capture="onChartClickCapture"
          >
            <div ref="chartEl" class="chart-wrap">
              <svg class="wires" :class="{ lit: !!focusId, 'continuity-hi': continuityHighlight }" :width="box.w" :height="box.h" aria-hidden="true">
                <path
                  v-for="(ln, i) in lines"
                  :key="i"
                  :d="ln.d"
                  fill="none"
                  :class="['wire', ln.type, ln.status, { on: ln.from === focusId || ln.to === focusId }]"
                />
                <g v-for="d in dots" :key="'dot-' + d.id" class="continuity-dot" :class="{ on: d.id === focusId }">
                  <circle :cx="d.x" :cy="d.y - 1" r="2.4" />
                </g>
              </svg>
              <div class="chart-stack">
                <div
                  v-for="band in eraBands"
                  :key="band.key"
                  class="chart"
                  :style="{ gridTemplateColumns: bandCols(band.tracks) }"
                >
                  <div class="head-cell era-h">年代</div>
                  <div
                    v-for="t in band.tracks"
                    :key="band.key + '-h-' + t.id"
                    class="head-cell"
                    :data-track="t.id"
                    :style="spanStyle(t)"
                  >
                    <span>{{ t.label }}</span>
                  </div>

                  <template v-for="row in band.rows" :key="row.era.id">
                    <aside class="era-meta" :class="{ expandable: row.era.desc && row.era.desc.length > 60 }" @click="row.era.desc && row.era.desc.length > 60 && toggleExpand(row.era.id)">
                      <time>{{ row.era.years }}</time>
                      <h3>{{ row.era.title }}</h3>
                      <p v-if="row.era.desc" class="clampable" :class="{ open: expanded.has(row.era.id) }">{{ row.era.desc }}</p>
                      <button
                        v-if="row.era.desc && row.era.desc.length > 60"
                        type="button"
                        class="clamp-toggle"
                        @click.stop="toggleExpand(row.era.id)"
                      >{{ expanded.has(row.era.id) ? '收起 ▲' : '展开全文 ▾' }}</button>
                    </aside>
                    <div
                      v-for="t in band.tracks"
                      :key="row.era.id + t.id"
                      class="cell"
                      :data-track="t.id"
                      :style="spanStyle(t)"
                    >
                      <template v-if="nodesBySubtrack(row.era, t.id)">
                        <div
                          v-for="subInfo in nodesBySubtrack(row.era, t.id).track.subtracks"
                          :key="row.era.id + t.id + '-sg-' + subInfo.id"
                          class="subtrack-group"
                          :data-subtrack="subInfo.id"
                        >
                          <div class="subtrack-head">{{ subInfo.label }}</div>
                          <router-link
                            v-for="id in (nodesBySubtrack(row.era, t.id).bySub[subInfo.id] || [])"
                            :key="id + row.era.id + subInfo.id"
                            :data-node="id"
                            class="node"
                            :class="nodeClass(id, row.era)"
                            :to="ruleHref(id)"
                            @mouseenter="enterNode(id)"
                            @mouseleave="leaveNode"
                            @focus="enterNode(id)"
                            @blur="leaveNode"
                          >
                            <span class="node-name">{{ ruleName(id) }}</span>
                            <span v-if="!row.era.hide_node_year && appeared(id)" class="node-year">{{ appeared(id) }}</span>
                            <span v-if="isSalasasa(id)" class="tag enter">进入平台</span>
                            <span v-else-if="platformInEra(id, row.era)" class="tag">平台支持</span>
                          </router-link>
                        </div>
                      </template>
                      <template v-else>
                        <router-link
                          v-for="id in nodesAt(row.era, t.id)"
                          :key="id + row.era.id"
                          :data-node="id"
                          class="node"
                          :class="nodeClass(id, row.era)"
                          :to="ruleHref(id)"
                          @mouseenter="enterNode(id)"
                          @mouseleave="leaveNode"
                          @focus="enterNode(id)"
                          @blur="leaveNode"
                        >
                          <span class="node-name">{{ ruleName(id) }}</span>
                          <span v-if="!row.era.hide_node_year && appeared(id)" class="node-year">{{ appeared(id) }}</span>
                          <span v-if="isSalasasa(id)" class="tag enter">进入平台</span>
                          <span v-else-if="platformInEra(id, row.era)" class="tag">平台支持</span>
                        </router-link>
                      </template>
                    </div>
                  </template>

                  <aside class="continuity-meta" data-continuity-row="1">
                    <span class="continuity-label">现在还在打的延续线</span>
                    <span class="continuity-hint">下方每个节点对应一个当年出现在这一列、但没有再分出新规则的桌规</span>
                  </aside>
                  <div
                    v-for="t in band.tracks"
                    :key="band.key + '-cont-' + t.id"
                    class="continuity-cell"
                    :data-track="t.id"
                    :data-continuity-row="1"
                    :style="spanStyle(t)"
                  >
                    <template v-if="continuityBySubtrack(band, t.id)">
                      <div
                        v-for="subInfo in continuityBySubtrack(band, t.id).track.subtracks"
                        :key="band.key + '-cont-' + t.id + '-sg-' + subInfo.id"
                        class="subtrack-group"
                        :data-subtrack="subInfo.id"
                      >
                        <div class="subtrack-head muted">{{ subInfo.label }}</div>
                        <router-link
                          v-for="id in (continuityBySubtrack(band, t.id).bySub[subInfo.id] || [])"
                          :key="'cont-' + id + subInfo.id"
                          :data-node="id"
                          class="node continuity"
                          :class="nodeClass(id)"
                          :to="ruleHref(id)"
                          @mouseenter="enterNode(id)"
                          @mouseleave="leaveNode"
                        >
                          <span class="node-name">{{ ruleName(id) }}</span>
                          <span class="node-year">{{ appeared(id) || '—' }}</span>
                          <span class="tag cont">现存</span>
                        </router-link>
                      </div>
                    </template>
                    <template v-else>
                      <router-link
                        v-for="id in continuityAt(band, t.id)"
                        :key="'cont-' + id"
                        :data-node="id"
                        class="node continuity"
                        :class="nodeClass(id)"
                        :to="ruleHref(id)"
                        @mouseenter="enterNode(id)"
                        @mouseleave="leaveNode"
                      >
                        <span class="node-name">{{ ruleName(id) }}</span>
                        <span class="node-year">{{ appeared(id) || '—' }}</span>
                        <span class="tag cont">现存</span>
                      </router-link>
                    </template>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </template>

        <template v-else-if="tab === 'rel' && areal">
          <div class="rel-scroll">
            <article v-for="fam in visibleFamilies" :key="fam.id" class="family">
              <aside class="era-meta" :class="{ expandable: fam.relatedness && fam.relatedness.length > 60 }" @click="fam.relatedness && fam.relatedness.length > 60 && toggleExpand(fam.id)">
                <time>{{ fam.region }}</time>
                <h3>{{ fam.label }}</h3>
                <p v-if="fam.relatedness" class="clampable" :class="{ open: expanded.has(fam.id) }">{{ fam.relatedness }}</p>
                <button
                  v-if="fam.relatedness && fam.relatedness.length > 60"
                  type="button"
                  class="clamp-toggle"
                  @click.stop="toggleExpand(fam.id)"
                >{{ expanded.has(fam.id) ? '收起 ▲' : '展开全文 ▾' }}</button>
              </aside>
              <div class="fam-body">
                <div class="fam-row">
                  <span class="role">主干</span>
                  <router-link
                    class="node"
                    :class="[nodeClass(fam.trunk), 'trunk']"
                    :to="ruleHref(fam.trunk)"
                    @mouseenter="enterNode(fam.trunk)"
                    @mouseleave="leaveNode"
                  >
                    <span class="node-name">{{ ruleName(fam.trunk) }}</span>
                    <span v-if="isSalasasa(fam.trunk)" class="tag enter">进入平台</span>
                    <span v-else-if="isPlatform(fam.trunk)" class="tag">平台支持</span>
                  </router-link>
                </div>
                <div class="fam-row">
                  <span class="role">同组</span>
                  <div class="nodes">
                    <router-link
                      v-for="m in fam.members"
                      :key="m.id"
                      class="node"
                      :class="nodeClass(m.id)"
                      :to="ruleHref(m.id)"
                      @mouseenter="enterNode(m.id)"
                      @mouseleave="leaveNode"
                    >
                      <span class="node-name">{{ ruleName(m.id) }}</span>
                      <small v-if="m.note">{{ m.note }}</small>
                      <span v-if="isSalasasa(m.id)" class="tag enter">进入平台</span>
                      <span v-else-if="isPlatform(m.id)" class="tag">平台支持</span>
                    </router-link>
                  </div>
                </div>
              </div>
            </article>
          </div>
        </template>

        <p class="foot">
          <router-link to="/rule-research/mahjong-phylogeny">谱系用过的史料</router-link>
          ·
          <router-link to="/rule-research/mahjong-studies">分类书与通论</router-link>
        </p>
      </div>
    </div>
  </section>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { LIBRARY_RULES } from '@/constants/libraryRules'
import { useResizablePanel } from '@/composables/useResizablePanel'

const props = defineProps({
  phy: { type: Object, default: null },
  areal: { type: Object, default: null },
  catalog: { type: Array, default: () => [] },
  tab: { type: String, default: 'year' },
  embedded: { type: Boolean, default: false },
})

const leftPanel = useResizablePanel({ key: 'lineage-legend', side: 'left', defaultWidth: 200, min: 160, max: 340 })
const continuityHighlight = ref(false)

const isoFilter = ref('')
const focusId = ref('')
const chartEl = ref(null)
const chartScrollEl = ref(null)
const dragging = ref(false)
const lines = ref([])
const dots = ref([])
const box = ref({ w: 0, h: 0 })
const expanded = ref(new Set())

const originStories = computed(() => props.phy?.legends_not_edges || [])
const glossaryTags = computed(() => props.phy?.glossary || [])

// 表格常常比屏幕宽/高，同时支持按住拖动平移和滚轮上下/左右滚动。
let panState = null
let suppressClickUntil = 0

function pointOf(e) {
  if (e.touches && e.touches.length) return { x: e.touches[0].clientX, y: e.touches[0].clientY }
  return { x: e.clientX, y: e.clientY }
}

function onPanMove(e) {
  if (!panState) return
  const el = chartScrollEl.value
  if (!el) return
  const p = pointOf(e)
  const dx = p.x - panState.startX
  const dy = p.y - panState.startY
  if (!panState.moved && (Math.abs(dx) > 4 || Math.abs(dy) > 4)) {
    panState.moved = true
    dragging.value = true
  }
  if (panState.moved) {
    el.scrollLeft = panState.scrollLeft - dx
    el.scrollTop = panState.scrollTop - dy
    if (e.cancelable) e.preventDefault()
  }
}

function onPanUp() {
  window.removeEventListener('mousemove', onPanMove)
  window.removeEventListener('mouseup', onPanUp)
  window.removeEventListener('touchmove', onPanMove)
  window.removeEventListener('touchend', onPanUp)
  if (panState?.moved) suppressClickUntil = Date.now() + 50
  panState = null
  dragging.value = false
}

function onPanDown(e) {
  if (e.type === 'mousedown' && e.button !== 0) return
  const el = chartScrollEl.value
  if (!el) return
  const p = pointOf(e)
  panState = {
    startX: p.x,
    startY: p.y,
    scrollLeft: el.scrollLeft,
    scrollTop: el.scrollTop,
    moved: false,
  }
  window.addEventListener('mousemove', onPanMove)
  window.addEventListener('mouseup', onPanUp)
  window.addEventListener('touchmove', onPanMove, { passive: false })
  window.addEventListener('touchend', onPanUp)
  if (e.type === 'mousedown') e.preventDefault()
}

function onChartWheel(e) {
  const el = chartScrollEl.value
  if (!el) return
  el.scrollTop += e.deltaY
  el.scrollLeft += e.deltaX
  e.preventDefault()
}

function onChartClickCapture(e) {
  if (Date.now() < suppressClickUntil) {
    e.preventDefault()
    e.stopPropagation()
  }
}

function toggleExpand(id) {
  const next = new Set(expanded.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expanded.value = next
  bind()
}

let ro = null

const platformKeys = computed(() => {
  const s = new Set(['guobiao-lanshi'])
  for (const r of LIBRARY_RULES) {
    if (r.categories?.includes('platform')) s.add(r.key)
  }
  return s
})

const nameMap = computed(() => {
  const m = {}
  for (const r of props.catalog || []) m[r.slug] = r.name_zh
  return m
})

const catalogMap = computed(() => {
  const m = {}
  for (const r of props.catalog || []) m[r.slug] = r
  return m
})

const ledeText = computed(() => {
  const raw = props.tab === 'year' ? props.phy?.lede : props.areal?.lede
  return typeof raw === 'string' ? raw.trim() : ''
})

const axes = computed(() => (props.tab === 'year' ? props.phy?.classification_axes || [] : []))

const COL_W = 122
const ERA_W = 176

// 现代分支（粤港、华北、东北……美式在内）从「分化」那一era起列坐标就固定死，
// 不再按每个era各自出现的track临时拼表头——这样全表只有一次表头切换（史前纸牌/骨牌
// → 宁波单干 → 现代分支定死到底），东北、华北这类track的列位置从头到尾都对得上，
// 美麻(us)也并进同一张表的最右列，不再单独开一条侧栏。
const eraBands = computed(() => {
  const eras = props.phy?.eras || []
  const allTracks = props.phy?.tracks || []
  const firstAt = {}
  eras.forEach((era, i) => {
    for (const g of era.groups || []) {
      if (!g.track || !(g.nodes || []).length) continue
      if (firstAt[g.track] == null) firstAt[g.track] = i
    }
  })
  const livingCore = allTracks.filter((t) => !t.merges_into && !t.span_from && !t.outlier)
  const livingOutliers = allTracks.filter((t) => !t.merges_into && !t.span_from && t.outlier)
  const livingFirst = livingCore.map((t) => firstAt[t.id]).filter((i) => i != null)
  const splitAt = livingFirst.length ? Math.min(...livingFirst) : eras.length
  const modernTracks = [...livingCore, ...livingOutliers].map((t) => ({ ...t, span: 1 }))
  // 把「现在仍在打、但没分出后代」的规则，按各自出现的 track 归档。
  const continuity = leafActiveIds.value.map((id) => ({ id, track: trackOf(id) }))
  const bands = []
  let current = null
  eras.forEach((era, i) => {
    let tracks
    let key
    if (i >= splitAt) {
      tracks = modernTracks
      key = 'modern'
    } else {
      tracks = []
      for (const t of allTracks) {
        if (t.outlier) continue
        const first = firstAt[t.id]
        if (first == null) continue
        if (t.merges_into) {
          const until = firstAt[t.merges_into]
          if (i >= first && (until == null || i < until)) tracks.push({ ...t, span: 1 })
        } else if (t.span_from) {
          if (i >= first && i < splitAt) tracks.push({ ...t, span: t.span_from.length || 2 })
        }
      }
      key = tracks.map((t) => `${t.id}:${t.span}`).join('|')
    }
    const row = { era, index: i }
    if (current && current.key === key) {
      current.rows.push(row)
    } else {
      current = { key, tracks, rows: [row], continuityNodes: continuity.slice() }
      bands.push(current)
    }
  })
  return bands
})

function bandCols(tracks) {
  const units = (tracks || []).reduce((n, t) => n + (t.span || 1), 0)
  return `${ERA_W}px repeat(${Math.max(units, 1)}, ${COL_W}px)`
}

function spanStyle(t) {
  const n = t?.span || 1
  return n > 1 ? { gridColumn: `span ${n}` } : {}
}

function ruleName(id) {
  return nameMap.value[id] || id
}

function ruleBySlug(id) {
  return catalogMap.value[id] || null
}

function isPlatform(id) {
  const key = catalogMap.value[id]?.library_key
  return !!(key && platformKeys.value.has(key))
}

function isSalasasa(id) {
  return id === 'salasasa' || !!ruleBySlug(id)?.enter_href
}

function platformInEra(id, era) {
  if (isSalasasa(id) || id === 'guobiao') return false
  return !!(era?.show_platform && isPlatform(id))
}

function ruleHref(id) {
  const row = catalogMap.value[id]
  if (row?.enter_href) return row.enter_href
  const key = row?.library_key || id
  return `/library/${key}`
}

function nodeClass(id, era) {
  const cls = []
  if (isSalasasa(id)) cls.push('salasasa')
  else if (era ? platformInEra(id, era) : isPlatform(id)) cls.push('play')
  else {
    const r = ruleBySlug(id)
    if (!r) cls.push('unknown')
    else if (r.research_state === 'candidate') cls.push('cand')
    else cls.push('id')
  }
  return cls
}

function appeared(id) {
  return props.phy?.appeared?.[id] || ''
}

function enterNode(id) {
  focusId.value = id
}

function leaveNode() {
  focusId.value = ''
}

function nodesAt(era, trackId) {
  const g = (era.groups || []).find((x) => x.track === trackId)
  return g?.nodes || []
}

function nodesBySubtrack(era, trackId) {
  // 大分支（带 subtracks）按子分支再分组；返回 [{ id, subtrack }]。
  const track = (props.phy?.tracks || []).find((t) => t.id === trackId)
  if (!track?.subtracks?.length) return null
  const nodes = nodesAt(era, trackId)
  const bySub = {}
  for (const n of nodes) {
    const r = catalogMap.value[n]
    const sub = r?.subtrack || track.subtracks[0].id
    if (!bySub[sub]) bySub[sub] = []
    bySub[sub].push(n)
  }
  return { track, bySub }
}

function continuityAt(band, trackId) {
  // 把「现在仍在打、但没分出后代」的规则，按列放回它当初出现的那一支。
  return band.continuityNodes.filter((entry) => entry.track === trackId)
}

function continuityBySubtrack(band, trackId) {
  const track = (props.phy?.tracks || []).find((t) => t.id === trackId)
  if (!track?.subtracks?.length) return null
  const entries = continuityAt(band, trackId)
  const bySub = {}
  for (const e of entries) {
    const r = catalogMap.value[e.id]
    const sub = r?.subtrack || track.subtracks[0].id
    if (!bySub[sub]) bySub[sub] = []
    bySub[sub].push(e.id)
  }
  return { track, bySub }
}

function subLabel(track, subId) {
  return track.subtracks.find((s) => s.id === subId)?.label || subId
}

const visibleFamilies = computed(() => {
  const list = props.areal?.families || []
  if (!isoFilter.value) return list
  return list.filter((f) => (f.shared || []).includes(isoFilter.value))
})

function collect(root, id, pick) {
  const els = [...root.querySelectorAll(`[data-node="${id}"]`)]
  if (!els.length) return null
  return pick === 'last' ? els[els.length - 1] : els[0]
}

function collectAbove(root, fromId, toEl) {
  const els = [...root.querySelectorAll(`[data-node="${fromId}"]`)]
  if (!els.length) return null
  const toTop = toEl.getBoundingClientRect().top
  const above = els.filter((el) => el.getBoundingClientRect().bottom <= toTop + 2)
  return above.length ? above[above.length - 1] : els[0]
}

// 仍在打、但没有再分出后代节点的规则：给它续一段虚线画到表底，标个「现存」点，
// 免得读者以为线断在某年代就等于这打法消失了。
const leafActiveIds = computed(() => {
  const eras = props.phy?.eras || []
  if (!eras.length) return []
  const nowEra = eras[eras.length - 1]
  const nowIds = new Set()
  for (const g of nowEra.groups || []) for (const n of g.nodes || []) nowIds.add(n)
  const sourcesOfEdge = new Set((props.phy.edges || []).map((e) => e.from))
  const seen = new Set()
  const out = []
  for (const era of eras) {
    for (const g of era.groups || []) {
      for (const id of g.nodes || []) {
        if (seen.has(id)) continue
        seen.add(id)
        const r = catalogMap.value[id]
        if (!r || r.status !== 'active') continue
        if (nowIds.has(id)) continue
        if (sourcesOfEdge.has(id)) continue
        out.push(id)
      }
    }
  }
  return out
})

function trackOf(id) {
  for (const era of props.phy?.eras || []) {
    for (const g of era.groups || []) {
      if ((g.nodes || []).includes(id)) return g.track
    }
  }
  return null
}

function lastTrackBottom(root, origin, trackId) {
  // 找出这一列最底部的「延续格子」顶边，把延续线终点落到这里。
  const cells = [...root.querySelectorAll(`.continuity-cell[data-track="${trackId}"]`)]
  if (!cells.length) return null
  return cells[cells.length - 1].getBoundingClientRect().top - origin.top
}

function layout() {
  const root = chartEl.value
  if (!root || !props.phy) {
    lines.value = []
    dots.value = []
    return
  }
  const origin = root.getBoundingClientRect()
  box.value = { w: Math.max(root.scrollWidth, 1), h: Math.max(root.scrollHeight, 1) }
  const out = []
  for (const e of props.phy.edges || []) {
    const bEl = collect(root, e.to, 'first')
    if (!bEl) continue
    const aEl = collectAbove(root, e.from, bEl)
    if (!aEl) continue
    const a = aEl.getBoundingClientRect()
    const b = bEl.getBoundingClientRect()
    const x1 = a.left - origin.left + a.width / 2
    const y1 = a.bottom - origin.top
    const x2 = b.left - origin.left + b.width / 2
    const y2 = b.top - origin.top
    if (y2 <= y1 + 2) continue
    const dy = y2 - y1
    const k = Math.max(72, dy * 0.62)
    out.push({
      ...e,
      d: `M ${x1.toFixed(1)} ${y1.toFixed(1)} C ${x1.toFixed(1)} ${(y1 + k).toFixed(1)}, ${x2.toFixed(1)} ${(y2 - k).toFixed(1)}, ${x2.toFixed(1)} ${y2.toFixed(1)}`,
    })
  }
  const marks = []
  for (const id of leafActiveIds.value) {
    const nodeEl = collect(root, id, 'last')
    if (!nodeEl) continue
    const trackId = trackOf(id)
    if (!trackId) continue
    const y2 = lastTrackBottom(root, origin, trackId)
    if (y2 == null) continue
    const a = nodeEl.getBoundingClientRect()
    const x = a.left - origin.left + a.width / 2
    const y1 = a.bottom - origin.top
    if (y2 <= y1 + 6) continue
    out.push({
      from: id,
      to: id,
      type: 'continuity',
      status: '',
      d: `M ${x.toFixed(1)} ${y1.toFixed(1)} L ${x.toFixed(1)} ${y2.toFixed(1)}`,
    })
    marks.push({ id, x, y: y2 })
  }
  lines.value = out
  dots.value = marks
}

function bind() {
  nextTick(() => {
    requestAnimationFrame(() => {
      layout()
      if (ro) ro.disconnect()
      if (typeof ResizeObserver !== 'undefined' && chartEl.value) {
        ro = new ResizeObserver(() => layout())
        ro.observe(chartEl.value)
      }
    })
  })
}

function onWinResize() {
  layout()
}

onMounted(() => {
  bind()
  window.addEventListener('resize', onWinResize)
})
watch(() => props.phy, bind, { deep: true })
watch(() => props.catalog, bind)
watch(() => props.tab, (t) => {
  if (t === 'year') bind()
})
onBeforeUnmount(() => {
  if (ro) ro.disconnect()
  window.removeEventListener('resize', onWinResize)
  window.removeEventListener('mousemove', onPanMove)
  window.removeEventListener('mouseup', onPanUp)
  window.removeEventListener('touchmove', onPanMove)
  window.removeEventListener('touchend', onPanUp)
})
</script>

<style scoped>
.map {
  --muted: var(--ink-soft, #5c6b64);
  margin-top: 2.75rem;
  padding-top: 1.75rem;
  border-top: 1px solid var(--line);
}

.map.embedded {
  margin-top: 0.35rem;
  padding-top: 0.85rem;
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.eyebrow {
  letter-spacing: 0.14em;
  font-size: 0.8rem;
  color: var(--muted);
  margin: 0 0 0.45rem;
}

.map h2 {
  font-family: 'Noto Serif SC', serif;
  font-size: 1.85rem;
  margin: 0 0 0.55rem;
}

.lede,
.foot {
  color: var(--muted);
  font-size: 1.05rem;
  line-height: 1.7;
  max-width: 52rem;
}

.axes {
  list-style: none;
  margin: 0.85rem 0 0;
  padding: 0;
  max-width: 58rem;
  display: grid;
  gap: 0.35rem;
}

.axes li {
  font-size: 0.92rem;
  line-height: 1.5;
  color: var(--muted);
}

.axes b {
  color: var(--ink);
  font-weight: 600;
  margin-right: 0.35rem;
}

.axes .src {
  color: var(--accent);
  margin-right: 0.4rem;
}

.foot {
  flex: 0 0 auto;
  margin-top: 0.5rem;
  margin-bottom: 0;
  font-size: 0.85rem;
}

.foot a {
  color: var(--accent);
}

.tabs {
  display: flex;
  flex: 0 0 auto;
  gap: 0.55rem;
  margin: 0.6rem 0 0.6rem;
}

.tabs a,
.legend.iso button {
  display: inline-block;
  border: 1px solid var(--line);
  background: #fffef9;
  color: var(--ink);
  padding: 0.5rem 1.05rem;
  font-size: 1rem;
  cursor: pointer;
}

.tabs a {
  text-decoration: none;
}

.tabs a.on,
.legend.iso button.on {
  background: var(--ink);
  color: #f5f1e8;
  border-color: var(--ink);
}

.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.1rem;
  font-size: 0.9rem;
  color: var(--muted);
  margin: 0 0 1.15rem;
}

.legend span[data-k='play'] { color: #1a2e28; font-weight: 600; }
.legend span[data-k='salasasa'] { color: #b45309; font-weight: 600; }
.legend span[data-k='attested'] { color: var(--accent); }
.legend span[data-k='inferred'] { border-bottom: 1px dashed var(--muted); }
.legend span[data-k='hypothesis'] { color: #6b5744; border-bottom: 1px dotted #6b5744; }
.legend span[data-k='diffusion'] { color: #6b4f8a; }

.continuity-legend {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  border: 1px solid var(--line);
  border-radius: 6px;
  background: #fffef9;
  padding: 0.45rem 0.55rem;
  margin: 0 0 1.1rem;
  font-size: 0.78rem;
  line-height: 1.4;
  color: var(--muted);
  text-align: left;
  cursor: pointer;
}

.continuity-legend:hover {
  border-color: #1a7a5e;
}

.continuity-legend.on {
  background: rgba(26, 122, 94, 0.1);
  border-color: #1a7a5e;
  color: var(--ink);
}

.continuity-swatch {
  flex: 0 0 auto;
  stroke: #1a7a5e;
  stroke-width: 1.6;
  stroke-dasharray: 3 3;
  fill: #1a7a5e;
  opacity: 0.55;
  transition: opacity 0.15s ease, stroke-width 0.15s ease;
}

.continuity-legend.on .continuity-swatch {
  opacity: 1;
  stroke-width: 2.4;
  stroke-dasharray: none;
}

.node.legend.glossary {
  border-style: dotted;
  cursor: help;
}

.stage {
  display: flex;
  align-items: stretch;
  margin-top: 0.4rem;
  flex: 1 1 auto;
  min-height: 0;
}

.stage-main {
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.side {
  position: relative;
  flex: 0 0 auto;
  align-self: stretch;
  display: flex;
  flex-direction: column;
  background: #fbf8ef;
  border: 1px solid var(--line);
}

.side.collapsed {
  flex: 0 0 26px;
  width: 26px;
  overflow: hidden;
}

.side-toggle {
  position: absolute;
  top: 0.4rem;
  z-index: 5;
  width: 22px;
  height: 22px;
  border: 1px solid var(--line);
  background: #fffef9;
  color: var(--muted);
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.85rem;
  line-height: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.side.left .side-toggle { right: 0.35rem; }

.side-body {
  padding: 2.1rem 0.85rem 1.1rem;
  overflow-y: auto;
  flex: 1;
}

.drag-handle {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 7px;
  cursor: col-resize;
  z-index: 4;
}

.side.left .drag-handle { right: -4px; }
.drag-handle:hover { background: rgba(31, 107, 82, 0.15); }

.side-h {
  margin: 0.9rem 0 0.4rem;
  font-size: 0.72rem;
  letter-spacing: 0.08em;
  color: var(--muted);
  text-transform: uppercase;
  font-weight: 700;
}

.side-body .side-h:first-child {
  margin-top: 0;
}

.side-hint {
  font-size: 0.82rem;
  color: var(--muted);
  line-height: 1.5;
}

.side-hint .clear {
  margin-left: 0.4rem;
  border: 0;
  background: none;
  color: var(--accent);
  cursor: pointer;
  font-size: 0.78rem;
  text-decoration: underline;
}

.track-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

.track-chip {
  border: 1px solid var(--line);
  background: #fffef9;
  color: var(--ink);
  padding: 0.28rem 0.6rem;
  font-size: 0.78rem;
  border-radius: 999px;
  cursor: pointer;
}

.track-chip.on {
  background: var(--ink);
  color: #f5f1e8;
  border-color: var(--ink);
}

.chart-scroll {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  margin: 0 -0.25rem;
  cursor: grab;
}

.chart-scroll.dragging {
  cursor: grabbing;
}

.chart-scroll.dragging .node:not(.continuity) {
  pointer-events: none;
}

.rel-scroll {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  margin: 0 -0.25rem;
  padding: 0 0.25rem;
}

.chart-wrap {
  position: relative;
  min-width: 100%;
  width: max-content;
}

.chart-stack {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  width: max-content;
}

.chart {
  display: grid;
  border: 1px solid #b7aa94;
  width: max-content;
  background: #f7f1e4;
}

.chart + .chart {
  margin-top: -1px;
}

.wires {
  position: absolute;
  left: 0;
  top: 0;
  pointer-events: none;
  overflow: visible;
  z-index: 2;
}

.wire {
  stroke: #8a9a92;
  stroke-width: 1.6;
  stroke-linecap: round;
  opacity: 0.5;
}

.wire.attested { stroke: var(--accent); opacity: 0.72; }
.wire.inferred { stroke-dasharray: 5 4; }
.wire.disputed { stroke-dasharray: 2 3; stroke: #8b6914; }
.wire.hypothesis { stroke: #6b5744; stroke-dasharray: 1 4; }
.wire.diffusion { stroke: #6b4f8a; stroke-dasharray: 6 4; }
.wire.standardization { stroke: #8b6914; }
.wire.constructed { stroke: #a35a3a; }
.wire.continuity { stroke: #1a7a5e; stroke-dasharray: 2 4; opacity: 0.55; }
.wire.on { opacity: 1; stroke-width: 3.2; }

.wires.lit .wire:not(.on) {
  opacity: 0.12;
}

.continuity-dot circle {
  fill: #1a7a5e;
  opacity: 0.85;
}

.continuity-dot text {
  font-size: 9px;
  fill: #1a7a5e;
  opacity: 0.85;
  font-weight: 600;
}

.continuity-dot.on circle,
.continuity-dot.on text {
  opacity: 1;
}

.wires.lit .continuity-dot:not(.on) {
  opacity: 0.15;
}

.wires.continuity-hi .wire.continuity {
  opacity: 1;
  stroke-width: 2.4;
}

.wires.continuity-hi .continuity-dot circle,
.wires.continuity-hi .continuity-dot text {
  opacity: 1;
}

.wires.continuity-hi .wire:not(.continuity) {
  opacity: 0.1;
}

.head-cell,
.era-meta,
.cell {
  border-right: 1px solid rgba(160, 150, 132, 0.35);
  border-bottom: 1px solid rgba(160, 150, 132, 0.35);
}

.head-cell {
  position: sticky;
  top: 0;
  z-index: 4;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.12rem;
  padding: 0.5rem 0.35rem 0.42rem;
  font-family: 'Noto Serif SC', serif;
  font-size: 0.86rem;
  font-weight: 700;
  text-align: center;
  border-bottom: 1px solid var(--line);
  border-top: 6px solid transparent;
}

.head-cell small {
  font-family: 'DM Sans', system-ui, sans-serif;
  font-size: 0.62rem;
  font-weight: 500;
  color: var(--muted);
}

.head-cell.era-h {
  background: #efe8d8;
  text-align: left;
  align-items: flex-start;
  padding-left: 0.9rem;
  border-top-color: #8b7355;
}

.era-meta {
  padding: 0.8rem 0.75rem 0.9rem;
  background: #f7f1e4;
  z-index: 1;
}

.era-meta.expandable {
  cursor: pointer;
}

.era-meta time {
  display: block;
  font-family: 'Noto Serif SC', serif;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--ink);
  margin: 0 0 0.24rem;
}

.era-meta h3 {
  font-family: 'Noto Serif SC', serif;
  font-size: 0.92rem;
  margin: 0 0 0.34rem;
  line-height: 1.4;
}

.era-meta p {
  margin: 0;
  font-size: 0.82rem;
  color: var(--muted);
  line-height: 1.5;
}

.era-meta p.clampable {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  overflow: hidden;
}

.era-meta p.clampable.open {
  display: block;
  -webkit-line-clamp: unset;
  line-clamp: unset;
  overflow: visible;
}

.clamp-toggle {
  display: block;
  width: 100%;
  margin-top: 0.4rem;
  border: 1px dashed rgba(139, 115, 85, 0.4);
  border-radius: 6px;
  background: rgba(139, 115, 85, 0.08);
  padding: 0.4rem 0.5rem;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--accent);
  cursor: pointer;
  text-align: center;
}

.clamp-toggle:hover {
  background: rgba(139, 115, 85, 0.16);
}

.era-meta em {
  font-style: normal;
  color: var(--accent);
  font-weight: 600;
}

.cell {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.36rem;
  padding: 0.6rem 0.4rem;
  min-height: 3.6rem;
  z-index: 1;
}

.continuity-meta {
  margin-top: 0.6rem;
  padding: 0.55rem 0.75rem 0.55rem;
  background: linear-gradient(180deg, #ecf6f0, #d8ece1);
  border-top: 1px dashed #1a7a5e;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.2rem;
  z-index: 1;
}

.continuity-label {
  font-family: 'Noto Serif SC', serif;
  font-weight: 700;
  font-size: 0.88rem;
  color: #1a4a3a;
}

.continuity-hint {
  font-size: 0.74rem;
  color: #366955;
  line-height: 1.45;
}

.continuity-cell {
  margin-top: 0.6rem;
  padding: 0.55rem 0.4rem 0.7rem;
  border-top: 1px dashed #1a7a5e;
  background: linear-gradient(180deg, #f4faf6, #e6f1ea);
  display: flex;
  flex-direction: column;
  gap: 0.36rem;
  align-items: flex-start;
  min-height: 3.2rem;
  z-index: 1;
}

.node.continuity {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.18rem;
  border: 1px solid #1a7a5e;
  background: #fff;
  border-radius: 6px;
  padding: 0.32rem 0.55rem 0.42rem;
  text-decoration: none;
  cursor: pointer;
  transition: background 0.15s ease, transform 0.15s ease;
}

.node.continuity:hover {
  background: #d8ece1;
  transform: translateY(-1px);
}

.node.continuity .node-name {
  font-weight: 600;
  font-size: 0.82rem;
  color: var(--ink);
}

.node.continuity .node-year {
  font-size: 0.72rem;
  color: #1a4a3a;
  opacity: 0.7;
}

.node.continuity .tag.cont {
  background: #1a7a5e;
  color: #fff;
  margin-top: 0.2rem;
  align-self: flex-start;
}

.subtrack-group {
  display: flex;
  flex-direction: column;
  gap: 0.36rem;
  padding: 0.32rem 0.32rem 0.5rem;
  border-top: 1px dotted rgba(139, 115, 85, 0.4);
  align-items: flex-start;
  width: 100%;
}

.subtrack-group:first-child {
  border-top: 0;
  padding-top: 0;
}

.subtrack-head {
  font-size: 0.72rem;
  letter-spacing: 0.04em;
  color: var(--muted);
  font-weight: 700;
  text-transform: none;
}

.subtrack-head.muted {
  color: #1a4a3a;
  opacity: 0.65;
}

.head-cell[data-track='paper'],
.cell[data-track='paper'] { background: #f3e6c4; }
.head-cell[data-track='paper'] { border-top-color: #c4a35a; }
.head-cell[data-track='domino'],
.cell[data-track='domino'] { background: #e6e0d4; }
.head-cell[data-track='domino'] { border-top-color: #8a8174; }
.head-cell[data-track='ningbo'],
.cell[data-track='ningbo'] { background: #c5ddd0; }
.head-cell[data-track='ningbo'] { border-top-color: #2f6f5e; }
.head-cell[data-track='yue'],
.cell[data-track='yue'] { background: #d4ead8; }
.head-cell[data-track='yue'] { border-top-color: #3d8b57; }
.head-cell[data-track='jp'],
.cell[data-track='jp'] { background: #f3d6d1; }
.head-cell[data-track='jp'] { border-top-color: #b45a4a; }
.head-cell[data-track='sixteen'],
.cell[data-track='sixteen'] { background: #cfe6e8; }
.head-cell[data-track='sixteen'] { border-top-color: #3a8a90; }
.head-cell[data-track='us'],
.cell[data-track='us'] { background: #d5dcf0; }
.head-cell[data-track='us'] { border-top-color: #4a5f9e; }
.head-cell[data-track='jiangnan'],
.cell[data-track='jiangnan'] { background: #c8e6da; }
.head-cell[data-track='jiangnan'] { border-top-color: #1f8a6a; }
.head-cell[data-track='chuanyu'],
.cell[data-track='chuanyu'] { background: #f5cfc0; }
.head-cell[data-track='chuanyu'] { border-top-color: #c24b2a; }
.head-cell[data-track='north'],
.cell[data-track='north'] { background: #dce4ed; }
.head-cell[data-track='north'] { border-top-color: #5a7190; }
.head-cell[data-track='dongbei'],
.cell[data-track='dongbei'] { background: #c5d4c8; }
.head-cell[data-track='dongbei'] { border-top-color: #3d6b58; }
.head-cell[data-track='huazhong'],
.cell[data-track='huazhong'] { background: #e8d0b8; }
.head-cell[data-track='huazhong'] { border-top-color: #a0673a; }
.head-cell[data-track='mcr'],
.cell[data-track='mcr'] { background: #cfd8ee; }
.head-cell[data-track='mcr'] { border-top-color: #3b5bb5; }
.head-cell[data-track='laizi'],
.cell[data-track='laizi'] { background: #edd9c8; }
.head-cell[data-track='laizi'] { border-top-color: #a35a3a; }
.head-cell[data-track='custom'],
.cell[data-track='custom'] { background: #edd6dc; }
.head-cell[data-track='custom'] { border-top-color: #a45d78; }

.node {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.14rem;
  padding: 0.34rem 0.5rem;
  border: 1px solid rgba(50, 42, 32, 0.22);
  background: rgba(255, 255, 255, 0.72);
  color: inherit;
  text-decoration: none;
  font-size: 0.9rem;
  line-height: 1.32;
  position: relative;
  z-index: 3;
  max-width: 100%;
  -webkit-user-drag: none;
  user-select: none;
}

.node-name {
  font-weight: 600;
}

.node-year,
.node small {
  font-size: 0.68rem;
  color: var(--muted);
  font-weight: 400;
}

.tag {
  font-size: 0.62rem;
  letter-spacing: 0.03em;
  padding: 0.04rem 0.28rem;
  border: 1px solid currentColor;
  opacity: 0.9;
}

.node:hover,
.node:focus {
  border-color: var(--accent);
  color: var(--accent);
  outline: none;
}

.node.play {
  background: #1a2e28;
  color: #f4efe6;
  border-color: #1a2e28;
}

.node.play .node-year,
.node.play small {
  color: #c9d4ce;
}

.node.play:hover,
.node.play:focus {
  background: var(--accent);
  border-color: var(--accent);
  color: #f4efe6;
}

.node.salasasa {
  background: #f59e0b;
  color: #1a1408;
  border-color: #d97706;
}

.node.salasasa .node-year,
.node.salasasa small,
.node.salasasa .tag {
  color: #1a1408;
  border-color: #1a1408;
}

.node.salasasa:hover,
.node.salasasa:focus {
  background: #d97706;
  border-color: #d97706;
  color: #fff7ed;
}

.node.salasasa:hover .tag,
.node.salasasa:focus .tag {
  color: #fff7ed;
  border-color: #fff7ed;
}

.node.cand { color: #4e5c56; }
.node.unknown { opacity: 0.75; }
.node.legend {
  border-style: dashed;
  color: var(--muted);
  cursor: default;
}

.node.trunk .node-name {
  font-family: 'Noto Serif SC', serif;
}

.nodes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.legends {
  display: flex;
  align-items: center;
  gap: 0.85rem;
  margin-top: 1rem;
  padding-top: 0.85rem;
  border-top: 1px solid var(--line);
}

.legends-k {
  margin: 0;
  font-size: 0.95rem;
  color: var(--muted);
  white-space: nowrap;
}

.family {
  display: grid;
  grid-template-columns: minmax(200px, 260px) 1fr;
  gap: 0.9rem 1.5rem;
  padding: 1.15rem 0;
  border-top: 1px solid var(--line);
}

.fam-body {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.fam-row {
  display: grid;
  grid-template-columns: 7rem 1fr;
  gap: 0.55rem;
  align-items: start;
}

.role {
  font-size: 0.85rem;
  color: var(--muted);
  padding-top: 0.35rem;
}

@media (max-width: 900px) {
  .family {
    grid-template-columns: 1fr;
  }
  .fam-row {
    grid-template-columns: 1fr;
  }
  .stage {
    flex-direction: column;
  }
  .side,
  .side.collapsed {
    width: 100% !important;
    flex-basis: auto !important;
    max-height: 220px;
  }
  .side.collapsed {
    max-height: 32px;
  }
  .drag-handle {
    display: none;
  }
  .side.left .side-toggle {
    right: 0.35rem;
    left: auto;
  }
}
</style>
