<template>
  <div class="lab">
    <aside class="lab__side">
      <header class="lab__head">
        <p class="lab__eyebrow">LAB</p>
        <div class="lab__head-row">
          <h1>测试台</h1>
          <span class="lab__status" :class="{ ok: health.ok, bad: health.ok === false }">{{ health.ok ? '在线' : (health.ok === false ? '离线' : '…') }}</span>
        </div>
      </header>
      <input v-model="query" class="lab__search" placeholder="搜索" />
      <div class="lab__ops">
        <button type="button" class="lab__btn lab__btn--primary" :disabled="!selected || busy" @click="openSelected">打开</button>
        <button type="button" class="lab__btn" :disabled="!selected || busy" @click="runSelected">跑完</button>
        <button type="button" class="lab__btn" :disabled="!selected || busy" @click="runSelectedGroup">跑本规则</button>
        <button type="button" class="lab__btn" :disabled="busy" @click="loadCatalog(true)">刷新</button>
      </div>
      <div class="lab__ops">
        <button type="button" class="lab__btn" :disabled="busy" @click="checkVisible(true)">全选</button>
        <button type="button" class="lab__btn" :disabled="busy" @click="checkVisible(false)">清空</button>
        <button type="button" class="lab__btn" :disabled="busy || !checkedCount" @click="runChecked">跑选中 {{ checkedCount || '' }}</button>
        <button type="button" class="lab__btn" :disabled="busy || !failedCount" @click="runFailed">失败 {{ failedCount || '' }}</button>
      </div>
      <p v-if="selected" class="lab__picked">{{ selected.title }}<small v-if="selected.comment && selected.comment !== selected.title">{{ selected.comment }}</small></p>
      <div v-if="catalogError" class="lab__error">{{ catalogError }}</div>
      <section v-for="group in filteredGroups" :key="group.id" class="lab__group">
        <div class="lab__group-head">
          <button type="button" class="lab__fold" @click="toggleGroup(group.id)">{{ collapsed[group.id] ? '+' : '−' }}</button>
          <label>
            <input type="checkbox" :checked="ruleChecked(group)" :indeterminate.prop="rulePartial(group)" @change="checkRule(group, $event.target.checked)" />
            {{ group.title }}
          </label>
          <small>{{ group.count }}</small>
        </div>
        <div v-show="!collapsed[group.id]">
          <div v-for="folder in group.files" :key="folder.file" class="lab__file">
            <div class="lab__file-head">
              <label>
                <input type="checkbox" :checked="fileChecked(folder)" :indeterminate.prop="filePartial(folder)" @change="checkFile(folder, $event.target.checked)" />
                {{ folder.file_doc || shortFile(folder.file) }}
              </label>
            </div>
            <button
              v-for="item in folder.items"
              :key="item.id"
              type="button"
              class="lab__item"
              :class="{ active: selected && selected.id === item.id }"
              :title="item.comment || item.title"
              @click="selectItem(item)"
              @dblclick="openItem(item)"
            >
              <span class="lab__dot" :class="statusOf(item.id)"></span>
              <span class="lab__item-copy">
                <span class="lab__item-title">{{ item.title }}</span>
                <span v-if="item.comment && item.comment !== item.title" class="lab__item-comment">{{ item.comment }}</span>
              </span>
              <input type="checkbox" :checked="!!checked[item.id]" @click.stop @change="toggleCheck(item.id, $event.target.checked)" />
            </button>
          </div>
        </div>
      </section>
    </aside>

    <section class="lab__board">
      <div v-if="verdict" class="lab__verdict" :class="{ pass: verdict.ok, fail: !verdict.ok }">
        <strong>{{ (verdict.verdict && verdict.verdict.summary) || (verdict.ok ? '通过' : '失败') }}</strong>
        <span>{{ (verdict.verdict && verdict.verdict.detail) || '' }}</span>
        <button
          v-for="(problem, i) in verdictProblems"
          :key="i"
          type="button"
          class="lab__verdict-item"
          :disabled="problem.frame == null"
          @click="problem.frame != null && onSlider(problem.frame)"
        >
          <b>{{ problem.where }}</b> {{ problem.message }}
        </button>
      </div>
      <LabTable
        :components="components"
        :legal="legal"
        :dump="dump"
        :interactive="interactive"
        :show-other-hands="showOtherHands"
        :at-last-frame="!canNext"
        :chong-hint="chongHint"
        :waiting-tiles="waitingTiles"
        @action="onAction"
        @board-step="onBoardStep"
      />
      <div v-if="hasSnapshot" class="lab__tools">
        <button type="button" :class="{ 'is-on': showOtherHands }" @click="showOtherHands = !showOtherHands">
          {{ showOtherHands ? '隐藏他家手牌' : '显示他家手牌' }}
        </button>
        <button type="button" :class="{ 'is-on': chongHint }" title="标红他家待牌；查看牌山时标出听牌" @click="chongHint = !chongHint">铳张提示</button>
        <button type="button" :class="{ 'is-on': wallVisible }" @click="wallVisible = !wallVisible">查看牌山</button>
        <button type="button" :class="{ 'is-on': scoreboardOpen }" @click="scoreboardOpen = !scoreboardOpen">
          {{ scoreboardOpen ? '关闭计分板' : '打开计分板' }}
        </button>
      </div>
      <div v-if="actionDisplays.length" class="lab__hud">
        <span v-for="item in actionDisplays" :key="item.pos">{{ item.label }} {{ item.text }}</span>
      </div>
      <div v-if="waitHint || (interactive && branchGroups.length)" class="lab__branches" @click.stop>
        <span v-if="waitHint" class="lab__wait">{{ waitHint }}</span>
        <div v-for="group in branchGroups" :key="group.seat" class="lab__branch-group">
          <em>{{ group.label }}</em>
          <button
            v-for="(branch, i) in group.branches"
            :key="i"
            type="button"
            :disabled="busy || !interactive"
            @click="onBranch(branch)"
          >
            <img v-if="branch.tile_id || branch.target_tile" :src="tileUrl(branch.tile_id || branch.target_tile)" alt="" />
            {{ actionText(branch.action_type) }}
          </button>
        </div>
      </div>
      <div v-if="wallVisible" class="lab__wall-layer" @click.self="wallVisible = false">
        <section class="lab__wall">
          <header>
            <strong>牌山阅览</strong>
            <span>剩余 {{ wallTiles.length }} 张</span>
            <button type="button" @click="wallVisible = false">×</button>
          </header>
          <div class="lab__wall-rows">
            <span
              v-for="(tile, i) in wallTiles"
              :key="i"
              :class="{ 'is-danger': chongHint && waitingSet.has(Number(tile) % 100 || Number(tile)) }"
            >
              <img :src="tileUrl(tile)" alt="" />
            </span>
          </div>
        </section>
      </div>
      <div v-if="scoreboardOpen" class="lab__score-layer" @click.self="scoreboardOpen = false">
        <section class="lab__score">
          <header>
            <strong>计分板</strong>
            <button type="button" @click="scoreboardOpen = false">×</button>
          </header>
          <table>
            <thead>
              <tr><th>座位</th><th>玩家</th><th>风</th><th>分</th><th>手牌</th></tr>
            </thead>
            <tbody>
              <tr v-for="row in scoreRows" :key="row.seat">
                <td>{{ row.seat }}</td>
                <td>{{ row.username }}</td>
                <td>{{ row.wind }}</td>
                <td>{{ row.score }}</td>
                <td>{{ row.hand }}</td>
              </tr>
            </tbody>
          </table>
        </section>
      </div>
      <div class="lab__controls">
        <div class="lab__controls-row">
          <button type="button" title="上一步" :disabled="busy || !canPrev" @click="step(-1)">‹</button>
          <button type="button" class="is-play" :disabled="busy || (!canNext && !interactive)" @click="togglePlaying">{{ playing ? '暂停' : '播放' }}</button>
          <button type="button" title="下一步" :disabled="busy || (!canNext && !interactive)" @click="step(1)">›</button>
          <button type="button" class="is-wide" :disabled="busy || !interactive" @click="playToEnd">跑完本局</button>
          <label class="lab__range-wrap">
            <span>{{ frameIndex }} / {{ Math.max(traceLen - 1, 0) }}</span>
            <input
              type="range"
              min="0"
              :max="Math.max(traceLen - 1, 0)"
              :disabled="traceLen === 0"
              :value="frameIndex"
              @input="onSlider($event.target.value)"
            />
          </label>
          <span class="lab__now">{{ currentTypeShort }}</span>
        </div>
      </div>
      <div v-if="runError" class="lab__toast">{{ runError }}</div>
    </section>

    <LabInspector :components="components" :verdict="verdict" @jump="onSlider" />
  </div>
