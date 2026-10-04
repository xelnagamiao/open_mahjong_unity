import test from 'node:test'
import assert from 'node:assert/strict'
import { salasasaClient as client } from '../src/game2d/salasasa/client.ts'

test('退房保留对局；当前对局关闭才清空重连状态，迟到的旧局通知不影响新局', async () => {
  const originalWindow = globalThis.window
  const originalSocket = globalThis.WebSocket
  let socket
  class TestSocket {
    static OPEN = 1
    readyState = 1
    send() {}
    close() { this.readyState = 3; this.onclose?.({ code: 1000 }) }
    constructor() { socket = this }
    receive(message) { this.onmessage({ data: JSON.stringify(message) }) }
  }
  globalThis.WebSocket = TestSocket
  globalThis.window = {
    location: { origin: 'http://localhost' },
    setTimeout, clearTimeout, setInterval, clearInterval,
  }
  const delivered = []
  const unsubscribe = client.subscribe(message => delivered.push(message))
  try {
    const connecting = client.connect('test-player', 'test-only')
    socket.onopen()
    socket.receive({ type: 'login', success: true, login_info: { user_id: 101, username: 'test-player' } })
    await connecting
    const start = { type: 'gamestate/guobiao/game_start', game_info: { room_id: 7, gamestate_id: 'current-game' } }
    socket.receive(start)
    socket.receive({ type: 'room/leave_room_done', success: true, room_id: '7', room_instance_id: 'removed-lobby' })
    assert.deepEqual(client.lastGameStart, start)
    assert.equal(client.isLoggedIn, true)

    const vote = { type: 'gamestate/vote_update', vote_info: { phase: 'end' } }
    socket.receive(vote)
    const count = delivered.length
    socket.receive({ type: 'gamestate/closed', gamestate_id: 'older-game' })
    socket.receive({ type: 'gamestate/vote_end', gamestate_id: 'older-game' })
    assert.equal(delivered.length, count)
    assert.deepEqual(client.lastVoteUpdate, vote)
    assert.deepEqual(client.lastGameStart, start)

    socket.receive({ type: 'message', message: 'reconnect_ask' })
    assert.equal(client.hasReconnectOffer, true)
    socket.receive({ type: 'gamestate/closed', gamestate_id: 'current-game', reason: 'all_humans_offline' })
    assert.equal(client.lastGameStart, null)
    assert.equal(client.lastVoteUpdate, null)
    assert.equal(client.hasReconnectOffer, false)
    assert.deepEqual(client.drainGuobiaoBuffer(), [])
    assert.equal(client.isLoggedIn, true)
    assert.equal(delivered.at(-1).type, 'gamestate/closed')
  } finally {
    unsubscribe()
    client.logout()
    globalThis.window = originalWindow
    globalThis.WebSocket = originalSocket
  }
})
