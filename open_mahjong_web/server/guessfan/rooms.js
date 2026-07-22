const {
  GUESS_FAN_BY_ID,
  MAX_GUESSES,
  filterCatalogByRules,
  rollAnswer,
  findFanByName,
} = require('./catalog')
const { compareGuess, revealAnswer, extractTonePreview } = require('./compare')

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

/** @type {Map<string, { userId: string, username: string, wins: number, matches: number }>} */
const leaderboard = new Map()

const LOBBY_CHANNEL = 'guessfan:lobby'
const START_COUNTDOWN_MS = 3000

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

function leaderboardTop(limit = 20) {
  return [...leaderboard.values()]
    .map((row) => ({
      ...row,
      losses: row.matches - row.wins,
      winRate: row.matches ? Math.round((row.wins / row.matches) * 100) : 0,
    }))
    .sort((a, b) => b.rating - a.rating || b.wins - a.wins || a.matches - b.matches)
    .slice(0, limit)
}

function queuePublicList() {
  return [...matchQueue.values()]
    .sort((a, b) => a.at - b.at)
    .map((e) => ({
      userId: e.userId,
      username: e.username,
    }))
}

function lobbyPayload() {
  return {
    rooms: listPublicRooms(),
    leaderboard: leaderboardTop(),
    queueSize: matchQueue.size,
    queuePlayers: queuePublicList(),
  }
}

function broadcastLobby(io) {
  io.to(LOBBY_CHANNEL).emit('guessfan:lobby', lobbyPayload())
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
    roundEndsAt: null,
    roundStartsAt: null,
    nextRoundAt: null,
    roundTimer: null,
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

function removePlayer(socketId, io) {
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
      recordMatchResult(room)
    }
    room.players.delete(socketId)
    if (room.hostId === socketId) {
      const next = room.players.keys().next().value
      room.hostId = next || null
    }
    if (room.players.size === 0) {
      rooms.delete(code)
      if (io) broadcastLobby(io)
      return { code, empty: true }
    }
    if (io) {
      emitRoomState(io, room)
      broadcastLobby(io)
    }
    return { code, empty: false, room }
  }
  if (io) broadcastLobby(io)
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
      if (io) {
        emitRoomState(io, room)
        if (room.status === 'match_over') broadcastLobby(io)
      }
    }, room.timeLimitSec * 1000)
  }
}

function prepareRound(room, io) {
  clearRoundTimer(room)
  room.status = 'starting'
  room.roundStartsAt = Date.now() + START_COUNTDOWN_MS
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
  }, START_COUNTDOWN_MS)
}

function recordMatchResult(room) {
  // 仅匹配对局计入排行榜
  if (!room.ranked) return
  if (room.status !== 'match_over' || !room.matchWinnerId) return
  const players = [...room.players.values()].filter((p) => p.userId)
  if (players.length !== 2) return
  const rows = players.map((p) => {
    const key = String(p.userId)
    const row = leaderboard.get(key) || {
      userId: key,
      username: p.nick,
      wins: 0,
      matches: 0,
      rating: 1000,
      streak: 0,
      bestStreak: 0,
    }
    row.username = p.nick
    return { player: p, key, row }
  })

  const [a, b] = rows
  const expectedA = 1 / (1 + 10 ** ((b.row.rating - a.row.rating) / 400))
  const scoreA = a.player.id === room.matchWinnerId ? 1 : 0
  const delta = Math.round(32 * (scoreA - expectedA))

  for (const entry of rows) {
    const won = entry.player.id === room.matchWinnerId
    entry.row.matches += 1
    if (won) {
      entry.row.wins += 1
      entry.row.streak += 1
      entry.row.bestStreak = Math.max(entry.row.bestStreak, entry.row.streak)
    } else {
      entry.row.streak = 0
    }
    entry.row.rating = Math.max(0, entry.row.rating + (entry === a ? delta : -delta))
    leaderboard.set(entry.key, entry.row)
  }
}

