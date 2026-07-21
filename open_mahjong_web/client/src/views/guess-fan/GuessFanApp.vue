<template>
  <div class="gf">
    <header class="welcome">
      <h1>猜番对抗</h1>
    </header>

    <div v-if="!auth.loaded" class="tip-box">加载中…</div>

    <div v-else-if="!auth.isLoggedIn" class="tip-box">
      <p>请先使用 salasasa 游戏账号登录后再进入猜番对抗。</p>
      <router-link class="panel-btn" :to="{ path: '/login', query: { redirect: '/guess-fan' } }">去登录</router-link>
    </div>

    <template v-else-if="mode === 'lobby'">
      <section class="sec">
        <div class="sec-h">■ 个人训练、创建房间、匹配</div>
        <div class="grid g3">
          <button type="button" class="card" style="background: #9b59b6" @click="openTrainModal">
            <h3>个人训练</h3>
            <p>本地单人猜番，可开关关联提示，不计排行榜。</p>
          </button>
          <button type="button" class="card" style="background: #45B7D1" @click="openCreateModal">
            <h3>创建房间</h3>
            <p>开设 1v1 房间，等待对手加入后开始同题竞速。</p>
          </button>
          <button
            type="button"
            class="card"
            style="background: #e6a23c"
            :disabled="matching"
            @click="openMatchModal"
          >
            <h3>{{ matching ? `匹配中(${queueSize})…` : '进入匹配' }}</h3>
            <p>国标+立直 · BO5 · 限时60s · 每人8猜 · 计入排行榜</p>
          </button>
        </div>
        <div v-if="matching" class="match-bar">
          <span>匹配队列中（{{ queueSize }} 人）· {{ auth.username }}</span>
          <button type="button" class="panel-btn ghost" @click="cancelMatch">取消匹配</button>
        </div>
      </section>

      <section class="sec">
        <div class="lobby-split">
          <div class="split-rooms">
            <div class="sec-h">■ 房间列表</div>
            <div class="panel-box">
              <div class="side-tools">
                <div class="join-code">
                  <input v-model="joinCode" maxlength="6" placeholder="房间码" @keyup.enter="joinByInput" />
                  <button type="button" class="panel-btn" :disabled="connecting" @click="joinByInput">加入</button>
                </div>
                <button type="button" class="panel-btn ghost" :disabled="connecting" @click="refreshLobby">刷新</button>
              </div>
              <div v-if="!lobbyRooms.length" class="empty">暂无等待中的房间</div>
              <ul v-else class="room-list">
                <li
                  v-for="r in lobbyRooms"
                  :key="r.code"
                  class="room-item"
                  :class="{ mine: isMyLobbyRoom(r) }"
                >
                  <div class="room-main">
                    <strong class="code">{{ r.code }}</strong>
                    <div class="room-players" aria-label="房间玩家">
                      <span
                        v-for="(nick, index) in (r.playerNicks || [r.hostName])"
                        :key="`${r.code}-${index}`"
                        class="player-chip"
                      >
                        <i>{{ nick?.slice(0, 1) || '?' }}</i>
                        <b>{{ nick }}</b>
                        <small v-if="index === 0">房主</small>
                      </span>
                      <span v-if="r.playerCount < 2" class="player-chip waiting">等待玩家</span>
                    </div>
                    <span class="meta">{{ ruleText(r.rules) }} · BO{{ r.bestOf }}</span>
                    <span class="meta">{{ r.playerCount }}/2</span>
                    <span v-if="r.disableRelated" class="tag">无关联</span>
                    <span v-if="isMyLobbyRoom(r)" class="tag you">我的</span>
                  </div>
                  <div class="room-actions">
                    <template v-if="isMyLobbyRoom(r)">
                      <button
                        v-if="isLobbyHost(r) && r.playerCount >= 2"
                        type="button"
                        class="panel-btn"
                        :disabled="connecting"
                        @click="startMatch"
                      >开始</button>
                      <button type="button" class="panel-btn ghost" :disabled="connecting" @click="leaveRoom">退出</button>
                    </template>
                    <button
                      v-else
                      type="button"
                      class="panel-btn"
                      :disabled="connecting || r.playerCount >= 2"
                      @click="joinRoomByCode(r.code)"
                    >加入</button>
                  </div>
                </li>
              </ul>
            </div>
          </div>

          <div class="split-rank">
            <div class="sec-h">■ 排行榜（仅匹配）</div>
            <div class="panel-box rank-box">
              <p class="rank-hint">只统计匹配对局胜场（自建房不计）</p>
              <ol v-if="lobbyBoard.length" class="rank-list">
                <li v-for="(row, i) in lobbyBoard" :key="row.userId">
                  <span class="rk">{{ i + 1 }}</span>
                  <span class="nm">{{ row.username }}</span>
                  <span class="sc">{{ row.wins }}胜 / {{ row.matches }}场</span>
                </li>
              </ol>
              <div v-else class="empty sm">暂无数据</div>
            </div>
          </div>
        </div>
      </section>
      <p v-if="error" class="err">{{ error }}</p>
    </template>

    <GuessFanPlay
      v-else-if="mode === 'solo'"
      title="个人训练"
      :subtitle="ruleLabels"
      :status-text="soloStatus"
      :rules="rules"
      :input-disabled="soloDone"
      :my-rows="soloRows"
      :me-label="auth.username || '我'"
      :max-guesses="MAX_GUESSES"
      :reveal="soloReveal"
      :error="error"
      @guess="onSoloGuess"
      @leave="resetToLobby"
    >
      <template #actions>
        <button type="button" class="play-btn" @click="restartSolo">再来一局</button>
      </template>
    </GuessFanPlay>

    <GuessFanPlay
      v-else-if="mode === 'multi'"
      :title="multiTitle"
      :subtitle="scoreText"
      :status-text="multiStatus"
      :remain-sec="remainSec"
      :time-limit-sec="room?.timeLimitSec || 0"
      :rules="room?.rules || rules"
      :input-disabled="multiInputDisabled"
      :my-rows="myMultiRows"
      :opp-preview-rows="oppPreviewRows"
      :show-opponent="true"
      :me-label="meNick"
      :opp-label="oppNick"
      :opp-correct="!!oppPlayer?.correct"
      :max-guesses="room?.maxGuesses || MAX_GUESSES"
      :reveal="room?.reveal"
      :result-visible="showRoundResult"
      :result-title="roundResultTitle"
      :result-message="roundResultMessage"
      :result-players="roundResultPlayers"
      :next-round-sec="nextRoundSec"
      :error="error"
      @guess="onMultiGuess"
      @leave="leaveRoom"
    >
      <template #actions>
        <button
          v-if="isHost && room?.status === 'lobby'"
          type="button"
          class="play-btn"
          @click="startMatch"
        >开始</button>
      </template>
    </GuessFanPlay>

    <!-- 个人训练面板 -->
    <el-dialog v-model="showTrain" title="个人训练" width="420px" destroy-on-close>
      <el-form label-width="96px" class="room-form" @submit.prevent>
        <el-form-item label="番种集">
          <el-checkbox-group v-model="rules">
            <el-checkbox label="guobiao">国标</el-checkbox>
            <el-checkbox label="riichi">立直</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item label="关联提示">
          <el-switch v-model="enableRelated" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showTrain = false">取消</el-button>
        <el-button type="primary" :disabled="!rules.length" @click="confirmTrain">开始训练</el-button>
      </template>
    </el-dialog>

    <!-- 创建房间面板（对齐赛事空房 / 2D Modal 表单） -->
    <el-dialog v-model="showCreate" title="创建房间" width="480px" destroy-on-close>
      <el-form label-width="96px" class="room-form" @submit.prevent>
        <el-form-item label="番种集">
          <el-checkbox-group v-model="rules">
            <el-checkbox label="guobiao">国标</el-checkbox>
            <el-checkbox label="riichi">立直</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item label="局制">
          <el-radio-group v-model="bestOf">
            <el-radio-button :label="1">BO1</el-radio-button>
            <el-radio-button :label="3">BO3</el-radio-button>
            <el-radio-button :label="5">BO5</el-radio-button>
            <el-radio-button :label="7">BO7</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="关联提示">
          <el-switch v-model="enableRelated" />
        </el-form-item>
        <el-form-item label="对局规则">
          <span class="form-static">限时 60 秒 · 每人 8 次猜测 · 不计排行榜</span>
        </el-form-item>
        <el-form-item label="房主">
          <span class="form-static">{{ auth.username }}</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showCreate = false">取消</el-button>
        <el-button type="primary" :loading="connecting" :disabled="!rules.length" @click="confirmCreate">创建房间</el-button>
      </template>
    </el-dialog>

    <!-- 匹配面板：固定配置 -->
    <el-dialog v-model="showMatch" title="进入匹配" width="420px" destroy-on-close>
      <div class="match-defaults">
        <p>匹配使用固定配置，计入排行榜：</p>
        <ul>
          <li>番种集：国标 + 立直</li>
          <li>局制：BO5</li>
          <li>限时：每局 60 秒</li>
          <li>猜测次数：每人 8 次</li>
          <li>关联提示：开启</li>
        </ul>
      </div>
      <template #footer>
        <el-button @click="showMatch = false">取消</el-button>
        <el-button type="primary" :loading="connecting" @click="confirmMatch">开始匹配</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { io } from 'socket.io-client'
