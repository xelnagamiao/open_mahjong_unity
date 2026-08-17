import { insertOpeningReset } from './openingReset.js'

function isSilent(tick) {
  const action = String(tick?.[0] ?? '')
  return !action || action === 'reset' || action === 'ask_hand' || action === 'ask_other' || action === 'ca'
}

function nextVisibleNode(ticks, node) {
  let index = node
  while (index < ticks.length && isSilent(ticks[index])) index += 1
  return index
}

function previousUserStepNode(ticks, node) {
  let lastVisible = -1
  for (let index = node - 1; index >= 0; index -= 1) {
    if (!isSilent(ticks[index])) {
      lastVisible = index
      break
    }
  }
  if (lastVisible < 0) return 0
  let start = lastVisible
  while (start > 0 && isSilent(ticks[start - 1])) start -= 1
  return start
}

function assert(cond, message) {
  if (!cond) throw new Error(message)
}

const noFlower = [['c', 11, 'F']]
assert(insertOpeningReset(noFlower, 0) === true, '无人补花应插入 reset')
assert(JSON.stringify(noFlower[0]) === JSON.stringify(['reset', 0]), 'reset 应在 ticks 最前')
assert(insertOpeningReset(noFlower, 0) === false, '重复插入应跳过')
assert(nextVisibleNode(noFlower, 0) === 1, '无人补花第一击应跳过 reset 落到庄家切牌')
assert(previousUserStepNode(noFlower, 2) === 0, '从庄家切牌后退应连同 reset 回到局初')

const withFlower = [
  ['bh', 51, 1, 'F'],
  ['bd', 12, 1],
  ['c', 11, 'F'],
]
assert(insertOpeningReset(withFlower, 0) === true, '有补花应在花后插入 reset')
assert(JSON.stringify(withFlower[2]) === JSON.stringify(['reset', 0]), 'reset 应紧接开局补花')
assert(nextVisibleNode(withFlower, 2) === 3, '补花结束后下一击应跳过 reset 落到庄家切牌')
assert(previousUserStepNode(withFlower, 4) === 2, '从庄家首切后退应停在 reset 前（补花后）')

console.log('openingReset silent-skip tests passed')
