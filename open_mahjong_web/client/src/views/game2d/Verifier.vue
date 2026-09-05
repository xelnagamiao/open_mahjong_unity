<template>
  <main class="verifier-page">
    <aside class="verifier-pane verifier-pane--left">
      <header>
        <h1>国标验证器</h1>
        <p class="hint">本地 lab · 不入库 · 合法动作才会入队</p>
      </header>

      <label>
        场景
        <select v-model="scenarioId">
          <option v-for="item in scenarios" :key="item.id" :value="item.id">{{ item.title }}</option>
        </select>
      </label>
      <div class="row">
        <button type="button" :disabled="busy" @click="startSession">开局</button>
        <button type="button" :disabled="busy || !session" @click="stopSession">结束</button>
      </div>
      <p v-if="errorMessage" class="error">{{ errorMessage }}</p>
      <p v-if="session" class="meta">
        id={{ session.id }} seed={{ session.seed }} status={{ dump.game_status }}
        tick={{ dump.server_action_tick }} 步数={{ session.action_count }}
      </p>

      <label>
        视角座位
        <select v-model.number="viewerSeat">
          <option v-for="seat in 4" :key="seat" :value="seat - 1">{{ winds[seat - 1] }} ({{ seat - 1 }})</option>
        </select>
      </label>

      <section v-if="session">
        <h2>合法操作</h2>
        <p v-if="!currentSeatLegal.waiting" class="hint">该座位当前无询问</p>
        <div class="actions">
          <button
            v-for="action in visibleButtons"
            :key="action"
            type="button"
            :disabled="busy"
            @click="submitAction(action)"
          >
            {{ actionLabel(action) }}
          </button>
          <button type="button" :disabled="busy" @click="readyAll">四家准备</button>
          <button type="button" :disabled="busy" @click="forceTimeout">强制超时</button>
        </div>
        <div v-if="currentSeatLegal.client_actions?.includes('cut')" class="cut-tiles">
          <button
            v-for="tile in currentSeatLegal.cut_tiles || []"
            :key="`cut-${tile}`"
            type="button"
            :disabled="busy"
            :title="`切 ${tile}`"
            @click="submitAction('cut', tile)"
          >
            <img :src="tileAsset(tile)" alt="" />
            <span>{{ tile }}</span>
          </button>
        </div>
        <div v-if="kongTargets.length" class="cut-tiles">
          <button
            v-for="item in kongTargets"
            :key="`${item.action}-${item.tile}`"
            type="button"
            :disabled="busy"
            @click="submitAction(item.action, item.tile)"
          >
            {{ actionLabel(item.action) }}
            <img :src="tileAsset(item.tile)" alt="" />
          </button>
        </div>
      </section>

      <section v-if="session">
        <h2>回退 / 书签</h2>
        <div class="row">
          <button type="button" :disabled="busy || session.action_count < 1" @click="restoreTo(session.action_count - 1)">上一步</button>
          <button type="button" :disabled="busy" @click="restoreTo(0)">重放到开局询问</button>
        </div>
        <div class="row">
          <input v-model="bookmarkName" placeholder="书签名" />
          <button type="button" :disabled="busy" @click="saveBookmark">记下</button>
        </div>
        <div class="bookmarks">
          <button
            v-for="(index, name) in session.bookmarks"
            :key="name"
            type="button"
            :disabled="busy"
            @click="restoreTo(index)"
          >
            {{ name }} #{{ index }}
          </button>
        </div>
        <ol class="log">
          <li v-for="(action, index) in session.actions" :key="index">
            {{ index }} · seat{{ action.player_index }} {{ action.action_type }}
            <span v-if="action.tile_id">{{ action.tile_id }}</span>
            <span v-if="action.target_tile">{{ action.target_tile }}</span>
          </li>
        </ol>
      </section>
    </aside>

    <section class="verifier-board">
      <div ref="stageElement" class="game-stage" />
      <p v-if="!session" class="board-empty">开局后显示四家手牌 / 河 / 副露（验证器亮全部手牌）</p>
    </section>

    <aside class="verifier-pane verifier-pane--right">
      <div class="tabs">
        <button type="button" :class="{ active: tab === 'dump' }" @click="tab = 'dump'">GameState</button>
        <button type="button" :class="{ active: tab === 'ws' }" @click="tab = 'ws'">WS</button>
        <button type="button" :class="{ active: tab === 'record' }" @click="tab = 'record'">牌谱</button>
        <button type="button" :class="{ active: tab === 'tools' }" @click="tab = 'tools'">工具</button>
      </div>
      <pre v-if="tab === 'dump'" class="json">{{ pretty(inspectDump) }}</pre>
      <pre v-if="tab === 'ws'" class="json">{{ pretty(session?.messages || []) }}</pre>
      <div v-if="tab === 'record'" class="record-tab">
        <div class="row">
          <button type="button" :disabled="!recordReplay" @click="stepRecord(-1)">上一节点</button>
          <button type="button" :disabled="!recordReplay" @click="stepRecord(1)">下一节点</button>
        </div>
        <p v-if="recordReplay">局 {{ recordRound + 1 }} / {{ recordReplay.rounds.length }} · 节点 {{ recordNode }}</p>
        <textarea v-model="pastedRecord" rows="6" placeholder="粘贴牌谱 JSON（含 game_title / game_round）" />
        <button type="button" @click="loadPastedRecord">用粘贴牌谱覆盖桌面</button>
        <pre class="json">{{ pretty(currentTicks) }}</pre>
      </div>
      <div v-if="tab === 'tools'">
        <button type="button" :disabled="busy || !session" @click="runTingpai">听牌 GB_tingpai_check（视角座位）</button>
        <pre class="json">{{ pretty(session?.tool_calls || []) }}</pre>
      </div>
    </aside>
  </main>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { MahjongScene } from '@/game2d/game/scene/MahjongScene'
