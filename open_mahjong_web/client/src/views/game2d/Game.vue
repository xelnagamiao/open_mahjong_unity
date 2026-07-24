<template>
  <div v-if="!session.player && !session.restoring" class="game-blocked">
    <div class="game-blocked-card">
      <h1>需要先登录</h1>
      <p>登录后才能恢复或进入国标对局。</p>
      <div class="game-blocked-actions">
        <button type="button" @click="router.push('/2d')">返回大厅</button>
        <button type="button" @click="session.init()">重新检查</button>
      </div>
    </div>
  </div>

  <div v-else class="mahjongGame" :style="{ background: appearance.backgroundColorOutside }">
    <div class="game-page__layout" :style="{ background: appearance.backgroundColorOutside }">
      <section class="game-page__board-panel">
        <div class="game-page__stage-shell" :style="{ background: appearance.backgroundColorTable }">
          <div ref="stageElement" class="game-stage" />
          <div class="game-stage-toolbar">
            <GameAssistPanel
              :settings="assistSettings"
              :expanded="assistExpandOpen"
              @update="patchAssistSettings"
              @toggle-expand="toggleAssistExpand"
            />
            <button
              type="button"
              class="scene-appearance-toggle__button"
              :aria-expanded="tileSkipOpen"
              @click="toggleTileSkipPanel"
            >
              牌张设置
            </button>
          </div>
          <div v-if="assistExpandOpen" class="game-stage-panel">
            <div class="scene-appearance-toggle__card">
              <GameAssistPanel
                detail-only
                :settings="assistSettings"
                :expanded="true"
                @update="patchAssistSettings"
              />
            </div>
          </div>
          <div v-if="tileSkipOpen" class="game-stage-panel game-stage-panel--wide">
            <div class="scene-appearance-toggle__card">
              <GameTileSkipPanel
                :settings="assistSettings"
                :tile-src="mmcrTileAsset"
                @update="patchAssistSettings"
                @clear-tiles="patchAssistSettings({ silentTiles: [] })"
              />
            </div>
          </div>
          <div v-if="!sceneReady || !hasSnapshot" class="game-loading">
            {{ sceneReady ? '等待服务端恢复国标牌局…' : '正在加载中…' }}
          </div>

          <div v-if="roundResult && !finalResult" class="end-result-layer">
            <!-- 流局：立刻出框，无按键 -->
            <section
              v-if="isDrawResult"
              class="end-result-panel end-result-panel--draw"
              aria-label="本局流局"
            >
              <div
                v-for="seat in drawSeatDeltas"
                :key="`draw-delta-${seat.relative}`"
                class="end-result-draw-delta"
                :class="`is-rel-${seat.relative}`"
              >
                <span class="end-result-draw-delta__name">{{ seat.username }}</span>
                <span
                  v-if="seat.change"
                  class="end-result-draw-delta__change"
                  :class="seat.change > 0 ? 'is-plus' : 'is-minus'"
                >
                  {{ seat.change > 0 ? '+' : '' }}{{ seat.change }}
                </span>
                <span v-else class="end-result-draw-delta__change is-zero">0</span>
              </div>
              <div class="end-result-draw-title">流局</div>
            </section>

            <!-- 和牌：2D 牌面 + 两排番表 + 棱形座位 + 右下角确定倒计时 -->
            <template v-else>
              <button
                type="button"
                class="end-result-visibility"
                @click="resultContentVisible = !resultContentVisible"
              >
                {{ resultContentVisible ? '隐藏面板' : '显示面板' }}
              </button>
              <section
                v-show="resultContentVisible"
                class="end-result-panel end-result-panel--win"
                aria-label="本局和牌结算"
              >
                <div v-if="resultClosedTiles.length || resultWinTile" class="end-result-hand">
                  <span
                    v-for="(tile, index) in resultClosedTiles"
                    :key="`closed-${index}`"
                    class="end-result-tile"
                  >
                    <img :src="tileAsset(tile)" alt="" />
                  </span>
                  <span v-if="resultMeldTiles.length" class="end-result-hand__split" />
                  <span
                    v-for="(tile, index) in resultMeldTiles"
                    :key="`meld-${index}`"
                    class="end-result-tile"
                  >
                    <img :src="tileAsset(tile)" alt="" />
                  </span>
                  <span class="end-result-hand__split" />
                  <span v-if="resultWinTile" class="end-result-tile is-winning">
                    <img :src="tileAsset(resultWinTile)" alt="和牌张" />
                  </span>
                </div>
                <div v-if="resultFlowerTiles.length" class="end-result-flowers">
                  <span class="end-result-flowers__label">花</span>
                  <span
                    v-for="(tile, index) in resultFlowerTiles"
                    :key="`flower-${index}`"
                    class="end-result-tile end-result-tile--flower"
                  >
                    <img :src="tileAsset(tile)" alt="" />
                  </span>
                </div>

                <div
                  class="end-result-fan-grid"
                  aria-label="番种"
                  :style="{ minHeight: fanGridMinHeight }"
                >
                  <div
                    v-for="(fan, index) in resultFans"
                    :key="`${fan.name}-${index}`"
                    class="end-result-fan"
                    :class="{ 'is-visible': index < revealedFanCount }"
                  >
                    <span>{{ fan.name }}</span>
                    <strong>{{ fan.value }}</strong>
                  </div>
                </div>

                <div
                  class="end-result-total"
                  aria-label="总番数"
                  :class="{ 'is-visible': showResultTotal }"
                >
                  <div class="end-result-total__line">
                    <strong>{{ roundResult.hu_score ?? 0 }}</strong><span>番</span>
                  </div>
                  <small>{{ resultClassLabel }}</small>
                </div>

                <div class="end-result-diamond" aria-label="分数">
                  <article
                    v-for="slot in diamondSlots"
                    :key="`seat-rel-${slot.relative}`"
                    class="end-result-seat"
                    :class="[
                      `is-rel-${slot.relative}`,
                      {
                        'is-ready': slot.player?.ready,
                        'is-winner': slot.player
                          && slot.player.player_index === roundResult.hepai_player_index,
                      },
                    ]"
                  >
                    <template v-if="slot.player">
                      <strong class="end-result-seat__name">{{ slot.player.username }}</strong>
                      <div class="end-result-seat__score">
                        <span>{{ slot.player.score }}</span>
                        <span
                          v-if="slot.player.change"
                          :class="slot.player.change > 0 ? 'is-plus' : 'is-minus'"
                        >
                          {{ slot.player.change > 0 ? '+' : '' }}{{ slot.player.change }}
                        </span>
                        <span v-else class="is-zero">0</span>
                      </div>
                    </template>
                  </article>
                </div>

                <button
                  v-if="showReadyButton"
                  type="button"
                  class="end-result-ready-button"
                  :disabled="readySent"
                  @click="sendReady"
                >
                  {{ readyButtonLabel }}
                </button>
              </section>
            </template>
          </div>
        </div>
      </section>

      <aside class="game-page__sidebar">
        <div class="game-page__ratings-area" :class="{ 'is-expanded': ratingsExpanded }">
          <div v-for="player in sidebarPlayers" :key="player.player_index" class="game-page__sidebar-card">
            <div class="player-name">{{ player.username || `#${player.user_id}` }}</div>
            <div class="player-rating">{{ player.guobiao_rank }} · {{ player.guobiao_score.toFixed(2) }} PT</div>
            <div class="player-rating">对局分 {{ player.score }}</div>
          </div>
        </div>

        <div class="game-page__sidebar-bottom-row">
          <button
            v-if="sidebarPlayers.length"
            type="button"
            class="scene-appearance-toggle__button"
            :aria-expanded="ratingsExpanded"
            @click="toggleRatings"
          >
            玩家信息
          </button>
          <button
            type="button"
            class="scene-appearance-toggle__button"
            :aria-expanded="appearanceOpen"
            @click="appearanceOpen = !appearanceOpen; assistExpandOpen = false; tileSkipOpen = false"
          >
            外观
          </button>
        </div>

        <div v-if="appearanceOpen" class="scene-appearance-toggle__panel">
          <div class="scene-appearance-toggle__card">
            <SceneAppearancePanel
              :appearance="appearance"
              :background-image-name="backgroundImage?.name ?? null"
              :background-image-loading="backgroundImageLoading"
              :volume="volume"
              @volume="changeVolume"
              @table-color="setAppearanceField('backgroundColorTable', $event)"
              @outside-color="setAppearanceField('backgroundColorOutside', $event)"
              @image-enabled="setAppearanceField('backgroundImageEnabled', $event)"
              @image-alpha="setAppearanceField('backgroundImageAlpha', $event)"
              @image-selected="uploadBackgroundImage"
              @image-cleared="clearBackgroundImage"
              @cover-color="setTileCoverColor"
              @add-cover-color="addTileCoverColor"
              @remove-cover-color="removeTileCoverColor"
              @cover-rotate-mode="setAppearanceField('tileCoverRotateMode', $event)"
              @moqie-shortcut="setAppearanceField('moqieShortcutMode', $event)"
              @pass-shortcut="setAppearanceField('passShortcutMode', $event)"
              @flower-area-display="setAppearanceField('flowerAreaDisplay', $event)"
              @flower-area-color="setAppearanceField('flowerAreaColor', $event)"
              @flower-area-alpha="setAppearanceField('flowerAreaAlpha', $event)"
              @tile-face-theme="setAppearanceField('tileFaceTheme', $event)"
              @flower-face-theme="setAppearanceField('flowerFaceTheme', $event)"
              @reset="resetAppearance"
            />
          </div>
        </div>
      </aside>
    </div>

    <el-dialog
      :model-value="Boolean(finalResult)"
      title="国标排位结束"
      :show-close="false"
      :close-on-click-modal="false"
      :close-on-press-escape="false"
      width="min(94vw, 620px)"
    >
      <el-table :data="finalRows">
        <el-table-column prop="rank" label="名次" width="80"><template #default="scope">第 {{ scope.row.rank }} 名</template></el-table-column>
        <el-table-column prop="username" label="玩家" />
        <el-table-column prop="score" label="总分" width="90" />
        <el-table-column label="段位变化" width="160"><template #default="scope">{{ scope.row.rank_before ?? '—' }} → {{ scope.row.rank_after ?? '—' }}</template></el-table-column>
      </el-table>
      <template #footer><el-button type="primary" @click="router.push('/2d')">返回匹配大厅</el-button></template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import SceneAppearancePanel from './SceneAppearancePanel.vue'
