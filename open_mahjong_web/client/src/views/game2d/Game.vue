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
          <div v-if="!sceneReady || !hasSnapshot" class="game-loading">
            {{ sceneReady ? '等待服务端恢复国标牌局…' : '正在加载中…' }}
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
            :aria-expanded="settingsOpen"
            @click="settingsOpen = !settingsOpen"
          >
            设置
          </button>
        </div>

        <div v-if="settingsOpen" class="scene-appearance-toggle__panel">
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

    <div v-if="roundResult && !finalResult" class="end-result-layer">
      <button type="button" class="end-result-visibility" @click="resultContentVisible = !resultContentVisible">
        {{ resultContentVisible ? '隐藏面板' : '显示面板' }}
      </button>
      <section v-show="resultContentVisible" class="end-result-panel" aria-label="本局结算与准备状态">
        <header class="end-result-header">
          <div>
            <span class="end-result-kicker">{{ roundTitle }}</span>
            <h2>{{ resultClassLabel }}</h2>
          </div>
          <div class="end-result-rule-mark">国标</div>
        </header>

        <div v-if="resultClosedTiles.length" class="end-result-hand-block">
          <div class="end-result-section-title">和牌手牌</div>
          <div class="end-result-hand">
            <img v-for="(tile, index) in resultClosedTiles" :key="`closed-${index}`" :src="tileAsset(tile)" alt="" />
            <span v-if="resultMeldTiles.length" class="end-result-hand__split" />
            <img v-for="(tile, index) in resultMeldTiles" :key="`meld-${index}`" :src="tileAsset(tile)" alt="" />
            <span class="end-result-hand__split" />
            <img v-if="resultWinTile" class="is-winning" :src="tileAsset(resultWinTile)" alt="和牌张" />
          </div>
          <div v-if="resultFlowerTiles.length" class="end-result-flowers">
            <span>花牌</span>
            <img v-for="(tile, index) in resultFlowerTiles" :key="`flower-${index}`" :src="tileAsset(tile)" alt="" />
          </div>
        </div>

        <div class="end-result-score-sheet">
          <div class="end-result-fans">
            <div class="end-result-section-title">番种明细</div>
            <div v-if="resultFans.length" class="end-result-fan-grid">
              <div v-for="(fan, index) in resultFans" :key="`${fan.name}-${index}`" class="end-result-fan">
                <span>{{ fan.name }}</span><strong>{{ fan.value }}</strong>
              </div>
            </div>
            <div v-else class="end-result-empty">本局无番种明细</div>
          </div>
          <aside class="end-result-total" aria-label="本局总番数">
            <span class="end-result-total__label">和牌得分</span>
            <div><strong>{{ roundResult.hu_score ?? 0 }}</strong><span>番</span></div>
            <small>{{ resultClassLabel }} · {{ resultFans.length }} 项番种</small>
          </aside>
        </div>

        <div class="end-result-section-title end-result-seats-title">分数与准备</div>
        <div class="end-result-seats">
          <article
            v-for="player in resultPlayers"
            :key="player.player_index"
            class="end-result-seat"
            :class="{ 'is-ready': player.ready, 'is-winner': player.player_index === roundResult.hepai_player_index }"
          >
            <div class="end-result-seat__topline">
              <span class="end-result-seat__position">{{ player.position }}</span>
              <span v-if="player.player_index === roundResult.hepai_player_index" class="end-result-seat__winner">和牌</span>
            </div>
            <strong>{{ player.username }}</strong>
            <div class="end-result-seat__score">
              {{ player.score }}
              <span v-if="player.change" :class="player.change > 0 ? 'is-plus' : 'is-minus'">
                {{ player.change > 0 ? '+' : '' }}{{ player.change }}
              </span>
            </div>
            <div class="end-result-seat__ready">
              <span class="end-result-seat__ready-dot" />
              {{ player.ready ? '已准备' : '等待准备' }}
            </div>
          </article>
        </div>

        <button type="button" class="end-result-ready-button" :disabled="readySent" @click="sendReady">
          {{ readySent ? '已准备，等待其他玩家' : '确定并准备下一局' }}
        </button>
      </section>
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
import { DEFAULT_SCENE_APPEARANCE, normalizeSceneAppearanceSettings } from '@/game2d/lib/sceneAppearance'
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
  ...[1, 2].flatMap((voiceId) =>
    ['chi', 'peng', 'gang', 'buhua', 'hu'].map((voice) => ({
      alias: `voice-${voiceId}-${voice}`,
      file: `voices/${voiceId}/${voice}.mp3`,
    })),
  ),
]

