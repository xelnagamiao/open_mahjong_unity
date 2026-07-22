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
            <p>开启单人猜番</p>
          </button>
          <button type="button" class="card" style="background: #45B7D1" @click="openCreateModal">
            <h3>创建房间</h3>
            <p>开设联机房间，不计统计数据</p>
          </button>
          <button
            type="button"
            class="card"
            style="background: #e6a23c"
            :disabled="matching"
            @click="enterMatch"
          >
            <h3>{{ matchCardTitle }}</h3>
            <p>规则集：国标+立直、BO5、限时60s、8次猜番机会、计入排行</p>
          </button>
        </div>
        <div class="match-bar" :class="{ active: matching, busy: queueSize > 0 }">
          <div class="match-bar-main">
            <strong>{{ matchQueueTitle }}</strong>
            <div v-if="queuePlayers.length" class="queue-players">
              <span
                v-for="p in queuePlayers"
                :key="`${p.userId}-${p.username}`"
                class="queue-chip"
                :class="{ me: isMeInQueue(p) }"
              >{{ p.username }}<small v-if="isMeInQueue(p)">我</small></span>
            </div>
            <span v-else class="queue-wait">当前无人排队</span>
          </div>
          <button v-if="matching" type="button" class="panel-btn ghost" @click="cancelMatch">取消匹配</button>
          <button v-else type="button" class="panel-btn" :disabled="connecting" @click="enterMatch">加入匹配</button>
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
            <div class="sec-h rank-sec-h">
              <span>■ 排行榜（仅匹配）</span>
              <el-tooltip placement="left" :show-after="200" popper-class="elo-tooltip">
                <template #content>
                  <div class="elo-tip-content">
                    <b>Elo 积分规则</b><br />
                    初始 1000 分，仅系统匹配计分，自建房不计。<br />
                    K 值为 32：胜者加分与败者扣分数值相同。<br />
                    战胜高分玩家加分更多，战胜低分玩家加分更少；反之亦然。<br />
                    计算式：变化值 = 32 ×（实际结果 − 预期胜率），结果四舍五入。
                  </div>
                </template>
                <button type="button" class="elo-help" aria-label="查看 Elo 积分规则">?</button>
              </el-tooltip>
            </div>
            <div class="panel-box rank-box">
              <ol v-if="lobbyBoard.length" class="rank-list">
                <li v-for="(row, i) in lobbyBoard" :key="row.userId">
                  <span class="rk" :class="`top-${i + 1}`">{{ i + 1 }}</span>
                  <span class="rank-player">
                    <b class="nm">{{ row.username }}</b>
                    <small>{{ row.wins }}胜 {{ row.losses }}负 · 胜率 {{ row.winRate }}%</small>
                  </span>
                  <span class="rank-score">
                    <b>{{ row.rating }}</b>
                    <small v-if="row.streak > 1">{{ row.streak }} 连胜</small>
                  </span>
                </li>
              </ol>
              <div v-else class="empty sm">暂无数据</div>
            </div>
          </div>
        </div>

        <section class="game-guide">
          <div class="guide-heading">
            <h2>猜番说明</h2>
          </div>
          <div class="guide-grid">
            <article>
              <h3>什么是关联提示</h3>
              <p>黄色不是「接近答案」的单一含义，而是这一列与答案存在以下任一种关联：</p>
              <ul class="related-cases">
                <li><b>同义、同名关联：</b>两个规则中的同名番，或共享同一种番型的同义番。例如答案是立直「一气通贯」，猜国标「清龙」，名字显示黄色；只有猜中题库中的那个具体番种才会绿色。<br />示例 1：答案一气通贯 [一气通贯、清龙]，猜出清龙 [清龙、一气通贯] 时名字一侧会显示关联的黄色提示。<br />示例 2：答案平和 [国标]，猜测时选择平和 [日麻]，同名的平和一侧会显示关联的黄色提示。</li>
                <li><b>类型关联：</b>在答案是多类型番时，猜测到该类型番的任意一个副类型时，则显示黄色；只有猜中具体番种的主类型时，才显示绿色。<br />示例 1：答案组合龙 [顺子系、全不靠系]，猜出三色三步高 [顺子系] 时番种类型一侧会显示关联的黄色提示。<br />示例 2：答案里宝牌 [条件系、偶然系]，猜出立直 [条件系] 时番种类型一侧会显示关联的黄色提示。</li>
                <li><b>浮动番数关联：</b>同一立直役可能因门清、副露拥有两个番数。例如一气通贯为门清 2 番、副露 1 番；猜测命中该役的另一种合法番数时显示黄色，命中本题实际抽到的番数才绿色。<br />示例：答案一气通贯 [2, 1]，猜出红宝牌 [1] 时番种番数一侧会显示关联的黄色提示。</li>
              </ul>
              <p class="related-off"><b>关闭关联提示：</b>所有上述黄色提示都会变成灰色，只保留完全匹配的绿色和数值方向箭头，提升了对局难度。</p>
            </article>
            <article>
              <h3>同时拥有多个值的番种是如何标定的</h3>
              <ol class="marking-rules">
                <li><b>出现概率原则</b>：组合龙复合全不靠的概率为 29.3%（161/550），因此组合龙应当被优先计为顺子系、其次再被计为全不靠系，即 [顺子系、全不靠系]。</li>
                <li><b>先置逻辑优先原则</b>：在能够预测到导致和牌的行动的具体番数的情况下，优先记条件系，其次记偶然系。例如一发、里宝牌、抢杠、岭上开花、海底捞月应当记 [条件系、偶然系]；天和、地和应当记 [偶然系]；立直、双立直、和绝张应当记 [条件系]。</li>
                <li><b>复计只计其一原则</b>：在可以复计的番种当中，例如四归一、花牌、红宝牌，应当计单个番种的番值，例如四归一应当记 [2]。</li>
                <li><b>食下役一律副值原则</b>：涉及食下的番种，应当以番种门清状态下的原生番数为准，例如纯全带幺九应当记 [3、2]。</li>
                <li><b>声明特殊番种原则</b>：四归一的组数规定为 [4、3、2]；组合龙、九莲宝灯的组数规定为 [1]；全不靠、七星不靠与一色系番种的组数规定为 [全体]；对子系的组数规定为 [7]。</li>
              </ol>
            </article>
            <article>
              <h3>番种如何归类</h3>
              <ul>
                <li><b>顺子系</b>：由特定顺子组合构成的番种，例如清龙、三色同顺、三色三步高等。</li>
                <li><b>刻子系</b>：由刻子、雀头或杠构成的番种，例如小三元、三暗刻、三风刻等。</li>
                <li><b>对子系</b>：由对子构成的番种，例如七对子、连七对、大七星等。</li>
                <li><b>全体系</b>：满足整手牌固定特征的番种，例如清一色、断幺九、全带五、全带幺；组数显示「全体」。</li>
                <li><b>全不靠系</b>：包括全不靠、七星不靠、组合龙（副归类）等。</li>
                <li><b>特殊系</b>：由特殊规定的牌型和牌构成的番种，例如十三幺、九莲宝灯。</li>
                <li><b>条件系</b>：满足特定条件构成的番种，例如立直、一发、门前清、不求人、岭上开花（副归类）、宝牌等。</li>
                <li><b>偶然系</b>：满足偶然条件构成的番种，例如岭上开花、里宝牌、宝牌（副归类）等。</li>
              </ul>
            </article>
            <article>
              <h3>对战与排行</h3>
              <p>每局最多猜 8 次、限时 60 秒。有人猜中或时间结束后展示答案与双方猜测，6 秒后自动进入下一局。</p>
              <p>只有系统匹配计入排行榜。战胜高分对手获得更多积分，负于低分对手扣分更多。</p>
            </article>
          </div>
        </section>
      </section>
      <p v-if="error" class="err">{{ error }}</p>
    </template>

    <GuessFanPlay
      v-else-if="mode === 'solo'"
      title="个人训练"
      :subtitle="ruleLabels"
      :status-text="soloStatus"
      :remain-sec="soloRemainSec"
      :time-limit-sec="60"
      :rules="rules"
      :input-disabled="soloDone"
      :my-rows="soloRows"
      :me-label="auth.username || '我'"
      :max-guesses="MAX_GUESSES"
      :reveal="soloReveal"
      :error="error"
      :start-countdown-sec="soloStartSec"
      :result-visible="soloDone && !!soloReveal"
      :result-title="soloResultTitle"
      :result-message="soloResultMessage"
      :result-players="soloResultPlayers"
      :result-restart-visible="true"
      result-finished-text="个人训练已结束"
      result-leave-text="退出"
      @guess="onSoloGuess"
      @leave="leaveSolo"
      @restart="restartSolo"
    />

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
      :start-countdown-sec="roundStartSec"
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

  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
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
const route = useRoute()
const router = useRouter()

