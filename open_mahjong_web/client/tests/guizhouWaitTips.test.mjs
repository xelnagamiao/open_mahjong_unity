import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { guizhouReadyAt } from '../src/utils/guizhouReplay.js'

const compiled = await build({ entryPoints: [new URL('../src/game2d/calc/guizhou/waitTips.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')],
  bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { guizhouBasicScore, guizhouWaitingTiles, guizhouRonWaitingTiles, buildGuizhouWaitData } =
  await import('data:text/javascript;base64,' + Buffer.from(compiled.outputFiles[0].text).toString('base64'))

const plain = [11,12,13,14,15,16,21,22,23,34,35,36,28,28]
const pungs = [11,11,11,22,22,22,33,33,33,38,38,38,29,29]
const dragon = [11,11,11,11,22,22,24,24,31,31,35,35,39,39]
const pureDragon = [11,11,11,11,13,13,15,15,17,17,18,18,19,19]
const ctx = (hand, combinations = [], ready = '') => ({ hand, combinations, ready, seatDiscards: [[],[],[],[]], seatCombinations: [combinations,[],[],[]] })

test('普通平和显示自摸基本3分；碰不算通行证，明暗杠均可点和', () => {
  assert.deepEqual(guizhouBasicScore(plain), { points: 3, canRon: false })
  for (const prefix of ['k','g','G']) assert.deepEqual(guizhouBasicScore(plain.slice(3), [prefix+'11']), { points: 3, canRon: prefix !== 'k' })
  const data = buildGuizhouWaitData(ctx(plain.slice(0,-1)), { includeDiscards: false })
  const wait = data.details.find((item) => item.tile === 28)
  assert.equal(wait.base_f, 0); assert.equal(wait.selfdrawn_f, 3); assert.equal(wait.unit, '分')
  assert.ok(guizhouWaitingTiles(plain.slice(0,-1)).includes(28))
  assert.ok(!guizhouRonWaitingTiles(plain.slice(0,-1)).includes(28))
})

test('贵州番型、原报和软报按分计算；单吊不重复叠大对子，基本分封顶39', () => {
  assert.equal(guizhouBasicScore(pungs).points, 8)
  assert.equal(guizhouBasicScore(dragon).points, 26)
  assert.equal(guizhouBasicScore(plain, [], 'soft_ready').points, 13)
  assert.equal(guizhouBasicScore(plain, [], 'hard_ready').points, 26)
  assert.equal(guizhouBasicScore(pungs, [], 'soft_ready').points, 21)
  assert.equal(guizhouBasicScore(pureDragon, [], 'hard_ready').points, 39)
  assert.equal(guizhouBasicScore([29,29], ['k11','G22','k33','g38']).points, 13)
  assert.ok(guizhouRonWaitingTiles(plain.slice(0,-1), [], 'soft_ready').includes(28))
})

test('物理第五张不能显示待牌，错误副露和字花牌不形成有效听牌', () => {
  assert.ok(!guizhouWaitingTiles([21,21,21,19], ['k18','k19','k24']).includes(19))
  assert.equal(guizhouBasicScore([19,19,19,19,19], ['k18','k21','k24']), null)
  assert.equal(guizhouBasicScore(plain.slice(3), ['s12']), null)
  assert.deepEqual(guizhouWaitingTiles([...plain.slice(0,-2), 41]), [])
})

test('切牌预览与稳定手牌的分值和资格一致，待切牌计入剩余张数', () => {
  const stable = buildGuizhouWaitData(ctx(plain.slice(0,-1), [], 'hard_ready'), { includeDiscards: false })
  const preview = buildGuizhouWaitData(ctx([...plain.slice(0,-1),39], [], 'hard_ready'), { includeDiscards: true })
  assert.equal(preview.type, 'waits_all')
  assert.deepEqual(preview.details.find((item) => item.discard_tile === 39).adds, stable.details)
  const cut28 = buildGuizhouWaitData(ctx(plain), { includeDiscards: true }).details.find((item) => item.discard_tile === 28)
  assert.equal(cut28.adds.find((item) => item.tile === 28).remaining_count, 2)
})

test('桌面明暗杠和弃牌减少剩余牌，不给出额外基本分', () => {
  const context = ctx(plain.slice(0,-1))
  context.seatDiscards[1] = [28,28,28]
  context.seatCombinations[2] = ['G11','g39']
  const wait = buildGuizhouWaitData(context, { includeDiscards: false }).details.find((item) => item.tile === 28)
  assert.equal(wait.remaining_count, 0); assert.equal(wait.selfdrawn_f, 3)
})

test('牌谱报听按座位和当前节点读取，前后跳转不泄漏未来原报/软报', () => {
  const round = { action_ticks: [['guizhou','ready',0,'hard_ready'], ['d',39], ['guizhou','ready',1,'soft_ready']] }
  for (const node of [3,0,1,2,3,1]) {
    assert.equal(guizhouReadyAt(round,node,0), node >= 1 ? 'hard_ready' : '')
    assert.equal(guizhouReadyAt(round,node,1), node >= 3 ? 'soft_ready' : '')
  }
  assert.equal(guizhouReadyAt(round, -1, 0), '')
  assert.equal(guizhouReadyAt({ action_ticks: [] }, 20, 0), '')
})