// Match Unity's guobiao reveal: 0.2s winning-tile travel + 1.5s hand hold.
const RESULT_HAND_REVEAL_MS = 1700

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
const settingsOpen = ref(false)
const ratingsExpanded = ref(window.matchMedia('(min-width: 901px)').matches)
const sidebarPlayers = ref([])
const volume = ref(loadStoredVolume())
const appearance = ref(loadStoredSceneAppearance())
const backgroundImage = ref(null)
const backgroundImageLoading = ref(true)
let scene = null
let adapter = null
let unsubscribe = null
let mounted = false
let pendingGameResponses = []
let resultPanelTimer = null

function clearResultPanelTimer() {
  if (resultPanelTimer !== null) {
    window.clearTimeout(resultPanelTimer)
    resultPanelTimer = null
  }
}

function revealRoundResultAfterHand(result, targetScene) {
  clearResultPanelTimer()
  roundResult.value = null
  resultContentVisible.value = true
  resultPanelTimer = window.setTimeout(() => {
    resultPanelTimer = null
    if (!mounted || scene !== targetScene || finalResult.value) return
    roundResult.value = result
    targetScene.playResultGong(result.hu_fan ?? [])
  }, RESULT_HAND_REVEAL_MS)
}

const selfSeat = computed(() => sidebarPlayers.value.find(
  (player) => Number(player.user_id) === Number(session.player?.user_id),
)?.player_index ?? 0)

const roundTitle = computed(() => {
  const winner = sidebarPlayers.value.find(
    (player) => player.player_index === roundResult.value?.hepai_player_index,
  )
  return roundResult.value?.hepai_player_index == null ? '本局流局' : `${winner?.username || '玩家'} 和牌`
})

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
      clearResultPanelTimer()
      targetScene.flushFromSnapshot(update.snapshot)
      hasSnapshot.value = true
      roundResult.value = null
      readySent.value = false
      readyStatus.value = {}
      resultContentVisible.value = true
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
      clearResultPanelTimer()
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
    // When nobody can flower, game_start and the dealer's first hand prompt are
    // sent back-to-back. Pixi may still be mounting; preserve the complete
    // ordered stream instead of losing the first actionable prompt.
    if (!sceneReady.value || !adapter || !scene) {
      pendingGameResponses.push(response)
    } else {
      applyMessage(response)
    }
  }
  if (response.type === 'tips' && response.message) ElMessage.info(response.message)
}

function flushPendingGameResponses(currentAdapter, currentScene) {
  const queued = pendingGameResponses.splice(0)
  const hasGameStart = queued.some((response) => response.type === 'gamestate/guobiao/game_start')
  if (!hasGameStart && salasasaClient.lastGameStart) queued.unshift(salasasaClient.lastGameStart)
  for (const response of queued) applyMessage(response, currentAdapter, currentScene)
}

async function mountScene() {
  if (!mounted || scene || !session.player || !stageElement.value) return
  const currentAdapter = new SalasasaGameAdapter(session.player.user_id)
  adapter = currentAdapter
  const currentScene = new MahjongScene((type, payload) => {
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
  clearResultPanelTimer()
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
  const outgoing = adapter?.readyMessage()
  if (!outgoing || !salasasaClient.send(outgoing)) {
    ElMessage.error('暂时无法发送准备状态')
    return
  }
  readySent.value = true
  readyStatus.value = { ...readyStatus.value, [String(selfSeat.value)]: true }
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