import GameAssistPanel from './GameAssistPanel.vue'
import GameTileSkipPanel from './GameTileSkipPanel.vue'
import { useGame2dSessionStore } from '@/stores/game2dSession'
import { MahjongScene } from '@/game2d/game/scene/MahjongScene'
import { SalasasaGameAdapter } from '@/game2d/salasasa/gameAdapter'
import { salasasaClient } from '@/game2d/salasasa/client'
import {
  loadStoredSceneAppearance,
  loadStoredVolume,
  resetStoredSceneAppearance,
  saveStoredSceneAppearance,
  saveStoredVolume,
} from '@/game2d/lib/storage'
import {
  loadStoredAssistSettings,
  normalizeAssistSettings,
  saveStoredAssistSettings,
} from '@/game2d/lib/assistSettings'
import { DEFAULT_SCENE_APPEARANCE, MAX_TILE_COVER_COLORS, normalizeSceneAppearanceSettings } from '@/game2d/lib/sceneAppearance'
import { findFanByName, formatFanField } from '@/constants/guessFanCatalog'
import {
  clearStoredSceneBackgroundImage,
  loadStoredSceneBackgroundImage,
  saveStoredSceneBackgroundImage,
} from '@/game2d/lib/sceneBackgroundImage'

const SOUND_ASSETS = [
  ...[
    '01-start', '03-cd', '05-draw', '06-discard', '08-inquire', '09-cpk', '25-xchg',
  ].map((alias) => ({ alias, file: `${alias}.wav` })),
  { alias: 'fan-reveal', file: 'fan-reveal.mp3' },
  ...[1, 2].flatMap((voiceId) =>
    ['chi', 'peng', 'gang', 'buhua', 'hu'].map((voice) => ({
      alias: `voice-${voiceId}-${voice}`,
      file: `voices/${voiceId}/${voice}.mp3`,
    })),
  ),
]

