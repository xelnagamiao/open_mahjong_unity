import assert from 'node:assert/strict'
import test from 'node:test'
import { createRequire } from 'node:module'
import { once } from 'node:events'
import { randomUUID } from 'node:crypto'
import { createServer } from 'node:http'
import { setTimeout as delay } from 'node:timers/promises'
import { Server } from 'socket.io'
import pg from 'pg'
import dotenv from 'dotenv'
import jwt from '../utils/jwt.js'
import { registerMenchinChallenges } from './index.mjs'
import { menchinStore } from './store.mjs'
import { candidates } from '../../shared/menchin.mjs'
import { solveHand } from '../../client/src/utils/scoring/handShapes.ts'

const require = createRequire(import.meta.url)
const clientRequire = createRequire(new URL('../../client/package.json', import.meta.url))
const { io: connect } = clientRequire('socket.io-client')
const expected = hand => candidates(hand).filter(tile => hand.filter(t => t === tile).length < 4 &&
  solveHand([...hand, tile], [], { riichiSevenPairs: true }).length)

async function until(condition, timeoutMs = 3500) {
  const deadline = Date.now() + timeoutMs
  while (!condition()) {
    if (Date.now() > deadline) assert.fail('Timed out waiting for server state')
    await delay(5)
  }
}

test('Menchin challenge lifecycle and ranking with Socket.IO and PostgreSQL', {
  skip: process.env.MENCHIN_TEST_DATABASE !== '1', timeout: 90000,
}, async t => {
  dotenv.config()
  const { db } = require('../config/config')
  assert.ok(['localhost', '127.0.0.1', '::1'].includes(db.host))
  const connection = { host: db.host, port: db.port, database: db.database, user: db.user, password: db.password }
  const admin = new pg.Pool(connection)
  const schema = 'menchin_test_' + randomUUID().replaceAll('-', '')
  await admin.query('CREATE SCHEMA ' + schema)
  const pool = new pg.Pool({ ...connection, options: '-c search_path=' + schema })
  const server = createServer()
  const io = new Server(server, { path: '/api/socket.io' })
  const auth = { audience: 'menchin-test', jwtSecret: randomUUID() }
  const clients = []
  t.after(async () => {
    clients.forEach(c => c.socket.close())
    await io.close()
    await pool.end()
    await admin.query('DROP SCHEMA ' + schema + ' CASCADE')
    await admin.end()
  })
  await pool.query(`
    CREATE TABLE users (user_id BIGINT PRIMARY KEY, username TEXT, ban_type TEXT, ban_reason TEXT, ban_expires_at TIMESTAMP);
    INSERT INTO users (user_id, username) VALUES (1,'测试甲'), (2,'测试乙');
  `)
  await registerMenchinChallenges(io, pool, auth, { openingMs: 20, reviewMs: 90, reconnectMs: 1500 })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const url = 'http://127.0.0.1:' + server.address().port + '/menchin'

  async function client(id) {
    const token = id ? jwt.signToken({ user_id: id, aud: auth.audience }, auth.jwtSecret, 300) : ''
    const socket = connect(url, { path: '/api/socket.io', auth: { token }, autoConnect: false, forceNew: true, reconnection: false })
    const c = {
      socket,
      play: (action, payload = {}) => socket.timeout(3000).emitWithAck('play', {
        action, gameId: c.state?.game?.id, round: c.state?.game?.round, ...payload,
      }),
      scores: () => socket.timeout(3000).emitWithAck('scores'),
    }
    socket.on('state', state => { c.state = state })
    clients.push(c)
    socket.connect()
    await until(() => c.state)
    return c
  }

  const guest = await client()
  let a = await client(1)
  const b = await client(2)
  const ok = async (c, name, data) => assert.deepEqual(await c.play(name, data), { ok: true })
  const decline = async (c, name, data) => assert.equal((await c.play(name, data)).ok, false)
  const key = () => randomUUID().replaceAll('-', '')
  const start = (c, level, requestId = key()) => ok(c, 'challenge', { level, requestId })

  await t.test('only the two single-player modes are available; guests play all 50 hands without a public score', async () => {
    assert.deepEqual((await guest.scores()).boards, { challenge: [], duel: [] })
    for (const action of ['create', 'queue', 'join', 'solo']) await decline(a, action)
    await decline(guest, 'challenge', { level: 5, requestId: key() })
    await start(guest, 4)
    assert.equal(guest.state.game.limit, 50)
    for (let n = 1; n <= 50; n++) {
      const game = guest.state.game
      assert.equal(game.round, n)
      assert.equal(game.phase, 'playing')
      assert.equal(game.waits, null)
      assert.equal(game.endsAt - game.opensAt, 20000)
      const tiles = expected(game.hand)
      assert.ok(tiles.length >= 3)
      await ok(guest, 'answer', { tiles })
      if (n < 50) await decline(guest, 'answer', { round: n, tiles })
    }
    const done = guest.state.game
    assert.equal(done.phase, 'finished')
    assert.equal(done.endReason, 'complete')
    assert.equal(done.players[0].correct, 50)
    assert.ok(done.players[0].points > 0 && done.players[0].points <= 30000)
    assert.deepEqual((await guest.scores()).boards, { challenge: [], duel: [] })
  })

  await t.test('abandonment is scoped to its run; leaving and restarting never locks the player or records abandoned points', async () => {
    const previous = key()
    await start(a, 1, previous)
    const id = a.state.game.id
    await start(a, 1, previous)
    assert.equal(a.state.game.id, id)
    await ok(a, 'answer', { tiles: expected(a.state.game.hand) })
    await ok(a, 'abandon', { requestId: previous })
    assert.equal(a.state.game, null)
    const next = key()
    await start(a, 2, next)
    await ok(a, 'abandon', { requestId: previous })
    assert.equal(a.state.game.requestId, next)
    await ok(a, 'abandon', { requestId: next })
    assert.equal((await a.scores()).profile.answered, 0)
    assert.equal((await a.scores()).boards.challenge.length, 0)
  })

  await t.test('reconnect restores the hand; mistakes settle once and only comparable challenges enter the daily ranking', async () => {
    await pool.query('INSERT INTO menchin_results (game_id,finished_at,solo_player,solo_score) VALUES ($1,NOW(),2,99000)', [randomUUID()])
    await start(a, 3)
    const initial = a.state.game
    a.socket.close()
    a = await client(1)
    assert.deepEqual(a.state.game.hand, initial.hand)
    assert.equal(a.state.game.endsAt, initial.endsAt)
    await decline(a, 'answer', { tiles: [] })
    await ok(a, 'answer', { tiles: expected(a.state.game.hand) })
    const earned = a.state.game.lastPoints
    assert.ok(earned > 0 && earned <= 400 && earned % 2 === 0)
    const waits = expected(a.state.game.hand)
    await ok(a, 'answer', { tiles: waits.slice(0, 1) })
    await until(() => a.state.game.saving === 'saved')
    const done = a.state.game
    assert.equal(done.endReason, 'wrong')
    assert.deepEqual(done.waits, waits)
    assert.equal(done.round, 2)
    await menchinStore(pool).settle({ ...done, finishedAt: Date.now() })
    const { profile, boards } = await a.scores()
    assert.equal(profile.answered, 2)
    assert.equal(profile.correct, 1)
    assert.equal(profile.points, earned)
    assert.deepEqual(boards.challenge, [{ id: '1', name: '测试甲', score: earned, level: 3 }])
    await pool.query("UPDATE menchin_results SET finished_at=NOW()-interval '2 days' WHERE game_id=$1", [done.id])
    assert.deepEqual((await b.scores()).boards.challenge, [])
  })

  await t.test('a finished run can restart immediately and the server settles a real 20-second deadline', async () => {
    await start(a, 1)
    const id = a.state.game.id
    await until(() => a.state.game.saving === 'saved', 22500)
    assert.equal(a.state.game.endReason, 'timeout')
    assert.equal(a.state.game.players[0].points, 0)
    assert.equal(a.state.game.players[0].answered, 1)
    await start(a, 2)
    assert.notEqual(a.state.game.id, id)
    await ok(a, 'answer', { tiles: [] })
    assert.equal(a.state.game.players[0].answered, 1)
    await ok(a, 'abandon', { requestId: a.state.game.requestId })
  })
})
