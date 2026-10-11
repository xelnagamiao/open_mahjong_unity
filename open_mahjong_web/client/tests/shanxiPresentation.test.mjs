import test from 'node:test'
import assert from 'node:assert/strict'
import { parseShanxiFan } from '../src/utils/shanxiPresentation.js'

for (const [name, points] of [['平和',0],['一条龙',20],['清一色',20],['七对',20],['豪华七对',40],['十三幺',60]]) {
  test(`${name} shows MIL points in the value column`, () => {
    assert.deepEqual(parseShanxiFan(name), { name, value: `${points}分` })
  })
}
for (const points of [3,4,5,6,7,8,9,10]) {
  test(`legacy ${points}-point winning tile splits its name and value`, () => {
    assert.deepEqual(parseShanxiFan(`和牌张（${points}分）`), { name: '和牌张', value: `${points}分` })
  })
}
test('flush one dragon plus nine remains an additive 49-point hand', () => {
  const rows = ['一条龙','清一色','和牌张（9分）'].map(parseShanxiFan)
  assert.equal(rows.reduce((sum,row) => sum + Number.parseInt(row.value,10),0),49)
  assert.deepEqual(rows.map(row => row.name),['一条龙','清一色','和牌张'])
})
test('unknown legacy labels do not borrow a Guobiao fan value', () => {
  assert.deepEqual(parseShanxiFan('历史说明'),{ name:'历史说明',value:'' })
})