// Align with Unity RoundEndTiming / HepaiRevealTiming.
const RESULT_HAND_REVEAL_MS = 1700
const HU_FAN_REVEAL_INTERVAL_MS = 500
const HU_BEFORE_TOTAL_MS = 500
const HU_CONFIRM_COUNTDOWN_SEC = 8

const router = useRouter()
const session = useGame2dSessionStore()
const stageElement = ref(null)
const sceneReady = ref(false)
const hasSnapshot = ref(false)
const roundResult = ref(null)
const readySent = ref(false)
const readyStatus = ref({})
const resultContentVisible = ref(true)
const finalResult = ref(null)
const appearanceOpen = ref(false)
const assistExpandOpen = ref(false)
const tileSkipOpen = ref(false)
const ratingsExpanded = ref(window.matchMedia('(min-width: 901px)').matches)
const sidebarPlayers = ref([])
const volume = ref(loadStoredVolume())
const appearance = ref(loadStoredSceneAppearance())
const assistSettings = ref(loadStoredAssistSettings())
const backgroundImage = ref(null)
const backgroundImageLoading = ref(true)

/** Staged win reveal: how many fan rows are visible. */
const revealedFanCount = ref(0)
const showResultTotal = ref(false)
const showReadyButton = ref(false)
const readyCountdown = ref(0)

