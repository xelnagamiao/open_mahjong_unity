<template>
  <div class="lobby-page">
    <header class="lobby-header">
      <div class="lobby-brand">
        <img src="/game2d-assets/logo512.png" alt="MMCR">
        <div><strong>Salasasa 2D</strong><span>国标麻将 · MMCR 桌面</span></div>
      </div>
      <el-space wrap>
        <el-icon v-if="session.restoring" class="is-loading"><Loading /></el-icon>
        <template v-if="session.player">
          <el-tag :type="session.status === 'online' ? 'success' : 'warning'">
            {{ session.status === 'online' ? '已连接' : '重连中' }}
          </el-tag>
          <el-button :icon="User" @click="router.push(`/2d/player/${session.player.user_id}`)">
            {{ session.player.username }}
          </el-button>
          <el-button :icon="SwitchButton" @click="handleLogout">退出</el-button>
        </template>
        <el-button v-else type="primary" :icon="User" @click="loginOpen = true">登录</el-button>
      </el-space>
    </header>

    <main class="lobby-main">
      <section class="lobby-hero">
        <div>
          <h1>同一牌桌，两种视觉</h1>
          <p>2D 网页端与现有 Unity 3D 客户端共用原有登录、匹配和国标对局服务。</p>
        </div>
        <div class="rank-chip">
          <el-icon><TrophyBase /></el-icon>
          <span>当前段位</span>
          <strong>{{ session.rank?.guobiao_rank ?? '登录后查看' }}</strong>
          <small>{{ session.rank ? `${session.rank.guobiao_score} 分` : '国标排位' }}</small>
        </div>
      </section>

      <el-card v-if="joinedQueue" class="queue-banner" shadow="never">
        <el-space wrap>
          <el-icon class="is-loading"><Loading /></el-icon>
          <strong>{{ matchFound ? '匹配成功，正在准备牌桌……' : `正在匹配：${joinedQueue}` }}</strong>
          <el-button v-if="!matchFound" type="danger" @click="leaveQueue">取消匹配</el-button>
        </el-space>
      </el-card>

      <div class="lobby-grid">
        <section>
          <div class="section-title">
            <div><h2>匹配房间</h2><p>仅支持国标规则；段位和资格由现有服务端校验。</p></div>
            <el-tag type="primary">12 个排位队列</el-tag>
          </div>
          <div class="queue-grid">
            <el-card
              v-for="queue in queueCards"
              :key="queue.queueKey"
              shadow="hover"
              :class="['queue-card', `tier-${queue.tierKey}`]"
            >
              <div class="queue-card__top"><strong>{{ queue.title }}</strong><el-tag type="info">{{ queue.format }}</el-tag></div>
              <p>{{ queue.note }}</p>
              <div class="queue-card__counts">
                <span><el-icon><UserFilled /></el-icon> 等待 {{ queue.status?.waiting ?? 0 }}</span>
                <span>对局中 {{ queue.status?.playing ?? 0 }}</span>
              </div>
              <el-button
                type="primary"
                class="queue-button"
                :disabled="Boolean(joinedQueue) || session.status !== 'online'"
                @click="joinQueue(queue.queueKey)"
              >{{ queue.rounds }} · 开始匹配</el-button>
            </el-card>
          </div>
        </section>

        <aside>
          <el-card class="leaderboard-card" shadow="never">
            <template #header>
              <div class="leaderboard-title">
                <span><el-icon><TrophyBase /></el-icon> 国标排行榜</span>
                <el-button text :icon="Refresh" :loading="leadersBusy" aria-label="刷新排行榜" @click="refreshLeaderboard" />
              </div>
            </template>
            <el-table
              v-loading="leadersBusy"
              :data="leaders"
              size="small"
              class="leaderboard-table"
              @row-click="openPlayer"
            >
              <el-table-column prop="rank_position" label="#" width="44" />
              <el-table-column prop="username" label="玩家" show-overflow-tooltip />
              <el-table-column prop="guobiao_rank" label="段位" width="86" />
            </el-table>
          </el-card>
        </aside>
      </div>
    </main>

    <el-dialog v-model="loginOpen" title="登录 Salasasa" width="min(92vw, 480px)" destroy-on-close>
      <el-form label-position="top" @submit.prevent="submitLogin">
        <el-form-item label="用户名" required><el-input v-model="loginForm.username" autocomplete="username" /></el-form-item>
        <el-form-item label="密码" required><el-input v-model="loginForm.password" type="password" show-password autocomplete="current-password" @keyup.enter="submitLogin" /></el-form-item>
        <el-button type="primary" class="login-button" :loading="loginBusy" @click="submitLogin">登录并连接游戏服务</el-button>
      </el-form>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Loading, Refresh, SwitchButton, TrophyBase, User, UserFilled } from '@element-plus/icons-vue'
