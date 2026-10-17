import assert from 'node:assert/strict'
import test from 'node:test'
import { specialDrawCaption } from '../src/utils/specialDrawCaptions.js'
import { actionText } from '../src/views/lab/labUnityMap.js'

for (const [rule, expected] of [
  ['riichi', '九种九牌'], ['riichi/sanma', '九种九牌'], ['riichi/standard', '九种九牌'],
  ['riichi/washizu', '九种九牌'], ['classical', '九老峰回'], ['classical/standard', '九老峰回'],
]) {
  test(`${rule}: shared draw action keeps its rule-specific name`, () => {
    assert.equal(specialDrawCaption('jiuzhongjiupai', rule), expected)
    assert.equal(actionText('jiuzhongjiupai', rule), expected)
  })
}

test('other abortive draws have human-readable labels and ordinary actions are unchanged', () => {
  for (const [action, expected] of Object.entries({
    four_wind_abort: '四风连打', four_kan_abort: '四杠散了',
    four_riichi_abort: '四人立直', three_ron_abort: '三家和流局',
  })) assert.equal(specialDrawCaption(action, 'riichi'), expected)
  assert.equal(specialDrawCaption('hu_self', 'riichi'), null)
  assert.equal(actionText('peng', 'riichi'), '碰')
  assert.equal(actionText('custom_action', 'riichi'), 'custom_action')
})

test('shared replacement action respects the existing rule wording', () => {
  assert.equal(actionText('buhua', 'jiujiang'), '补发财')
  assert.equal(actionText('buhua', 'wuhan/mil2024'), '杠')
  assert.equal(actionText('buhua', 'classical'), '补花')
})