let scene = null
let adapter = null
let unsubscribe = null
let mounted = false
let pendingGameResponses = []
/** @type {number[]} */
let resultTimers = []
/** Invalidates pending reveal/countdown timers without relying on reactive Proxy identity. */
let resultRevealToken = 0

function clearResultTimers() {
  for (const id of resultTimers) window.clearTimeout(id)
  resultTimers = []
}

function scheduleResultTimer(fn, delayMs) {
  const id = window.setTimeout(() => {
    resultTimers = resultTimers.filter((timerId) => timerId !== id)
    fn()
  }, delayMs)
  resultTimers.push(id)
  return id
}

function resetWinRevealState() {
  revealedFanCount.value = 0
  showResultTotal.value = false
  showReadyButton.value = false
  readyCountdown.value = 0
}

function clearRoundResultUi() {
  resultRevealToken += 1
  clearResultTimers()
  roundResult.value = null
  resetWinRevealState()
  resultContentVisible.value = true
  readySent.value = false
}

/**
 * @param {object} result
 * @param {import('@/game2d/game/scene/MahjongScene').MahjongScene} targetScene
 */
function revealRoundResultAfterHand(result, targetScene) {
  clearResultTimers()
  roundResult.value = null
  resetWinRevealState()
  resultContentVisible.value = true
  const token = ++resultRevealToken

  const isDraw = result?.hepai_player_index == null
  if (isDraw) {
    if (!mounted || scene !== targetScene || finalResult.value || token !== resultRevealToken) return
    roundResult.value = result
    return
  }

  // 1. 展示手牌 → 2. 弹出面板 → 3. 逐条番种 → 4. 倒计时准备
  // 不在弹面板时播 hu / 锣：国标 hu 已在 show_result 事件里播过（对局和牌路径）。
  scheduleResultTimer(() => {
    if (!mounted || scene !== targetScene || finalResult.value || token !== resultRevealToken) return
    roundResult.value = result
    startWinFanRevealSequence(result, token, targetScene)
  }, RESULT_HAND_REVEAL_MS)
}