import { GAME_SOUND_ASSETS, getPreloadedSoundUrl } from '@/game2d/game/resources'
import { RecordReplay, type PublicGameRecord, type RecordTick } from '@/game2d/replay/recordReplay'
import { tileFaceAssetUrl, salasasaFaceId } from '@/game2d/lib/tileFaceAsset'
import { dumpToSnapshot, type VerifierDump } from '@/game2d/verifier/snapshot'

const API = '/verifier-api'
const winds = ['东', '南', '西', '北']
const actionNames: Record<string, string> = {
  pass: '过',
  force_pass: '强制过',
  ready: '准备',
  buhua: '补花',
  peng: '碰',
  gang: '杠',
  angang: '暗杠',
  jiagang: '加杠',
  chi_left: '吃左',
  chi_mid: '吃中',
  chi_right: '吃右',
  hu_self: '自摸',
  hu_first: '和',
  hu_second: '和',
  hu_third: '和',
  hu: '和',
}

type SeatLegal = {
  waiting?: boolean
  client_actions?: string[]
  cut_tiles?: number[]
  target_tiles?: Record<string, number[]>
}

type SessionSnap = {
  id: string
  seed: number
  action_count: number
  actions: Array<Record<string, any>>
  bookmarks: Record<string, number>
  legal: { seats: Record<string, SeatLegal> }
  dump: VerifierDump
  messages: unknown[]
  record: PublicGameRecord
  tool_calls: unknown[]
  loop_error?: string | null
}

const scenarios = ref<Array<{ id: string; title: string }>>([])
const scenarioId = ref('qi_dui_tenpai_1m')
const session = ref<SessionSnap | null>(null)
const busy = ref(false)
const errorMessage = ref('')
const viewerSeat = ref(0)
const tab = ref<'dump' | 'ws' | 'record' | 'tools'>('dump')
const bookmarkName = ref('ask')
const pastedRecord = ref('')
const recordRound = ref(0)
const recordNode = ref(0)
const usePasted = ref(false)
const stageElement = ref<HTMLElement | null>(null)
let scene: MahjongScene | null = null