const mode = ref('lobby')
const rules = ref(['guobiao', 'riichi'])
const enableRelated = ref(true)
const bestOf = ref(5)
const joinCode = ref('')
const error = ref('')
const connecting = ref(false)
const showTrain = ref(false)
const showCreate = ref(false)
const matching = ref(false)
const queueSize = ref(0)
const queuePlayers = ref([])

const lobbyRooms = ref([])
const lobbyBoard = ref([])

const soloRows = ref([])
const soloAnswer = ref(null)
const soloRolled = ref(null)
const soloReveal = ref(null)
const soloDone = ref(false)
const soloEndReason = ref('')

const socket = ref(null)
const room = ref(null)
const myId = ref('')
const nowMs = ref(Date.now())
let tickTimer = null
let soloTimer = null
let lastCountdownTone = null
let lastNextTone = null
let lastSoloStartTone = null
let previousRoomStatus = null
let startSoundTimer = null
let scheduledStartSoundKey = ''
let lastStartSoundAt = 0
const START_SOUND_LEAD_MS = 150
const soloStartAt = ref(null)
const soloRoundEndsAt = ref(null)
const countdownAudio = new Audio(`${import.meta.env.BASE_URL}game2d-assets/sounds/guessfan-countdown.wav`)
const startAudio = new Audio(`${import.meta.env.BASE_URL}game2d-assets/sounds/guessfan-start.mp3`)
countdownAudio.preload = 'auto'
startAudio.preload = 'auto'

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
const roundStartSec = computed(() => {
  if (room.value?.status !== 'starting' || !room.value?.roundStartsAt) return null
  return Math.max(0, Math.ceil((room.value.roundStartsAt - nowMs.value) / 1000))
})
const soloStartSec = computed(() => {
  if (!soloStartAt.value) return null
  return Math.max(0, Math.ceil((soloStartAt.value - nowMs.value) / 1000))
})
const soloRemainSec = computed(() => {
  if (!soloRoundEndsAt.value || soloDone.value) return null
  return Math.max(0, Math.ceil((soloRoundEndsAt.value - nowMs.value) / 1000))
})