/**
 * @param {object} result
 * @param {number} token
 * @param {import('@/game2d/game/scene/MahjongScene').MahjongScene} targetScene
 */
function startWinFanRevealSequence(result, token, targetScene) {
  const fans = Array.isArray(result?.hu_fan) ? result.hu_fan : []
  let delay = 0
  for (let index = 0; index < fans.length; index += 1) {
    delay += HU_FAN_REVEAL_INTERVAL_MS
    const count = index + 1
    scheduleResultTimer(() => {
      if (!mounted || token !== resultRevealToken) return
      revealedFanCount.value = count
      targetScene.playUiSound('fan-reveal')
    }, delay)
  }
  delay += HU_BEFORE_TOTAL_MS
  scheduleResultTimer(() => {
    if (!mounted || token !== resultRevealToken) return
    showResultTotal.value = true
    // 高番锣与 3D 总分出现时机对齐，不在弹面板瞬间播。
    targetScene.playResultGong(result.hu_fan ?? [])
    beginReadyCountdown(token)
  }, Math.max(delay, HU_BEFORE_TOTAL_MS))
}

/**
 * @param {number} token
 */
function beginReadyCountdown(token) {
  showReadyButton.value = true
  readyCountdown.value = HU_CONFIRM_COUNTDOWN_SEC
  for (let tick = 1; tick <= HU_CONFIRM_COUNTDOWN_SEC; tick += 1) {
    scheduleResultTimer(() => {
      if (!mounted || token !== resultRevealToken) return
      readyCountdown.value = Math.max(0, HU_CONFIRM_COUNTDOWN_SEC - tick)
    }, tick * 1000)
  }
}

const selfSeat = computed(() => sidebarPlayers.value.find(
  (player) => Number(player.user_id) === Number(session.player?.user_id),
)?.player_index ?? 0)

const isDrawResult = computed(() => roundResult.value?.hepai_player_index == null)

const resultClassLabel = computed(() => ({
  hu_self: '自摸', hu_first: '荣和', hu_second: '荣和', hu_third: '荣和', liuju: '流局',
}[roundResult.value?.hu_class] || roundResult.value?.hu_class || '本局结算'))

const resultPlayers = computed(() => sidebarPlayers.value
  .map((player) => {
    const seat = player.player_index
    const score = Number(roundResult.value?.player_to_score?.[String(seat)] ?? player.score ?? 0)
    const changeKey = String(player.original_player_index ?? seat)
    const change = Number(roundResult.value?.score_changes?.[changeKey] ?? 0)
    const relative = (seat - selfSeat.value + 4) % 4
    return {
      ...player,
      score,
      change,
      ready: Boolean(readyStatus.value[String(seat)]),
      position: ['自家', '下家', '对家', '上家'][relative],
      relative,
    }
  })
  .sort((left, right) => left.relative - right.relative))

const drawSeatDeltas = computed(() => resultPlayers.value)

/** 棱形：对家上(2)、下家右(1)、自家下(0)、上家左(3) */
const diamondSlots = computed(() => {
  const byRelative = new Map(resultPlayers.value.map((player) => [player.relative, player]))
  return [
    { relative: 2, player: byRelative.get(2) ?? null },
    { relative: 3, player: byRelative.get(3) ?? null },
    { relative: 1, player: byRelative.get(1) ?? null },
    { relative: 0, player: byRelative.get(0) ?? null },
  ]
})

const resultRawHand = computed(() => roundResult.value?.hepai_player_hand ?? [])
const resultWinTile = computed(() => resultRawHand.value.at(-1) ?? 0)
const resultClosedTiles = computed(() => resultRawHand.value.slice(0, -1).sort((a, b) => a - b))
const resultMeldTiles = computed(() => (roundResult.value?.hepai_player_combination_mask ?? [])
  .flatMap((mask) => mask.filter((value, index) => index % 2 === 1 && value > 10)))
