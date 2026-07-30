<template>
  <div class="lobby-page">
    <header class="lobby-header">
      <div class="lobby-brand">
        <strong>Salasasa 2D</strong>
        <a
          class="lobby-table-credit"
          href="https://mmcr.online/"
          target="_blank"
          rel="noopener noreferrer"
          aria-label="牌桌设计鸣谢 mmcr.online"
        >牌桌设计鸣谢：mmcr.online</a>
      </div>
      <el-space wrap class="lobby-header__actions">
        <el-icon v-if="session.restoring" class="is-loading"><Loading /></el-icon>
        <template v-if="session.player">
          <el-tag :type="session.status === 'online' ? 'success' : 'warning'">
            {{ session.status === 'online' ? '已连接' : '重连中' }}
          </el-tag>
          <el-button :icon="User" @click="router.push(`/2d/player/${session.player.user_id}`)">
            {{ session.player.username }}
          </el-button>
          <el-button :icon="SwitchButton" @click="handleLogout">退出登录</el-button>
        </template>
        <template v-else>
          <el-button v-if="auth.isLoggedIn" type="primary" :loading="loginBusy" @click="connectWithWebsite">
            以 {{ auth.username || '网站账号' }} 连接
          </el-button>
          <el-button v-else type="primary" :icon="User" @click="goWebsiteLogin">网站登录</el-button>
        </template>
        <el-button text @click="router.push('/')">返回主站</el-button>
      </el-space>
    </header>

    <nav class="lobby-subheader" aria-label="2D 大厅功能">
      <div class="lobby-subheader__inner">
        <button :class="['lobby-subheader__unit', { active: activeSection === 'battle' }]" @click="activeSection = 'battle'">
          <span>对战</span><small>BATTLE</small>
        </button>
        <button :class="['lobby-subheader__unit', { active: activeSection === 'record' }]" @click="activeSection = 'record'">
          <span>牌谱阅览</span><small>RECORD</small>
        </button>
        <button class="lobby-subheader__unit lobby-subheader__data" @click="openDataStation">
          <span>前往数据站</span><small>DATA</small>
        </button>
      </div>
    </nav>

    <main class="lobby-main">
      <section v-show="activeSection === 'battle'" class="battle-mode">
      <section class="lobby-summary">
        <div class="lobby-summary__copy">
          <h1>国标游戏大厅</h1>
        </div>
        <div class="rank-summary">
          <span>当前段位</span>
          <strong>{{ session.rank?.guobiao_rank ?? '—' }}</strong>
          <small>{{ session.rank ? `${formatRankPt(session.rank)} PT` : '— / — PT' }}</small>
        </div>
      </section>
      <section class="custom-room-section">
        <CustomRoomPanel
          ref="roomPanelRef"
          :online="session.status === 'online'"
          :my-user-id="session.player?.user_id"
          :joined-queue="joinedQueue"
          expanded
          @occupy-changed="inCustomRoom = $event"
        />
      </section>

      <div v-if="joinedQueue" class="queue-banner">
        <div>
          <el-icon class="is-loading"><Loading /></el-icon>
          <span>{{ matchFound ? '匹配成功，正在准备牌桌' : '正在匹配' }}</span>
          <strong>{{ joinedQueueLabel }}</strong>
        </div>
        <el-button v-if="!matchFound" @click="leaveQueue">取消匹配</el-button>
      </div>

      <div class="lobby-grid">
        <section class="match-section">
          <div class="section-title">
            <h2>匹配场</h2>
            <div class="population-summary">
              <span>等待：<b>{{ totalWaiting }}</b></span>
              <span>对局：<b>{{ totalPlaying }}</b></span>
            </div>
          </div>
          <div class="tier-grid">
            <article
              v-for="tier in tierGroups"
              :key="tier.key"
              :class="['tier-card', `tier-${tier.key}`]"
            >
              <header class="tier-card__header">
                <span class="tier-mark" />
                <strong>{{ tier.title }}</strong>
              </header>
              <div class="match-list">
                <div
                  v-for="queue in tier.queues"
                  :key="queue.queueKey"
                  class="match-row"
                >
                  <div class="match-row__name">
                    <strong>{{ queue.title }}</strong>
                    <small>{{ queue.rounds }}</small>
                  </div>
                  <div class="match-row__counts" aria-label="匹配人数">
                    <span>等待：<b>{{ queue.status?.waiting ?? 0 }}</b></span>
                    <span>对局：<b>{{ queue.status?.playing ?? 0 }}</b></span>
                  </div>
                  <el-button
                    type="primary"
                    size="small"
                    :class="{ 'match-button--unqualified': !canEnterTier(tier.key) }"
                    :disabled="Boolean(joinedQueue) || inCustomRoom || session.status !== 'online'"
                    @click="joinQueue(queue.queueKey)"
                  >匹配</el-button>
                </div>
              </div>
            </article>
          </div>
        </section>

        <aside>
          <el-card class="leaderboard-card" shadow="never">
            <template #header>
              <div class="leaderboard-title">
                <span>国标排行榜</span>
                <el-button text :loading="leadersBusy" @click="refreshLeaderboard">刷新</el-button>
              </div>
            </template>
            <el-table
              v-loading="leadersBusy"
              :data="leaders"
              size="small"
              class="leaderboard-table"
              @row-click="openPlayer"
            >
              <el-table-column prop="rank_position" label="排名" width="56" align="center" />
              <el-table-column prop="username" label="玩家" show-overflow-tooltip />
              <el-table-column label="PT" width="92" align="right">
                <template #default="{ row }">
                  <span class="leaderboard-pt">{{ formatRankPt(row) }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="guobiao_rank" label="段位" width="68" align="center" />
            </el-table>
          </el-card>
        </aside>
      </div>
      </section>

      <LobbyRecordPanel v-if="activeSection === 'record'" />
    </main>

  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Loading, SwitchButton, User } from '@element-plus/icons-vue'
