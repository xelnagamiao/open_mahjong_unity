<template>
  <div v-if="!session.player && !session.restoring" class="game-blocked">
    <div class="game-blocked-card">
      <h1>需要先登录</h1>
      <p>登录后才能恢复或进入国标对局。</p>
      <el-button type="primary" @click="router.push('/2d')">返回大厅</el-button>
    </div>
  </div>

  <div v-else class="mahjongGame" :style="{ background: appearance.backgroundColorOutside }">
    <div class="game-page__stage-shell game-page__stage-shell--full">
      <div ref="stageElement" class="game-stage" />
      <div v-if="!sceneReady || !hasSnapshot" class="game-loading">
        <el-icon class="is-loading" :size="36"><Loading /></el-icon>
        <span>{{ sceneReady ? '等待服务端恢复国标牌局……' : '正在加载 2D 牌桌……' }}</span>
      </div>
      <div class="game-toolbar">
        <el-tag :type="session.status === 'online' ? 'success' : 'warning'">
          <el-icon><Connection /></el-icon> {{ session.status === 'online' ? '已连接' : '重连中' }}
        </el-tag>
        <el-button :icon="Setting" @click="settingsOpen = !settingsOpen">设置</el-button>
        <el-button :icon="ArrowLeft" :disabled="!finalResult" @click="router.push('/2d')">返回大厅</el-button>
      </div>
      <el-card v-if="settingsOpen" class="game-settings" shadow="always">
        <template #header>牌桌设置</template>
        <span>音量</span>
        <el-slider v-model="volume" :min="0" :max="1" :step="0.05" @input="changeVolume" />
      </el-card>
    </div>

    <el-dialog
      :model-value="Boolean(roundResult) && !finalResult"
      :title="roundTitle"
      :show-close="false"
      :close-on-click-modal="false"
      :close-on-press-escape="false"
      width="min(92vw, 520px)"
    >
      <div class="round-result">
        <div class="round-score"><strong>{{ roundResult?.hu_score ?? 0 }} 番</strong><span>{{ roundResult?.hu_class || '流局' }}</span></div>
        <div class="fan-list">
          <template v-if="roundResult?.hu_fan?.length"><el-tag v-for="fan in roundResult.hu_fan" :key="fan" type="primary">{{ fan }}</el-tag></template>
          <span v-else>无番种信息</span>
        </div>
        <div v-if="roundResult?.score_changes" class="score-changes">
          <span v-for="(score, seat) in roundResult.score_changes" :key="seat">座位 {{ Number(seat) + 1 }}：{{ score >= 0 ? '+' : '' }}{{ score }}</span>
        </div>
      </div>
      <template #footer>
        <el-button type="primary" :loading="readySent" :disabled="readySent" @click="sendReady">
          {{ readySent ? '已准备，等待其他玩家' : '准备下一局' }}
        </el-button>
      </template>
    </el-dialog>

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
import { ElMessage } from 'element-plus'
import { ArrowLeft, Connection, Loading, Setting } from '@element-plus/icons-vue'
import { useGame2dSessionStore } from '@/stores/game2dSession'
import { MahjongScene } from '@/game2d/game/scene/MahjongScene'
import { SalasasaGameAdapter } from '@/game2d/salasasa/gameAdapter'
import { salasasaClient } from '@/game2d/salasasa/client'
import { loadStoredSceneAppearance, loadStoredVolume, saveStoredVolume } from '@/game2d/lib/storage'
import { loadStoredSceneBackgroundImage } from '@/game2d/lib/sceneBackgroundImage'

const SOUND_ALIASES = [
  '01-start', '03-cd', '05-draw', '06-discard', '08-inquire', '09-cpk',
  '14-chow-m', '16-pung-m', '18-kong-m', '20-win-m', '25-xchg',
]

const router = useRouter()
const session = useGame2dSessionStore()
const stageElement = ref(null)
const sceneReady = ref(false)
const hasSnapshot = ref(false)
const roundResult = ref(null)
const readySent = ref(false)
const finalResult = ref(null)
const settingsOpen = ref(false)
const volume = ref(loadStoredVolume())
const appearance = ref(loadStoredSceneAppearance())
let scene = null
let adapter = null
let unsubscribe = null
let mounted = false

const roundTitle = computed(() => roundResult.value?.hepai_player_index == null
  ? '本局流局'
  : `第 ${Number(roundResult.value.hepai_player_index) + 1} 家和牌`)

const finalRows = computed(() => Object.entries(finalResult.value?.player_final_data ?? {})
  .map(([seat, value]) => ({ seat, ...value }))
  .sort((a, b) => (a.rank ?? 99) - (b.rank ?? 99)))

function applyMessage(response, targetAdapter = adapter, targetScene = scene) {
  if (!targetAdapter || !targetScene) return
  try {
    const update = targetAdapter.accept(response)
    if (!update) return
    if (update.snapshot) {
      targetScene.flushFromSnapshot(update.snapshot)
      hasSnapshot.value = true
      roundResult.value = null
      readySent.value = false
    }
    if (update.event) targetScene.handleEvent(update.event)
    if (update.events) update.events.forEach((event) => targetScene.handleEvent(event))
    if (update.result) {
      roundResult.value = update.result
      readySent.value = false
    }
    if (update.ended) finalResult.value = update.ended
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '2D 牌桌数据转换失败')
  }
}

function handleResponse(response) {
  if (response.type.startsWith('gamestate/guobiao/')) applyMessage(response)
  if (response.type === 'tips' && response.message) ElMessage.info(response.message)
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
  try {
    const backgroundImage = await loadStoredSceneBackgroundImage()
    if (scene === currentScene) currentScene.setBackgroundImage(backgroundImage?.dataUrl ?? null)
  } catch {
    currentScene.setBackgroundImage(null)
  }
  for (const alias of SOUND_ALIASES) {
    const audio = new Audio(`${import.meta.env.BASE_URL}game2d-assets/sounds/${alias}.wav`)
    audio.preload = 'auto'
    currentScene.loadSound(alias, audio)
  }
  sceneReady.value = true
  if (salasasaClient.lastGameStart) applyMessage(salasasaClient.lastGameStart, currentAdapter, currentScene)
}

function destroyScene() {
  sceneReady.value = false
  hasSnapshot.value = false
  scene?.destroy()
  scene = null
  adapter = null
}

function changeVolume(next) {
  volume.value = Number(next)
  saveStoredVolume(volume.value)
  scene?.setVolume(volume.value)
}

function sendReady() {
  const outgoing = adapter?.readyMessage()
  if (!outgoing || !salasasaClient.send(outgoing)) {
    ElMessage.error('暂时无法发送准备状态')
    return
  }
  readySent.value = true
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

<style scoped src="./Game.css"></style>
