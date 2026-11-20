import assert from 'node:assert/strict'
import test from 'node:test'
import {
  mergeReplayRows,
  parseReplayJSON,
  selectCachedReplay,
  stringifyReplayJSON,
} from '../src/utils/browserRecordMigration.js'

function detail(id, side) {
  const title = {
    rule: 'sichuan',
    sub_rule: side ? 'sichuan/doubles_duplicate' : 'sichuan/standard',
    p0_uid: side === 'closed' ? 201 : 101,
  }
  if (side)
    Object.assign(title, {
      duplicate_match_id: 'test-match',
      duplicate_table: side,
      team_by_player_index: side === 'open' ? [0, 1, 0, 1] : [1, 0, 1, 0],
    })
  return {
    game_id: id,
    rank: side === 'closed' ? 3 : 1,
    record: {
      game_title: title,
      game_round: {
        round_index_1: { round_index: 1, action_ticks: [['ask_hand', 1], ['end']], action_times_ms: [11] },
      },
      seed: 9223372036854775807n,
    },
  }
}

test('single replay keeps its original identifier, unknown fields, unpadded clock and old node map', () => {
  const { row } = mergeReplayRows([detail('Lsolo')])
  const table = row.record.tables.main
  assert.equal(row.game_id, 'Lsolo')
  assert.equal(row.record.match_format, 'single_table')
  assert.deepEqual(table.payload.game_round.round_index_1.action_ticks, [['end']])
  assert.equal(table.payload.seed, 9223372036854775807n)
  assert.deepEqual(table.transport_actions.round_index_1, [{ node: 1, tick: ['ask_hand', 1], time_ms: 11 }])
  assert.deepEqual(table.source_round_fields.round_index_1, { time_count: 1 })
  assert.deepEqual(selectCachedReplay(row).legacy_cursor_map.node_map.round_index_1, [0, 0, 1])
  assert.deepEqual(parseReplayJSON(stringifyReplayJSON(row)), row)
})

test('two table keys map to one replay and a missing sibling remains explicit', () => {
  const first = detail('Lopen', 'open'),
    second = detail('Lclosed', 'closed')
  const partial = mergeReplayRows([first])
  assert.equal(partial.row.record.completeness, 'partial')
  const merged = mergeReplayRows([partial.row, second])
  const direct = mergeReplayRows([first, second])
  assert.deepEqual(merged.row.record, direct.row.record)
  assert.equal(direct.row.record.match_format, 'paired_tables_2v2')
  assert.equal(direct.row.record.completeness, 'complete')
  assert.equal(direct.aliases.length, 2)
  const alias = direct.aliases.find((a) => a.game_id === 'Lclosed')
  assert.equal(selectCachedReplay(direct.row, alias).rank, 3)
  assert.equal(selectCachedReplay(direct.row, alias).selected_table_id, 'closed')
  assert.throws(() => mergeReplayRows([first, { ...first, record: { ...first.record, seed: 1n } }]), /冲突/)
})

test('transport parser preserves 64-bit integer tokens, numeric-looking strings and object keys', () => {
  const json =
    '{"unsigned":18446744073709551615,"signed":-9223372036854775808,"str":"18446744073709551615","__proto__":{"n":1}}'
  const value = parseReplayJSON(json)
  assert.equal(value.unsigned, 18446744073709551615n)
  assert.equal(value.signed, -9223372036854775808n)
  assert.equal(value.str, '18446744073709551615')
  assert.equal(value.n, undefined)
  assert.equal(value.__proto__.n, 1)
  assert.deepEqual(parseReplayJSON(stringifyReplayJSON(value)), value)
})

