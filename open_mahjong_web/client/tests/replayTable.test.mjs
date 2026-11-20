import test from 'node:test'
import assert from 'node:assert/strict'
import { replayTableView, replayLinkNode } from '../src/utils/replayTable.js'
import { replaySharePath, parseRecordShareInput } from '../src/utils/recordShareLink.js'

const table = (side, uid) => ({
  table_id: side, table_record_id: 'id' + side,
  players: [{ user_id: uid, username: side, original_player_index: 0 }],
  payload: { game_title: { p0_uid: uid }, game_round: {
    round_index_1: { action_ticks: [['d', 11], ['c', 11]], action_times_ms: [30, null] },
  } },
  transport_actions: { round_index_1: [{ node: 1, tick: ['ask_hand'], time_ms: 20 }] },
  source_round_fields: { round_index_1: { time_count: 2 } },
  round_meta: { round_index_1: { legacy_node_map: [0, 0, 1, 2] } },
})
const record = { format: 'open_mahjong.match_record', tables: { closed: table('closed', 202), open: table('open',101) } }
const detail = { game_id: 'P1', record, selected_table_id: 'closed' }

test('old table URLs preserve their seat and remap once, and new URLs retain their exact position', () => {
  assert.equal(replayTableView(detail).table_record_id, 'idclosed')
  const view = replayTableView(detail, { tableId: 'open' })
  const meta = view.round_meta.round_index_1
  assert.equal(replayLinkNode(2, meta, false), 1)
  const url = replaySharePath('3d-node', view.game_id, 1, 1, 101, 0, 'open')
  const parsed = parseRecordShareInput(url)
  assert.equal(parsed.table, 'open')
  assert.equal(replayLinkNode(parsed.node, meta, parsed.cursor === 'v2'), 1)
})

test('analysis selects the requested player and recovers original prompts and timestamps without changing the download', () => {
  const view = replayTableView(detail, { userId: 101, sourceActions: true })
  assert.equal(view.selected_table_id, 'open')
  assert.deepEqual(view.record.game_round.round_index_1.action_ticks, [['ask_hand'], ['d', 11], ['c', 11]])
  assert.deepEqual(view.record.game_round.round_index_1.action_times_ms, [20,30])
  assert.deepEqual(record.tables.open.payload.game_round.round_index_1.action_ticks, [['d', 11], ['c', 11]])
  assert.throws(() => replayTableView(detail, { tableId: 'main' }))
})