import GuessFanPlay from './GuessFanPlay.vue'
import {
  GUESS_FAN_BY_ID,
  MAX_GUESSES,
  RULE_LABEL,
  findFanByName,
  rollAnswer,
} from '@/constants/guessFanCatalog'
import { compareGuess, extractTonePreview, revealAnswer } from '@/utils/guessFanCompare'
import { usePlayerAuthStore } from '@/stores/playerAuth'

const auth = usePlayerAuthStore()

const mode = ref('lobby')
const rules = ref(['guobiao', 'riichi'])
const enableRelated = ref(true)
const bestOf = ref(5)
const joinCode = ref('')
const error = ref('')
const connecting = ref(false)
const showTrain = ref(false)
const showCreate = ref(false)
const showMatch = ref(false)
const matching = ref(false)
const queueSize = ref(0)

const lobbyRooms = ref([])
const lobbyBoard = ref([])

const soloRows = ref([])
const soloAnswer = ref(null)
const soloRolled = ref(null)
const soloReveal = ref(null)
const soloDone = ref(false)

const socket = ref(null)
const room = ref(null)
const myId = ref('')
const nowMs = ref(Date.now())
let tickTimer = null

const disableRelated = computed({
  get: () => !enableRelated.value,
  set: (v) => {
    enableRelated.value = !v
  },
})

