import test from 'node:test'
import assert from 'node:assert/strict'
import { groupEventRoomCards } from '../src/utils/sichuanDuplicate.js'

test('an eight-player lobby is one room card, with no duplicate open/closed entries', () => {
  const room = { room_id: '10', duplicate_match_id: 'pair', duplicate_lobby: { version: 1, waiting: [101, 102] } }
  const groups = groupEventRoomCards([{ room }])
  assert.equal(groups.length, 1)
  assert.equal(groups[0].paired, false)
  assert.equal(groups[0].cards.length, 1)
})

test('separate eight-player lobbies remain separate even with identical names', () => {
  const cards = [1, 2].map(id => ({ room: { room_id: String(id), room_name: '复式', duplicate_match_id: `p${id}`, duplicate_lobby: { version: 1 } } }))
  assert.equal(groupEventRoomCards(cards).length, 2)
})

test('legacy paired rooms retain their two-table grouping', () => {
  const cards = ['closed', 'open'].map(table => ({ room: { room_id: table, duplicate_match_id: 'old', duplicate_table: table } }))
  const groups = groupEventRoomCards(cards)
  assert.equal(groups.length, 1)
  assert.equal(groups[0].paired, true)
  assert.deepEqual(groups[0].cards.map(c => c.room.duplicate_table), ['open', 'closed'])
})
