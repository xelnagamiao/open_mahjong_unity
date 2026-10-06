import {
  createGuobiaoXunmuClock,
  guobiaoXunmuGoTo,
  guobiaoXunmuOnCut,
  guobiaoXunmuOnClaim,
} from './guobiaoXunmu.js'

function assert(cond, msg) {
  if (!cond) throw new Error(msg)
}

const clock = createGuobiaoXunmuClock(0)
guobiaoXunmuGoTo(clock, 0)
guobiaoXunmuOnCut(clock)
guobiaoXunmuOnClaim(clock, 2)
guobiaoXunmuOnCut(clock)
guobiaoXunmuGoTo(clock, 3)
guobiaoXunmuOnCut(clock)
guobiaoXunmuGoTo(clock, 0)
assert(clock.xunmu === 2, `庄家首张被碰后回庄为 2 巡，got ${clock.xunmu}`)
assert(clock.eastRiver === 0 && clock.eastOrigin === 1, '庄家被鸣走的弃牌仍被保留')

guobiaoXunmuOnCut(clock)
guobiaoXunmuOnClaim(clock, 2)
guobiaoXunmuOnCut(clock)
guobiaoXunmuGoTo(clock, 3)
guobiaoXunmuOnCut(clock)
guobiaoXunmuGoTo(clock, 0)
assert(clock.xunmu === 3, `庄家弃牌连续被碰后回庄为 3 巡，got ${clock.xunmu}`)
guobiaoXunmuGoTo(clock, 0)
assert(clock.xunmu === 3, '同家补花或杠后补牌不加巡')

const opening = createGuobiaoXunmuClock(0)
for (const seat of [0, 1, 2, 3, 0]) guobiaoXunmuGoTo(opening, seat)
assert(opening.xunmu === 1, `新一盘开局补花回庄仍为 1 巡，got ${opening.xunmu}`)
assert(opening.eastRiver === 0 && opening.eastOrigin === 0, '新一盘不保留旧弃牌')

const keep = createGuobiaoXunmuClock(0)
guobiaoXunmuGoTo(keep, 0)
guobiaoXunmuOnCut(keep)
guobiaoXunmuGoTo(keep, 1)
guobiaoXunmuOnCut(keep)
guobiaoXunmuGoTo(keep, 2)
guobiaoXunmuOnCut(keep)
guobiaoXunmuGoTo(keep, 3)
guobiaoXunmuOnCut(keep)
guobiaoXunmuGoTo(keep, 0)
assert(keep.xunmu === 2, `庄河仍在时回庄为 2 巡，got ${keep.xunmu}`)

console.log('guobiaoXunmu tests passed')