const remainSec = computed(() => {
  if (!room.value?.roundEndsAt || room.value.status !== 'playing') return null
  return Math.max(0, Math.ceil((room.value.roundEndsAt - nowMs.value) / 1000))
})
const nextRoundSec = computed(() => {
  if (room.value?.status !== 'round_over' || !room.value?.nextRoundAt) return null
  return Math.max(0, Math.ceil((room.value.nextRoundAt - nowMs.value) / 1000))
})

const ruleLabels = computed(() => rules.value.map((r) => RULE_LABEL[r] || r).join('、'))
const soloStatus = computed(() => {
  if (soloReveal.value && soloDone.value) {
    const hit = soloRows.value.some((r) => r.result.correct)
    return hit ? `猜中！答案：${soloReveal.value.name}` : `未猜中。答案：${soloReveal.value.name}`
  }
  return `剩余 ${MAX_GUESSES - soloRows.value.length} 次 · 第 ${soloRows.value.length + 1} 猜`
})
const isHost = computed(() => room.value && room.value.hostId === myId.value)
const scoreText = computed(() => {
  if (!room.value) return ''
  return (room.value.players || [])
    .map((p) => `${p.nick} ${room.value.scores?.[p.id] || 0}`)
    .join(' : ')
})
const multiTitle = computed(() => {
  if (!room.value) return '对局'
  const tag = room.value.ranked ? '匹配' : `房间 ${room.value.code}`
  return `${tag} · BO${room.value.bestOf}`
})
const mePlayer = computed(() => room.value?.players?.find((p) => p.id === myId.value) || null)
const oppPlayer = computed(() => room.value?.players?.find((p) => p.id !== myId.value) || null)
const meNick = computed(() => mePlayer.value?.nick || auth.username || '我')
const oppNick = computed(() => oppPlayer.value?.nick || '对手')
const showRoundResult = computed(() => ['round_over', 'match_over'].includes(room.value?.status))
const roundResultPlayers = computed(() => room.value?.players || [])
const roundResultTitle = computed(() => {
  if (!room.value) return ''
  const winnerId = room.value.status === 'match_over' ? room.value.matchWinnerId : room.value.roundWinnerId
  const winner = room.value.players?.find((p) => p.id === winnerId)
  if (!winner) return '本局无人猜中'
  return winner.id === myId.value ? '你猜中了！' : `${winner.nick} 猜中了`
})
const roundResultMessage = computed(() => {
  if (room.value?.status === 'match_over') {
    const winner = room.value.players?.find((p) => p.id === room.value.matchWinnerId)
    return `${winner?.nick || '胜者'} 赢得本场 BO${room.value.bestOf} 对战`
  }
  return room.value?.roundWinnerId ? `第 ${room.value.round} 局结束` : '时间到或双方次数已用尽'
})
const myMultiRows = computed(() => {
  return (mePlayer.value?.guesses || [])
    .filter((g) => g.result)
    .map((g) => ({ result: g.result }))
})
const oppPreviewRows = computed(() => {
  return (oppPlayer.value?.guesses || []).map((g) => {
    if (g.preview) return { preview: g.preview }
    if (g.result) return { preview: extractTonePreview(g.result) }
    return { preview: { name: 'gray', rules: ['gray'], types: ['gray'], reqLength: 'gray', fan: 'gray', correct: false } }
  })
})
const multiInputDisabled = computed(() => {
  if (!room.value || room.value.status !== 'playing') return true
  if (remainSec.value === 0) return true
  const me = mePlayer.value
  return !me || me.done || me.correct
})
const multiStatus = computed(() => {
  if (!room.value) return ''
  const maxG = room.value.maxGuesses || MAX_GUESSES
  if (room.value.status === 'match_over') {
    const w = room.value.players.find((p) => p.id === room.value.matchWinnerId)
    return `胜者：${w?.nick || '?'}`
  }
  if (room.value.status === 'lobby') return '等待开始'
  if (room.value.reveal) {
    const w = room.value.players.find((p) => p.id === room.value.roundWinnerId)
    return w
      ? `${w.nick} 猜中 · 答案 ${room.value.reveal.name}`
      : `平局 · 答案 ${room.value.reveal.name}`
  }
  return `第 ${room.value.round} 局 · 剩余 ${maxG - myMultiRows.value.length} 次`
})