</template>

<script>
import LabInspector from './LabInspector.vue'
import LabTable from './LabTable.vue'
import { ACTION_TEXT } from './labUnityMap'
import { salasasaFaceId, tileFaceAssetUrl } from '@/game2d/lib/tileFaceAsset'

const API = '/verifier-api'
const POS_LABEL = { self: '本家', right: '下家', top: '对家', left: '上家' }

export default {
  name: 'UnitySimLab',
  components: { LabTable, LabInspector },
  data() {
    return {
      health: {},
      catalog: { groups: [] },
      catalogError: '',
      query: '',
      selected: null,
      loading: false,
      batchRunning: false,
      runningId: '',
      runError: '',
      checked: {},
      collapsed: {},
      results: {},
      sessionId: null,
      traceLen: 0,
      traceMeta: [],
      frameIndex: 0,
      components: {},
      currentType: '',
      verdict: null,
      legal: {},
      dump: {},
      ended: true,
      live: false,
      playing: false,
      playTimer: null,
      showOtherHands: false,
      chongHint: false,
      wallVisible: false,
      scoreboardOpen: false,
    }
  },
  computed: {
    filteredGroups() {
      const q = (this.query || '').trim().toLowerCase()
      return (this.catalog.groups || []).map((group) => ({
        ...group,
        files: (group.files || []).map((folder) => ({
          ...folder,
          items: (folder.items || []).filter((item) => {
            if (!q) return true
            return `${item.title} ${item.comment || ''} ${item.id} ${item.nodeid || ''}`.toLowerCase().includes(q)
          }),
        })).filter((folder) => folder.items.length),
      })).map((group) => ({
        ...group,
        count: group.files.reduce((n, folder) => n + folder.items.length, 0),
      })).filter((group) => group.count)
    },
    checkedCount() {
      return Object.keys(this.checked).filter((id) => this.checked[id]).length
    },
    failedCount() {
      return Object.values(this.results).filter((item) => item && item.ok === false).length
    },
    busy() {
      return this.batchRunning || !!this.runningId || this.loading
    },
    canPrev() {
      return this.traceLen > 0 && this.frameIndex > 0
    },
    canNext() {
      return this.traceLen > 0 && this.frameIndex < this.traceLen - 1
    },
    interactive() {
      return this.live && !this.ended && this.frameIndex === Math.max(this.traceLen - 1, 0)
    },
    currentTypeShort() {
      const type = this.currentType || ''
      const idx = type.lastIndexOf('/')
      return idx >= 0 ? type.slice(idx + 1) : type
    },
    hasSnapshot() {
      const c = this.components || {}
      return !!(c.GsmSim || c.Game3DSim || c.BoardCanvasSim)
    },
    verdictProblems() {
      return (this.verdict && this.verdict.verdict && this.verdict.verdict.problems) || []
    },
    waitingTiles() {
      const tips = (this.components && this.components.TipsSim) || {}
      return (tips.waiting_tiles || []).map((tile) => Number(tile)).filter(Boolean)
    },
    waitingSet() {
      return new Set(this.waitingTiles.map((tile) => (tile >= 100 ? tile % 100 : tile)))
    },
    wallTiles() {
      return ((this.dump && this.dump.tiles_list) || []).map((tile) => Number(tile)).filter(Boolean)
    },
    actionDisplays() {
      const display = (this.components && this.components.ActionDisplaySim) || {}
      return ['self', 'right', 'top', 'left']
        .filter((pos) => display[pos])
        .map((pos) => ({ pos, label: POS_LABEL[pos], text: display[pos] }))
    },
    scoreRows() {
      const gsm = (this.components && this.components.GsmSim) || {}
      const board = (this.components && this.components.BoardCanvasSim) || {}
      const g3 = (this.components && this.components.Game3DSim) || {}
      const map = gsm.indexToPosition || {}
      const info = gsm.player_to_info || {}
      return [0, 1, 2, 3].map((seat) => {
        const pos = map[seat] || map[String(seat)] || 'self'
        const pane = info[pos] || {}
        return {
          seat,
          username: pane.username || `P${seat}`,
          wind: pane.wind || (board.winds && board.winds[pos]) || '',
          score: pane.score ?? (board.scores && board.scores[pos]) ?? 0,
          hand: (g3[pos] && g3[pos].hand_count) || pane.hand_tiles_count || 0,
        }
      })
    },
    waitHint() {
      if (!this.interactive) return ''
      const seats = (this.legal && this.legal.seats) || {}
      const self = Number(((this.components && this.components.GsmSim) || {}).selfIndex || 0)
      const waiting = Object.values(seats).filter((seat) => seat && seat.waiting)
      if (!waiting.length) return ''
      const names = waiting.map((seat) => {
        const mine = Number(seat.player_index) === self
        const acts = (seat.client_actions || []).filter((item) => item !== 'cut')
        const verb = acts.length ? acts.map((item) => ACTION_TEXT[item] || item).join('/') : '切牌'
        return `${mine ? '本家' : (seat.username || `P${seat.player_index}`)} ${verb}`
      })
      return `等待 ${names.join('、')}`
    },
    branchGroups() {
      if (!this.interactive) return []
      const branches = ((this.legal && this.legal.branches) || []).slice()
      const seats = (this.legal && this.legal.seats) || {}
      const self = Number(((this.components && this.components.GsmSim) || {}).selfIndex || 0)
      const bySeat = new Map()
      for (const branch of branches) {
        const seat = Number(branch.player_index)
        if (!bySeat.has(seat)) bySeat.set(seat, [])
        bySeat.get(seat).push(branch)
      }
      return [...bySeat.entries()].map(([seat, list]) => {
        const info = seats[String(seat)] || {}
        const cuts = list.filter((item) => item.action_type === 'cut')
        const others = list.filter((item) => item.action_type !== 'cut')
        const trimmedCuts = cuts.length > 14 ? cuts.slice(0, 14) : cuts
        return {
          seat,
          label: Number(seat) === self ? '本家' : (info.username || `P${seat}`),
          branches: [...others, ...trimmedCuts],
        }
      }).filter((group) => group.branches.length)
    },
  },
  async mounted() {
    window.addEventListener('keydown', this.onKey)
    await this.ping()
    await this.loadCatalog(false)
  },
  beforeUnmount() {
    window.removeEventListener('keydown', this.onKey)
    this.stopPlaying()
  },
  methods: {
    shortFile(file) {
      const parts = String(file || '').split('/')
      return parts.slice(-2).join('/')
    },
    statusOf(id) {
      if (this.runningId === id) return 'running'
      const result = this.results[id]
      if (!result) return 'idle'
      return result.ok ? 'pass' : 'fail'
    },
    folderItems(folder) {
      return folder.items || []
    },
    groupItems(group) {
      return (group.files || []).flatMap((folder) => folder.items || [])
    },
    fileChecked(folder) {
      const items = this.folderItems(folder)
      return items.length > 0 && items.every((item) => this.checked[item.id])
    },
    filePartial(folder) {
      const items = this.folderItems(folder)
      const n = items.filter((item) => this.checked[item.id]).length
      return n > 0 && n < items.length
    },
    ruleChecked(group) {
      const items = this.groupItems(group)
      return items.length > 0 && items.every((item) => this.checked[item.id])
    },
    rulePartial(group) {
      const items = this.groupItems(group)
      const n = items.filter((item) => this.checked[item.id]).length
      return n > 0 && n < items.length
    },
    toggleGroup(id) {
      this.collapsed = { ...this.collapsed, [id]: !this.collapsed[id] }
    },
    toggleCheck(id, on) {
      const next = { ...this.checked }
      if (on) next[id] = true
      else delete next[id]
      this.checked = next
    },
    checkFile(folder, on) {
      const next = { ...this.checked }
      for (const item of this.folderItems(folder)) {
        if (on) next[item.id] = true
        else delete next[item.id]
      }
      this.checked = next
    },
    checkRule(group, on) {
      const next = { ...this.checked }
      for (const item of this.groupItems(group)) {
        if (on) next[item.id] = true
        else delete next[item.id]
      }
      this.checked = next
    },
    checkVisible(on) {
      const next = on ? { ...this.checked } : {}
      if (on) {
        for (const group of this.filteredGroups) {
          for (const item of this.groupItems(group)) next[item.id] = true
        }
      }
      this.checked = next
    },
    selectItem(item) {
      this.selected = item
      this.runError = ''
      this.stopPlaying()
      const saved = this.results[item.id]
      if (saved) this.applyPayload(saved, { keepStatus: true })
      else this.resetView()
    },
    resetView() {
      this.verdict = null
      this.sessionId = null
      this.traceLen = 0
      this.traceMeta = []
      this.components = {}
      this.currentType = ''
      this.legal = {}
      this.dump = {}
      this.ended = true
      this.live = false
      this.wallVisible = false
      this.scoreboardOpen = false
    },
    applyPayload(data, { keepStatus } = {}) {
      this.sessionId = data.id || data.sessionId || null
      this.traceLen = data.trace_len || 0
      this.traceMeta = data.trace_meta || []
      this.components = data.unity || {}
      this.frameIndex = Math.max(this.traceLen - 1, 0)
      this.currentType = (this.traceMeta[this.frameIndex] && this.traceMeta[this.frameIndex].type) || ''
      this.verdict = {
        ok: !!data.ok,
        verdict: data.verdict || { summary: data.ok ? '通过' : '失败', problems: [] },
      }
      this.legal = data.legal || {}
      this.dump = data.dump || {}
      this.ended = !!data.ended
      this.live = !this.ended && (this.selected && (this.selected.kind === 'debug' || this.selected.kind === 'script'))
      if (this.selected && !keepStatus) {
        this.results = {
          ...this.results,
          [this.selected.id]: { ...data, sessionId: this.sessionId },
        }
      }
    },
    async ping() {
      try {
        this.health = await (await fetch(`${API}/health`)).json()
      } catch {
        this.health = { ok: false }
      }
    },
    async loadCatalog(refresh) {
      this.loading = true
      this.catalogError = ''
      try {
        const res = await fetch(`${API}/tests?refresh=${refresh ? 'true' : 'false'}`)
        if (!res.ok) throw new Error(await res.text())
        this.catalog = await res.json()
        const next = { ...this.collapsed }
        for (const group of this.catalog.groups || []) {
          if (!(group.id in next)) next[group.id] = group.id !== 'guobiao_debug'
        }
        this.collapsed = next
        if (!this.selected && this.catalog.groups?.[0]?.files?.[0]?.items?.[0]) {
          this.selected = this.catalog.groups[0].files[0].items[0]
        }
      } catch (err) {
        this.catalogError = `无法读取目录，请先启动 lab。${err.message || err}`
      } finally {
        this.loading = false
        await this.ping()
      }
    },
    async openSelected() {
      if (this.selected) await this.openItem(this.selected)
    },
    async runSelected() {
      if (!this.selected) return
      await this.runOne(this.selected, true)
    },
    async runSelectedGroup() {
      if (!this.selected) return
      const group = this.filteredGroups.find((item) => this.groupItems(item).some((row) => row.id === this.selected.id))
      if (!group) return
      await this.runIds(this.groupItems(group).map((item) => item.id))
    },
    async openItem(item) {
      this.selected = item
      const autoplay = item.kind === 'pytest' || item.kind === 'tactical'
      await this.runOne(item, autoplay)
    },
    async runChecked() {
      await this.runIds(Object.keys(this.checked).filter((id) => this.checked[id]))
    },
    async runFailed() {
      const ids = Object.entries(this.results)
        .filter(([, result]) => result && result.ok === false)
        .map(([id]) => id)
      await this.runIds(ids)
    },
    findById(id) {
      for (const group of this.catalog.groups || []) {
        for (const item of this.groupItems(group)) {
          if (item.id === id) return item
        }
      }
      return null
    },
    async runIds(ids) {
      if (!ids.length || this.batchRunning) return
      if (ids.length > 50 && !window.confirm(`将运行 ${ids.length} 条，确认？`)) return
      this.batchRunning = true
      this.runError = ''
      this.stopPlaying()
      try {
        for (const id of ids) {
          const item = this.findById(id)
          if (!item) continue
          this.selected = item
          await this.runOne(item, true)
        }
      } finally {
        this.batchRunning = false
        this.runningId = ''
      }
    },
    async runOne(item, autoplay) {
      this.runningId = item.id
      this.runError = ''
      this.stopPlaying()
      try {
        const res = await fetch(`${API}/tests/run`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            item_id: item.id,
            max_steps: 400,
            policy: 'heuristic',
            turbo: true,
            autoplay,
          }),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) {
          const message = typeof data.detail === 'string' ? data.detail : JSON.stringify(data.detail || data)
          const failed = {
            ok: false,
            id: null,
            trace_len: 0,
            trace_meta: [],
            unity: {},
            verdict: {
              summary: '失败',
              detail: message,
              problems: [{ where: 'http', message, frame: null }],
            },
          }
          this.applyPayload(failed)
          this.results = { ...this.results, [item.id]: failed }
          this.runError = message
          return
        }
        this.applyPayload(data)
        this.results = { ...this.results, [item.id]: { ...data, sessionId: data.id } }
        if (this.traceLen) await this.loadFrame(this.frameIndex)
      } catch (err) {
        const message = String(err.message || err)
        this.runError = message
        const failed = {
          ok: false,
          verdict: { summary: '失败', detail: message, problems: [{ where: 'http', message, frame: null }] },
        }
        this.results = { ...this.results, [item.id]: failed }
        this.verdict = failed
      } finally {
        if (this.runningId === item.id) this.runningId = ''
      }
    },
    async onSlider(value) {
      const index = Number(value)
      this.frameIndex = index
      await this.loadFrame(index)
    },
    async loadFrame(index) {
      if (!this.sessionId) return
      const res = await fetch(`${API}/sessions/${this.sessionId}/trace?frame=${index}`)
      if (!res.ok) return
      const frame = await res.json()
      this.currentType = frame.type || ''
      this.components = frame.components || {}
    },
    async step(delta) {
      if (delta < 0) {
        if (!this.canPrev) return
        await this.onSlider(this.frameIndex - 1)
        return
      }
      if (this.canNext) {
        await this.onSlider(this.frameIndex + 1)
        return
      }
      if (this.interactive) await this.liveStep()
    },
    onBoardStep(delta) {
      this.step(delta)
    },
    async liveStep() {
      if (!this.sessionId || this.busy) return
      this.runningId = this.selected ? this.selected.id : 'step'
      this.runError = ''
      try {
        const res = await fetch(`${API}/sessions/${this.sessionId}/step`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ policy: 'heuristic' }),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) {
          this.runError = typeof data.detail === 'string' ? data.detail : '无法前进一步'
          return
        }
        this.applyPayload(data)
        if (this.selected) this.results = { ...this.results, [this.selected.id]: { ...data, sessionId: data.id } }
        if (this.traceLen) await this.loadFrame(this.frameIndex)
      } finally {
        this.runningId = ''
      }
    },
    async playToEnd() {
      if (!this.sessionId || !this.interactive) return
      this.runningId = this.selected ? this.selected.id : 'auto'
      try {
        const res = await fetch(`${API}/sessions/${this.sessionId}/autoplay`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ max_steps: 400, policy: 'heuristic' }),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) {
          this.runError = typeof data.detail === 'string' ? data.detail : '无法跑完'
          return
        }
        this.applyPayload(data)
        if (this.selected) this.results = { ...this.results, [this.selected.id]: { ...data, sessionId: data.id } }
        if (this.traceLen) await this.loadFrame(this.frameIndex)
      } finally {
        this.runningId = ''
      }
    },
    async onAction(payload) {
      if (!this.interactive || !this.sessionId) return
      const selfIndex = Number((this.components.GsmSim || {}).selfIndex || 0)
      await this.submit({ player_index: selfIndex, ...payload })
    },
    async onBranch(branch) {
      if (!this.interactive || !this.sessionId || !branch) return
      await this.submit({
        player_index: branch.player_index,
        action_type: branch.action_type,
        tile_id: branch.tile_id,
        target_tile: branch.target_tile,
      })
    },
    actionText(action) {
      return ACTION_TEXT[action] || action
    },
    tileUrl(tile) {
      const id = salasasaFaceId(Number(tile) || 0)
      return tileFaceAssetUrl(id, { baseUrl: import.meta.env.BASE_URL || '/' })
    },
    async submit(body) {
      this.runningId = this.selected ? this.selected.id : 'act'
      this.runError = ''
      try {
        const res = await fetch(`${API}/sessions/${this.sessionId}/actions`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) {
          this.runError = typeof data.detail === 'string' ? data.detail : '操作被拒绝'
          return
        }
        this.applyPayload(data)
        if (this.selected) this.results = { ...this.results, [this.selected.id]: { ...data, sessionId: data.id } }
        if (this.traceLen) await this.loadFrame(this.frameIndex)
      } finally {
        this.runningId = ''
      }
    },
    togglePlaying() {
      if (this.playing) {
        this.stopPlaying()
        return
      }
      this.playing = true
      this.playTimer = setInterval(async () => {
        if (!this.playing || this.busy) return
        await this.step(1)
        if (this.ended && !this.canNext) this.stopPlaying()
      }, 420)
    },
    stopPlaying() {
      this.playing = false
      if (this.playTimer) {
        clearInterval(this.playTimer)
        this.playTimer = null
      }
    },
    onKey(ev) {
      if (ev.target && ['INPUT', 'TEXTAREA'].includes(ev.target.tagName)) return
      if (ev.key === 'ArrowLeft') this.step(-1)
      else if (ev.key === 'ArrowRight' || ev.key === ' ') {
        ev.preventDefault()
        this.step(1)
      }
    },
  },
}
</script>

