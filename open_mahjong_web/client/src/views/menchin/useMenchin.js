import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { io } from 'socket.io-client'
import { usePlayerAuthStore } from '@/stores/playerAuth'

const GUEST_STATS = 'menchin.training.v1'
const GUEST_SESSION = 'menchin.session.v1'
const ABANDONED = 'menchin.abandoned.v1'
const emptyGuest = () => ({ points: 0, answered: 0, correct: 0, bestStreak: 0, recent: [] })

function readGuest() {
  try {
    const value = JSON.parse(localStorage.getItem(GUEST_STATS))
    if (value && ['points', 'answered', 'correct', 'bestStreak'].every(k => Number.isSafeInteger(value[k]) && value[k] >= 0) &&
      value.correct <= value.answered && Array.isArray(value.recent)) return value
  } catch { return emptyGuest() }
  return emptyGuest()
}

function randomKey() {
  return Array.from(crypto.getRandomValues(new Uint8Array(16)), n => n.toString(16).padStart(2, '0')).join('')
}

function readSession(key) {
  try { return sessionStorage.getItem(key) || '' } catch { return '' }
}
function writeSession(key, value) {
  try {
    if (value) sessionStorage.setItem(key, value)
    else sessionStorage.removeItem(key)
  } catch { return }
}

