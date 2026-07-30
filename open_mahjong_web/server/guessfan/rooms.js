const {
  GUESS_FAN_BY_ID,
  MAX_GUESSES,
  filterCatalogByRules,
  rollAnswer,
  findFanByName,
} = require('./catalog')
const { compareGuess, revealAnswer, extractTonePreview } = require('./compare')
const {
  MATCH_OPENING_COUNTDOWN_MS,
  ROUND_RESULT_WAIT_MS,
  shouldUseMatchOpeningCountdown,
} = require('./timing')
const {
  fetchLeaderboardTop,
  applyMatchRating,
} = require('../utils/guessFanTables')

/** 匹配对局固定配置 */
const MATCH_DEFAULTS = {
  rules: ['guobiao', 'riichi'],
  bestOf: 5,
  disableRelated: false,
  maxGuesses: 8,
  timeLimitSec: 60,
  ranked: true,
}

/** @type {Map<string, object>} */
const rooms = new Map()

/** @type {Map<string, { socketId: string, userId: number|string, username: string, at: number }>} */
const matchQueue = new Map()

const LOBBY_CHANNEL = 'guessfan:lobby'
function genCode() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  let s = ''
  for (let i = 0; i < 6; i++) s += chars[Math.floor(Math.random() * chars.length)]
  return s
}

function publicPlayer(p, { stripContent = false } = {}) {
  const guesses = stripContent
    ? p.guesses.map((g) => ({
        preview: extractTonePreview(g.result),
        correct: !!g.result?.correct,
        at: g.at,
      }))
    : p.guesses
  return {
    id: p.id,
    userId: p.userId,
    nick: p.nick,
    guesses,
    done: p.done,
    correct: p.correct,
    guessCount: p.guesses.length,
  }
}

function roomListItem(room) {
  const players = [...room.players.values()]
  const host = players.find((p) => p.id === room.hostId) || players[0]
  return {
    code: room.code,
    hostName: host?.nick || '—',
    hostUserId: host?.userId || null,
    memberUserIds: players.map((p) => p.userId).filter(Boolean),
    playerNicks: players.map((p) => p.nick),
    playerCount: players.length,
    maxPlayers: 2,
    rules: room.rules,
    bestOf: room.bestOf,
    disableRelated: room.disableRelated,
    ranked: !!room.ranked,
    timeLimitSec: room.timeLimitSec || 0,
    status: room.status,
    createdAt: room.createdAt,
  }
}

function listPublicRooms() {
  return [...rooms.values()]
    .filter((r) => r.status === 'lobby' && !r.ranked)
    .sort((a, b) => b.createdAt - a.createdAt)
    .map(roomListItem)
}

function queuePublicList() {
  return [...matchQueue.values()]
    .sort((a, b) => a.at - b.at)
    .map((e) => ({
      userId: e.userId,
      username: e.username,
    }))
}

async function lobbyPayload() {
  let leaderboard = []
  try {
    leaderboard = await fetchLeaderboardTop(20)
  } catch (err) {
    console.error('[guessfan] fetch leaderboard failed', err.message)
  }
  return {
    rooms: listPublicRooms(),
    leaderboard,
    queueSize: matchQueue.size,
    queuePlayers: queuePublicList(),
  }
}

async function broadcastLobby(io) {
  if (!io) return
  const payload = await lobbyPayload()
  io.to(LOBBY_CHANNEL).emit('guessfan:lobby', payload)
}

function roomPublicState(room, viewerId) {
  const scores = {}
  for (const p of room.players.values()) scores[p.id] = room.scores[p.id] || 0

  const playing = room.status === 'playing'
  const base = {
    code: room.code,
    hostId: room.hostId,
    status: room.status,
    rules: room.rules,
    bestOf: room.bestOf,
    disableRelated: room.disableRelated,
    maxGuesses: room.maxGuesses,
    timeLimitSec: room.timeLimitSec || 0,
    roundEndsAt: room.roundEndsAt || null,
    roundStartsAt: room.roundStartsAt || null,
    nextRoundAt: room.nextRoundAt || null,
    ranked: !!room.ranked,
    round: room.round,
    winsNeeded: Math.ceil(room.bestOf / 2),
    scores,
    players: [...room.players.values()].map((p) =>
      publicPlayer(p, {
        stripContent: playing && viewerId && p.id !== viewerId,
      }),
    ),
    reveal: room.reveal,
    roundWinnerId: room.roundWinnerId,
    matchWinnerId: room.matchWinnerId,
    endedByForfeit: !!room.endedByForfeit,
    forfeitedNick: room.forfeitedNick || null,
  }

  if (viewerId && room.players.has(viewerId)) {
    base.you = publicPlayer(room.players.get(viewerId))
  }
  return base
}

