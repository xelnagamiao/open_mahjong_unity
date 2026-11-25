import assert from 'node:assert/strict'
import test from 'node:test'
import { once } from 'node:events'
import { createServer } from 'node:http'
import { createRequire } from 'node:module'
import { randomUUID } from 'node:crypto'
import { setTimeout as delay } from 'node:timers/promises'
import { Server } from 'socket.io'
import dotenv from 'dotenv'
import pg from 'pg'
import jwt from '../utils/jwt.js'
import { registerMenchinChallenges } from './index.mjs'
import { menchinStore } from './store.mjs'

const require = createRequire(import.meta.url)
const { io: connect } = createRequire(new URL('../../client/package.json', import.meta.url))('socket.io-client')

async function until(check) {
  for (let i = 0; i < 700; i++) {
    if (check()) return
    await delay(5)
  }
  assert.fail('Timed out waiting for duel state')
}

function testWall(winning) {
  const dealer = [11,11,11,12,12,12,13,13,13,14,14,14,15]
  const other = [19,19,19,18,18,18,17,17,16,16,16,12,15]
  const draw = winning ? 15 : 19
  const rest = Array.from({ length: 36 }, (_, i) => 11 + Math.floor(i / 4))
  for (const tile of [...dealer, ...other, draw]) rest.splice(rest.indexOf(tile), 1)
  return [...dealer, ...other, draw, ...rest]
}

