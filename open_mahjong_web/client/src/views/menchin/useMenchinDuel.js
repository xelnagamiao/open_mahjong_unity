import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { io } from 'socket.io-client'
import { usePlayerAuthStore } from '@/stores/playerAuth'

export function useMenchinDuel() {
  const auth = usePlayerAuthStore()
  const connected = ref(false)
  const queued = ref(false)
  const rooms = shallowRef([])
  const room = shallowRef(null)
  const busy = ref(false)
  const error = ref('')
  const rating = ref(null)
  const standings = shallowRef([])
  const standingsStatus = ref('idle')
  const clock = ref(Date.now())
  let offset = 0
  let boardRequest = 0
  let socket, interval, stopWatch
  const seconds = computed(() => Math.max(0, Math.ceil(((room.value?.game?.deadline ?? 0) - clock.value) / 1000)))

  function dispose() {
    socket?.removeAllListeners()
    socket?.disconnect()
    socket = null
  }

  function connect() {
    dispose()
    room.value = null
    rooms.value = []
    rating.value = null
    standings.value = []
    standingsStatus.value = 'idle'
    queued.value = connected.value = busy.value = false
    error.value = ''
    if (!auth.token) return
    socket = io('/menchin-duel', {
      path: '/api/socket.io', auth: { token: auth.token }, autoConnect: false,
    })
    socket.on('state', state => {
      const first = !connected.value
      offset = state.serverNow - Date.now()
      clock.value = state.serverNow
      if (first) error.value = ''
      connected.value = true
      room.value = state.room
      rooms.value = state.rooms
      queued.value = state.queued
      rating.value = state.rating
      if (first && standingsStatus.value !== 'idle') readStandings()
    })
    socket.on('standings-changed', () => {
      if (standingsStatus.value !== 'idle') readStandings()
    })
    socket.on('connect_error', reason => {
      connected.value = false
      error.value = /[\u4e00-\u9fff]/.test(reason.message) ? reason.message : '对战服务连接失败'
    })
    socket.on('disconnect', () => {
      connected.value = false
      error.value = '连接中断，正在重连'
    })
    socket.connect()
  }

  async function play(action, data = {}) {
    if (!connected.value || !socket?.connected || busy.value) return false
    const current = socket
    const payload = { action, ...data }
    if (room.value) payload.roomId = room.value.id
    if (action === 'move') payload.version = room.value.game.revision
    busy.value = true
    error.value = ''
    try {
      const reply = await current.timeout(7000).emitWithAck('play', payload)
      if (current !== socket) return false
      if (!reply?.ok) {
        error.value = reply?.error || '操作失败'
        current.emit('sync')
        return false
      }
      return true
    } catch {
      if (current === socket) {
        error.value = '请求未确认，正在同步对局'
        if (current.connected) current.emit('sync')
      }
      return false
    } finally {
      if (current === socket) busy.value = false
    }
  }

  function retry() {
    if (socket?.connected) socket.emit('sync')
    else socket?.connect()
  }
  async function readStandings() {
    if (!connected.value) return
    const current = socket
    const request = ++boardRequest
    standingsStatus.value = 'loading'
    try {
      const reply = await current.timeout(7000).emitWithAck('standings')
      if (current !== socket || request !== boardRequest) return
      if (!reply?.ok || !Array.isArray(reply.rows)) throw new Error()
      standings.value = reply.rows
      standingsStatus.value = 'ready'
    } catch {
      if (current === socket && request === boardRequest) standingsStatus.value = 'error'
    }
  }
  onMounted(() => {
    stopWatch = watch(() => auth.token, connect, { immediate: true })
    interval = setInterval(() => {
      if (room.value?.phase === 'playing') clock.value = Date.now() + offset
    }, 125)
  })
  onBeforeUnmount(() => {
    stopWatch?.()
    clearInterval(interval)
    dispose()
  })
  return { auth, connected, queued, rooms, room, busy, error, seconds, rating, standings, standingsStatus, readStandings, play, retry }
}