// A browser exposes IndexedDB directly. A standalone Node run can provide its
// IndexedDB implementation with --import fake-indexeddb/auto.
test(
  'IndexedDB upgrades both v1 caches atomically and isolates failed or conflicting migrations',
  { skip: !globalThis.indexedDB },
  async () => {
    const { getLocalRecords, getLocalRecordIdSet, listCachedPlayers, putLocalRecords } = await import(
      '../src/utils/recordLocalStore.js'
    )
    const { getLocalGameRecord, listLocalGameRecords } = await import('../src/utils/localGameRecordStore.js')
    const { openReplayDatabase } = await import('../src/utils/browserRecordDatabase.js')
    function seed(name, rows, withIndex) {
      return new Promise((resolve, reject) => {
        const req = indexedDB.open(name, 1)
        req.onupgradeneeded = () => {
          const db = req.result,
            store = db.createObjectStore('records', { keyPath: 'game_id' })
          for (const row of rows) store.put(row)
          if (withIndex)
            db.createObjectStore('meta').put(
              rows.map(({ game_id, rank }) => ({ game_id, rank })),
              'index',
            )
        }
        req.onerror = () => reject(req.error)
        req.onsuccess = () => {
          req.result.close()
          resolve()
        }
      })
    }
    const rows = [detail('Lopen', 'open'), detail('Lclosed', 'closed')]
    for (const name of ['om.localGameRecords', 'om.recordCache']) await seed(name, rows, true)
    const [open, closed] = await getLocalRecords(['Lopen', 'Lclosed'])
    assert.equal(open.game_id, closed.game_id)
    assert.equal(closed.selected_table_id, 'closed')
    assert.equal(closed.rank, 3)
    assert.equal(await getLocalGameRecord('Lclosed').then((d) => d.game_id), open.game_id)
    assert.deepEqual(
      await getLocalRecordIdSet([open.game_id, 'Lopen', 'Lclosed', 'absent']),
      new Set([open.game_id, 'Lopen', 'Lclosed']),
    )
    assert.equal((await listLocalGameRecords()).length, 1)
    assert.equal((await listCachedPlayers()).length, 2)
    await Promise.all([putLocalRecords([detail('La')]), putLocalRecords([detail('Lb')])])
    assert.equal((await getLocalRecords(['La', 'Lb', open.game_id])).length, 3)
    await assert.rejects(putLocalRecords([{ ...rows[0], record: { ...rows[0].record, seed: 17 } }]), /校验|冲突/)
    assert.equal((await getLocalRecords(['Lopen']))[0].record.tables.open.payload.seed, 9223372036854775807n)
    // A published sibling can identify an older raw side whose header lacks the pair keys.
    const oldClosed = structuredClone(rows[1])
    delete oldClosed.record.game_title.duplicate_match_id
    delete oldClosed.record.game_title.duplicate_table
    const oldArchive = structuredClone(open.record)
    const { sha256Hex } = await import('../src/utils/seedVerify.js')
    oldArchive.tables.closed.source_sha256 = sha256Hex(stringifyReplayJSON(oldClosed.record))
    delete oldArchive.tables.closed.transport_actions
    delete oldArchive.tables.closed.source_round_fields
    await seed('test.embedded', [{ ...rows[0], match_record: oldArchive }, oldClosed], false)
    const archiveDb = await openReplayDatabase('test.embedded')
    const corrected = await new Promise((resolve, reject) => {
      const req = archiveDb.transaction('replays').objectStore('replays').get(open.game_id)
      req.onsuccess = () => resolve(req.result.record.tables.closed)
      req.onerror = () => reject(req.error)
    })
    archiveDb.close()
    assert.deepEqual(corrected.source_omitted_title_fields, ['duplicate_match_id', 'duplicate_table'])
    assert.deepEqual(corrected.transport_actions.round_index_1[0].tick, ['ask_hand', 1])
    await seed('test.corrupt', [{ game_id: 'invalid', record: { unknown: true } }], false)
    await assert.rejects(openReplayDatabase('test.corrupt'), /未知/)
    await new Promise((resolve, reject) => {
      const req = indexedDB.open('test.corrupt', 1)
      req.onsuccess = () => {
        const db = req.result,
          read = db.transaction('records').objectStore('records').get('invalid')
        read.onsuccess = () => {
          assert.deepEqual(read.result.record, { unknown: true })
          db.close()
          resolve()
        }
        read.onerror = () => reject(read.error)
      }
      req.onerror = () => reject(req.error)
    })
  },
)