const resultFlowerTiles = computed(() => roundResult.value?.hepai_player_huapai ?? [])
const resultFans = computed(() => (roundResult.value?.hu_fan ?? []).map((name) => {
  const definition = findFanByName(name, ['guobiao'])
  return { name, value: definition ? `${formatFanField(definition.fan)}番` : '' }
}))
/** Reserve fan rows up front so the diamond does not jump as fans appear. */
const fanGridMinHeight = computed(() => {
  const rows = Math.max(1, Math.ceil((resultFans.value.length || 1) / 2))
  return `${rows * 36}px`
})

const readyButtonLabel = computed(() => {
  if (readySent.value) return '已准备，等待其他玩家'
  if (readyCountdown.value > 0) return `确定(${readyCountdown.value})`
  return '确定(0)'
})

const finalRows = computed(() => Object.entries(finalResult.value?.player_final_data ?? {})
  .map(([seat, value]) => ({ seat, ...value }))
  .sort((a, b) => (a.rank ?? 99) - (b.rank ?? 99)))

function updateSidebarPlayers() {
  sidebarPlayers.value = [...(adapter?.gameInfo?.players_info ?? [])]
    .sort((left, right) => left.player_index - right.player_index)
    .map((player) => ({
      ...player,
      guobiao_rank: player.guobiao_rank || '10级',
      guobiao_score: Number(player.guobiao_score ?? 0),
    }))
}

function applyMessage(response, targetAdapter = adapter, targetScene = scene) {
  if (!targetAdapter || !targetScene) return
  try {
    const update = targetAdapter.accept(response)
    if (!update) return
    if (update.snapshot) {
      clearRoundResultUi()
      targetScene.flushFromSnapshot(update.snapshot)
      hasSnapshot.value = true
      updateSidebarPlayers()
    }
    if (update.event) targetScene.handleEvent(update.event)
    if (update.events) update.events.forEach((event) => targetScene.handleEvent(event))
    if (update.result) {
      readySent.value = false
      resultContentVisible.value = true
      readyStatus.value = Object.fromEntries(sidebarPlayers.value.map((player) => [
        String(player.player_index), Number(player.user_id) <= 10,
      ]))
      if (update.result.player_to_score) {
        sidebarPlayers.value = sidebarPlayers.value.map((player) => ({
          ...player,
          score: update.result.player_to_score[String(player.player_index)] ?? player.score,
        }))
      }
      revealRoundResultAfterHand(update.result, targetScene)
    }
    if (update.ready) readyStatus.value = { ...readyStatus.value, ...update.ready.player_to_ready }
    if (update.ended) {
      clearResultTimers()
      finalResult.value = update.ended
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '2D 牌桌数据转换失败')
  }
}

function handleResponse(response) {
  if (response.type === 'message' && response.message === 'login_kickout') {
    void ElMessageBox.alert(
      response.message_info?.content || '当前 2D 对局连接已失效，该账号刚刚在另一个 2D 或 3D 客户端登录。',
      response.message_info?.title || '账号已在其他客户端登录',
      {
        type: 'warning',
        confirmButtonText: '返回 2D 大厅',
        closeOnClickModal: false,
        closeOnPressEscape: false,
      },
    ).finally(() => router.push('/2d'))
    return
  }
  if (response.type.startsWith('gamestate/guobiao/')) {
    if (!sceneReady.value || !adapter || !scene) {
      pendingGameResponses.push(response)
    } else {
      applyMessage(response)
    }
  }
  if (response.type === 'tips' && response.message) ElMessage.info(response.message)
}

function flushPendingGameResponses(currentAdapter, currentScene) {
  const buffered = salasasaClient.drainGuobiaoBuffer()
  const queued = buffered.length > 0 ? buffered : pendingGameResponses.splice(0)
  pendingGameResponses = []
  if (!queued.length && salasasaClient.lastGameStart) {
    queued.push(salasasaClient.lastGameStart)
  }
  for (const response of queued) applyMessage(response, currentAdapter, currentScene)
}

