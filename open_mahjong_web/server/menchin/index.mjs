import { randomUUID } from 'node:crypto'
import jwt from '../utils/jwt.js'
import bans from '../utils/accountBans.js'
import { createArena } from './arena.mjs'
import { menchinStore } from './store.mjs'
import { registerDuelRooms } from './duelRooms.mjs'

export async function registerMenchinChallenges(io, pool, authConfig, timing) {
  const store = menchinStore(pool)
  await store.initialize()
  const ns = io.of('/menchin')
  const channel = id => 'player:' + id
  const scoresChanged = () => ns.emit('scores-changed')
  const arena = createArena({
    store, ...timing,
    notify: (id, state) => ns.to(channel(id)).emit('state', state),
    lobbyChanged: lobby => ns.emit('lobby', lobby),
    scoresChanged,
  })

  async function account(id) {
    const { rows } = await pool.query(`SELECT username, ${bans.BAN_COLUMNS} FROM users WHERE user_id=$1`, [id])
    if (!rows.length) throw new Error('账号不存在')
    const denied = bans.loginBanMessage(rows[0])
    if (denied) throw new Error(denied)
    return { id, name: rows[0].username, guest: false, matchBanned: rows[0].active_ban_type === 'match' }
  }

  async function verifyPlayer(token) {
    const claim = jwt.verifyToken(token, authConfig.jwtSecret)
    if (claim?.aud !== authConfig.audience || !Number.isSafeInteger(Number(claim.user_id)) || Number(claim.user_id) <= 0) {
      throw new Error('无法验证账号，请重新登录')
    }
    return { ...await account(String(claim.user_id)), expiresAt: claim.exp }
  }
  const duel = registerDuelRooms(io.of('/menchin-duel'), store, verifyPlayer, {
    ...timing?.duel, scoresChanged,
    roomsChanged: rooms => ns.emit('rooms-changed', rooms),
  })

  ns.use(async (socket, next) => {
    const { token, guestKey } = socket.handshake.auth || {}
    if (!token) {
      const key = typeof guestKey === 'string' && /^[a-f0-9]{32}$/.test(guestKey) ? guestKey : randomUUID()
      socket.data.actor = { id: 'guest:' + key, name: '游客', guest: true }
      return next()
    }
    try {
      socket.data.actor = await verifyPlayer(token)
      socket.data.expiresAt = socket.data.actor.expiresAt
      next()
    } catch {
      next(new Error('无法验证账号，请重新登录'))
    }
  })

  ns.on('connection', socket => {
    const actor = socket.data.actor
    socket.join(channel(actor.id))
    arena.reconnect(actor.id)
    function sync() {
      socket.emit('state', arena.state(actor.id))
    }
    sync()
    socket.on('sync', sync)
    socket.on('rooms', reply => {
      if (typeof reply === 'function') reply(duel.availableRooms())
    })
    socket.on('scores', async reply => {
      if (typeof reply !== 'function') return
      try {
        const [boards, profile] = await Promise.all([store.boards(), actor.guest ? null : store.stats(actor.id)])
        reply({ ok: true, boards, profile })
      } catch {
        reply({ ok: false, error: '成绩读取失败' })
      }
    })
    socket.on('play', async (data, reply) => {
      if (typeof reply !== 'function') return
      try {
        if (socket.data.expiresAt && Date.now() >= socket.data.expiresAt * 1000) throw new Error('登录已过期')
        if (!['challenge', 'answer', 'abandon'].includes(data?.action)) throw new Error('此模式尚未开放')
        await arena.act(actor, data)
        reply({ ok: true })
      } catch (error) {
        reply({ ok: false, error: error.code ? '服务暂不可用' : error.message })
      }
    })
    socket.on('disconnect', () => {
      if (!ns.adapter.rooms.has(channel(actor.id))) arena.disconnect(actor.id)
    })
  })
}