function emitRoomState(io, room) {
  for (const sid of room.players.keys()) {
    io.to(sid).emit('guessfan:state', roomPublicState(room, sid))
  }
}

function ensureAuth(socket) {
  if (!socket.data?.guessFanUserId || !socket.data?.guessFanUsername) {
    throw new Error('请先使用 salasasa 账号登录')
  }
  return {
    userId: socket.data.guessFanUserId,
    username: socket.data.guessFanUsername,
  }
}

function clearRoundTimer(room) {
  if (room.roundTimer) {
    clearTimeout(room.roundTimer)
    room.roundTimer = null
  }
}

function createRoom({
  socketId,
  userId,
  username,
  rules,
  bestOf,
  disableRelated,
  maxGuesses,
  timeLimitSec,
  ranked,
}) {
  let code = genCode()
  while (rooms.has(code)) code = genCode()

  const room = {
    code,
    hostId: socketId,
    status: 'lobby',
    rules: rules?.length ? rules : ['guobiao', 'riichi'],
    bestOf: [1, 3, 5, 7].includes(bestOf) ? bestOf : 5,
    disableRelated: !!disableRelated,
    maxGuesses: maxGuesses > 0 ? maxGuesses : MAX_GUESSES,
    timeLimitSec: timeLimitSec > 0 ? timeLimitSec : 0,
    ranked: !!ranked,
    round: 0,
    scores: {},
    players: new Map(),
    answerId: null,
    rolledFan: null,
    reveal: null,
    roundWinnerId: null,
    matchWinnerId: null,
    ratingRecorded: false,
    roundEndsAt: null,
    roundStartsAt: null,
    nextRoundAt: null,
    roundTimer: null,
    openingCountdownUsed: false,
    endedByForfeit: false,
    forfeitedNick: null,
    createdAt: Date.now(),
  }

  addPlayer(room, socketId, username, userId)
  rooms.set(code, room)
  return room
}

function addPlayer(room, socketId, nick, userId) {
  if (room.players.size >= 2 && !room.players.has(socketId)) {
    throw new Error('房间已满')
  }
  const p = {
    id: socketId,
    userId: userId != null ? String(userId) : null,
    nick: String(nick || '玩家').slice(0, 24),
    guesses: [],
    done: false,
    correct: false,
  }
  room.players.set(socketId, p)
  if (room.scores[socketId] == null) room.scores[socketId] = 0
  return p
}

function getRoom(code) {
  return rooms.get(String(code || '').toUpperCase()) || null
}

function findRoomBySocket(socketId) {
  return [...rooms.values()].find((r) => r.players.has(socketId)) || null
}

function removeFromQueue(socketId) {
  matchQueue.delete(socketId)
}

/** 同一用户只保留最新 socket，避免重连后残留旧排队项 */
function enqueueMatch(socketId, userId, username) {
  for (const [sid, entry] of matchQueue) {
    if (sid !== socketId && String(entry.userId) === String(userId)) {
      matchQueue.delete(sid)
    }
  }
  matchQueue.set(socketId, {
    socketId,
    userId,
    username,
    at: Date.now(),
  })
}

async function removePlayer(socketId, io) {
  removeFromQueue(socketId)
  for (const [code, room] of rooms) {
    if (!room.players.has(socketId)) continue
    clearRoundTimer(room)
    const leavingPlayer = room.players.get(socketId)
    const remainingPlayer = [...room.players.values()].find((p) => p.id !== socketId)
    const activeMatch = ['starting', 'playing', 'round_over'].includes(room.status)
    if (activeMatch && remainingPlayer) {
      room.status = 'match_over'
      room.matchWinnerId = remainingPlayer.id
      room.roundWinnerId = null
      room.roundEndsAt = null
      room.roundStartsAt = null
      room.nextRoundAt = null
      room.endedByForfeit = true
      room.forfeitedNick = leavingPlayer?.nick || '对手'
      await recordMatchResult(room)
    }
    room.players.delete(socketId)
    if (room.hostId === socketId) {
      const next = room.players.keys().next().value
      room.hostId = next || null
    }
    if (room.players.size === 0) {
      rooms.delete(code)
      await broadcastLobby(io)
      return { code, empty: true }
    }
    if (io) {
      emitRoomState(io, room)
      await broadcastLobby(io)
    }
    return { code, empty: false, room }
  }
  await broadcastLobby(io)
  return null
}