function finishRound(room, winnerId, io) {
  if (room.status !== 'playing') return
  clearRoundTimer(room)
  room.roundEndsAt = null
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
      recordMatchResult(room)
      break
    }
  }

  // 非终局统一展示 6 秒结算，再由服务端自动开始下一局，保证双方同步。
  if (room.status === 'round_over') {
    room.nextRoundAt = Date.now() + 6000
    room.roundTimer = setTimeout(() => {
      room.roundTimer = null
      if (room.status !== 'round_over' || room.players.size < 2) return
      startRound(room, io)
      if (io) emitRoomState(io, room)
    }, 6000)
  }
}

function checkRoundExhausted(room, io) {
  const allDone = [...room.players.values()].every((p) => p.done || p.correct)
  if (!allDone) return
  if (room.status !== 'playing') return
  const winner = [...room.players.values()].find((p) => p.correct)
  finishRound(room, winner ? winner.id : null, io)
}

function submitGuess(room, socketId, name, guessId, io) {
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
    finishRound(room, socketId, io)
    return { result, finished: true }
  }

  if (player.guesses.length >= room.maxGuesses) {
    player.done = true
    checkRoundExhausted(room, io)
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

  prepareRound(room, io)
  const stateA = roomPublicState(room, a.socketId)
  const stateB = roomPublicState(room, b.socketId)
  if (sa) sa.emit('guessfan:matched', { state: stateA })
  if (sb) sb.emit('guessfan:matched', { state: stateB })
  emitRoomState(io, room)
  broadcastLobby(io)
}

function registerGuessFanHandlers(socket, io) {
  socket.on('guessfan:auth', (payload = {}, cb) => {
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
          lobby: lobbyPayload(),
          matchDefaults: MATCH_DEFAULTS,
        })
      }
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:lobby', (_payload, cb) => {
    try {
      ensureAuth(socket)
      socket.join(LOBBY_CHANNEL)
      if (typeof cb === 'function') {
        cb({
          ok: true,
          lobby: lobbyPayload(),
          matchDefaults: MATCH_DEFAULTS,
        })
      }
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:create', (payload = {}, cb) => {
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
      broadcastLobby(io)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:join', (payload = {}, cb) => {
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
      broadcastLobby(io)
      if (typeof cb === 'function') cb({ ok: true, state: roomPublicState(room, socket.id) })
    } catch (e) {
      if (typeof cb === 'function') cb({ ok: false, error: e.message })
    }
  })

  socket.on('guessfan:queue', (_payload, cb) => {
    try {
      const { userId, username } = ensureAuth(socket)
      if (findRoomBySocket(socket.id)) throw new Error('已在房间中')
      socket.join(LOBBY_CHANNEL)
      enqueueMatch(socket.id, userId, username)
      console.log('[guessfan] queue join', { socketId: socket.id, userId, username, size: matchQueue.size })
      tryMatch(io)
      broadcastLobby(io)
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

  socket.on('guessfan:queue_cancel', (_payload, cb) => {
    removeFromQueue(socket.id)
    broadcastLobby(io)
    if (typeof cb === 'function') {
      cb({ ok: true, queueSize: matchQueue.size, queuePlayers: queuePublicList() })
    }
  })

  socket.on('guessfan:start', (_payload, cb) => {
    try {
      ensureAuth(socket)
      const room = findRoomBySocket(socket.id)
      if (!room) throw new Error('不在房间内')
      if (room.hostId !== socket.id) throw new Error('仅房主可开始')
      if (room.players.size < 2) throw new Error('需要两位玩家')
      if (room.status === 'match_over') throw new Error('比赛已结束')
      prepareRound(room, io)
      emitRoomState(io, room)
      broadcastLobby(io)
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

  socket.on('guessfan:guess', (payload = {}, cb) => {
    try {
      ensureAuth(socket)
      const room = findRoomBySocket(socket.id)
      if (!room) throw new Error('不在房间内')
      const out = submitGuess(room, socket.id, payload.name, payload.id, io)
      emitRoomState(io, room)
      if (room.status === 'match_over') broadcastLobby(io)
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

  socket.on('guessfan:leave', (_payload, cb) => {
    const info = removePlayer(socket.id, io)
    if (info?.code) socket.leave(`guessfan:${info.code}`)
    socket.join(LOBBY_CHANNEL)
    if (typeof cb === 'function') cb({ ok: true })
  })

  socket.on('disconnect', () => {
    removePlayer(socket.id, io)
  })
}

module.exports = { registerGuessFanHandlers, rooms, MATCH_DEFAULTS }
