import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'

async function load(path) {
  const result = await build({ entryPoints: [fileURLToPath(new URL(path, import.meta.url))], bundle: true, write: false, platform: 'node', format: 'esm' })
  return import('data:text/javascript;base64,' + Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const { buildLocalWaitData } = await load('../src/game2d/calc/guobiao/waitTips.ts')
const { calculateLocalHint } = await load('../src/game2d/calc/localHintEngine.ts')
const chicken = { hand: [23,24,25,35,36,17,17,17,42,42], combinations: ['s12'], win: 37 }
const begging = { hand: [25], combinations: ['s12','s24','s36','s17'], win: 25 }
function context(sample, extra = {}) {
  return { tips: true, flowerCount: 0, playerIndex: 0, currentRound: 1, hepaiLimit: 8,
    subRule: 'guobiao/standard', seatDiscards: [[],[],[],[]], seatCombinations: [sample.combinations,[],[],[]], ...sample, ...extra }
}
function row(ctx, cut) {
  const data = buildLocalWaitData(ctx, { includeDiscards: cut != null })
  return (cut == null ? data.details : data.details.find(x => x.discard_tile === cut).adds).find(x => x.tile === ctx.win)
}
function request(sample, sub_rule, extra = {}) {
  const way = ['场风东','自风东', ...(sample === begging ? ['和单张'] : [])]
  return { hand_tiles: [...sample.hand, sample.win], tiles_combination: sample.combinations, get_tile: sample.win,
    sub_rule, hepai_limit: 8, ron_way: [...way,'点和'], self_way: [...way,'自摸'], ...extra }
}

test('无番和、全求人显示仅点和；切牌预览与稳定听牌一致且保留点和番数', () => {
  for (const sample of [chicken, begging]) {
    const ctx = context(sample), before = structuredClone(ctx)
    const stable = row(ctx)
    assert.equal(stable.kind, 'ron_only')
    assert.equal(stable.label, '仅点和')
    assert.equal(stable.base_f, 8)
    assert.equal(stable.selfdrawn_f, 0)
    assert.deepEqual(row({ ...ctx, hand: [...ctx.hand, 47] }, 47), stable)
    assert.deepEqual(ctx, before)
  }
})

test('起和线分别判定，花牌不凑起和番；双方合法、仅自摸、未起和仍正确', () => {
  assert.equal(row(context(chicken, { flowerCount: 7 })).kind, 'ron_only')
  assert.equal(row(context(chicken, { hepaiLimit: 1 })).kind, undefined)
  assert.equal(row(context(chicken, { hepaiLimit: 9 })).base_f, 0)
  assert.equal(row(context(chicken, { hepaiLimit: 9 })).selfdrawn_f, 0)
  const self = row(context({ hand: [11,11,13,14,15,23,23,24,24,25,25,32,33], combinations: [], win: 34 }, { subRule: 'guobiao/lanshi', hepaiLimit: 5 }))
  assert.equal(self.base_f, 0)
  assert.equal(self.selfdrawn_f, 5)
})

test('四癞子和花混本地任务独立判定两种和法，花混奖励不凑起和', () => {
  for (const sub of ['guobiao/four_jokers','guobiao/huahun']) for (const sample of [chicken,begging]) {
    const q = request(sample, sub), before = structuredClone(q)
    const result = calculateLocalHint('guobiao.score', q)
    assert.equal(result.kind, 'ron_only')
    assert.equal(result.label, '仅点和')
    assert.deepEqual(q, before)
  }
  assert.equal(calculateLocalHint('guobiao.score', request(begging, 'guobiao/four_jokers', { hepai_limit: 4 })).kind, 'dianhe')
  assert.equal(calculateLocalHint('guobiao.score', request(begging, 'guobiao/four_jokers', { hepai_limit: 9 })).kind, 'wuyi')
  const joker = request(begging, 'guobiao/four_jokers', { hand_tiles: [25,59], get_tile: 59, hepai_limit: 1 })
  assert.equal(calculateLocalHint('guobiao.score', joker).kind, 'zimo')
})

test('十六张全求人支持仅点和', () => {
  const q = request(begging, 'guobiao/sixteen')
  q.tiles_combination = [...q.tiles_combination, 's27']
  assert.deepEqual(calculateLocalHint('guobiao.score', q), { kind: 'ron_only', label: '仅点和', score: 10 })
  q.hepai_limit = 6
  assert.equal(calculateLocalHint('guobiao.score', q).kind, 'dianhe')
})

test('蓝十默认五分起和、小林自定义起和线同样可以仅点和', () => {
  const blue = row(context({ hand: [37], combinations: ['k14','k45','k21','s16'], win: 37 }, { subRule: 'guobiao/lanshi', hepaiLimit: 5 }))
  assert.equal(blue.kind, 'ron_only')
  assert.equal(blue.base_f, 5)
  assert.equal(row(context(begging, { subRule: 'guobiao/xiaolin' })).kind, 'ron_only')
  assert.equal(row(context(begging, { subRule: 'guobiao/xiaolin', hepaiLimit: 1 })).kind, undefined)
})