function startRound(room, io) {
  clearRoundTimer(room)
  const pool = filterCatalogByRules(room.rules)
  if (!pool.length) throw new Error('题库为空')
  const { fan, rolledFan } = rollAnswer(room.rules)
  room.answerId = fan.id
  room.rolledFan = rolledFan
  room.round += 1
  room.status = 'playing'
  room.reveal = null
  room.roundWinnerId = null
  room.roundEndsAt = null
  room.roundStartsAt = null
  room.nextRoundAt = null
  room.endedByForfeit = false
  room.forfeitedNick = null
  for (const p of room.players.values()) {
    p.guesses = []
    p.done = false
    p.correct = false
  }

  if (room.timeLimitSec > 0) {
    room.roundEndsAt = Date.now() + room.timeLimitSec * 1000
    room.roundTimer = setTimeout(() => {
      room.roundTimer = null
      if (room.status !== 'playing') return
      const winner = [...room.players.values()].find((p) => p.correct)
      finishRound(room, winner ? winner.id : null, io)
        .then(async () => {
          if (!io) return
          emitRoomState(io, room)
          if (room.status === 'match_over') await broadcastLobby(io)
        })
        .catch((err) => console.error('[guessfan] timeout finishRound failed', err.message))
    }, room.timeLimitSec * 1000)
  }
}

function prepareMatchOpening(room, io) {
  clearRoundTimer(room)
  // 3 秒准备只属于整场首次开局。后续小局由结算阶段的 6 秒计时
  // 直接进入 startRound，不能再次经过这里叠加 3 秒。
  if (!shouldUseMatchOpeningCountdown(room)) {
    startRound(room, io)
    return
  }
  room.openingCountdownUsed = true
  room.status = 'starting'
  room.roundStartsAt = Date.now() + MATCH_OPENING_COUNTDOWN_MS
  room.roundEndsAt = null
  room.nextRoundAt = null
  room.reveal = null
  room.roundWinnerId = null
  room.matchWinnerId = null
  room.endedByForfeit = false
  room.forfeitedNick = null
  room.roundTimer = setTimeout(() => {
    room.roundTimer = null
    if (room.status !== 'starting' || room.players.size < 2) return
    startRound(room, io)
    if (io) emitRoomState(io, room)
  }, MATCH_OPENING_COUNTDOWN_MS)
}

async function recordMatchResult(room) {
  // 仅匹配对局计入排行榜；落库防重复
  if (!room.ranked) return
  if (room.status !== 'match_over' || !room.matchWinnerId) return
  if (room.ratingRecorded) return
  const players = [...room.players.values()].filter((p) => p.userId != null && p.userId !== '')
  if (players.length !== 2) return
  const winner = room.players.get(room.matchWinnerId)
  if (!winner?.userId) return
  try {
    await applyMatchRating({
      winnerUserId: winner.userId,
      players: players.map((p) => ({ userId: p.userId, username: p.nick })),
    })
    room.ratingRecorded = true
  } catch (err) {
    console.error('[guessfan] recordMatchResult failed', err.message)
  }
}

async function finishRound(room, winnerId, io) {
  if (room.status !== 'playing') return
  clearRoundTimer(room)
  room.roundEndsAt = null
  room.roundStartsAt = null
  room.nextRoundAt = null
  room.status = 'round_over'
  room.roundWinnerId = winnerId || null
  if (winnerId) {
    room.scores[winnerId] = (room.scores[winnerId] || 0) + 1
  }
  const answer = GUESS_FAN_BY_ID[room.answerId]
  room.reveal = revealAnswer(answer, room.rolledFan)

  const need = Math.ceil(room.bestOf / 2)
  for (const [pid, sc] of Object.entries(room.scores)) {
    if (sc >= need) {
      room.status = 'match_over'
      room.matchWinnerId = pid
      await recordMatchResult(room)
      break
    }
  }

  // 非终局统一展示 6 秒结算，再由服务端自动开始下一局，保证双方同步。
  if (room.status === 'round_over') {
    room.nextRoundAt = Date.now() + ROUND_RESULT_WAIT_MS
    room.roundTimer = setTimeout(() => {
      room.roundTimer = null
      if (room.status !== 'round_over' || room.players.size < 2) return
      room.nextRoundAt = null
      startRound(room, io)
      if (io) emitRoomState(io, room)
    }, ROUND_RESULT_WAIT_MS)
  }
}

