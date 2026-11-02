import type { HandDraft, HandDecomposition } from '../handAnalysis.ts'
import type { PailiOptions } from '../pailiCalculator.ts'
import { countsOf, orphan, setTiles, shapeGroups, solveHand, suit, terminal } from './handShapes.ts'
import type { HandSet, HandShape } from './handShapes.ts'

const ceil100 = (n: number) => Math.ceil(n / 100) * 100
export function riichiPoints(han: number, fu: number, dealer: boolean, tsumo: boolean, yakuman = 0) {
  const base = yakuman ? 8000 * yakuman : han >= 13 ? 8000 : han >= 11 ? 6000 : han >= 8 ? 4000 : han >= 6 ? 3000 : han >= 5 ? 2000 : Math.min(2000, fu * 2 ** (han + 2))
  if (tsumo) {
    const child = ceil100(base * (dealer ? 2 : 1)), parent = ceil100(base * 2)
    return { points: dealer ? child * 3 : parent + child * 2, payment: dealer ? `每家 ${child} 点` : `庄家 ${parent} 点，闲家各 ${child} 点` }
  }
  const points = ceil100(base * (dealer ? 6 : 4))
  return { points, payment: `放铳者支付 ${points} 点` }
}

function scoreShape(draft: HandDraft, shape: HandShape, winningSet: number, tsumo: boolean): HandDecomposition {
  const c = draft.context
  const sets = shape.sets
  const closed = sets.every(set => !set.open)
  const win = draft.winTile ?? draft.hand.at(-1)!
  const all = shape.shape === '十三幺' ? draft.hand : sets.flatMap(setTiles)
  const counts = countsOf(all)
  const seat = 41 + ['东', '南', '西', '北'].indexOf(c.seat)
  const round = 41 + ['东', '南', '西', '北'].indexOf(c.round)
  const pair = sets.find(set => set.kind === 'q')!.tile
  const triplets = sets.filter(set => set.kind === 'k' || set.kind === 'g')
  const sequences = sets.filter(set => set.kind === 's')
  const concealed = triplets.filter(set => !set.open && (tsumo || set.kind === 'g' || sets.indexOf(set) !== winningSet)).length
  const kongs = triplets.filter(set => set.kind === 'g').length
  const hasHonour = all.some(tile => tile > 40)
  const oneSuit = new Set(all.filter(tile => tile < 40).map(suit)).size === 1
  const yaku: { name: string; han: number }[] = []
  const yakumans: string[] = []
  const add = (name: string, han: number, condition = true) => { if (condition) yaku.push({ name, han }) }
  const yakuman = (name: string, condition: boolean) => { if (condition) yakumans.push(name) }
  const winning = sets[winningSet]
  const wait = shape.shape !== '一般型' || winning?.kind === 'q' ? 'tanki' : winning?.kind === 'k' ? 'shanpon' : win === winning?.tile ? 'kanchan' : (winning?.tile % 10 === 2 && win % 10 === 3) || (winning?.tile % 10 === 8 && win % 10 === 7) ? 'penchan' : 'ryanmen'
  if (shape.shape === '十三幺') yakuman(pair === win ? '国士无双十三面' : '国士无双', true)
  else {
    yakuman('字一色', all.every(tile => tile > 40))
    if (shape.shape === '一般型') {
      yakuman('大三元', triplets.filter(set => set.tile >= 45).length === 3)
      const winds = triplets.filter(set => set.tile >= 41 && set.tile <= 44).length
      yakuman('大四喜', winds === 4)
      yakuman('小四喜', winds === 3 && pair >= 41 && pair <= 44)
      yakuman('绿一色', all.every(tile => [32, 33, 34, 36, 38, 47].includes(tile)))
      yakuman('清老头', all.every(terminal))
      yakuman(wait === 'tanki' ? '四暗刻单骑' : '四暗刻', concealed === 4)
      yakuman('四杠子', kongs === 4)
      const nineGates = closed && !kongs && !hasHonour && oneSuit && [1, 2, 3, 4, 5, 6, 7, 8, 9].every(n => (counts[suit(win) * 10 + n] || 0) >= (n === 1 || n === 9 ? 3 : 1))
      const pureNine = nineGates && [1, 2, 3, 4, 5, 6, 7, 8, 9].every(n => counts[suit(win) * 10 + n] - Number(win % 10 === n) === (n === 1 || n === 9 ? 3 : 1))
      yakuman(pureNine ? '纯正九莲宝灯' : '九莲宝灯', nineGates)
    }
  }
  let fu = shape.shape === '一般型' ? 20 : 25
  if (!yakumans.length) {
    add('七对子', 2, shape.shape === '七对')
    add('断幺九', 1, all.every(tile => !orphan(tile)))
    add('混老头', 2, all.every(orphan) && hasHonour)
    if (oneSuit) add(hasHonour ? '混一色' : '清一色', hasHonour ? (closed ? 3 : 2) : (closed ? 6 : 5))
    if (shape.shape === '一般型') {
      for (const set of triplets) {
        if (set.tile >= 45) add(['役牌·中', '役牌·白', '役牌·发'][set.tile - 45], 1)
        add('自风', 1, set.tile === seat)
        add('场风', 1, set.tile === round)
      }
      const pinfu = closed && sequences.length === 4 && wait === 'ryanmen' && pair < 45 && pair !== seat && pair !== round
      add('平和', 1, pinfu)
      const sequenceCounts = countsOf(sequences.map(set => set.tile))
      const peikou = Object.values(sequenceCounts).reduce((n, count) => n + Math.floor(count / 2), 0)
      if (closed && peikou) add(peikou >= 2 ? '二杯口' : '一杯口', peikou >= 2 ? 3 : 1)
      const threeSuits = (list: HandSet[]) => list.some(set => set.tile < 40 && [1, 2, 3].every(s => list.some(other => other.tile === s * 10 + set.tile % 10)))
      add('三色同顺', closed ? 2 : 1, threeSuits(sequences))
      add('三色同刻', 2, threeSuits(triplets))
      add('一气通贯', closed ? 2 : 1, [1, 2, 3].some(s => [2, 5, 8].every(n => sequences.some(set => set.tile === s * 10 + n))))
      add('对对和', 2, triplets.length === 4)
      add('三暗刻', 2, concealed === 3)
      add('三杠子', 2, kongs === 3)
      add('小三元', 2, triplets.filter(set => set.tile >= 45).length === 2 && pair >= 45)
      if (sequences.length && sets.every(set => setTiles(set).some(orphan))) add(hasHonour ? '混全带幺九' : '纯全带幺九', hasHonour ? (closed ? 2 : 1) : (closed ? 3 : 2))
      if (pinfu) fu = tsumo ? 20 : 30
      else {
        if (closed && !tsumo) fu += 10
        if (tsumo) fu += 2
        if (pair >= 45) fu += 2
        if (pair === seat) fu += 2
        if (pair === round) fu += 2
        for (const set of triplets) {
          const open = set.open || (!tsumo && set.kind === 'k' && sets.indexOf(set) === winningSet)
          fu += (set.kind === 'g' ? 8 : 2) * (open ? 1 : 2) * (orphan(set.tile) ? 2 : 1)
        }
        if (['tanki', 'kanchan', 'penchan'].includes(wait)) fu += 2
        if (!closed && !tsumo && fu === 20) fu = 30
        fu = Math.ceil(fu / 10) * 10
      }
    }
    if (closed && c.riichi) add(c.riichi === 2 ? '双立直' : '立直', c.riichi)
    add('一发', 1, closed && !!c.riichi && !!c.ippatsu)
    add('门前清自摸', 1, closed && tsumo)
    add(tsumo ? '海底摸月' : '河底捞鱼', 1, c.lastTile)
    add('岭上开花', 1, tsumo && c.kongWin)
    add('抢杠', 1, !tsumo && c.robKong)
  }
  const yakuHan = yaku.reduce((sum, item) => sum + item.han, 0)
  if (!yakumans.length) {
    if (c.dora) add('宝牌', c.dora)
    if (c.redDora) add('赤宝牌', c.redDora)
    if (closed && c.riichi && c.uraDora) add('里宝牌', c.uraDora)
  }
  const multiplier = Math.min(6, yakumans.length)
  const fan = multiplier ? multiplier * 13 : yaku.reduce((sum, item) => sum + item.han, 0)
  const canWin = multiplier > 0 || yakuHan > 0
  const score = canWin ? riichiPoints(fan, fu, seat === 41, tsumo, multiplier) : { points: 0, payment: '无役，不能和牌（宝牌不算役）' }
  return {
    rule: 'riichi', fan, baseFan: yakuHan, fu, yakuman: multiplier, canWin, ...score,
    fanNames: multiplier ? yakumans : yaku.map(item => `${item.name}（${item.han} 番）`),
    groups: shape.shape === '十三幺' ? [{ label: '国士无双', tiles: [...draft.hand].sort((a, b) => a - b) }] : shapeGroups(shape), shape: shape.shape,
  }
}

/** Standard four-player rules: open tanyao, counted/multiple yakuman, no double yakuman or kiriage. */
export function scoreRiichi(draft: HandDraft, options: PailiOptions, method = draft.context.method): HandDecomposition[] {
  const shapes = solveHand(draft.hand, draft.melds, { ...options, mcrSevenPairs: false, riichiSevenPairs: !!(options.mcrSevenPairs || options.riichiSevenPairs) })
  const results = shapes.flatMap(shape => {
    if (shape.shape !== '一般型') return [scoreShape(draft, shape, 0, method === 'tsumo')]
    return shape.sets.flatMap((set, index) => !set.fixed && set.kind !== 'g' && setTiles(set).includes(draft.winTile ?? draft.hand.at(-1)!) ? [scoreShape(draft, shape, index, method === 'tsumo')] : [])
  })
  const unique = new Map(results.map(row => [JSON.stringify([row.groups, row.fanNames, row.fu]), row]))
  return [...unique.values()].sort((a, b) => Number(b.canWin) - Number(a.canWin) || b.points! - a.points! || b.fan - a.fan || b.fu! - a.fu!)
}