const ruleLabels = computed(() => rules.value.map((r) => RULE_LABEL[r] || r).join('、'))
const soloStatus = computed(() => {
  if (soloStartAt.value) return '准备开战'
  if (soloReveal.value && soloDone.value) {
    const hit = soloRows.value.some((r) => r.result.correct)
    return hit ? `猜中！答案：${soloReveal.value.name}` : `未猜中。答案：${soloReveal.value.name}`
  }
  return `剩余 ${MAX_GUESSES - soloRows.value.length} 次 · 第 ${soloRows.value.length + 1} 猜`
})
const soloResultTitle = computed(() =>
  soloRows.value.some((row) => row.result?.correct) ? '你猜中了！' : '本局未猜中',
)
const soloResultMessage = computed(() => {
  if (soloEndReason.value === 'correct') return '成功猜中答案，可以再来一局继续挑战'
  if (soloEndReason.value === 'timeout') return '60 秒时间到，可以再来一局继续挑战'
  return '8 次猜测机会已用尽，可以再来一局继续挑战'
})
const soloResultPlayers = computed(() => [
  {
    id: 'solo',
    nick: auth.username || '我',
    correct: soloRows.value.some((row) => row.result?.correct),
    guesses: soloRows.value.map((row) => ({ name: row.name, result: row.result })),
  },
])
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
  if (room.value.endedByForfeit) {
    return winnerId === myId.value ? '对手已退出，你获胜！' : '你已退出本场对抗'
  }
  if (!winner) return '本局无人猜中'
  return winner.id === myId.value ? '你猜中了！' : `${winner.nick} 猜中了`
})
const roundResultMessage = computed(() => {
  if (room.value?.endedByForfeit) {
    return `${room.value.forfeitedNick || '对手'}退出了对局，本场直接结束`
  }
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
  if (room.value.status === 'starting') return '准备开战'
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

const matchCardTitle = computed(() => {
  if (matching.value) return `匹配中（${queueSize.value}）…`
  if (queueSize.value > 0) return `进入匹配（队列 ${queueSize.value}）`
  return '进入匹配'
})

const matchQueueTitle = computed(() => {
  if (matching.value) return `你正在匹配（队列 ${queueSize.value} 人）`
  if (queueSize.value > 0) return `匹配队列（${queueSize.value} 人）`
  return '匹配队列（0 人）'
})

function isMeInQueue(p) {
  return String(p?.userId ?? '') === String(auth.userId ?? '')
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

function enterPlayRoute() {
  if (route.params.playMode === 'play') return
  router.push({ name: 'GuessFan', params: { playMode: 'play' } })
}

function applyLobby(lobby) {
  if (!lobby) return
  lobbyRooms.value = lobby.rooms || []
  lobbyBoard.value = lobby.leaderboard || []
  queueSize.value = lobby.queueSize || 0
  queuePlayers.value = lobby.queuePlayers || []
  // 仅以服务端队列为准：自己不在队列里就退出 matching
  if (matching.value && !queuePlayers.value.some(isMeInQueue) && !room.value) {
    matching.value = false
  }
}

function ensureSocket() {
  if (socket.value?.connected) return socket.value
  // 开发环境直连后端，避免 Vite 代理 WebSocket 丢包/连不上导致无法入队
  const url = import.meta.env.DEV ? 'http://localhost:3000' : undefined
  if (socket.value) {
    socket.value.removeAllListeners()
    socket.value.disconnect()
  }
  socket.value = io(url, {
    path: '/api/socket.io',
    transports: ['websocket', 'polling'],
    withCredentials: true,
  })
  socket.value.on('connect', () => {
    myId.value = socket.value.id
    authEmit()
  })
  socket.value.on('guessfan:state', (state) => {
    room.value = state
    if (
      state?.status === 'starting' ||
      state?.status === 'playing' ||
      state?.status === 'round_over' ||
      state?.status === 'match_over' ||
      state?.ranked
    ) {
      mode.value = 'multi'
      enterPlayRoute()
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
    enterPlayRoute()
  })
  socket.value.on('connect_error', (err) => {
    error.value = `连接失败：${err.message}`
    connecting.value = false
    matching.value = false
  })
  socket.value.on('disconnect', () => {
    // 断线后服务端会移出队列；本地同步状态，避免假匹配
    if (matching.value) {
      matching.value = false
      queueSize.value = 0
      queuePlayers.value = []
    }
  })
  return socket.value
}

function emitAck(event, payload = {}) {
  return new Promise((resolve) => {
    const s = ensureSocket()
    connecting.value = true
    const finish = (res) => {
      connecting.value = false
      resolve(res && typeof res === 'object' ? res : { ok: false, error: '无响应' })
    }
    const send = () => {
      let settled = false
      const timer = setTimeout(() => {
        if (settled) return
        settled = true
        finish({ ok: false, error: '请求超时' })
      }, 8000)
      // 不用 socket.timeout()，避免 ack 参数被误判成错误
      s.emit(event, payload, (res) => {
        if (settled) return
        settled = true
        clearTimeout(timer)
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
function enterMatch() {
  if (matching.value || connecting.value) return
  confirmMatch()
}

function confirmTrain() {
  if (!rules.value.length) return
  showTrain.value = false
  router.push({
    name: 'GuessFan',
    params: { playMode: 'solo' },
    query: {
      rules: rules.value.join(','),
      related: enableRelated.value ? '1' : '0',
    },
  })
}

function startSoloRound() {
  if (soloTimer) clearTimeout(soloTimer)
  soloTimer = null
  error.value = ''
  const { fan, rolledFan } = rollAnswer(rules.value)
  soloAnswer.value = fan
  soloRolled.value = rolledFan
  soloRows.value = []
  soloReveal.value = null
  soloDone.value = false
  soloEndReason.value = ''
  soloStartAt.value = null
  soloRoundEndsAt.value = Date.now() + 60000
  nowMs.value = Date.now()
  mode.value = 'solo'
  if (Date.now() - lastStartSoundAt > 500) playStartSound()
  cancelScheduledStartSound()
  soloTimer = setTimeout(() => finishSoloRound('timeout'), 60000)
}

function prepareSoloRound() {
  if (soloTimer) clearTimeout(soloTimer)
  mode.value = 'solo'
  soloRows.value = []
  soloReveal.value = null
  soloDone.value = true
  soloEndReason.value = ''
  soloRoundEndsAt.value = null
  soloStartAt.value = Date.now() + 3000
  nowMs.value = Date.now()
  soloTimer = setTimeout(startSoloRound, 3000)
}

function startSoloFromRoute() {
  const requestedRules = String(route.query.rules || '')
    .split(',')
    .filter((rule) => ['guobiao', 'riichi'].includes(rule))
  if (requestedRules.length) rules.value = requestedRules
  enableRelated.value = String(route.query.related ?? '1') !== '0'
  prepareSoloRound()
}

function restartSolo() {
  prepareSoloRound()
}

function finishSoloRound(reason) {
  if (soloDone.value || !soloAnswer.value) return
  if (soloTimer) clearTimeout(soloTimer)
  soloTimer = null
  soloDone.value = true
  soloEndReason.value = reason
  soloRoundEndsAt.value = null
  soloReveal.value = revealAnswer(soloAnswer.value, soloRolled.value)
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
  soloRows.value.push({ name: guess.names[0], result })
  if (result.correct || soloRows.value.length >= MAX_GUESSES) {
    finishSoloRound(result.correct ? 'correct' : 'exhausted')
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
  // 先鉴权再入队；matching 仅在服务端确认 queued 后置 true
  const authRes = await authEmit()
  if (!authRes?.ok) {
    matching.value = false
    error.value = authRes?.error || '鉴权失败，无法匹配'
    return
  }
  const res = await emitAck('guessfan:queue', {})
  if (!res?.ok) {
    matching.value = false
    error.value = res?.error || '匹配失败'
    await refreshLobby()
    return
  }
  queueSize.value = res.queueSize || 0
  queuePlayers.value = res.queuePlayers || []
  matching.value = !!res.queued
  if (res.state) {
    matching.value = false
    room.value = res.state
    mode.value = 'multi'
    myId.value = socket.value.id
    enterPlayRoute()
  }
}

async function cancelMatch() {
  matching.value = false
  const res = await emitAck('guessfan:queue_cancel', {})
  if (res?.ok) {
    queueSize.value = res.queueSize || 0
    queuePlayers.value = res.queuePlayers || []
  } else {
    await refreshLobby()
  }
}

async function startMatch() {
  error.value = ''
  const res = await emitAck('guessfan:start', {})
  if (!res.ok) {
    error.value = res.error || '无法开始'
    return
  }
  room.value = res.state
  if (['starting', 'playing'].includes(res.state?.status)) {
    mode.value = 'multi'
    enterPlayRoute()
  }
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

async function leaveRoom(skipConfirm = false, navigate = true) {
  if (!skipConfirm && ['starting', 'playing', 'round_over'].includes(room.value?.status)) {
    if (!window.confirm('是否退出猜番对抗？退出后对手将直接获胜。')) return
  }
  cancelScheduledStartSound()
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
  if (navigate && route.params.playMode === 'play') {
    await router.replace('/guess-fan')
  }
}

function resetToLobby(navigate = true) {
  cancelScheduledStartSound()
  if (soloTimer) {
    clearTimeout(soloTimer)
    soloTimer = null
  }
  mode.value = 'lobby'
  soloRows.value = []
  soloReveal.value = null
  soloEndReason.value = ''
  soloStartAt.value = null
  soloRoundEndsAt.value = null
  error.value = ''
  refreshLobby()
  if (navigate && route.params.playMode === 'solo') {
    router.replace('/guess-fan')
  }
}

function leaveSolo() {
  if (hasActiveSession() && !window.confirm('是否退出猜番训练？')) return
  resetToLobby()
}

function playAudio(audio, volume = 1) {
  audio.currentTime = 0
  audio.volume = volume
  audio.play().catch(() => {})
}

function playCountdownSound() {
  playAudio(countdownAudio)
}

function playStartSound() {
  lastStartSoundAt = Date.now()
  playAudio(startAudio, 0.1)
}

function scheduleStartSound(deadline, key) {
  if (!deadline || scheduledStartSoundKey === key) return
  if (startSoundTimer) clearTimeout(startSoundTimer)
  scheduledStartSoundKey = key
  const delay = Math.max(0, Number(deadline) - Date.now() - START_SOUND_LEAD_MS)
  startSoundTimer = setTimeout(() => {
    startSoundTimer = null
    if (Date.now() - lastStartSoundAt > 500) playStartSound()
  }, delay)
}

function cancelScheduledStartSound() {
  if (startSoundTimer) clearTimeout(startSoundTimer)
  startSoundTimer = null
  scheduledStartSoundKey = ''
}

function hasActiveSession() {
  if (mode.value === 'solo') return !soloDone.value || !!soloStartAt.value
  if (mode.value === 'multi') return ['starting', 'playing', 'round_over'].includes(room.value?.status)
  return false
}

function beforeUnload(event) {
  if (!hasActiveSession()) return
  event.preventDefault()
  event.returnValue = ''
}

async function confirmRouteExit(to) {
  const targetMode = to.params?.playMode
  if (targetMode === 'solo' && mode.value === 'solo') return true
  if (targetMode === 'play' && mode.value === 'multi') return true
  if (!hasActiveSession()) return true
  const text = mode.value === 'solo'
    ? '是否退出猜番训练？'
    : '是否退出猜番对抗？退出后对手将直接获胜。'
  if (!window.confirm(text)) return false
  if (mode.value === 'multi') await leaveRoom(true, false)
  else resetToLobby(false)
  return true
}

onBeforeRouteLeave(confirmRouteExit)
onBeforeRouteUpdate(confirmRouteExit)

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
  () => [room.value?.roundEndsAt, room.value?.roundStartsAt, room.value?.nextRoundAt, soloStartAt.value, soloRoundEndsAt.value],
  ([ends, starts, next, soloStarts, soloEnds]) => {
    if (tickTimer) {
      clearInterval(tickTimer)
      tickTimer = null
    }
    if (ends || starts || next || soloStarts || soloEnds) {
      nowMs.value = Date.now()
      tickTimer = setInterval(() => {
        nowMs.value = Date.now()
      }, 250)
    }
  },
)

watch(
  () => route.params.playMode,
  async (playMode, previousPlayMode) => {
    if (playMode === 'solo' && auth.isLoggedIn && mode.value !== 'solo') {
      startSoloFromRoute()
      return
    }
    if (!playMode && previousPlayMode === 'solo' && mode.value === 'solo') {
      resetToLobby(false)
      return
    }
    if (!playMode && previousPlayMode === 'play' && mode.value === 'multi') {
      await leaveRoom(true, false)
      return
    }
    if (playMode === 'play' && !room.value && mode.value !== 'multi') {
      router.replace('/guess-fan')
    }
  },
)

watch(roundStartSec, (sec) => {
  if (sec != null && sec > 0 && sec !== lastCountdownTone) playCountdownSound()
  if (sec === 1) scheduleStartSound(room.value?.roundStartsAt, `round-start:${room.value?.roundStartsAt}`)
  lastCountdownTone = sec
})

watch(nextRoundSec, (sec) => {
  if (sec != null && sec > 0 && sec !== lastNextTone) playCountdownSound()
  if (sec === 1) scheduleStartSound(room.value?.nextRoundAt, `round-next:${room.value?.nextRoundAt}`)
  lastNextTone = sec
})

watch(soloStartSec, (sec) => {
  if (sec != null && sec > 0 && sec !== lastSoloStartTone) playCountdownSound()
  if (sec === 1) scheduleStartSound(soloStartAt.value, `solo-start:${soloStartAt.value}`)
  lastSoloStartTone = sec
})

watch(
  () => room.value?.status,
  (status) => {
    if (status === 'playing' && previousRoomStatus !== 'playing' && Date.now() - lastStartSoundAt > 500) {
      playStartSound()
    }
    if (status === 'playing' && previousRoomStatus !== 'playing') cancelScheduledStartSound()
    previousRoomStatus = status
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
  window.addEventListener('beforeunload', beforeUnload)
  if (!auth.loaded) await auth.fetchMe()
  if (!auth.isLoggedIn) return
  ensureSocket()
  if (socket.value.connected) await authEmit()
  if (route.params.playMode === 'solo') startSoloFromRoute()
  else if (route.params.playMode === 'play' && !room.value) {
    await router.replace('/guess-fan')
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', beforeUnload)
  document.documentElement.style.overflow = ''
  document.body.style.overflow = ''
  if (tickTimer) clearInterval(tickTimer)
  if (soloTimer) clearTimeout(soloTimer)
  cancelScheduledStartSound()
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
  background: #f7f8fa;
  border: 1px solid #e4e7ed;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
  color: #555;
}
.match-bar.busy {
  background: #fff8ef;
  border-color: #f0d9a8;
}
.match-bar.active {
  background: #fff3df;
  border-color: #e6a23c;
}
.match-bar-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  min-width: 0;
}
.match-bar-main strong {
  color: #606266;
  font-weight: 700;
}
.match-bar.busy .match-bar-main strong,
.match-bar.active .match-bar-main strong {
  color: #b26a00;
}
.queue-players {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.queue-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 8px;
  border-radius: 14px;
  background: #fff;
  border: 1px solid #e8d2a4;
  color: #333;
  font-size: 12px;
}
.queue-chip.me {
  border-color: #e6a23c;
  background: #fff3df;
  font-weight: 700;
}
.queue-chip small {
  padding: 0 4px;
  border-radius: 3px;
  background: #e6a23c;
  color: #fff;
  font-size: 10px;
}
.queue-wait {
  color: #999;
  font-size: 12px;
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
  grid-template-columns: minmax(0, 1fr) 300px;
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

.rank-sec-h {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.elo-help {
  width: 20px;
  height: 20px;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.55);
  border-radius: 50%;
  background: transparent;
  color: #fff;
  font-size: 12px;
  font-weight: 800;
  line-height: 1;
  cursor: help;
  flex-shrink: 0;
}
.elo-help:hover {
  border-color: #fff;
  background: rgba(255, 255, 255, 0.12);
}

.rank-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.rank-list li {
  display: grid;
  grid-template-columns: 28px 1fr auto;
  align-items: center;
  gap: 8px;
  padding: 9px 0;
  border-bottom: 1px solid #eee;
  font-size: 13px;
}

.rank-list .rk {
  display: grid;
  place-items: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: #f0f2f5;
  color: #909399;
  font-weight: 700;
}
.rank-list .rk.top-1 { background: #f5c451; color: #6f4c00; }
.rank-list .rk.top-2 { background: #d8dde5; color: #4f5968; }
.rank-list .rk.top-3 { background: #d9a676; color: #6f3818; }

.rank-list .nm {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rank-player, .rank-score { display: flex; min-width: 0; flex-direction: column; }
.rank-player small, .rank-score small { color: #909399; font-size: 10px; }
.rank-score { align-items: flex-end; color: #409eff; }

.game-guide { margin-top: 18px; padding: 22px; border: 1px solid #dfe7ef; background: linear-gradient(135deg, #f8fbff, #fff); }
.guide-heading { padding-bottom: 14px; border-bottom: 1px solid #e8edf3; }
.guide-heading h2 { margin: 0; font-size: 20px; }
.guide-grid { display: grid; grid-template-columns: 1.4fr 1fr; gap: 14px; margin-top: 16px; }
.guide-grid article { padding: 15px 16px; border-radius: 8px; background: #fff; box-shadow: 0 2px 12px rgba(31, 45, 61, .06); }
.guide-grid h3 { margin: 0 0 10px; font-size: 15px; }
.guide-grid p, .guide-grid li { color: #606266; font-size: 13px; line-height: 1.65; }
.guide-grid p { margin: 7px 0; }
.guide-grid ul { margin: 0; padding-left: 1.25em; }
.related-cases li { margin-bottom: 7px; }
.marking-rules { margin: 0; padding-left: 1.25em; }
.marking-rules li { margin-bottom: 8px; }
.related-off { margin-top: 12px !important; padding: 9px 11px; border-left: 3px solid #909399; background: #f5f7fa; }
.tone-line { display: grid; grid-template-columns: 18px 48px 1fr; align-items: center; gap: 7px; margin: 9px 0; font-size: 13px; }
.tone { width: 15px; height: 15px; border-radius: 3px; }
.tone.green { background: #67c23a; }
.tone.yellow { background: #e6a23c; }
.tone.gray { background: #c0c4cc; }

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
  .guide-grid { grid-template-columns: 1fr; }
}
</style>

<style>
.elo-tooltip { max-width: 360px; }
.elo-tip-content { line-height: 1.7; }
</style>