const dump = computed<VerifierDump>(() => session.value?.dump || {})
const currentSeatLegal = computed<SeatLegal>(() => session.value?.legal?.seats?.[String(viewerSeat.value)] || {})
const visibleButtons = computed(() =>
  (currentSeatLegal.value.client_actions || []).filter((action) => !['cut', 'angang', 'jiagang'].includes(action)),
)
const kongTargets = computed(() => {
  const targets = currentSeatLegal.value.target_tiles || {}
  const out: Array<{ action: string; tile: number }> = []
  for (const action of ['angang', 'jiagang']) {
    if (!(currentSeatLegal.value.client_actions || []).includes(action)) continue
    for (const tile of targets[action] || []) out.push({ action, tile })
  }
  return out
})
const inspectDump = computed(() => {
  const value = dump.value as Record<string, unknown>
  const { tiles_list, game_record, ...rest } = value
  return {
    ...rest,
    tiles_remaining: Array.isArray(tiles_list) ? tiles_list.length : value.tiles_remaining,
    tiles_list_head: Array.isArray(tiles_list) ? tiles_list.slice(0, 16) : [],
  }
})
const recordSource = computed<PublicGameRecord | null>(() => {
  if (usePasted.value) return parseRecord(pastedRecord.value)
  return session.value?.record ?? null
})
const recordReplay = computed(() => {
  const detail = recordSource.value
  if (!detail?.record?.game_round || !Object.keys(detail.record.game_round).length) return null
  try {
    return new RecordReplay(detail)
  } catch {
    return null
  }
})
const currentTicks = computed<RecordTick[]>(() => {
  const replay = recordReplay.value
  if (!replay) return []
  const round = replay.rounds[Math.max(0, Math.min(replay.rounds.length - 1, recordRound.value))]
  return (round?.action_ticks || []).slice(0, recordNode.value + 1)
})

function pretty(value: unknown): string {
  return JSON.stringify(value, null, 2)
}

function actionLabel(action: string): string {
  return actionNames[action] || action
}

function tileAsset(tile: number): string {
  const faceId = salasasaFaceId(tile)
  return tileFaceAssetUrl(faceId, { baseUrl: import.meta.env.BASE_URL })
}

function parseRecord(text: string): PublicGameRecord | null {
  try {
    const parsed = JSON.parse(text)
    if (parsed?.record?.game_round) return parsed as PublicGameRecord
    if (parsed?.game_title && parsed?.game_round) {
      return {
        game_id: 'pasted',
        created_at: '',
        rule: 'guobiao',
        players: [],
        record: parsed,
      }
    }
  } catch {
    return null
  }
  return null
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers || {}) },
    ...init,
  })
  const data = await response.json().catch(() => ({}))
  if (!response.ok) {
    const detail = (data as { detail?: string }).detail
    throw new Error(detail || `${response.status} ${path}`)
  }
  return data as T
}

async function loadScenarios() {
  try {
    const data = await api<{ scenarios: Array<{ id: string; title: string }> }>('/scenarios')
    scenarios.value = data.scenarios || []
    if (!scenarios.value.some((item) => item.id === scenarioId.value) && scenarios.value[0]) {
      scenarioId.value = scenarios.value[0].id
    }
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  }
}

async function mountScene() {
  if (!stageElement.value || scene) return
  const current = new MahjongScene(() => {})
  current.setPresentationMode('replay')
  current.setReplayRecordVersion(6)
  for (const sound of GAME_SOUND_ASSETS) {
    const audio = new Audio(getPreloadedSoundUrl(sound.file))
    audio.preload = 'auto'
    audio.load()
    current.loadSound(sound.alias, audio)
  }
  scene = current
  const mounted = await current.mount(stageElement.value)
  if (!mounted) return
  current.forceResize()
}

async function renderLive() {
  await nextTick()
  await mountScene()
  if (!scene || usePasted.value) return
  const snapshot = dumpToSnapshot(dump.value, viewerSeat.value)
  if (snapshot) scene.flushFromSnapshot(snapshot)
}

async function renderRecord() {
  await nextTick()
  await mountScene()
  const replay = recordReplay.value
  if (!scene || !replay) return
  const maxRound = replay.rounds.length - 1
  recordRound.value = Math.max(0, Math.min(maxRound, recordRound.value))
  const ticks = replay.rounds[recordRound.value]?.action_ticks || []
  recordNode.value = Math.max(0, Math.min(ticks.length, recordNode.value))
  const position = replay.build(recordRound.value, recordNode.value, viewerSeat.value, true)
  scene.flushFromSnapshot(position.snapshot)
}

async function applySession(next: SessionSnap) {
  session.value = next
  if (next.loop_error) errorMessage.value = next.loop_error
  else errorMessage.value = ''
  if (!usePasted.value) await renderLive()
}

