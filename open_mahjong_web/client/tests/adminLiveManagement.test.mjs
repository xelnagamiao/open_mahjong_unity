import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { filterAdminGames, gameVenueOptions } from '../src/utils/adminGameFilters.js'
import { roomSeatItems, roomStateLabel } from '../src/utils/adminRoomLayout.js'

const games = [
  { gamestate_id: 'lobby-a', room_id: '123456', vote_phase: 'idle', players: [{ user_id: 1234, username: 'Alpha' }] },
  { gamestate_id: 'event-a1', event_id: 'event-a', event_name: '春季赛', venue_kind: 'event', vote_phase: 'paused', players: [] },
  { gamestate_id: 'event-a2', event_id: 'event-a', event_name: '春季赛', venue_kind: 'event', vote_phase: 'pause_pending', players: [] },
  { gamestate_id: 'event-b1', event_id: 'event-b', event_name: '夏季赛', venue_kind: 'event', vote_phase: 'voting', players: [] },
  { gamestate_id: 'base-a1', event_id: 'base-a', event_name: '牌友基地', venue_kind: 'base', vote_phase: 'resume_voting', players: [{ user_id: 999, username: '测试玩家' }] },
  { gamestate_id: 'base-a2', event_id: 'base-a', venue_kind: 'base', vote_phase: 'end_countdown', players: [] },
]
const ids = rows => rows.map(row => row.gamestate_id)

test('scope and venue filters separate lobby, events and bases', () => {
  assert.deepEqual(ids(filterAdminGames(games, { scope: 'lobby' })), ['lobby-a'])
  assert.equal(filterAdminGames(games, { scope: 'event' }).length, 3)
  assert.equal(filterAdminGames(games, { scope: 'base' }).length, 2)
  assert.equal(filterAdminGames(games, { scope: 'all' }).length, 6)
  assert.deepEqual(ids(filterAdminGames(games, { scope: 'event', venueId: 'event-a' })), ['event-a1', 'event-a2'])
  assert.equal(filterAdminGames(games, { scope: 'base', venueId: 'event-a' }).length, 0)
  assert.deepEqual(gameVenueOptions(games, 'event').map(venue => venue.event_id).sort(), ['event-a', 'event-b'])
  assert.deepEqual(gameVenueOptions(games, 'base').map(venue => venue.event_id), ['base-a'])
})

test('keywords search game IDs, room IDs, names and numeric player IDs', () => {
  for (const keyword of [' lobby-A ', '123456', 'alpha', '1234']) {
    assert.deepEqual(ids(filterAdminGames(games, { keyword })), ['lobby-a'])
  }
  assert.deepEqual(ids(filterAdminGames(games, { scope: 'base', keyword: '测试玩家' })), ['base-a1'])
  assert.deepEqual(ids(filterAdminGames(games, { keyword: '春季赛' })), ['event-a1', 'event-a2'])
})

test('phase filtering includes pending pauses and resume votes without counting ending games as running', () => {
  assert.deepEqual(ids(filterAdminGames(games, { status: 'paused' })), ['event-a1', 'event-a2', 'base-a1'])
  assert.deepEqual(ids(filterAdminGames(games, { status: 'voting' })), ['event-b1', 'base-a1'])
  assert.deepEqual(ids(filterAdminGames(games, { status: 'running' })), ['lobby-a', 'event-b1'])
  assert.deepEqual(ids(filterAdminGames(games, { status: 'ending' })), ['base-a2'])
  assert.deepEqual(ids(filterAdminGames([{ gamestate_id: 'old' }], { status: 'running' })), ['old'])
})

test('compact room seats preserve empty positions and exclude waiting players', () => {
  const room = { max_player: 4, players: [{ user_id: 100, seat_index: 2 }, { user_id: 200, seat_index: 0 }, { user_id: 300, waiting: true, seat_index: null }] }
  const seats = roomSeatItems(room)
  assert.deepEqual(seats.map(seat => seat.player?.user_id), [200, undefined, 100, undefined])
  assert.equal(roomSeatItems({ max_player: 3 }).length, 3)
  const duplicate = roomSeatItems({ max_player: 4, duplicate_lobby: { version: 1 }, players: room.players })
  assert.equal(duplicate.length, 8)
  assert.deepEqual(duplicate.map(seat => seat.label), ['闭东', '闭南', '闭西', '闭北', '开东', '开南', '开西', '开北'])
})

test('room status keeps paired-match intervention and seating states visible', () => {
  assert.equal(roomStateLabel({ is_game_running: true }), '对局中')
  assert.equal(roomStateLabel({ event_seating_pending: true }), '组桌中')
  assert.equal(roomStateLabel({ duplicate_status: 'interrupted' }), '待赛务处理')
  assert.equal(roomStateLabel({ duplicate_status: 'ready' }), '等待对室')
  assert.equal(roomStateLabel({ duplicate_status: 'completed' }), '已结束')
})

test('admin game and compact room pages compile with their shared components', () => {
  for (const filename of ['views/admin/GameControl.vue', 'views/admin/RoomControl.vue', 'components/AdminRoomRow.vue', 'components/EventGameControl.vue']) {
    const { descriptor, errors } = parse(fs.readFileSync(new URL(`../src/${filename}`, import.meta.url), 'utf8'))
    assert.deepEqual(errors, [])
    const script = compileScript(descriptor, { id: 'admin-live-management' })
    const result = compileTemplate({ source: descriptor.template.content, filename, id: 'admin-live-management', compilerOptions: { bindingMetadata: script.bindings } })
    assert.deepEqual(result.errors, [])
  }
})