import { useGame2dSessionStore } from '@/stores/game2dSession'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { getPlayerToken } from '@/api/playerClient'
import { leaderboardUrl, publicApiGet, queueStatusUrl } from '@/game2d/salasasa/api'
import { salasasaClient } from '@/game2d/salasasa/client'
import { getRankEntry } from '@/constants/rankTable'
import CustomRoomPanel from './CustomRoomPanel.vue'
import LobbyRecordPanel from './LobbyRecordPanel.vue'

const TIERS = [
  { key: 'beginner', title: '初级场' },
  { key: 'intermediate', title: '中级场' },
  { key: 'advanced', title: '高级场' },
  { key: 'mcrpl', title: 'MCRPL' },
]

const FORMATS = [
  { key: 'dongfeng', title: '东风局', rounds: '4 小局' },
  { key: 'banzhuang', title: '半庄', rounds: '8 小局' },
  { key: 'quanzhuang', title: '全庄', rounds: '16 小局' },
]

const router = useRouter()
const session = useGame2dSessionStore()
const auth = usePlayerAuthStore()
const roomPanelRef = ref(null)
const loginBusy = ref(false)
const queueStatus = ref({})
const joinedQueue = ref(null)
const matchFound = ref(false)
const inCustomRoom = ref(false)
const leaders = ref([])
const leadersBusy = ref(true)
const activeSection = ref('battle')
let unsubscribe = null
let queueTimer = null

const tierGroups = computed(() => TIERS.map((tier) => ({
  ...tier,
  queues: FORMATS.map((format) => {
    const queueKey = `${tier.key}_${format.key}`
    return { ...format, queueKey, status: queueStatus.value[queueKey] }
  }),
})))

const totalWaiting = computed(() => Object.values(queueStatus.value)
  .reduce((total, status) => total + Number(status?.waiting ?? 0), 0))
const totalPlaying = computed(() => Object.values(queueStatus.value)
  .reduce((total, status) => total + Number(status?.playing ?? 0), 0))
const joinedQueueLabel = computed(() => {
  for (const tier of tierGroups.value) {
    const queue = tier.queues.find((item) => item.queueKey === joinedQueue.value)
    if (queue) return `${tier.title} · ${queue.title}`
  }
  return joinedQueue.value
})

function formatPt(value) {
  const score = Number(value)
  if (!Number.isFinite(score)) return '—'
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(score)
}

function formatRankPt(row) {
  const current = formatPt(row?.guobiao_score)
  const target = getRankEntry(row?.guobiao_rank)?.promoteScore
  return target == null ? current : `${current}/${formatPt(target)}`
}

function canEnterTier(tierKey) {
  if (tierKey === 'beginner') return true
  const rankIndex = getRankEntry(session.rank?.guobiao_rank)?.index ?? 0
  if (tierKey === 'intermediate') {
    return rankIndex < 16 && (rankIndex >= 8 || Boolean(session.rank?.is_sponsor))
  }
  if (tierKey === 'advanced') return rankIndex >= 12
  if (tierKey === 'mcrpl') return Boolean(session.rank?.is_mcrpl_qualified)
  return false
}

async function refreshLeaderboard() {
  leadersBusy.value = true
  try {
    leaders.value = await publicApiGet(leaderboardUrl(20))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '排行榜加载失败')
  } finally {
    leadersBusy.value = false
  }
}