function ruleText(rs) {
  return (rs || []).map((r) => RULE_LABEL[r] || r).join('+') || '—'
}

function isMyLobbyRoom(r) {
  const uid = String(auth.userId ?? '')
  if (!uid) return false
  if ((r.memberUserIds || []).map(String).includes(uid)) return true
  return !!(room.value && room.value.code === r.code && mode.value === 'lobby')
}

function isLobbyHost(r) {
  return String(r.hostUserId ?? '') === String(auth.userId ?? '')
}

function applyLobby(lobby) {
  if (!lobby) return
  lobbyRooms.value = lobby.rooms || []
  lobbyBoard.value = lobby.leaderboard || []
  queueSize.value = lobby.queueSize || 0
}

function ensureSocket() {
  if (socket.value?.connected) return socket.value
  const url = import.meta.env.DEV ? '/' : undefined
  if (socket.value) {
    socket.value.removeAllListeners()
    socket.value.disconnect()
  }
  socket.value = io(url, { transports: ['websocket', 'polling'] })
  socket.value.on('connect', () => {
    myId.value = socket.value.id
    authEmit()
  })
  socket.value.on('guessfan:state', (state) => {
    room.value = state
    // 自建房在 lobby 等待时不跳转；开局/匹配对局进入对战页
    if (
      state?.status === 'playing' ||
      state?.status === 'round_over' ||
      state?.status === 'match_over' ||
      state?.ranked
    ) {
      mode.value = 'multi'
    }
  })
  socket.value.on('guessfan:lobby', (lobby) => {
    applyLobby(lobby)
  })
  socket.value.on('guessfan:matched', ({ state }) => {
    matching.value = false
    room.value = state
    mode.value = 'multi'
    myId.value = socket.value.id
  })
  socket.value.on('connect_error', (err) => {
    error.value = `连接失败：${err.message}`
    connecting.value = false
  })
  return socket.value
}

