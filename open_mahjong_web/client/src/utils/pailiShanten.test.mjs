import { calculatePaili, calculatePailiShanten } from './pailiCalculator.ts'

function assert(cond, msg) {
  if (!cond) throw new Error(msg)
}

function pailiShanten(hand, combinations = []) {
  const result = calculatePaili({ handTiles: hand, combinations })
  return result.mode === 'discard' ? result.best_shanten : result.shanten
}

const closed13 = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24]
const closed14 = [...closed13, 25]
const melded13 = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21]
const melded14 = [...melded13, 22]

assert(calculatePailiShanten(closed13) === pailiShanten(closed13), '13 张向听应与完整牌理一致')
assert(calculatePailiShanten(closed14) === pailiShanten(closed14), '14 张向听应与完整牌理一致')
assert(
  calculatePailiShanten(melded13, ['k31']) === pailiShanten(melded13, ['k31']),
  '带副露 13 张向听应一致',
)
assert(
  calculatePailiShanten(melded14, ['k31']) === pailiShanten(melded14, ['k31']),
  '带副露 14 张向听应一致',
)

const loops = 80
const startFull = performance.now()
for (let i = 0; i < loops; i += 1) pailiShanten(closed14)
const fullMs = performance.now() - startFull
const startOnly = performance.now()
for (let i = 0; i < loops; i += 1) calculatePailiShanten(closed14)
const onlyMs = performance.now() - startOnly
assert(onlyMs < fullMs, `只算向听应更快：${onlyMs.toFixed(1)}ms vs ${fullMs.toFixed(1)}ms`)

console.log(
  `pailiShanten tests passed  14张×${loops}: shanten ${onlyMs.toFixed(1)}ms / paili ${fullMs.toFixed(1)}ms`,
)