async function checkRoundExhausted(room, io) {
  const allDone = [...room.players.values()].every((p) => p.done || p.correct)
  if (!allDone) return
  if (room.status !== 'playing') return
  const winner = [...room.players.values()].find((p) => p.correct)
  await finishRound(room, winner ? winner.id : null, io)
}

async function submitGuess(room, socketId, name, guessId, io) {
  if (room.status !== 'playing') throw new Error('当前不可猜测')
  if (room.roundEndsAt && Date.now() >= room.roundEndsAt) {
    throw new Error('本局已超时')
  }
  const player = room.players.get(socketId)
  if (!player) throw new Error('不在房间内')
  if (player.done || player.correct) throw new Error('本局已结束')
  if (player.guesses.length >= room.maxGuesses) throw new Error('次数用尽')

  const answer = GUESS_FAN_BY_ID[room.answerId]
  if (!answer) throw new Error('答案丢失')

  const guess = guessId ? GUESS_FAN_BY_ID[guessId] : findFanByName(name, room.rules)
  if (!guess) throw new Error('未找到该番种')

  if (!guess.rules.some((r) => room.rules.includes(r))) {
    throw new Error('该番种不在本房题库中')
  }

  const result = compareGuess({
    answer,
    rolledFan: room.rolledFan,
    guess,
    disableRelated: room.disableRelated,
  })

  player.guesses.push({
    guessId: guess.id,
    name: guess.names[0],
    result,
    at: Date.now(),
  })

  if (result.correct) {
    player.correct = true
    player.done = true
    await finishRound(room, socketId, io)
    return { result, finished: true }
  }

  if (player.guesses.length >= room.maxGuesses) {
    player.done = true
    await checkRoundExhausted(room, io)
  }

  return { result, finished: room.status !== 'playing' }
}

function tryMatch(io) {
  const entries = [...matchQueue.values()]
  if (entries.length < 2) return

  const a = entries[0]
  const b = entries[1]
  if (!b || b.socketId === a.socketId) return

  matchQueue.delete(a.socketId)
  matchQueue.delete(b.socketId)

  const room = createRoom({
    socketId: a.socketId,
    userId: a.userId,
    username: a.username,
    ...MATCH_DEFAULTS,
  })
  addPlayer(room, b.socketId, b.username, b.userId)

  const sa = io.sockets.sockets.get(a.socketId)
  const sb = io.sockets.sockets.get(b.socketId)
  if (sa) sa.join(`guessfan:${room.code}`)
  if (sb) sb.join(`guessfan:${room.code}`)

  prepareMatchOpening(room, io)
  const stateA = roomPublicState(room, a.socketId)
  const stateB = roomPublicState(room, b.socketId)
  if (sa) sa.emit('guessfan:matched', { state: stateA })
  if (sb) sb.emit('guessfan:matched', { state: stateB })
  emitRoomState(io, room)
  broadcastLobby(io).catch((err) => console.error('[guessfan] broadcast after match failed', err.message))
}