function handleResponse(response) {
  roomPanelRef.value?.handleResponse?.(response)
  if (response.type === 'message' && response.message === 'login_kickout') {
    void ElMessageBox.alert(
      response.message_info?.content || '当前 2D 连接已失效，该账号刚刚在另一个 2D 或 3D 客户端登录。',
      response.message_info?.title || '账号已在其他客户端登录',
      {
        type: 'warning',
        confirmButtonText: '知道了',
        closeOnClickModal: false,
        closeOnPressEscape: false,
      },
    )
    return
  }
  if (response.type === 'match/queue_status' && response.queue_status) queueStatus.value = response.queue_status
  if (response.type === 'match/join_queue_done' && response.success) ElMessage.success(response.message || '已加入匹配')
  if (response.type === 'match/leave_queue_done' && response.success) {
    joinedQueue.value = null
    matchFound.value = false
    ElMessage.success(response.message || '已取消匹配')
  }
  if (response.type === 'match/match_found') {
    matchFound.value = true
    ElMessage.success(response.message || '匹配成功，即将开局')
  }
  if (response.type === 'tips' && response.message) {
    if (response.success === false) joinedQueue.value = null
    ElMessage[response.success === false ? 'error' : 'info'](response.message)
  }
  if (response.type === 'error_message' && response.message) {
    ElMessage.error(response.message)
  }
  if (response.type === 'gamestate/guobiao/game_start') router.push('/2d/game')
}

async function refreshQueueStatus() {
  if (session.status === 'online') {
    salasasaClient.send({ type: 'match/get_queue_status' })
    return
  }
  try {
    const status = await publicApiGet(queueStatusUrl())
    if (session.status !== 'online') queueStatus.value = status
  } catch (error) {
    console.warn('[2D] 公开匹配人数加载失败', error)
  }
}

watch(() => session.status, (status) => {
  if (queueTimer) window.clearInterval(queueTimer)
  void refreshQueueStatus()
  queueTimer = window.setInterval(() => void refreshQueueStatus(), 5_000)
}, { immediate: true })

function ensureConnected() {
  if (session.player && session.status === 'online') return true
  if (auth.isLoggedIn) {
    void connectWithWebsite()
    return false
  }
  goWebsiteLogin()
  return false
}

function joinQueue(queueKey) {
  if (!ensureConnected()) return
  if (inCustomRoom.value) {
    ElMessage.warning('请先离开自定义房间再匹配')
    return
  }
  if (!salasasaClient.send({ type: 'match/join_queue', queue_type: queueKey })) {
    ElMessage.error('游戏连接尚未就绪')
    return
  }
  joinedQueue.value = queueKey
}

function leaveQueue() {
  if (!salasasaClient.send({ type: 'match/leave_queue' })) ElMessage.error('游戏连接尚未就绪')
}

function goWebsiteLogin() {
  router.push({ path: '/login', query: { redirect: '/2d' } })
}

async function connectWithWebsite({ silent = false } = {}) {
  if (!auth.loaded) await auth.fetchMe()
  const token = getPlayerToken()
  if (!token || !auth.isLoggedIn) {
    goWebsiteLogin()
    return
  }
  loginBusy.value = true
  try {
    await session.loginWithWebsiteToken(token)
    if (!silent) ElMessage.success('已使用网站登录态连接游戏')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '连接游戏失败')
  } finally {
    loginBusy.value = false
  }
}

function handleLogout() {
  joinedQueue.value = null
  matchFound.value = false
  inCustomRoom.value = false
  auth.logout()
}

function openPlayer(row) {
  router.push({ path: '/player-data', query: { player: String(row.user_id) } })
}

function openDataStation() {
  const userId = session.player?.user_id ?? auth.userId
  if (userId != null) {
    router.push({ path: '/player-data', query: { player: String(userId) } })
    return
  }
  router.push('/player-data/platform')
}

onMounted(async () => {
  unsubscribe = salasasaClient.subscribe(handleResponse)
  try {
    await Promise.all([
      import('./Game.vue'),
      auth.loaded ? Promise.resolve() : auth.fetchMe(),
    ])
    await Promise.all([session.init(), refreshLeaderboard()])
  } catch (error) {
    console.error('[2D] 资源预加载失败', error)
    const detail = error instanceof Error && error.message
      ? error.message
      : '请检查网络后刷新页面'
    ElMessage.error(`2D 资源加载失败：${detail}`)
  }
})

onBeforeUnmount(() => {
  unsubscribe?.()
  if (queueTimer) window.clearInterval(queueTimer)
})
</script>

<style scoped src="./Lobby.css"></style>
