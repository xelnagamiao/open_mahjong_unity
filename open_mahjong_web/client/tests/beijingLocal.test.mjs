import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { readFileSync } from 'node:fs'

async function moduleAt(path) {
  const result = await build({ entryPoints: [fileURLToPath(new URL(path, import.meta.url))], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
  return import('data:text/javascript;base64,' + Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const { BeijingLocal, beijingHun } = await moduleAt('../src/game2d/calc/beijingLocal.ts')
const { regionalSnapshot } = await moduleAt('../src/game2d/calc/regionalLocal.ts')
const standard = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]

test('all three hun modes wrap suits, winds and the platform dragon order', () => {
  for (const suit of [1, 2, 3]) {
    assert.equal(beijingHun(suit * 10 + 9), suit * 10 + 1)
    assert.equal(beijingHun(suit * 10 + 1, 'down'), suit * 10 + 9)
  }
  assert.equal(beijingHun(44), 41)
  assert.equal(beijingHun(41, 'down'), 44)
  assert.equal(beijingHun(45), 47)
  assert.equal(beijingHun(47), 46)
  assert.equal(beijingHun(46), 45)
  assert.equal(beijingHun(45, 'down'), 46)
  for (const tile of [11, 41, 47]) assert.equal(beijingHun(tile, 'self'), tile)
  assert.equal(beijingHun(51), 0)
})

test('closed and open hands follow chi-peng-ti and allow a zero-fan self draw', () => {
  const closed = new BeijingLocal([], { indicator: 45 })
  assert.equal(closed.score(standard, 42, { source: 'discard' }).points, 4)
  const open = new BeijingLocal(['s12'], { indicator: 45 })
  const hand = standard.slice(3)
  assert.equal(open.score(hand, 42, { source: 'discard' }), null)
  assert.equal(open.score(hand, 42).fan, 1)
  hand[hand.indexOf(42)] = 47
  assert.equal(open.score(hand, 42).fan, 0)
  assert.equal(open.score(hand, 42).points, 1)
  assert.equal(open.describe(hand, 42).label, '仅自摸\n3点')
})

test('a discarded hun is natural for ron; four identical hun cannot form a kong meld', () => {
  const engine = new BeijingLocal([], { indicator: 45 })
  const hand = [...standard.slice(0, 13), 47]
  assert.ok(engine.score(hand, 47))
  assert.equal(engine.score(hand, 47, { source: 'discard' }), null)
  const natural = [...standard.slice(0, 12), 47, 47]
  assert.ok(engine.score(natural, 47, { source: 'discard' }))
  assert.equal(engine.score(natural, 47, { discarded_hun: true }), null)
  assert.equal(new BeijingLocal(['G47'], { indicator: 45 }).score(standard.slice(3), 42), null)
})

test('three-hun restriction counts held wildcards rather than a natural ron tile', () => {
  const engine = new BeijingLocal([], { indicator: 45, config: { three_hun_self_draw: true } })
  const hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 47, 47, 47]
  assert.equal(engine.score(hand, 41, { source: 'discard' }), null)
  assert.ok(engine.score(hand, 47, { source: 'discard' }))
  assert.ok(engine.score(hand, 41))
})

test('luxury seven pairs, heavenly, earthly and optional fans stack independently', () => {
  const engine = new BeijingLocal([], { indicator: 45, config: { fan_zimo: true, fan_dealer: true, fan_repeat: true } })
  const hand = [11, 11, 11, 11, 22, 22, 22, 22, 33, 33, 33, 33, 41, 41]
  const score = engine.score(hand, 41, { heavenly: true, dealer: true, repeat: 2 })
  assert.equal(score.fan_values['豪华七对'], 3)
  assert.equal(score.fan_values['七对'], 1)
  assert.equal(score.fan_values['天和'], 2)
  assert.equal(score.fan_values['连庄'], 2)
  assert.equal(score.fan, 12)
  const earthly = engine.score(standard, 42, { earthly: true, tian_ting: true, after_kong: true, last_tile: true })
  for (const fan of ['地和', '天听', '杠上开花', '海底捞月']) assert.equal(earthly.fan_values[fan], 1)
})

test('strict waits include exhausted logical tiles while visible waits respect physical supply', () => {
  const engine = new BeijingLocal([], { indicator: 45 })
  const hand = [15, 16, 17, 18, 18, 18, 18, 41, 41, 41, 42, 42, 42]
  assert.ok(engine.logicalWaits(hand).includes(18))
  assert.ok(!engine.waiting(hand).includes(18))
  assert.equal(engine.score([...hand, 15], 15).fan_values['捉五魁'], undefined)
  const fourIndicators = [11, 11, 11, 11, 22, 22, 22, 22, 42, 42, 42, 42, 41, 41]
  assert.equal(new BeijingLocal([], { indicator: 42, config: { hun_mode: 'self' } }).score(fourIndicators, 42), null)
})

test('catch-five uses natural candidate waits while held hun may substitute', () => {
  const engine = new BeijingLocal([], { indicator: 45 })
  const hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 14, 16]
  assert.deepEqual(engine.logicalWaits(hand, true), [15])
  assert.deepEqual(engine.waiting(hand), [15, 47])
  assert.equal(engine.score([...hand, 15], 15).fan_values['捉五魁'], 1)
  assert.equal(engine.score([...hand, 47], 47).fan_values['捉五魁'], undefined)
  assert.equal(engine.score([...hand, 47], 47, { source: 'discard' }), null)
  const heldHun = [...hand]; heldHun[0] = 47
  assert.ok(engine.logicalWaits(heldHun, true).includes(15))
  const fiveHun = new BeijingLocal([], { indicator: 14 })
  assert.deepEqual(fiveHun.logicalWaits(hand, true), [15])
  assert.equal(fiveHun.score([...hand, 15], 15).fan_values['捉五魁'], 1)
})

test('half-hun preserves one fan and does not increase a zero-fan hand', () => {
  const engine = new BeijingLocal(['s12'], { indicator: 45, config: { half_hun: true } })
  const zero = [21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 47]
  assert.equal(engine.score(zero, 42).fan, 0)
  const flush = [14, 15, 16, 17, 18, 19, 11, 11, 11, 12, 47]
  const result = engine.score(flush, 12)
  assert.equal(result.raw_fan, 2)
  assert.equal(result.fan, 1)
  assert.equal(result.points, 2)
})

test('worker previews retain blocked waits, proposed hun cuts and allowed discard filtering', () => {
  const hand = standard.slice(0, 13)
  const body = { rule: 'beijing', hand, melds: [], context: { indicator: 45, seat: 1, remaining: 40 } }
  assert.equal(regionalSnapshot(body).waiting_details[42].label, '4点')
  const blocked = regionalSnapshot({ ...body, context: { ...body.context, discarded_hun: true } })
  assert.ok(blocked.waiting_tiles.includes(42))
  assert.equal(blocked.waiting_details[42].label, '未满足')
  const cuts = regionalSnapshot({ ...body, hand: [...hand, 47], cuts: [47] })
  assert.deepEqual(Object.keys(cuts.waiting_by_discard), ['47'])
  assert.equal(cuts.waiting_details_by_discard[47][42].label, '未满足')
})

test('hint amounts are winner gains for ordinary ron, all-pay ron and self draw', () => {
  const ordinary = new BeijingLocal([], { indicator: 45 }).describe(standard, 42)
  assert.equal(ordinary.ron_points, 4)
  assert.equal(ordinary.self_points, 12)
  assert.equal(ordinary.label, '4点')
  const allPay = new BeijingLocal([], { indicator: 45, config: { ron_all_pay: true } }).describe(standard, 42)
  assert.equal(allPay.ron_points, 12)
  assert.equal(allPay.self_points, 12)
  assert.equal(allPay.label, '12点')
})

test('four-hun hints cover the fourth hun draw and unrelated draws while respecting supply', () => {
  const three = [45, 45, 45, 11, 14, 17, 21, 24, 27, 31, 34, 37, 41]
  const context = { indicator: 46, config: { four_hun_win: true } }
  const engine = new BeijingLocal([], context)
  const complete = [...three, 45]
  assert.equal(engine.score(complete, 45), null)
  assert.ok(engine.waiting(three).includes(45))
  assert.equal(engine.describe(complete, 45).label, '仅自摸\n12点')
  const four = complete.slice(0, 12).concat(45)
  assert.equal(four.filter(t => t === 45).length, 4)
  const waits = engine.waiting(four)
  assert.equal(waits.length, 33)
  assert.ok(!waits.includes(45))
  assert.equal(engine.describe([...four, 42], 42).label, '仅自摸\n12点')
  assert.equal(new BeijingLocal([], { indicator: 46 }).waiting(three).includes(45), false)
  assert.equal(new BeijingLocal([], { ...context, discarded_hun: true }).describe(complete, 45).label, '未满足')
  assert.equal(engine.describe(complete, 45, 45).label, '未满足')
})

test('four-hun hints keep a more valuable normal self draw', () => {
  const engine = new BeijingLocal([], { indicator: 46, config: { four_hun_win: true } })
  const hand = [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 45, 45, 45, 45]
  const normal = engine.score(hand, 45)
  assert.ok(normal.points > 4)
  assert.equal(engine.describe(hand, 45).self_points, normal.points * 3)
})

test('discard previews remove tian-ting only when changing the original hand', () => {
  const engine = new BeijingLocal([], { indicator: 45, tian_ting: true, drawn_tile: 19 })
  assert.equal(engine.describe(standard, 42).ron_points, 8)
  assert.equal(engine.describe(standard, 42, 19).ron_points, 8)
  assert.equal(engine.describe(standard, 42, 11).ron_points, 4)
})

test('kong-draw and opening bonuses expire in proposed-discard previews', () => {
  const kong = new BeijingLocal([], { indicator: 45, has_draw_slot: true, after_kong: true })
  assert.equal(kong.describe(standard, 42).self_points, 24)
  assert.equal(kong.describe(standard, 42, 19).self_points, 12)
  const opening = new BeijingLocal([], { indicator: 45, seat: 0, opening: true })
  assert.equal(opening.describe(standard, 42).self_points, 48)
  assert.equal(opening.describe(standard, 42, 19).self_points, 12)
})

test('server and C# shared scoring corpus agrees with the independent Web calculator', () => {
  const cases = JSON.parse(readFileSync(new URL('../../../open_mahjong_server/tests/fixtures/beijing_calculator.json', import.meta.url), 'utf8'))
  const fields = ['fan', 'raw_fan', 'points', 'substitutions', 'fan_values']
  for (const c of cases) {
    const quote = new BeijingLocal(c.melds, c.context).score(c.hand, c.winning_tile, { ...c.context, source: c.source })
    const select = value => value ? Object.fromEntries(fields.map(key => [key, value[key]])) : null
    assert.deepEqual(select(quote), select(c.expected), c.label)
  }
})