import { useGame2dSessionStore } from '@/stores/game2dSession'
import { leaderboardUrl, publicApiGet } from '@/game2d/salasasa/api'
import { salasasaClient } from '@/game2d/salasasa/client'

const TIERS = [
  { key: 'beginner', title: '初级场', note: '适合熟悉国标规则与平台操作的玩家' },
  { key: 'intermediate', title: '中级场', note: '面向稳定完成国标对局的进阶玩家' },
  { key: 'advanced', title: '高级场', note: '高段位国标竞技房间' },
  { key: 'mcrpl', title: 'MCRPL', note: '仅限已取得 MCRPL 资格的玩家' },
]

const FORMATS = [
  { key: 'dongfeng', title: '东风局', rounds: '4 小局' },
  { key: 'banzhuang', title: '半庄', rounds: '8 小局' },
  { key: 'quanzhuang', title: '全庄', rounds: '16 小局' },
]

const router = useRouter()
const session = useGame2dSessionStore()
const loginOpen = ref(false)
const loginBusy = ref(false)
const loginForm = reactive({ username: '', password: '' })
const queueStatus = ref({})
const joinedQueue = ref(null)
const matchFound = ref(false)
const leaders = ref([])
const leadersBusy = ref(true)
let unsubscribe = null
let queueTimer = null

const queueCards = computed(() => TIERS.flatMap((tier) => FORMATS.map((format) => {
  const queueKey = `${tier.key}_${format.key}`
  return { ...tier, tierKey: tier.key, format: format.title, rounds: format.rounds, queueKey, status: queueStatus.value[queueKey] }
})))

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
  if (response.type === 'gamestate/guobiao/game_start') router.push('/2d/game')
}

function refreshQueueStatus() {
  salasasaClient.send({ type: 'match/get_queue_status' })
}

watch(() => session.status, (status) => {
  if (queueTimer) window.clearInterval(queueTimer)
  queueTimer = null
  if (status === 'online') {
    refreshQueueStatus()
    queueTimer = window.setInterval(refreshQueueStatus, 5_000)
  }
}, { immediate: true })

function joinQueue(queueKey) {
  if (!session.player) {
    loginOpen.value = true
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

async function submitLogin() {
  if (!loginForm.username.trim() || !loginForm.password) {
    ElMessage.warning('请输入用户名和密码')
    return
  }
  loginBusy.value = true
  try {
    await session.login(loginForm.username, loginForm.password)
    loginOpen.value = false
    loginForm.password = ''
    ElMessage.success('登录成功')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登录失败')
  } finally {
    loginBusy.value = false
  }
}

function handleLogout() {
  joinedQueue.value = null
  matchFound.value = false
  session.logout()
}

function openPlayer(row) {
  router.push(`/2d/player/${row.user_id}`)
}

onMounted(async () => {
  unsubscribe = salasasaClient.subscribe(handleResponse)
  await Promise.all([session.init(), refreshLeaderboard()])
})

onBeforeUnmount(() => {
  unsubscribe?.()
  if (queueTimer) window.clearInterval(queueTimer)
})
</script>

<style scoped src="./Lobby.css"></style>
