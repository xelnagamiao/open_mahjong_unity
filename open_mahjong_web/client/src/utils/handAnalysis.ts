import { calculatePaili } from './pailiCalculator.ts'
import type { PailiOptions } from './pailiCalculator.ts'
import { Chinese_Hepai_Check, PlayerTiles } from '../game2d/calc/guobiao/gbHepai.ts'
import { tingpaiCheck } from '../game2d/calc/guobiao/gbTingpai.ts'
import { STANDARD_TILES, TILE_NAME, parseNotationWithGetTile, notationTextWithGetTile, meldDisplayTiles } from '../composables/useMahjongTiles.js'

export type WinMethod = 'ron' | 'tsumo'
export interface AnalysisContext {
  method: WinMethod
  round: string
  seat: string
  flowers: number
  lastTile: boolean
  kongWin: boolean
  robKong: boolean
  lastCopy: boolean
}
export interface HandDraft {
  version: 1
  hand: number[]
  melds: string[]
  winTile: number | null
  visible: number[]
  river: number[]
  context: AnalysisContext
}
export interface HandDecomposition {
  fan: number
  fanNames: string[]
  baseFan: number
  canWin: boolean
  groups: { label: string; tiles: number[] }[]
  shape: string
}

export const defaultContext = (): AnalysisContext => ({ method: 'ron', round: '东', seat: '东', flowers: 0, lastTile: false, kongWin: false, robKong: false, lastCopy: false })
export const emptyDraft = (): HandDraft => ({ version: 1, hand: [], melds: [], winTile: null, visible: [], river: [], context: defaultContext() })
export const effectiveCount = (draft: HandDraft) => draft.hand.length + 3 * draft.melds.length
export const cloneDraft = (draft: HandDraft): HandDraft => JSON.parse(JSON.stringify(draft))

export function expandMeld(code: string): number[] {
  if (!/^[skgGKS](1[1-9]|2[1-9]|3[1-9]|4[1-7])$/.test(code)) throw new Error(`副露格式不正确：${code}`)
  const tile = Number(code.slice(1))
  if (code[0].toLowerCase() === 's' && (tile >= 40 || tile % 10 < 2 || tile % 10 > 8)) throw new Error('顺子须为同一花色的三张连续数牌')
  return meldDisplayTiles(code[0], tile)
}

/** Used by editing, importing and the worker. Validate before touching a live draft. */
export function validateDraft(draft: HandDraft): void {
  if (!draft || draft.version !== 1) throw new Error('无法识别这份手牌数据')
  for (const key of ['hand', 'melds', 'visible', 'river']) {
    if (!Array.isArray(draft[key])) throw new Error('手牌数据格式不正确')
  }
  if (draft.melds.length > 4 || effectiveCount(draft) > 14) throw new Error('等效张数不能超过 14 张；每组副露按 3 张计算')
  if (draft.visible.length + draft.river.length > 136) throw new Error('已见牌数量不正确')
  const tiles = [...draft.hand, ...draft.melds.flatMap(expandMeld), ...draft.visible, ...draft.river]
  const counts = new Map<number, number>()
  for (const tile of tiles) {
    if (!STANDARD_TILES.includes(tile)) throw new Error(`不支持的牌号：${tile}`)
    counts.set(tile, (counts.get(tile) || 0) + 1)
    if (counts.get(tile)! > 4) throw new Error(`${TILE_NAME[tile]}超过 4 张，请检查手牌、副露与已见牌`)
  }
  if (draft.winTile !== null && (!draft.hand.includes(draft.winTile) || effectiveCount(draft) !== 14)) throw new Error('和牌张必须来自完整手牌')
  const c = draft.context
  if (!c || !['ron', 'tsumo'].includes(c.method) || !['东', '南', '西', '北'].includes(c.round) || !['东', '南', '西', '北'].includes(c.seat)) throw new Error('和牌条件格式不正确')
  if (!Number.isInteger(c.flowers) || c.flowers < 0 || c.flowers > 8) throw new Error('花牌应为 0 至 8 张')
  for (const flag of ['lastTile', 'kongWin', 'robKong', 'lastCopy']) if (typeof c[flag] !== 'boolean') throw new Error('和牌条件格式不正确')
  if ((c.method === 'ron' && c.kongWin) || (c.method === 'tsumo' && c.robKong) || (c.kongWin && c.robKong) || (c.robKong && c.lastTile)) throw new Error('和牌方式与特殊条件冲突')
}