async function startSession() {
  busy.value = true
  errorMessage.value = ''
  try {
    if (session.value) await api(`/sessions/${session.value.id}`, { method: 'DELETE' }).catch(() => null)
    const next = await api<SessionSnap>('/sessions', {
      method: 'POST',
      body: JSON.stringify({ scenario_id: scenarioId.value }),
    })
    usePasted.value = false
    await applySession(next)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

async function stopSession() {
  if (!session.value) return
  busy.value = true
  try {
    await api(`/sessions/${session.value.id}`, { method: 'DELETE' })
    session.value = null
  } finally {
    busy.value = false
  }
}

async function submitAction(actionType: string, tile?: number) {
  if (!session.value) return
  busy.value = true
  errorMessage.value = ''
  try {
    const body: Record<string, unknown> = {
      player_index: viewerSeat.value,
      action_type: actionType,
    }
    if (actionType === 'cut') body.tile_id = tile
    if (actionType === 'angang' || actionType === 'jiagang') body.target_tile = tile
    const next = await api<SessionSnap>(`/sessions/${session.value.id}/actions`, {
      method: 'POST',
      body: JSON.stringify(body),
    })
    await applySession(next)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

async function readyAll() {
  if (!session.value) return
  busy.value = true
  try {
    await applySession(await api<SessionSnap>(`/sessions/${session.value.id}/ready-all`, { method: 'POST' }))
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

async function forceTimeout() {
  if (!session.value) return
  busy.value = true
  try {
    await applySession(await api<SessionSnap>(`/sessions/${session.value.id}/timeout`, { method: 'POST' }))
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

async function restoreTo(index: number) {
  if (!session.value) return
  busy.value = true
  try {
    await applySession(await api<SessionSnap>(`/sessions/${session.value.id}/restore`, {
      method: 'POST',
      body: JSON.stringify({ index }),
    }))
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

async function saveBookmark() {
  if (!session.value) return
  const data = await api<{ name: string; index: number }>(`/sessions/${session.value.id}/bookmarks`, {
    method: 'POST',
    body: JSON.stringify({ name: bookmarkName.value || 'ask' }),
  })
  session.value.bookmarks = { ...session.value.bookmarks, [data.name]: data.index }
}

async function runTingpai() {
  if (!session.value) return
  busy.value = true
  try {
    await api(`/sessions/${session.value.id}/tools/tingpai`, {
      method: 'POST',
      body: JSON.stringify({ seat: viewerSeat.value }),
    })
    session.value = await api<SessionSnap>(`/sessions/${session.value.id}`)
    tab.value = 'tools'
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : String(error)
  } finally {
    busy.value = false
  }
}

function loadPastedRecord() {
  if (!parseRecord(pastedRecord.value)) {
    errorMessage.value = '粘贴的不是可解析牌谱 JSON'
    return
  }
  usePasted.value = true
  recordRound.value = 0
  recordNode.value = 0
  tab.value = 'record'
  void renderRecord()
}

function stepRecord(delta: number) {
  const replay = recordReplay.value
  if (!replay) return
  const ticks = replay.rounds[recordRound.value]?.action_ticks || []
  const next = recordNode.value + delta
  if (next < 0) {
    if (recordRound.value > 0) {
      recordRound.value -= 1
      recordNode.value = replay.rounds[recordRound.value]?.action_ticks?.length || 0
    }
    return
  }
  if (next > ticks.length) {
    if (recordRound.value < replay.rounds.length - 1) {
      recordRound.value += 1
      recordNode.value = 0
    }
    return
  }
  recordNode.value = next
}

watch(viewerSeat, () => {
  if (usePasted.value || tab.value === 'record') void renderRecord()
  else void renderLive()
})
watch(tab, () => {
  if (tab.value === 'record') void renderRecord()
  else if (!usePasted.value) void renderLive()
})
watch([recordRound, recordNode, usePasted], () => {
  if (usePasted.value || tab.value === 'record') void renderRecord()
})

onMounted(() => {
  void loadScenarios()
  void mountScene()
})

onBeforeUnmount(() => {
  scene?.destroy()
  scene = null
})
</script>

<style src="./Game.css"></style>
<style src="./Verifier.css"></style>