async function mountScene() {
  if (!mounted || scene || !session.player || !stageElement.value) return
  const currentAdapter = new SalasasaGameAdapter(session.player.user_id)
  adapter = currentAdapter
  const currentScene = new MahjongScene((type, payload) => {
    if (type === 'appearance.lastTileCoverIndex' && typeof payload?.index === 'number') {
      onTileCoverIndexChosen(payload.index)
      return
    }
    if (type === 'ping' || type !== 'game.input') return
    const outgoing = currentAdapter.encodeSceneInput(payload)
    if (!outgoing || !salasasaClient.send(outgoing)) ElMessage.error('操作未发送，请等待连接恢复')
  })
  scene = currentScene
  const mountedScene = await currentScene.mount(stageElement.value)
  if (!mountedScene || !mounted || scene !== currentScene) return
  currentScene.setVolume(volume.value)
  currentScene.setAppearance(appearance.value)
  currentScene.setBackgroundImage(backgroundImage.value?.dataUrl ?? null)
  currentScene.setAssistSettings(assistSettings.value)
  for (const sound of SOUND_ASSETS) {
    const audio = new Audio(`${import.meta.env.BASE_URL}game2d-assets/sounds/${sound.file}`)
    audio.preload = 'auto'
    audio.load()
    currentScene.loadSound(sound.alias, audio)
  }
  sceneReady.value = true
  flushPendingGameResponses(currentAdapter, currentScene)
}

function destroyScene() {
  clearRoundResultUi()
  sceneReady.value = false
  hasSnapshot.value = false
  sidebarPlayers.value = []
  pendingGameResponses = []
  scene?.destroy()
  scene = null
  adapter = null
}

function persistAppearance(next) {
  appearance.value = normalizeSceneAppearanceSettings(next)
  saveStoredSceneAppearance(appearance.value)
  scene?.setAppearance(appearance.value)
}

function setAppearanceField(field, value) {
  persistAppearance({ ...appearance.value, [field]: value })
}

function setTileCoverColor(index, color) {
  const colors = [...appearance.value.tileCoverColors]
  colors[index] = color
  persistAppearance({ ...appearance.value, tileCoverColors: colors })
}

function addTileCoverColor() {
  const colors = appearance.value.tileCoverColors
  if (colors.length >= MAX_TILE_COVER_COLORS) return
  persistAppearance({
    ...appearance.value,
    tileCoverColors: [...colors, colors.at(-1) ?? '#f6bc1e'],
  })
}

function removeTileCoverColor(index) {
  if (appearance.value.tileCoverColors.length <= 1) return
  persistAppearance({
    ...appearance.value,
    tileCoverColors: appearance.value.tileCoverColors.filter((_, colorIndex) => colorIndex !== index),
  })
}

function onTileCoverIndexChosen(index) {
  persistAppearance({
    ...appearance.value,
    lastTileCoverIndex: index,
  })
}

async function uploadBackgroundImage(file) {
  try {
    backgroundImage.value = await saveStoredSceneBackgroundImage(file)
    persistAppearance({ ...appearance.value, backgroundImageEnabled: true })
    scene?.setBackgroundImage(backgroundImage.value.dataUrl)
  } catch {
    ElMessage.error('背景图片保存失败')
  }
}

async function clearBackgroundImage() {
  try {
    await clearStoredSceneBackgroundImage()
    backgroundImage.value = null
    persistAppearance({ ...appearance.value, backgroundImageEnabled: false })
    scene?.setBackgroundImage(null)
  } catch {
    ElMessage.error('背景图片移除失败')
  }
}