export function parseHandText(text: string, draft: HandDraft): HandDraft {
  // The shared parser tolerates extra empty + segments; this editor deliberately does not.
  if (text.includes('+') && (text.split('+').length !== 2 || text.split('+').some(part => !part.trim()))) throw new Error('请使用「手牌 + 单张和牌张」，且只用一个 +')
  const parsed = parseNotationWithGetTile(text)
  const next = cloneDraft(draft)
  next.hand = [...parsed.hand, ...(parsed.getTile === null ? [] : [parsed.getTile])]
  next.winTile = effectiveCount(next) === 14 ? (parsed.getTile ?? next.hand.at(-1)!) : null
  if (parsed.getTile !== null && effectiveCount(next) !== 14) throw new Error('+ 后的和牌张需要与手牌、副露合计为 14 张')
  const sameTiles = [...next.hand].sort((a, b) => a - b).join(',') === [...draft.hand].sort((a, b) => a - b).join(',')
  if (!sameTiles) next.river = []
  validateDraft(next)
  return next
}

export function draftNotation(draft: HandDraft): string {
  const hand = [...draft.hand]
  if (draft.winTile !== null) hand.splice(hand.indexOf(draft.winTile), 1)
  return notationTextWithGetTile(hand, draft.winTile)
}

export function setWinMethod(draft: HandDraft, method: WinMethod): HandDraft {
  const next = cloneDraft(draft)
  next.context.method = method
  if (method === 'ron') next.context.kongWin = false
  else next.context.robKong = false
  return next
}

export function discardTile(draft: HandDraft, tile: number): HandDraft {
  validateDraft(draft)
  if (effectiveCount(draft) !== 14 || !draft.hand.includes(tile)) throw new Error('请从 14 张手牌中选择舍牌')
  const next = cloneDraft(draft)
  next.hand.splice(next.hand.indexOf(tile), 1)
  next.river.push(tile)
  next.winTile = null
  return next
}

export function drawTile(draft: HandDraft, tile: number): HandDraft {
  if (effectiveCount(draft) !== 13) throw new Error('13 张状态才能摸牌')
  const next = cloneDraft(draft)
  next.hand.push(tile)
  next.winTile = tile
  validateDraft(next)
  return next
}

function wayFor(draft: HandDraft, method: WinMethod, winTile: number): string[] {
  const c = draft.context
  const hand13 = [...draft.hand]
  hand13.splice(hand13.indexOf(winTile), 1)
  const way = [method === 'ron' ? '点和' : '自摸', `场风${c.round}`, `自风${c.seat}`]
  if (tingpaiCheck(hand13, draft.melds, false).length === 1) way.push('和单张')
  if (c.lastCopy) way.push('和绝张')
  if (c.lastTile) way.push(method === 'ron' ? '海底捞月' : '妙手回春')
  if (method === 'tsumo' && c.kongWin) way.push('杠上开花')
  if (method === 'ron' && c.robKong) way.push('抢杠和')
  for (let i = 0; i < c.flowers; i++) way.push('花牌')
  return way
}

function decompositionGroups(pt: PlayerTiles, original: number[]) {
  if (pt.fan_list.some(key => ['qiduizi', 'lianqidui'].includes(key))) {
    const sorted = [...original].sort((a, b) => a - b)
    return Array.from({ length: 7 }, (_, i) => ({ label: '对子', tiles: sorted.slice(i * 2, i * 2 + 2) }))
  }
  if (pt.fan_list.includes('shisanyao')) return [{ label: '十三幺', tiles: [...original].sort((a, b) => a - b) }]
  if (pt.fan_list.some(key => ['quanbukao', 'qixingbukao'].includes(key))) return [{ label: pt.fan_list.includes('qixingbukao') ? '七星不靠' : '全不靠', tiles: [...original].sort((a, b) => a - b) }]
  return pt.combination_list.map(code => {
    const prefix = code[0]
    const labels = { s: '顺子', S: '暗顺', k: '明刻', K: '暗刻', g: '明杠', G: '暗杠', q: '雀头', z: '组合龙' }
    // The scoring dictionary treats a kong as a triplet; render all four physical tiles.
    return { label: labels[prefix] || code, tiles: prefix === 'g' || prefix === 'G' ? expandMeld(code) : [...(Chinese_Hepai_Check.combination_to_tiles_dict[code] || [])] as number[] }
  })
}

