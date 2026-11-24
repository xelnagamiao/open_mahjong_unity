import { randomInt, randomUUID } from 'node:crypto'
import { createDuel, duelAct, duelTick, duelView } from './duel.mjs'

export function registerDuelRooms(ns, store, verify, options = {}) {
  const rooms = new Map()
  const assigned = new Map()
  const queue = new Map()
  const offline = new Map()
  const ratings = new Map()
  const now = options.now || Date.now
  const reconnectMs = options.reconnectMs ?? 30000
  const channel = id => 'duelist:' + id
  const online = id => ns.adapter.rooms.has(channel(id))

  function availableRooms() {
    return [...rooms.values()]
      .filter(room => room.phase === 'waiting' && room.players.length === 1 && online(room.players[0].id))
      .map(room => ({ code: room.code, host: room.players[0].name }))
  }

  function snapshot(id, listings = availableRooms()) {
    const room = assigned.get(id)
    const result = { serverNow: now(), rooms: listings, queued: queue.has(id), rating: ratings.get(id), room: null }
    if (!room) return result
    const seat = room.players.findIndex(p => p.id === id)
    const game = room.game ? duelView(room.game, seat) : null
    if (game && room.phase === 'finished') {
      game.phase = 'finished'
      game.turn = null
      game.deadline = null
      game.actions = []
    }
    result.room = {
      id: room.id, code: room.code, ranked: room.ranked, phase: room.phase, seat,
      players: room.players.map(p => ({ id: p.id, name: p.name, online: online(p.id) })),
      game,
      winner: room.winner, forfeit: room.forfeit, changes: room.changes, saving: room.saving,
    }
    return result
  }
  function publishRoom(room) {
    const listings = availableRooms()
    for (const { id } of room.players) {
      if (assigned.get(id) === room) ns.to(channel(id)).emit('state', snapshot(id, listings))
    }
  }
  function publishLobby() {
    const listings = availableRooms()
    options.roomsChanged?.(listings)
    const ids = new Set([...ns.sockets.values()].map(s => s.data.actor.id))
    for (const id of ids) ns.to(channel(id)).emit('state', snapshot(id, listings))
  }
  function create(players, ranked) {
    let code
    do { code = String(randomInt(100000, 1000000)) } while (rooms.has(code))
    const room = {
      id: randomUUID(), code, players, ranked, phase: 'waiting', game: null, timer: null,
      winner: null, forfeit: null, changes: {}, saving: '',
    }
    rooms.set(code, room)
    players.forEach(p => assigned.set(p.id, room))
    return room
  }

  async function save(room, attempt = 0) {
    try {
      room.changes = await store.settle({
        mode: 'ranked', id: room.id, players: room.players, winner: room.players[room.winner].id, finishedAt: room.finishedAt,
      })
      room.saving = 'saved'
      for (const [id, change] of Object.entries(room.changes)) {
        if (online(id)) ratings.set(id, change.rating)
      }
    } catch (error) {
      console.error('[menchin-duel] score settlement:', error.message)
      room.saving = 'retry'
      setTimeout(() => save(room, attempt + 1), Math.min(60000, 2000 * 2 ** Math.min(attempt, 5))).unref()
    }
    publishRoom(room)
    if (room.saving === 'saved') {
      for (const { id } of room.players) {
        if (online(id) && assigned.get(id) !== room) ns.to(channel(id)).emit('state', snapshot(id))
      }
      ns.emit('standings-changed')
      options.scoresChanged?.()
    }
  }
  function finish(room, winner, forfeit = null) {
    if (room.phase !== 'playing') return
    clearTimeout(room.timer)
    room.phase = 'finished'
    room.winner = winner
    room.forfeit = forfeit
    room.finishedAt = Date.now()
    if (room.ranked && winner !== null) {
      room.saving = 'saving'
      void save(room)
    }
  }
  function schedule(room) {
    clearTimeout(room.timer)
    if (room.game.phase === 'finished') {
      const [a, b] = room.game.scores
      finish(room, a === b ? null : (a > b ? 0 : 1))
      return
    }
    room.timer = setTimeout(() => {
      if (room.phase !== 'playing') return
      duelTick(room.game, now())
      schedule(room)
      publishRoom(room)
    }, Math.max(1, room.game.deadline - now())).unref()
  }
  function expire(room) {
    if (room?.phase === 'playing' && duelTick(room.game, now())) {
      schedule(room)
      publishRoom(room)
    }
  }
  function start(room) {
    room.game = createDuel({ now: now(), wallFactory: options.wallFactory })
    room.phase = 'playing'
    schedule(room)
  }
  function leave(id) {
    queue.delete(id)
    const room = assigned.get(id)
    if (!room) return
    const seat = room.players.findIndex(p => p.id === id)
    expire(room)
    if (room.phase === 'playing') {
      const opponent = 1 - seat
      finish(room, !online(id) && !online(room.players[opponent].id) ? null : opponent, seat)
    }
    assigned.delete(id)
    if (room.phase === 'waiting') {
      if (seat === 0) room.players.forEach(p => assigned.delete(p.id))
      else room.players.splice(seat, 1)
    }
    if (room.players.every(p => assigned.get(p.id) !== room)) {
      clearTimeout(room.timer)
      rooms.delete(room.code)
    }
  }

  function play(actor, data) {
    const id = actor.id
    const room = assigned.get(id)
    if (data.action === 'cancel') {
      queue.delete(id)
      ns.to(channel(id)).emit('state', snapshot(id))
      return
    }
    if (['create', 'join', 'queue'].includes(data.action)) {
      if (room || queue.has(id)) throw new Error('请先退出当前房间或匹配')
      if (data.action === 'queue') {
        const opponent = queue.values().next().value
        if (opponent) {
          queue.delete(opponent.id)
          start(create([opponent, actor], true))
        } else queue.set(id, actor)
      } else if (data.action === 'create') create([actor], false)
      else {
        const target = rooms.get(data.code)
        if (!target || target.phase !== 'waiting' || target.players.length !== 1 || !online(target.players[0].id)) {
          throw new Error('房间不存在或已满')
        }
        target.players.push(actor)
        assigned.set(id, target)
      }
      publishLobby()
      return
    }
    if (!room || room.id !== data.roomId) throw new Error('对局已变更')
    if (data.action === 'leave') {
      leave(id)
      publishLobby()
      return
    }
    if (data.action === 'start') {
      if (room.phase !== 'waiting' || room.players[0].id !== id || room.players.length !== 2 ||
        !room.players.every(p => online(p.id))) throw new Error('请等待双方入房后由房主开始')
      start(room)
      publishLobby()
      return
    }
    expire(room)
    if (data.action !== 'move' || room.phase !== 'playing') throw new Error('当前不能操作')
    if (data.version !== room.game.revision) throw new Error('当前操作已过期')
    const seat = room.players.findIndex(p => p.id === id)
    duelAct(room.game, seat, { type: data.move?.type, tile: data.move?.tile, revision: data.version }, now())
    schedule(room)
    publishRoom(room)
  }

  ns.use(async (socket, next) => {
    try {
      const token = socket.handshake.auth?.token
      const actor = await verify(token)
      if (actor.matchBanned) throw new Error()
      socket.data.actor = actor
      socket.data.rating = (await store.stats(actor.id)).rating
      next()
    } catch { next(new Error('请使用可参与对战的平台账号登录')) }
  })
  ns.on('connection', socket => {
    const actor = socket.data.actor
    const id = actor.id
    socket.join(channel(id))
    ratings.set(id, socket.data.rating)
    clearTimeout(offline.get(id))
    offline.delete(id)
    expire(assigned.get(id))
    if (assigned.has(id)) publishLobby()
    else socket.emit('state', snapshot(id))
    socket.on('sync', () => {
      expire(assigned.get(id))
      socket.emit('state', snapshot(id))
    })
    socket.on('standings', async reply => {
      if (typeof reply !== 'function') return
      try { reply({ ok: true, rows: await store.duelStandings() }) }
      catch { reply({ ok: false, error: '排行榜读取失败' }) }
    })
    socket.on('play', async (data, reply) => {
      if (typeof reply !== 'function') return
      try {
        if (actor.expiresAt && Date.now() >= actor.expiresAt * 1000) throw new Error('登录已过期')
        if (!data || typeof data !== 'object') throw new Error('操作无效')
        if (['create', 'join', 'queue', 'start'].includes(data.action)) {
          const current = await verify(socket.handshake.auth.token)
          if (current.matchBanned) throw new Error('账号当前不能参加对战')
          if (!socket.connected) return
        }
        play(actor, data)
        reply({ ok: true })
      } catch (error) {
        reply({ ok: false, error: error.code ? '服务暂不可用' : error.message })
      }
    })
    socket.on('disconnect', () => {
      if (online(id)) return
      queue.delete(id)
      ratings.delete(id)
      if (!assigned.has(id)) return
      publishLobby()
      offline.set(id, setTimeout(() => {
        offline.delete(id)
        leave(id)
        publishLobby()
      }, reconnectMs).unref())
    })
  })
  return { availableRooms }
}