async function resetAppearance() {
  resetStoredSceneAppearance()
  try { await clearStoredSceneBackgroundImage() } catch { /* IndexedDB 不可用时仍重置颜色 */ }
  appearance.value = { ...DEFAULT_SCENE_APPEARANCE, tileCoverColors: [...DEFAULT_SCENE_APPEARANCE.tileCoverColors] }
  backgroundImage.value = null
  scene?.setAppearance(appearance.value)
  scene?.setBackgroundImage(null)
}

function changeVolume(next) {
  volume.value = Number(next)
  saveStoredVolume(volume.value)
  scene?.setVolume(volume.value)
}

function toggleRatings() {
  ratingsExpanded.value = !ratingsExpanded.value
  window.setTimeout(() => scene?.forceResize(), 100)
}

function sendReady() {
  if (readySent.value) return
  const outgoing = adapter?.readyMessage()
  if (!outgoing || !salasasaClient.send(outgoing)) {
    ElMessage.error('暂时无法发送准备状态')
    return
  }
  readySent.value = true
  readyStatus.value = { ...readyStatus.value, [String(selfSeat.value)]: true }
}

function toggleAssistExpand() {
  assistExpandOpen.value = !assistExpandOpen.value
  if (assistExpandOpen.value) {
    tileSkipOpen.value = false
    appearanceOpen.value = false
  }
}

function toggleTileSkipPanel() {
  tileSkipOpen.value = !tileSkipOpen.value
  if (tileSkipOpen.value) {
    assistExpandOpen.value = false
    appearanceOpen.value = false
  }
}

function patchAssistSettings(partial) {
  assistSettings.value = normalizeAssistSettings({ ...assistSettings.value, ...partial })
  saveStoredAssistSettings(assistSettings.value)
  scene?.setAssistSettings(assistSettings.value)
}

function mmcrTileAsset(tid) {
  const suit = Number(tid) & 0xe0
  const rank = Number(tid) & 0x0f
  let prefix = 'z'
  if (suit === 0x40) prefix = 'Man'
  else if (suit === 0x60) prefix = 'Pin'
  else if (suit === 0xc0) prefix = 'Sou'
  else if (suit === 0xa0) prefix = 'z'
  else if (suit === 0xe0) prefix = 'Flower'
  const folder = appearance.value.tileFaceTheme === 'black' && prefix !== 'Flower' ? 'Black' : 'Regular'
  return `${import.meta.env.BASE_URL}game2d-assets/textures/riichi-mahjong-tiles/${folder}/${prefix}${rank}.svg`
}

function tileAsset(tile) {
  const normalized = Number(tile) >= 100 ? Number(tile) % 100 : Number(tile)
  const suit = Math.floor(normalized / 10)
  const rank = normalized % 10
  const prefix = ({ 1: 'Man', 2: 'Pin', 3: 'Sou', 4: 'z', 5: 'Flower' })[suit] || 'z'
  if (suit === 5 && appearance.value.flowerFaceTheme === 'unity') {
    return `${import.meta.env.BASE_URL}game2d-assets/textures/riichi-mahjong-tiles/Unity/${prefix}${rank}.png`
  }
  const folder = appearance.value.tileFaceTheme === 'black' && suit !== 5 ? 'Black' : 'Regular'
  return `${import.meta.env.BASE_URL}game2d-assets/textures/riichi-mahjong-tiles/${folder}/${prefix}${rank}.svg`
}

watch(() => session.player?.user_id, async (userId) => {
  if (!mounted) return
  if (!userId) {
    destroyScene()
    return
  }
  await nextTick()
  await mountScene()
}, { flush: 'post' })

onMounted(async () => {
  mounted = true
  unsubscribe = salasasaClient.subscribe(handleResponse)
  try {
    backgroundImage.value = await loadStoredSceneBackgroundImage()
  } catch {
    backgroundImage.value = null
  } finally {
    backgroundImageLoading.value = false
  }
  await session.init()
  await nextTick()
  await mountScene()
})

onBeforeUnmount(() => {
  mounted = false
  unsubscribe?.()
  destroyScene()
})
</script>

<style src="./Game.css"></style>