export function useMenchin(page) {
  const auth = usePlayerAuthStore()
  const guest = ref(readGuest())
  const account = ref(null)
  const profile = computed(() => auth.token ? account.value : guest.value)
  const game = ref(null)
  const scores = ref([])
  const duelScores = ref([])
  const boardStatus = ref('loading')
  const rooms = ref([])
  const roomsStatus = ref('loading')
  const connected = ref(false)
  const busy = ref(false)
  const error = ref('')
  const now = ref(Date.now())
  const guestKey = readSession(GUEST_SESSION) || randomKey()
  writeSession(GUEST_SESSION, guestKey)
  let socket, clock, exitPacket
  let offset = 0, revision = 0, operation = 0
  let roomsRevision = 0
  let starting = '', abandoned = readSession(ABANDONED)

  function saveGuest(run) {
    const [player] = run.players
    if (!player.guest || run.phase !== 'finished' || guest.value.recent.includes(run.id)) return
    const latest = readGuest()
    if (latest.recent.includes(run.id)) { guest.value = latest; return }
    const stats = latest.answered >= guest.value.answered ? latest : guest.value
    guest.value = {
      points: stats.points + player.points, answered: stats.answered + player.answered,
      correct: stats.correct + player.correct, bestStreak: Math.max(stats.bestStreak, player.bestStreak),
      recent: [...stats.recent.slice(-63), run.id],
    }
    try { localStorage.setItem(GUEST_STATS, JSON.stringify(guest.value)) } catch { return }
  }

  async function refreshScores() {
    const request = ++revision
    if (!socket?.connected) return
    try {
      const result = await socket.timeout(8000).emitWithAck('scores')
      if (request !== revision) return
      if (!result.ok || !Array.isArray(result.boards?.challenge) || !Array.isArray(result.boards?.duel)) throw new Error()
      scores.value = result.boards.challenge
      duelScores.value = result.boards.duel
      account.value = result.profile
      boardStatus.value = 'ready'
    } catch {
      if (request === revision) boardStatus.value = 'error'
    }
  }

  function setRooms(list) {
    roomsRevision++
    rooms.value = list
    roomsStatus.value = 'ready'
  }

  function clearRooms(status) {
    roomsRevision++
    rooms.value = []
    roomsStatus.value = status
  }

  async function refreshRooms() {
    roomsStatus.value = 'loading'
    if (!socket?.connected) {
      socket?.connect()
      return
    }
    const request = ++roomsRevision
    try {
      const list = await socket.timeout(8000).emitWithAck('rooms')
      if (request === roomsRevision) setRooms(list)
    } catch {
      if (request === roomsRevision) clearRooms('error')
    }
  }

  function sendAbandon() {
    if (!abandoned || !socket?.connected) return
    const requestId = abandoned
    exitPacket = socket.timeout(3000).emitWithAck('play', { action: 'abandon', requestId }).then(reply => {
      if (reply.ok && abandoned === requestId) {
        abandoned = ''
        if (readSession(ABANDONED) === requestId) writeSession(ABANDONED, '')
      }
    }).catch(() => {})
  }

  function exitChallenge() {
    const key = starting || game.value?.requestId
    if (!key) return
    abandoned = key
    writeSession(ABANDONED, key)
    starting = ''
    operation++
    busy.value = false
    game.value = null
    error.value = ''
    sendAbandon()
  }

  function disposeSocket() {
    revision++
    roomsRevision++
    const current = socket
    if (!current) return
    socket = null
    current.removeAllListeners()
    if (exitPacket) exitPacket.finally(() => current.disconnect())
    else current.disconnect()
    exitPacket = null
  }

  function connect() {
    disposeSocket()
    connected.value = busy.value = false
    game.value = account.value = null
    error.value = ''
    boardStatus.value = 'loading'
    clearRooms('loading')
    if (page.value === 'practice') return
    socket = io('/menchin', {
      path: '/api/socket.io', auth: { token: auth.token, guestKey }, autoConnect: false,
    })
    socket.on('connect', () => {
      error.value = ''
      sendAbandon()
      refreshScores()
      refreshRooms()
    })
    socket.on('connect_error', reason => {
      connected.value = false
      boardStatus.value = 'error'
      clearRooms('error')
      error.value = reason.message.includes('账号') ? reason.message : '挑战服务连接失败'
    })
    socket.on('disconnect', () => {
      revision++
      connected.value = false
      boardStatus.value = 'error'
      clearRooms('error')
      error.value = '连接中断，正在重连'
    })
    socket.on('state', state => {
      connected.value = true
      offset = state.serverNow - Date.now()
      now.value = state.serverNow
      const run = state.game
      game.value = run?.mode === 'challenge' && run.requestId !== abandoned ? run : null
      if (game.value) saveGuest(game.value)
    })
    socket.on('scores-changed', refreshScores)
    socket.on('rooms-changed', setRooms)
    socket.connect()
  }

  async function play(action, data) {
    if (busy.value || !connected.value) return
    const current = socket
    const request = ++operation
    busy.value = true
    error.value = ''
    try {
      const reply = await current.timeout(8000).emitWithAck('play', {
        action, gameId: game.value?.id, round: game.value?.round, ...data,
      })
      if (request === operation && current === socket && !reply.ok) error.value = reply.error
    } catch {
      if (request === operation && current === socket) {
        error.value = '请求未确认，正在同步挑战'
        if (current.connected) current.emit('sync')
      }
    } finally {
      if (request === operation && current === socket) {
        busy.value = false
        if (action === 'challenge' && (game.value || !error.value)) starting = ''
      }
    }
  }
  function startChallenge(level) {
    if (busy.value || !connected.value) return
    starting = randomKey()
    play('challenge', { level, requestId: starting })
  }
  function retry() {
    if (socket?.connected) { socket.emit('sync'); refreshScores() }
    else socket?.connect()
  }
  function storageChanged(event) {
    if (event.key === GUEST_STATS) guest.value = readGuest()
  }
  watch([() => auth.token, () => page.value === 'practice'], connect)
  onMounted(() => {
    connect()
    clock = setInterval(() => { if (game.value?.phase === 'playing') now.value = Date.now() + offset }, 100)
    window.addEventListener('storage', storageChanged)
  })
  onBeforeUnmount(() => {
    clearInterval(clock)
    window.removeEventListener('storage', storageChanged)
    disposeSocket()
  })
  return { auth, profile, game, scores, duelScores, boardStatus, rooms, roomsStatus, refreshRooms, connected, busy, error, now, play, startChallenge, exitChallenge, retry }
}