<style scoped>
.lab {
  --line: rgba(255, 255, 255, 0.17);
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr) 290px;
  height: 100dvh;
  background: #101214;
  color: #f4f4f2;
  font-family: system-ui, "Segoe UI", "Microsoft YaHei", sans-serif;
}
.lab__side {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: auto;
  padding: 14px 12px;
  border-right: 1px solid var(--line);
  background: #181b1d;
}
.lab__head { padding-bottom: 12px; border-bottom: 1px solid var(--line); margin-bottom: 10px; }
.lab__eyebrow { margin: 0 0 4px; color: #df7d23; font-size: 11px; font-weight: 700; letter-spacing: 0.12em; }
.lab__head-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.lab__head h1 { margin: 0; font-size: 20px; }
.lab__status { padding: 3px 8px; border-radius: 4px; background: #252a2d; font-size: 12px; }
.lab__status.ok { color: #58c987; }
.lab__status.bad { color: #ee7474; }
.lab__search {
  width: 100%;
  margin-bottom: 8px;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 6px;
  background: #121416;
  color: inherit;
}
.lab__ops { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 8px; }
.lab__btn {
  border: 1px solid rgba(255, 255, 255, 0.25);
  background: #252a2d;
  color: #f7f7f5;
  border-radius: 4px;
  padding: 6px 8px;
  cursor: pointer;
  font-size: 12px;
}
.lab__btn:hover { border-color: #df7d23; }
.lab__btn:disabled { opacity: 0.5; cursor: default; }
.lab__btn--primary { background: #d8741f; border-color: #d8741f; color: #fff; font-weight: 700; }
.lab__error, .lab__toast {
  background: #3a1f1f;
  color: #f3c0c0;
  padding: 8px 10px;
  border-radius: 6px;
  margin: 8px 0;
  white-space: pre-wrap;
  font-size: 12px;
}
.lab__toast {
  position: absolute;
  left: 16px;
  bottom: 70px;
  z-index: 8;
  max-width: 60%;
}
.lab__group { margin-bottom: 8px; }
.lab__group-head, .lab__file-head {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #c9c4b8;
}
.lab__group-head small { color: #7b8388; }
.lab__fold { width: 22px; border: 0; background: transparent; color: inherit; cursor: pointer; }
.lab__file { margin: 4px 0 4px 8px; }
.lab__picked {
  margin: 0 0 8px;
  padding: 6px 8px;
  border-radius: 6px;
  background: #121416;
  font-size: 12px;
  line-height: 1.35;
}
.lab__picked small {
  display: block;
  margin-top: 2px;
  color: #9aa3a8;
  font-size: 11px;
}
.lab__item {
  display: grid;
  grid-template-columns: 8px minmax(0, 1fr) auto;
  align-items: center;
  gap: 6px;
  width: 100%;
  text-align: left;
  background: transparent;
  color: inherit;
  border: 0;
  border-radius: 4px;
  padding: 4px 6px;
  cursor: pointer;
  font-size: 12px;
}
.lab__item:hover, .lab__item.active { background: #2a3144; }
.lab__item-copy { min-width: 0; display: flex; flex-direction: column; }
.lab__item-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.lab__item-comment {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #8b9398;
  font-size: 10px;
}
.lab__dot { width: 8px; height: 8px; border-radius: 50%; background: #555; }
.lab__dot.running { background: #f6d36b; }
.lab__dot.pass { background: #8ee0a8; }
.lab__dot.fail { background: #f0a0a0; }
.lab__board {
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  margin: 10px;
  border: 1px solid rgba(0, 0, 0, 0.6);
  border-radius: 12px;
  box-shadow: 0 12px 36px rgba(0, 0, 0, 0.28);
}
.lab__controls {
  flex: 0 0 auto;
  margin: 0 8px 8px;
  padding: 8px 11px;
  border: 1px solid rgba(255, 255, 255, 0.24);
  border-radius: 8px;
  background: rgba(18, 20, 21, 0.91);
}
.lab__controls-row { display: flex; align-items: center; gap: 6px; }
.lab__controls button {
  width: 34px;
  height: 29px;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.25);
  border-radius: 4px;
  background: #252a2d;
  color: #f7f7f5;
  cursor: pointer;
}
.lab__controls button.is-play { width: 52px; font-size: 12px; font-weight: 650; }
.lab__controls button.is-wide { width: auto; padding: 0 10px; font-size: 12px; }
.lab__controls button.is-on { border-color: #6da5e8; background: rgba(42, 91, 148, 0.92); }
.lab__controls button:disabled { opacity: 0.5; cursor: default; }
.lab__range-wrap {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1;
  color: #bfc4c6;
  font-size: 11px;
}
.lab__range-wrap input { flex: 1; accent-color: #df7d23; }
.lab__now { max-width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #f2f2f0; font-size: 12px; }
.lab__verdict {
  position: absolute;
  left: 12px;
  top: 12px;
  z-index: 40;
  max-width: min(52%, 420px);
  padding: 8px 10px;
  border-radius: 8px;
  background: rgba(18, 20, 21, 0.92);
  font-size: 12px;
  pointer-events: auto;
}
.lab__verdict.pass { background: rgba(28, 51, 36, 0.94); }
.lab__verdict.fail { background: rgba(58, 31, 31, 0.94); }
.lab__verdict strong { display: block; font-size: 14px; margin-bottom: 2px; }
.lab__verdict-item {
  display: block;
  width: 100%;
  margin-top: 4px;
  padding: 0;
  border: 0;
  background: transparent;
  color: #d7dde0;
  text-align: left;
  cursor: pointer;
  font-size: 11px;
}
.lab__verdict-item:disabled { cursor: default; }
.lab__tools {
  position: absolute;
  right: 12px;
  bottom: 78px;
  z-index: 39;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 6px;
}
.lab__tools button {
  min-height: 32px;
  padding: 0 12px;
  border: 1px solid rgba(255, 255, 255, 0.42);
  border-radius: 3px;
  background: rgba(20, 24, 28, 0.84);
  color: #f4f5f6;
  font: 600 12px/1.2 system-ui, "Microsoft YaHei", sans-serif;
  cursor: pointer;
}
.lab__tools button.is-on {
  border-color: #6da5e8;
  background: rgba(42, 91, 148, 0.92);
}
.lab__hud {
  position: absolute;
  left: 50%;
  top: 12px;
  transform: translateX(-50%);
  z-index: 38;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  color: #ffd36a;
  font-size: 13px;
  font-weight: 700;
  pointer-events: none;
}
.lab__branches {
  position: absolute;
  left: 12px;
  right: 150px;
  bottom: 78px;
  z-index: 41;
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 28%;
  overflow: auto;
  padding: 8px;
  border-radius: 8px;
  background: rgba(18, 20, 21, 0.88);
}
.lab__wait { color: #f2f2f0; font-size: 12px; }
.lab__branch-group { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
.lab__branch-group em { color: #c9c4b8; font-style: normal; font-size: 11px; }
.lab__branch-group button {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-height: 28px;
  padding: 0 8px;
  border: 1px solid rgba(255, 255, 255, 0.28);
  border-radius: 4px;
  background: #252a2d;
  color: #f7f7f5;
  cursor: pointer;
  font-size: 12px;
}
.lab__branch-group button img { width: 18px; height: 24px; object-fit: contain; }
.lab__wall-layer,
.lab__score-layer {
  position: absolute;
  inset: 0;
  z-index: 52;
  display: grid;
  place-items: center;
  padding: 22px;
  background: rgba(0, 0, 0, 0.35);
  box-sizing: border-box;
}
.lab__wall,
.lab__score {
  width: min(82%, 820px);
  max-height: min(70%, 560px);
  overflow: auto;
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.34);
  border-radius: 5px;
  background: rgba(17, 20, 23, 0.96);
}
.lab__wall header,
.lab__score header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}
.lab__wall header span { color: #aeb5b8; font-size: 12px; }
.lab__wall header button,
.lab__score header button {
  margin-left: auto;
  width: 30px;
  border: 0;
  background: transparent;
  color: inherit;
  font-size: 20px;
  cursor: pointer;
}
.lab__wall-rows {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.lab__wall-rows span {
  display: inline-flex;
  width: 28px;
  aspect-ratio: 3 / 4;
  background: #f7f7f0;
  border-radius: 3px;
  overflow: hidden;
}
.lab__wall-rows span.is-danger { outline: 2px solid #ff7575; }
.lab__wall-rows img { width: 100%; height: 100%; object-fit: contain; }
.lab__score table { width: 100%; border-collapse: collapse; font-size: 13px; }
.lab__score th, .lab__score td { padding: 6px 8px; border-bottom: 1px solid rgba(255, 255, 255, 0.12); text-align: left; }
@media (max-width: 1100px) {
  .lab { grid-template-columns: 1fr; height: auto; }
  .lab__board { min-height: 70dvh; }
}
</style>