function emitAck(event, payload) {
  return new Promise((resolve) => {
    const s = ensureSocket()
    connecting.value = true
    const finish = (res) => {
      connecting.value = false
      resolve(res || { ok: false, error: '无响应' })
    }
    const send = () => {
      s.timeout(8000).emit(event, payload, (err, res) => {
        if (err && typeof err === 'object' && 'ok' in err && res == null) {
          finish(err)
          return
        }
        if (err) {
          finish({ ok: false, error: '请求超时或连接失败' })
          return
        }
        finish(res)
      })
    }
    if (s.connected) send()
    else s.once('connect', send)
  })
}

async function authEmit() {
  if (!auth.isLoggedIn || !socket.value) return null
  const res = await emitAck('guessfan:auth', {
    userId: auth.userId,
    username: auth.username,
  })
  if (res.ok) applyLobby(res.lobby)
  else error.value = res.error || '鉴权失败'
  return res
}

async function refreshLobby() {
  error.value = ''
  const res = await emitAck('guessfan:lobby', {})
  if (res.ok) applyLobby(res.lobby)
  else error.value = res.error || '刷新失败'
}

function openTrainModal() {
  showTrain.value = true
}
function openCreateModal() {
  showCreate.value = true
}
function openMatchModal() {
  if (matching.value) return
  showMatch.value = true
}

function confirmTrain() {
  if (!rules.value.length) return
  showTrain.value = false
  error.value = ''
  const { fan, rolledFan } = rollAnswer(rules.value)
  soloAnswer.value = fan
  soloRolled.value = rolledFan
  soloRows.value = []
  soloReveal.value = null
  soloDone.value = false
  mode.value = 'solo'
}

function restartSolo() {
  confirmTrain()
}

function onSoloGuess(payload) {
  if (soloDone.value || !soloAnswer.value) return
  const guess = payload?.id ? GUESS_FAN_BY_ID[payload.id] : findFanByName(payload?.name, rules.value)
  if (!guess) {
    error.value = '未找到该番种'
    return
  }
  if (!guess.rules.some((r) => rules.value.includes(r))) {
    error.value = '该番种不在当前题库'
    return
  }
  error.value = ''
  const result = compareGuess({
    answer: soloAnswer.value,
    rolledFan: soloRolled.value,
    guess,
    disableRelated: disableRelated.value,
  })
  soloRows.value.push({ result })
  if (result.correct || soloRows.value.length >= MAX_GUESSES) {
    soloDone.value = true
    soloReveal.value = revealAnswer(soloAnswer.value, soloRolled.value)
  }
}

async function confirmCreate() {
  if (!rules.value.length) return
  error.value = ''
  const res = await emitAck('guessfan:create', {
    rules: [...rules.value],
    bestOf: bestOf.value,
    disableRelated: disableRelated.value,
  })
  if (!res.ok) {
    error.value = res.error || '创建失败'
    return
  }
  showCreate.value = false
  myId.value = socket.value.id
  room.value = res.state
  mode.value = 'lobby'
  await refreshLobby()
}

async function joinRoomByCode(code) {
  error.value = ''
  const res = await emitAck('guessfan:join', { code })
  if (!res.ok) {
    error.value = res.error || '加入失败'
    return
  }
  myId.value = socket.value.id
  room.value = res.state
  mode.value = 'lobby'
  await refreshLobby()
}

async function joinByInput() {
  const code = joinCode.value.trim().toUpperCase()
  if (!code) {
    error.value = '请输入房间码'
    return
  }
  await joinRoomByCode(code)
}

