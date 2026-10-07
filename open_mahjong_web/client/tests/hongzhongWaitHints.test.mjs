import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'
import { hongzhongHintsAt, hongzhongKnownTiles, hongzhongWaitDataAt } from '../src/utils/hongzhongReplay.js'

async function bundled(options) {
  const result = await build({ bundle: true, write: false, platform: 'node', format: 'esm', logLevel: 'silent', ...options })
  return import('data:text/javascript;base64,' + Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const { mmcrTileToSalasasa, salasasaTileToMmcr } = await bundled({
  entryPoints: [fileURLToPath(new URL('../src/game2d/salasasa/gameAdapter.ts', import.meta.url))],
})
const mixed = [11, 12, 13, 14, 15, 16, 21, 22, 23, 34, 35, 36, 28]
const allTiles = [...Array.from({ length: 3 }, (_, suit) => Array.from({ length: 9 }, (_, n) => (suit + 1) * 10 + n + 1)).flat(), 45]
function hints(hand = mixed, waits = [28, 45], overrides = {}) {
  return { hint_version: 2, source_hand_tiles: [...hand], source_melds: [], waiting_tiles: waits,
    waiting_by_discard: {}, waiting_details: Object.fromEntries(waits.map(tile => [tile, { fan: tile === 45 ? 0 : 1, base_score: tile === 45 ? 1 : 2 }])),
    waiting_details_by_discard: {}, win_blocked: false, ...overrides }
}
function round(payload, seat = 0) { return { action_ticks: [['hongzhong', 'hints', seat, payload]] } }
function snapshot(hand = mixed, drawn = null) {
  return { viewer: { seat_index: 0 }, seats: [0, 1, 2, 3].map(seat_index => ({ seat_index,
    hand_tiles: seat_index === 0 ? hand.map(salasasaTileToMmcr) : [],
    drawn_tile: seat_index === 0 && drawn != null ? salasasaTileToMmcr(drawn) : null,
    melds: [], flower_tiles: [], discard_pile: [],
  })) }
}
function rows(payload, hand = payload.source_hand_tiles, known = hand) {
  return hongzhongWaitDataAt(round(payload), 1, 0, hand, payload.source_melds, known)
}

test('普通番与自摸基本分来自权威提示，0番仍是合法听口', () => {
  const result = rows(hints())
  assert.deepEqual(result.details.map(row => [row.tile, row.label, row.kind, row.base_f]), [
    [28, '1番\n自摸2分', 'zimo', 0], [45, '0番\n自摸1分', 'zimo', 0],
  ])
  const hand = mixed.slice(0, -1).concat(45)
  const every = rows(hints(hand, allTiles, { waiting_details: Object.fromEntries(allTiles.map(tile => [tile, { fan: 0, base_score: 1 }])) }))
  assert.equal(every.details.length, 28)
  assert(every.details.every(row => row.kind === 'zimo' && row.label === '0番\n自摸1分'))
  assert.equal(rows(hints(mixed, [17], { waiting_details: { 17: { fan: 4, base_score: 16 } } })).details[0].label, '4番\n自摸16分')
})

test('切牌预览与切后稳定提示的分值、余枚一致，不重复减去同一张牌', () => {
  const stable = hints()
  const preview = hints([...mixed, 39], [], { waiting_by_discard: { 39: stable.waiting_tiles },
    waiting_details_by_discard: { 39: stable.waiting_details } })
  const pre = rows(preview)
  assert.equal(pre.type, 'waits_all')
  const projected = rows(preview, mixed, [...mixed, 39])
  assert.equal(projected.type, 'waits')
  assert.deepEqual(pre.details[0].adds, projected.details)
  assert.deepEqual(projected.details, rows(stable).details)
  // Discarding the waiting tile transfers a known copy from hand to the river.
  const sameTile = hints([...mixed, 28], [], { waiting_by_discard: { 28: [28] },
    waiting_details_by_discard: { 28: { 28: { fan: 1, base_score: 2 } } } })
  assert.equal(rows(sameTile).details[0].adds[0].remaining_count, 2)
  assert.equal(rows(sameTile, mixed, [...mixed, 28]).details[0].remaining_count, 2)
})

test('停和保留基本听口但标记未满足；旧牌谱无分值时安全退回仅自摸', () => {
  const blocked = rows(hints(mixed, [28, 45], { win_blocked: true }))
  assert.deepEqual(blocked.details.map(row => [row.tile, row.label, row.kind]), [[28, '未满足', 'wuyi'], [45, '未满足', 'wuyi']])
  const old = hints()
  delete old.waiting_details
  delete old.waiting_details_by_discard
  delete old.hint_version
  assert(rows(old).details.every(row => row.label === '仅自摸' && row.kind === 'zimo' && row.base_f === 0))
  for (const score of [{ fan: 5, base_score: 32 }, { fan: 1, base_score: 99 }, { fan: -1, base_score: 1 }]) {
    assert.equal(rows(hints(mixed, [28], { waiting_details: { 28: score } })).details[0].label, '仅自摸')
  }
})

test('回退、不同座位、旧手牌和副露不会借用未来或其他玩家的提示', () => {
  const payload = hints()
  const current = { action_ticks: [['c', 39, 'T'], ['hongzhong', 'hints', 0, payload]] }
  assert.equal(hongzhongHintsAt(current, 1, 0, mixed, []), null)
  assert.equal(hongzhongHintsAt(current, 2, 1, mixed, []), null)
  assert.equal(hongzhongHintsAt(current, 2, 0, mixed, ['G11']), null)
  assert.equal(hongzhongHintsAt(current, 2, 0, mixed.slice(1).concat(39), []), null)
  assert.equal(hongzhongHintsAt(current, 2, 0, [...mixed].reverse(), []), payload)
  assert.equal(hongzhongWaitDataAt(current, 1, 0, mixed, [], mixed), null)
  assert.equal(hongzhongWaitDataAt(round(hints(mixed, [])), 1, 0, mixed, [], mixed), null)
})

test('余枚只数本家手牌及公开实体牌，不窥看对家手牌或完全扣放的暗杠', () => {
  const value = snapshot([11, 45], 45)
  value.seats[1].hand_tiles = [45, 45, 45].map(salasasaTileToMmcr)
  value.seats[1].drawn_tile = salasasaTileToMmcr(45)
  value.seats[1].discard_pile = [salasasaTileToMmcr(45)]
  value.seats[2].melds = [{ type: 'kong', tile: salasasaTileToMmcr(11), concealed: true, concealed_face_down: [true, false, false, true] }]
  value.seats[3].melds = [{ type: 'kong', tile: salasasaTileToMmcr(12), concealed: true, concealed_face_down: [true, true, true, true] },
    { type: 'triplet', tile: salasasaTileToMmcr(13), physical_tiles: [13, 13, 45].map(salasasaTileToMmcr) }]
  assert.deepEqual(hongzhongKnownTiles(value, mmcrTileToSalasasa).sort((a, b) => a - b), [11, 11, 11, 11, 11, 13, 13, 45, 45, 45, 45])
  assert.equal(rows(hints(mixed, [45]), mixed, [45, 45, 45, 45, 45]).details[0].remaining_count, 0)
})

// Exercise the current Vue function bodies rather than a second copy of routing.
const vue = fs.readFileSync(new URL('../src/views/game2d/Replay.vue', import.meta.url), 'utf8').replace(/\r\n/g, '\n')
function body(pattern) { const match = vue.match(pattern); assert(match, 'Current replay entry must exist'); return match[1] }
const waitBody = body(/function waitDataForSnapshot\(snapshot: ActiveSessionSnapshot\) \{([\s\S]*?)\n\}\n/)
const dangerBody = body(/function waitingTilesForSnapshotSeat\(seat: ActiveSessionSnapshot\['seats'\]\[number\]\): Set<number> \{([\s\S]*?)\n\}\n/)
const dangerComputed = body(/const replayDangerBySeat = computed\(\(\) => \{([\s\S]*?)\n\}\)/)
const unexpected = () => { throw Error('Hongzhong must not use another rule solver') }
function currentEntries(payload) {
  const detail = { value: { rule: 'hongzhong' } }
  const currentRound = { value: round(payload) }
  const node = { value: 1 }
  const wait = new Function('detail', 'currentRound', 'node', 'mmcrTileToSalasasa', 'meldKey', 'hongzhongWaitDataAt', 'hongzhongKnownTiles', 'isChangchunRecord', 'buildLocalWaitData',
    'return function(snapshot) {' + waitBody + '\n}')(detail, currentRound, node, mmcrTileToSalasasa, unexpected, hongzhongWaitDataAt, hongzhongKnownTiles, () => false, unexpected)
  const danger = new Function('detail', 'mmcrTileToSalasasa', 'tingpaiCheck', 'isChangchunRecord',
    'return function(seat) {' + dangerBody.replace(/new Set<number>/g, 'new Set').replace(/\(wait: \{ tile: number \}\)/g, '(wait)') + '\n}')(detail, mmcrTileToSalasasa, unexpected, () => false)
  const computedDanger = new Function('detail', 'resultPosition', 'chongHintEnabled', 'waitingTilesForSnapshotSeat',
    'return function() {' + dangerComputed.replace(/new Map<number, Set<number>>/g, 'new Map').replace(/new Set<number>/g, 'new Set') + '\n}')(
    detail, { value: { snapshot: snapshot() } }, { value: true }, unexpected)
  return { wait, danger, computedDanger, node }
}

test('牌谱实际稳定与预览入口消费红中权威分值，放铳层始终为空', () => {
  const current = currentEntries(hints())
  assert.deepEqual(current.wait(snapshot()), rows(hints()))
  assert.deepEqual([...current.danger(snapshot().seats[0])], [])
  assert.equal(current.computedDanger().size, 0)
  current.node.value = 0
  assert.equal(current.wait(snapshot()), null)
  const stable = hints()
  const preview = hints([...mixed, 39], [], { waiting_by_discard: { 39: stable.waiting_tiles }, waiting_details_by_discard: { 39: stable.waiting_details } })
  assert.deepEqual(currentEntries(preview).wait(snapshot(mixed, 39)), rows(preview))
})

// Minimal drawing mocks execute the production renderer and forwarding code;
// they do not replace the renderer's eligibility, label, color or alpha logic.
const drawing = await bundled({ stdin: { resolveDir: fileURLToPath(new URL('../src/game2d/game/scene/', import.meta.url)), contents:
  "export { WaitDisplay } from './WaitDisplay'; export { WaitEntry } from './WaitEntry'; export { Container } from 'pixi.js'", loader: 'ts' },
  define: { 'import.meta.env.BASE_URL': '"/"' },
  plugins: [{ name: 'headless-drawing', setup(build) {
    build.onResolve({ filter: /^(pixi\.js|\.\/Tile|\.\.\/fontLoader|\.\.\/\.\.\/\.\.\/i18n)$/ }, args => ({ path: args.path, namespace: 'drawing-mock' }))
    build.onLoad({ filter: /.*/, namespace: 'drawing-mock' }, args => ({ loader: 'js', contents: args.path === 'pixi.js' ? `
      export const isMobile = { any: false };
      export class Container { constructor() { this.children=[]; this.scale={set(){}}; this.anchor={set(){}}; }
        addChild(child){this.children.push(child);return child} removeChild(child){this.children=this.children.filter(v=>v!==child)} destroy(){} }
      export class Text extends Container {constructor(options){super();this.text=options.text;this.style=options.style;this.width=this.text.length*100}}
      export class Graphics extends Container {roundRect(){return this} fill(){return this} stroke(){return this}}
    ` : args.path === './Tile' ? 'export class Tile {constructor(tid){this.tid=tid;this.alpha=1} destroy(){}}'
      : args.path === '../fontLoader' ? "export function getGameFontFamily(){return 'test-font'}"
        : "export const locale={value:'zh-CN'}; export function tr(s){return s}" }))
  } }],
})
function inspectEntry(entry) { return { label: entry.children[0].text, color: entry.children[0].style.fill, alpha: entry.children[2].alpha } }

test('显式0番自摸及未满足资格正确着色，旧国标数字入口行为保持一致', () => {
  const { WaitEntry } = drawing
  const entry = (base, self, count = 4, hint) => inspectEntry(new WaitEntry(null, 0, 0, 28, base, self, count, '番', hint))
  assert.deepEqual(entry(8, 10), { label: '8番', color: 0x000000, alpha: 1 })
  assert.deepEqual(entry(0, 5), { label: '仅自摸', color: 0x777777, alpha: 0.78 })
  assert.deepEqual(entry(0, 0), { label: '未起和', color: 0x8b2f2f, alpha: 0.58 })
  assert.deepEqual(entry(0, 0, 4, { kind: 'zimo', label: '0番\n自摸1分' }), { label: '0番\n自摸1分', color: 0x777777, alpha: 0.78 })
  assert.deepEqual(entry(0, 0, 4, { kind: 'wuyi', label: '未满足' }), { label: '未满足', color: 0x8b2f2f, alpha: 0.58 })
  assert.equal(entry(0, 0, 0, { kind: 'zimo', label: '0番\n自摸1分' }).alpha, 0.58)
})

test('WaitDisplay把稳定和切牌提示的标签与资格传给实际WaitEntry', () => {
  const { WaitDisplay, WaitEntry, Container } = drawing
  const display = new WaitDisplay(new Container())
  const payload = hints()
  display.setData(rows(payload), 28)
  assert.deepEqual(display.children.filter(child => child instanceof WaitEntry).map(inspectEntry).map(row => row.label), ['1番\n自摸2分', '0番\n自摸1分'])
  const blocked = hints([...mixed, 39], [], { win_blocked: true, waiting_by_discard: { 39: [28, 45] }, waiting_details_by_discard: { 39: payload.waiting_details } })
  display.setData(rows(blocked), 39)
  assert(display.children.filter(child => child instanceof WaitEntry).every(child => inspectEntry(child).label === '未满足'))
  display.setData(null)
  assert.equal(display.visible, false)
  assert.equal(display.children.length, 0)
})