function registerGuessFanHandlers(socket, io) {
  socket.on('guessfan:auth', async (payload = {}, cb) => {
    try {
      const userId = payload.userId
      const username = String(payload.username || '').trim()
      if (userId == null || userId === '' || !username) {
        throw new Error('请先使用 salasasa 账号登录')
      }
      socket.data.guessFanUserId = userId
      socket.data.guessFanUsername = username
      socket.join(LOBBY_CHANNEL)
      if (typeof cb === 'function') {
        cb({
          ok: true,
          lobby: await lobbyPayload(),
          matchDefaults: MATCH_DEFAULTS,
        })
      }
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:lobby', async (_payload, cb) => {
    try {
      ensureAuth(socket)
      socket.join(LOBBY_CHANNEL)
      if (typeof cb === 'function') {
        cb({
          ok: true,
          lobby: await lobbyPayload(),
          matchDefaults: MATCH_DEFAULTS,
        })
      }
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:create', async (payload = {}, cb) => {
    try {
      const { userId, username } = ensureAuth(socket)
      if (findRoomBySocket(socket.id)) throw new Error('已在房间中')
      removeFromQueue(socket.id)
      const room = createRoom({
        socketId: socket.id,
        userId,
        username,
        rules: payload.rules,
        bestOf: payload.bestOf,
        disableRelated: payload.disableRelated,
        maxGuesses: MAX_GUESSES,
        timeLimitSec: 60,
        ranked: false,
      })
      socket.join(`guessfan:${room.code}`)
      await broadcastLobby(io)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:join', async (payload = {}, cb) => {
    try {
      const { userId, username } = ensureAuth(socket)
      if (findRoomBySocket(socket.id)) throw new Error('已在房间中')
      removeFromQueue(socket.id)
      const room = getRoom(payload.code)
      if (!room) throw new Error('房间不存在')
      if (room.ranked) throw new Error('匹配房不可手动加入')
      if (room.status !== 'lobby') throw new Error('对局已开始，无法加入')
      addPlayer(room, socket.id, username, userId)
      socket.join(`guessfan:${room.code}`)
      emitRoomState(io, room)
      await broadcastLobby(io)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:queue', async (_payload, cb) => {
    try {
      const { userId, username } = ensureAuth(socket)
      if (findRoomBySocket(socket.id)) throw new Error('已在房间中')
      socket.join(LOBBY_CHANNEL)
      enqueueMatch(socket.id, userId, username)
      console.log('[guessfan] queue join', { socketId: socket.id, userId, username, size: matchQueue.size })
      tryMatch(io)
      await broadcastLobby(io)
      const matched = findRoomBySocket(socket.id)
      if (typeof cb === 'function') {
        cb({
          ok: true,
          queued: !matched,
          queueSize: matchQueue.size,
          queuePlayers: queuePublicList(),
          state: matched ? roomPublicState(matched, socket.id) : null,
          matchDefaults: MATCH_DEFAULTS,
        })
      }
    } catch (e) {
      console.warn('[guessfan] queue failed', e.message)
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:queue_cancel', async (_payload, cb) => {
    removeFromQueue(socket.id)
    await broadcastLobby(io)
    if (typeof cb === 'function') {
      cb({ ok: true, queueSize: matchQueue.size, queuePlayers: queuePublicList() })
    }
  })

  socket.on('guessfan:start', async (_payload, cb) => {
    try {
      ensureAuth(socket)
      const room = findRoomBySocket(socket.id)
      if (!room) throw new Error('不在房间内')
      if (room.hostId !== socket.id) throw new Error('仅房主可开始')
      if (room.players.size < 2) throw new Error('需要两位玩家')
      if (room.status === 'match_over') throw new Error('比赛已结束')
      if (room.status !== 'lobby') throw new Error('对局已经开始')
      prepareMatchOpening(room, io)
      emitRoomState(io, room)
      await broadcastLobby(io)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:next', (_payload, cb) => {
    try {
      ensureAuth(socket)
      const room = findRoomBySocket(socket.id)
      if (!room) throw new Error('不在房间内')
      if (room.hostId !== socket.id) throw new Error('仅房主可开下一局')
      if (room.status !== 'round_over') throw new Error('当前不能开下一局')
      startRound(room, io)
      emitRoomState(io, room)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:guess', async (payload = {}, cb) => {
    try {
      ensureAuth(socket)
      const room = findRoomBySocket(socket.id)
      if (!room) throw new Error('不在房间内')
      const out = await submitGuess(room, socket.id, payload.name, payload.id, io)
      emitRoomState(io, room)
      if (room.status === 'match_over') await broadcastLobby(io)
      if (typeof cb === 'function') {
        cb({ ok: true, result: out.result, state: roomPublicState(room, socket.id) })
      }
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:state', (_payload, cb) => {
    const room = findRoomBySocket(socket.id)
    if (typeof cb === 'function') {
      if (!room) cb({ ok: false, error: '不在房间内' })
      else cb({ ok: true, state: roomPublicState(room, socket.id) })
    }
  })

  socket.on('guessfan:leave', async (_payload, cb) => {
    const info = await removePlayer(socket.id, io)
    if (info?.code) socket.leave(`guessfan:${info.code}`)
    socket.join(LOBBY_CHANNEL)
    if (typeof cb === 'function') cb({ ok: true })
  })

  socket.on('disconnect', () => {
    removePlayer(socket.id, io).catch((err) =>
      console.error('[guessfan] disconnect removePlayer failed', err.message)
    )
  })
}

module.exports = { registerGuessFanHandlers, rooms, MATCH_DEFAULTS }
