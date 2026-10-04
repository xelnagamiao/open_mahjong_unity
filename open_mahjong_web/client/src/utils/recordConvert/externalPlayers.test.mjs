import assert from 'node:assert/strict'
import { tziakchaToSalasasa, salasasaToTziakcha } from './tziakchaGuobiao.js'
import { mjaiToSalasasaRecord } from './mjaiRiichi.js'
import { isExternalRecord, externalPlayerName } from './externalPlayers.js'

const names = ['雀渣用户🀄', '日本語の名前', 'Player "A"', ' 带空格的名字 ']
const ids = ['00123', 'user-string', 12345, 0]
const converted = await tziakchaToSalasasa({ step: {
  w: Array.from({ length: 144 }, (_, index) => index),
  a: [],
  p: names.map((n, index) => ({ n, i: ids[index] })),
} })
const title = JSON.parse(JSON.stringify(converted)).game_title
assert.equal(title.is_external, true)
assert.equal(title.source_format, 'tziakcha')
assert.equal(isExternalRecord(title), true)
for (let index = 0; index < 4; index++) {
  assert.equal(title[`p${index}_name`], names[index])
  assert.equal(title[`p${index}_external_id`], String(ids[index]))
  assert.equal(externalPlayerName(title, index, '本站同号用户'), names[index])
  assert.equal(typeof title[`p${index}_uid`], 'number')
  assert(title[`p${index}_uid`] > 0)
}
assert.equal(isExternalRecord({ p0_uid: title.p0_uid }), false)
assert.equal(isExternalRecord({ source_format: 'salasasa' }), false)
assert.equal(isExternalRecord({ tziakcha_session_id: null }), true)
assert.equal(isExternalRecord({ source_format: 'botzone' }), true)
assert.equal(isExternalRecord({ source_format: 'mjai' }), true)
assert.equal(isExternalRecord(undefined), false)
assert.equal(isExternalRecord(null), false)
const roundTrip = await salasasaToTziakcha(converted)
assert.deepEqual(roundTrip.records[0].step.p.map((player) => player.n), names)
assert.deepEqual(roundTrip.records[0].step.p.map((player) => player.i), ids.map(String))
const mjai = mjaiToSalasasaRecord(JSON.stringify([
  { type: 'start_game', names },
  { type: 'start_kyoku', bakaze: 'E', kyoku: 1, oya: 0,
    scores: [25000, 25000, 25000, 25000], tehais: [[], [], [], []] },
  { type: 'end_kyoku' },
]))
assert.equal(mjai.game_title.is_external, true)
assert.equal(mjai.game_title.p0_name, names[0])
const sameNames = await tziakchaToSalasasa({ step: {
  w: Array.from({ length: 144 }, (_, index) => index), a: [],
  p: names.map(() => ({ n: '同名玩家' })),
} })
assert.equal(new Set(sameNames.game_title.player_entry_order).size, 4)
console.log('External player conversion and identity checks passed')