async function confirmMatch() {
  error.value = ''
  showMatch.value = false
  matching.value = true
  const res = await emitAck('guessfan:queue', {})
  if (!res.ok) {
    matching.value = false
    error.value = res.error || '匹配失败'
    return
  }
  queueSize.value = res.queueSize || 0
  if (res.state) {
    matching.value = false
    room.value = res.state
    mode.value = 'multi'
    myId.value = socket.value.id
  }
}

async function cancelMatch() {
  matching.value = false
  await emitAck('guessfan:queue_cancel', {})
}

async function startMatch() {
  error.value = ''
  const res = await emitAck('guessfan:start', {})
  if (!res.ok) {
    error.value = res.error || '无法开始'
    return
  }
  room.value = res.state
  if (res.state?.status === 'playing') mode.value = 'multi'
}

async function onMultiGuess(payload) {
  error.value = ''
  const res = await emitAck('guessfan:guess', payload)
  if (!res.ok) {
    error.value = res.error || '猜测失败'
    return
  }
  room.value = res.state
}

async function leaveRoom() {
  matching.value = false
  if (socket.value?.connected) {
    try {
      await emitAck('guessfan:leave', {})
    } catch {
      /* ignore */
    }
  }
  room.value = null
  mode.value = 'lobby'
  await refreshLobby()
}

function resetToLobby() {
  mode.value = 'lobby'
  soloRows.value = []
  soloReveal.value = null
  error.value = ''
  refreshLobby()
}

watch(
  () => auth.isLoggedIn,
  async (ok) => {
    if (ok) {
      ensureSocket()
      await authEmit()
    }
  },
)

watch(
  () => [room.value?.roundEndsAt, room.value?.nextRoundAt],
  ([ends, next]) => {
    if (tickTimer) {
      clearInterval(tickTimer)
      tickTimer = null
    }
    if (ends || next) {
      nowMs.value = Date.now()
      tickTimer = setInterval(() => {
        nowMs.value = Date.now()
      }, 250)
    }
  },
)

watch(
  mode,
  (m) => {
    const lock = m === 'solo' || m === 'multi'
    document.documentElement.style.overflow = lock ? 'hidden' : ''
    document.body.style.overflow = lock ? 'hidden' : ''
  },
  { immediate: true },
)

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  if (!auth.isLoggedIn) return
  ensureSocket()
  if (socket.value.connected) await authEmit()
})

onBeforeUnmount(() => {
  document.documentElement.style.overflow = ''
  document.body.style.overflow = ''
  if (tickTimer) clearInterval(tickTimer)
  if (socket.value) {
    socket.value.removeAllListeners()
    socket.value.disconnect()
    socket.value = null
  }
})
</script>

<style scoped>
.gf {
  color: #333;
}

.welcome {
  margin-bottom: 18px;
  padding: 22px 20px;
  background: #1a1a1a;
  color: #fff;
  text-align: center;
}

.welcome h1 {
  margin: 0;
  font-size: 1.55rem;
  font-weight: 700;
}

.tip-box {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 28px 22px;
  text-align: center;
  color: #666;
}

.sec {
  margin-bottom: 18px;
}

.sec-h {
  background: rgba(0, 0, 0, 0.75);
  color: #fff;
  padding: 6px 12px;
  font-size: 13px;
  margin-bottom: 12px;
}

.sec-h.inner {
  margin: 0 0 12px;
}

.grid {
  display: grid;
  gap: 16px;
}

.g3 {
  grid-template-columns: repeat(3, 1fr);
}

.card {
  display: block;
  width: 100%;
  padding: 14px 16px;
  min-height: 98px;
  color: #fff;
  text-align: left;
  text-decoration: none;
  border: 0;
  cursor: pointer;
  font-family: inherit;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.12);
}

.card:hover:not(:disabled) {
  filter: brightness(1.05);
}

.card:disabled {
  opacity: 0.85;
  cursor: wait;
}

.card h3 {
  font-size: 1.05rem;
  margin: 0 0 6px;
  font-weight: 700;
}

.card p {
  margin: 0;
  font-size: 12px;
  opacity: 0.95;
  line-height: 1.45;
}

.match-bar {
  margin-top: 12px;
  padding: 10px 12px;
  background: #fff;
  border: 1px solid #e0e0e0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
  color: #555;
}