test('two-player Menchin rooms, hidden hands, deadlines, reconnects and Elo', {
  skip: process.env.MENCHIN_TEST_DATABASE !== '1', timeout: 60000,
}, async t => {
  dotenv.config()
  const { db } = require('../config/config')
  assert.ok(['localhost', '127.0.0.1', '::1'].includes(db.host))
  const connection = { host: db.host, port: db.port, database: db.database, user: db.user, password: db.password }
  const admin = new pg.Pool(connection)
  const schema = 'menchin_duel_test_' + randomUUID().replaceAll('-', '')
  await admin.query('CREATE SCHEMA ' + schema)
  const pool = new pg.Pool({ ...connection, options: '-c search_path=' + schema })
  const server = createServer()
  const io = new Server(server, { path: '/api/socket.io' })
  const clients = []
  t.after(async () => {
    clients.forEach(s => s.close())
    await io.close()
    await pool.end()
    await admin.query('DROP SCHEMA ' + schema + ' CASCADE')
    await admin.end()
  })
  await pool.query(`
    CREATE TABLE users (user_id BIGINT PRIMARY KEY, username TEXT, ban_type TEXT, ban_reason TEXT, ban_expires_at TIMESTAMP);
    INSERT INTO users (user_id, username) VALUES (11,'万字甲'), (22,'万字乙');
  `)
  const auth = { audience: 'duel-test', jwtSecret: randomUUID() }
  let offset = 0, hand = 0, alternating = false
  const now = () => Date.now() + offset
  await registerMenchinChallenges(io, pool, auth, { duel: {
    now, reconnectMs: 450, botDelayMs: 15, wallFactory: () => testWall(++hand % 2 === 1 || !alternating),
  } })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const url = 'http://127.0.0.1:' + server.address().port + '/menchin-duel'
  function attach(id, guestKey) {
    const token = id ? jwt.signToken({ user_id: id, aud: auth.audience }, auth.jwtSecret, 180) : ''
    const socket = connect(url, { path: '/api/socket.io', auth: { token, guestKey }, autoConnect: false, reconnection: false, forceNew: true })
    const c = {
      socket, play: (action, data = {}) => socket.timeout(2000).emitWithAck('play', { action, roomId: c.state?.room?.id, ...data }),
      move: move => c.play('move', { version: c.state.room.game.revision, move }),
      standings: () => socket.timeout(2000).emitWithAck('standings'),
    }
    socket.on('state', state => { c.state = state })
    clients.push(socket)
    socket.connect()
    return c
  }
  const guest = attach(null)
  const [denied] = await once(guest.socket, 'connect_error')
  assert.match(denied.message, /登录/)
  const a = attach(11)
  let b = attach(22)
  await until(() => a.state && b.state)
  const ok = async promise => assert.deepEqual(await promise, { ok: true })
  const declined = async promise => assert.equal((await promise).ok, false)
  async function expire(c) {
    const before = c.state.room.game
    offset += Math.max(0, before.deadline - now()) + 1
    c.socket.emit('sync')
    await until(() => c.state.room.game.revision > before.revision)
  }
  async function match() {
    await ok(a.play('queue'))
    await ok(b.play('queue'))
    await until(() => !!a.state.room?.game && !!b.state.room?.game)
  }

  const home = connect('http://127.0.0.1:' + server.address().port + '/menchin', {
    path: '/api/socket.io', forceNew: true, autoConnect: false,
  })
  clients.push(home)
  home.connect()
  await once(home, 'connect')

  await t.test('guest practice, automatic bot turns, reclaiming a seat, rematch and isolation from Elo', async () => {
    const guestKey = randomUUID().replaceAll('-', '')
    let practice = attach(null, guestKey)
    const outsider = attach(null, randomUUID().replaceAll('-', ''))
    await until(() => practice.state && outsider.state)
    for (const action of ['create', 'queue', 'join', 'start', 'cancel']) await declined(practice.play(action))
    await declined(practice.standings())
    await ok(practice.play('bot'))
    const original = practice.state.room
    assert.ok(original.bot && original.players[1].bot && original.players[1].online)
    assert.equal(original.ranked, false)
    assert.equal(practice.state.rating, null)
    assert.ok(!JSON.stringify(original).includes(guestKey))
    await declined(practice.play('bot'))
    await declined(outsider.play('leave', { roomId: original.id }))
    await declined(b.play('join', { code: original.code }))
    assert.deepEqual(await home.timeout(2000).emitWithAck('rooms'), [])
    practice.socket.close()
    practice = attach(null, guestKey)
    await until(() => practice.state?.room)
    assert.equal(practice.state.room.id, original.id)
    assert.deepEqual(practice.state.room.game.hand, original.game.hand)
    for (let round = 1; round <= 20; round++) {
      await until(() => practice.state.room.game.round === round)
      if (round % 2) await ok(practice.move({ type: 'win' }))
      await until(() => !!practice.state.room.game.result)
      assert.equal(practice.state.room.game.result.reason, 'tsumo')
      if (round < 20) await expire(practice)
    }
    assert.equal(practice.state.room.phase, 'finished')
    assert.deepEqual(practice.state.room.game.scores, [10, 10])
    assert.equal(practice.state.room.saving, '')
    await ok(practice.play('bot'))
    assert.notEqual(practice.state.room.id, original.id)
    assert.equal(practice.state.room.game.round, 1)
    await declined(practice.play('leave', { roomId: original.id }))
    practice.socket.close()
    await delay(550)
    practice = attach(null, guestKey)
    await until(() => practice.state)
    assert.equal(practice.state.room, null)
    await ok(a.play('queue'))
    await ok(a.play('bot'))
    assert.ok(!a.state.queued && a.state.room.bot)
    await ok(b.play('queue'))
    assert.ok(b.state.queued && !b.state.room)
    await ok(b.play('cancel'))
    await ok(a.play('leave'))
    assert.deepEqual((await a.standings()).rows, [])
    assert.equal((await pool.query('SELECT COUNT(*) FROM menchin_results')).rows[0].count, '0')
    assert.equal((await pool.query('SELECT COUNT(*) FROM menchin_players')).rows[0].count, '0')
    practice.socket.close()
    outsider.socket.close()
  })

  await t.test('guest home lists open rooms on load and follows seats, disconnects and closure', async () => {
    const readRooms = () => home.timeout(2000).emitWithAck('rooms')
    assert.deepEqual(await readRooms(), [])
    let rooms
    home.on('rooms-changed', list => { rooms = list })
    await ok(a.play('create'))
    await until(() => rooms?.length === 1)
    const listing = [{ code: a.state.room.code, host: '万字甲' }]
    assert.deepEqual(rooms, listing)

    home.disconnect().connect()
    await once(home, 'connect')
    assert.deepEqual(await readRooms(), listing)
    a.socket.disconnect()
    await until(() => rooms.length === 0)
    assert.deepEqual(await readRooms(), [])
    a.socket.connect()
    await until(() => rooms.length === 1)
    assert.deepEqual(rooms, listing)

    await ok(b.play('join', { code: listing[0].code }))
    await until(() => rooms.length === 0)
    await ok(b.play('leave'))
    await until(() => rooms.length === 1)
    assert.deepEqual(rooms, listing)
    await ok(a.play('leave'))
    await until(() => rooms.length === 0)
    home.off('rooms-changed')
  })

  await t.test('room owner, private views, automatic discard/pass and two independent connections', async () => {
    await ok(a.play('create'))
    await until(() => b.state.rooms.length)
    assert.deepEqual(b.state.rooms, [{ code: a.state.room.code, host: '万字甲' }])
    await ok(b.play('join', { code: a.state.room.code }))
    await declined(b.play('start'))
    await ok(a.play('start'))
    await until(() => b.state.room.game)
    const old = a.state.room.game
    assert.equal(old.hand.length, 14)
    assert.equal(b.state.room.game.hand.length, 13)
    assert.equal(b.state.room.game.drawn, null)
    assert.equal(old.wallCount, 9)
    assert.ok(!('hands' in old || 'wall' in old))
    assert.deepEqual(b.state.room.game.actions, [])
    await declined(b.move({ type: 'win' }))
    await expire(a)
    await until(() => b.state.room.game.phase === 'claim')
    assert.equal(b.state.room.game.rivers[0][0].tile, old.drawn)
    assert.ok(b.state.room.game.actions.some(move => move.type === 'win'))
    const claimMs = b.state.room.game.deadline - b.state.serverNow
    assert.ok(claimMs > 4900 && claimMs <= 5000)
    await declined(a.play('move', { version: old.revision, move: { type: 'discard', tile: old.drawn } }))
    await expire(b)
    assert.equal(b.state.room.game.phase, 'discard')
    assert.equal(b.state.room.game.wallCount, 8)
    const before = b.state.room.game
    b.socket.close()
    await until(() => a.state.room.players[1].online === false)
    b = attach(22)
    await until(() => b.state?.room?.game)
    assert.deepEqual(b.state.room.game.hand, before.hand)
    assert.equal(b.state.room.game.deadline, before.deadline)
    await ok(b.play('leave'))
    await until(() => a.state.room.phase === 'finished')
    assert.equal(a.state.room.winner, 0)
    assert.deepEqual(a.state.room.game.actions, [])
    assert.equal(a.state.room.game.deadline, null)
    assert.deepEqual((await a.standings()).rows, [])
    await ok(a.play('leave'))
  })

  async function twentyHands(winAll) {
    hand = 0
    alternating = winAll
    await match()
    for (let round = 1; round <= 20; round++) {
      const dealer = round % 2 ? a : b
      await until(() => dealer.state.room.game.round === round)
      assert.equal(dealer.state.room.game.dealer, (round - 1) % 2)
      await ok(dealer.move({ type: 'win' }))
      if (round < 20) await expire(dealer)
    }
    await until(() => a.state.room.phase === 'finished' && b.state.room.phase === 'finished')
  }
  await t.test('twenty alternating wins tie without Elo; accepting a queue twice cannot match a player against itself', async () => {
    await ok(a.play('queue'))
    await declined(a.play('queue'))
    await ok(a.play('cancel'))
    await twentyHands(false)
    assert.deepEqual(a.state.room.game.scores, [10,10])
    assert.equal(a.state.room.winner, null)
    assert.deepEqual((await a.standings()).rows, [])
    await ok(a.play('leave'))
    await ok(b.play('leave'))
  })
  await t.test('twenty rounds of wins and false wins settle one rated result; reconnect grace expires once', async () => {
    const homeScores = () => home.timeout(2000).emitWithAck('scores')
    assert.deepEqual((await homeScores()).boards, { challenge: [], duel: [] })
    let receivedUpdate = false
    home.once('scores-changed', () => { receivedUpdate = true })
    await twentyHands(true)
    await until(() => a.state.room.saving === 'saved')
    assert.deepEqual(a.state.room.game.scores, [20,0])
    assert.equal(a.state.room.winner, 0)
    assert.equal(a.state.room.game.result.reason, 'falseWin')
    assert.deepEqual(a.state.room.changes, { 11: { rating: 1516, delta: 16 }, 22: { rating: 1484, delta: -16 } })
    const replayed = await menchinStore(pool).settle({
      mode: 'ranked', id: a.state.room.id, players: a.state.room.players, winner: '11', finishedAt: Date.now(),
    })
    assert.deepEqual(replayed, a.state.room.changes)
    const rows = (await b.standings()).rows
    assert.deepEqual(rows.map(p => [p.score, p.matches, p.wins]), [[1516,1,1],[1484,1,0]])
    await until(() => receivedUpdate)
    const homeResult = await homeScores()
    assert.equal(homeResult.profile, null)
    assert.deepEqual(homeResult.boards, { challenge: [], duel: rows })
    await ok(a.play('leave'))
    await ok(b.play('leave'))
    await match()
    b.socket.close()
    await until(() => a.state.room.phase === 'finished' && a.state.room.saving === 'saved')
    assert.equal(a.state.room.winner, 0)
    assert.equal(a.state.room.forfeit, 1)
    await ok(a.play('leave'))
  })
})
