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
assert(clock.xunmu === 1, `庄家首张被碰后回庄仍为 1 巡，got ${clock.xunmu}`)

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