.panel-box {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 12px 14px;
}

.panel-box.rank-box {
  max-width: none;
}

.lobby-split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 240px;
  gap: 16px;
  align-items: start;
}

.split-rooms,
.split-rank {
  min-width: 0;
}

.side-tools {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;
}

.match-defaults {
  font-size: 14px;
  color: #555;
  line-height: 1.6;
}

.match-defaults p {
  margin: 0 0 8px;
}

.match-defaults ul {
  margin: 0;
  padding-left: 1.2em;
}

.join-code {
  display: flex;
  gap: 8px;
  flex: 1;
  min-width: 180px;
}

.join-code input {
  flex: 1;
  padding: 8px 10px;
  border: 1px solid #ddd;
  font-size: 14px;
  text-transform: uppercase;
  letter-spacing: 0.1em;
}

.room-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.room-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 10px 0;
  border-bottom: 1px solid #f0f0f0;
}

.room-item.mine {
  background: #f5faff;
  margin: 0 -8px;
  padding-left: 8px;
  padding-right: 8px;
}

.room-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.tag.you {
  background: #ecf5ff;
  color: #409eff;
}

.room-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  min-width: 0;
  font-size: 13px;
}

.room-main .code {
  font-family: ui-monospace, monospace;
  letter-spacing: 0.06em;
}

.room-players { display: flex; flex-wrap: wrap; gap: 6px; }
.player-chip { display: inline-flex; align-items: center; gap: 6px; max-width: 180px; padding: 4px 8px 4px 4px; border: 1px solid #d9ecff; border-radius: 18px; background: #f3f9ff; color: #303133; }
.player-chip i { display: grid; flex: 0 0 24px; place-items: center; width: 24px; height: 24px; border-radius: 50%; background: #409eff; color: #fff; font-style: normal; font-weight: 700; }
.player-chip b { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.player-chip small { flex-shrink: 0; padding: 1px 4px; border-radius: 3px; background: #409eff; color: #fff; font-size: 10px; }
.player-chip.waiting { border-style: dashed; background: #fafafa; color: #909399; }

.room-main .meta {
  color: #888;
}

.tag {
  font-size: 11px;
  padding: 1px 6px;
  background: #f0f0f0;
  color: #666;
}

.rank-hint {
  margin: 0 0 10px;
  font-size: 12px;
  color: #999;
}

.rank-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.rank-list li {
  display: grid;
  grid-template-columns: 22px 1fr auto;
  gap: 6px;
  padding: 7px 0;
  border-bottom: 1px solid #eee;
  font-size: 13px;
}

.rank-list .rk {
  color: #999;
}

.rank-list .nm {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rank-list .sc {
  color: #409eff;
  font-weight: 600;
}

.empty {
  color: #999;
  font-size: 13px;
  padding: 24px 8px;
  text-align: center;
}

.empty.sm {
  padding: 12px 0;
}

.panel-btn {
  display: inline-block;
  padding: 8px 14px;
  background: #409eff;
  color: #fff;
  border: 0;
  cursor: pointer;
  font-size: 13px;
  font-family: inherit;
  text-decoration: none;
  flex-shrink: 0;
}

.panel-btn:hover {
  background: #66b1ff;
}

.panel-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.panel-btn.ghost {
  background: transparent;
  border: 1px solid #ddd;
  color: #666;
}

.panel-btn.ghost:hover {
  border-color: #409eff;
  color: #409eff;
}

.err {
  color: #f56c6c;
  font-size: 13px;
  margin: 10px 0 0;
}

.play-btn {
  padding: 7px 12px;
  border: 0;
  background: #409eff;
  color: #fff;
  font-size: 13px;
  cursor: pointer;
  font-family: inherit;
}

.play-btn:hover {
  background: #66b1ff;
}

.room-form :deep(.el-form-item) {
  margin-bottom: 14px;
}

.form-static {
  color: #333;
  font-size: 14px;
}

@media (max-width: 900px) {
  .g3 {
    grid-template-columns: 1fr;
  }
  .lobby-split {
    grid-template-columns: 1fr;
  }
}
</style>