/** Adapter over the existing scoring engine, mirroring Python hepai_decompose.
 * Each decomposition gets independent context: fan_count mutates its arguments.
 */
export function scoreDecompositions(draft: HandDraft, method = draft.context.method): HandDecomposition[] {
  const winTile = draft.winTile ?? draft.hand.at(-1)
  if (!winTile || effectiveCount(draft) !== 14) return []
  const checker = new Chinese_Hepai_Check(false)
  const original = new PlayerTiles(draft.hand, draft.melds, draft.melds.length * 3)
  const candidates: PlayerTiles[] = []
  if (draft.hand.length === 14) {
    checker.GS_check(original, candidates)
    if (!candidates.length) checker.QBK_check(original, candidates)
    if (!candidates.length) checker.QD_check(original, candidates)
  } else checker.QBK_check(original, candidates)
  candidates.push(original)
  const complete: PlayerTiles[] = []
  for (const candidate of candidates) checker.normal_check(candidate, complete)
  if (!complete.length) return []
  const way = wayFor(draft, method, winTile)
  const unique = new Map<string, HandDecomposition>()
  for (const pt of complete) {
    const special = pt.fan_list.find(key => ['shisanyao', 'qiduizi', 'lianqidui', 'quanbukao', 'qixingbukao', 'zuhelong'].includes(key))
    const shape = ({ shisanyao: '十三幺', qiduizi: '七对', lianqidui: '七对', quanbukao: '全不靠', qixingbukao: '全不靠', zuhelong: '组合龙' })[special || ''] || (pt.combination_list.some(code => code[0] === 'z') ? '组合龙' : '一般型')
    const [fan, fanNames] = checker.fan_count(pt, winTile, [...way])
    const groups = decompositionGroups(pt, draft.hand)
    const key = JSON.stringify([groups.map(group => `${group.label}:${group.tiles.join(',')}`).sort(), fan, [...fanNames].sort()])
    unique.set(key, { fan, fanNames, groups, shape, baseFan: fan - draft.context.flowers, canWin: fan - draft.context.flowers >= 8 })
  }
  return [...unique.values()].sort((a, b) => b.fan - a.fan)
}

export function analyzeHand(draft: HandDraft, options?: PailiOptions) {
  validateDraft(draft)
  const count = effectiveCount(draft)
  if (count < 13) return { state: 'incomplete', count }
  const paili = calculatePaili({ handTiles: draft.hand, combinations: draft.melds, visibleTiles: [...draft.visible, ...draft.river], options })
  const decompositions = count === 14 ? scoreDecompositions(draft) : []
  const waits = paili.mode === 'shanten' && paili.is_tingpai ? paili.accept.map(item => {
    const candidate = drawTile(draft, item.tile)
    return { ...item, ron: scoreDecompositions(candidate, 'ron')[0] ?? null, tsumo: scoreDecompositions(candidate, 'tsumo')[0] ?? null }
  }) : []
  return {
    state: decompositions.length ? 'complete' : count === 14 ? 'discard' : paili.mode === 'shanten' && paili.is_tingpai ? 'ready' : 'improve',
    count, paili, decompositions, waits,
    shapes: [...new Set(decompositions.map(item => item.shape))],
  }
}

export const HAND_EXAMPLES = [
  { id: 'ready', notation: '123456789m23p55s' },
  { id: 'win', notation: '123456789m23p55s + 1p' },
  { id: 'discard', notation: '123456789m23p55s9s' },
  { id: 'low', notation: '345m456p67s22p + 8s', melds: ['s22'] },
  { id: 'pairs', notation: '1122334455667m + 7m' },
  { id: 'orphans', notation: '19m19p19s1234567z' },
]

export function exampleDraft(id: string): HandDraft {
  const example = HAND_EXAMPLES.find(item => item.id === id) || HAND_EXAMPLES[0]
  const draft = emptyDraft()
  draft.melds = [...(example.melds || [])]
  return parseHandText(example.notation, draft)
}

export function encodeDraft(draft: HandDraft): string {
  validateDraft(draft)
  return encodeURIComponent(JSON.stringify(draft))
}
export function decodeDraft(raw: string): HandDraft {
  if (raw.length > 8000) throw new Error('手牌链接过长')
  const draft = JSON.parse(decodeURIComponent(raw))
  validateDraft(draft)
  return cloneDraft(draft)
}
