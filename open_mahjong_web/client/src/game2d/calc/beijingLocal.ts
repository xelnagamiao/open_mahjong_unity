import { TILES, INDEX, prepare, canWin, shapes, shapeTiles, meldTiles, type LocalHand, type Policy } from './localJokerHand'

type Context = Record<string, any>
type Quote = { fan: number; raw_fan: number; points: number; fan_values: Record<string, number>; substitutions: number; shape: string; hun: number }

export function beijingHun(indicator: number, mode = 'up'): number {
  if (!INDEX.has(indicator) || !['up', 'down', 'self'].includes(mode)) return 0
  if (mode === 'self') return indicator
  const cycle = indicator < 40 ? Array.from({ length: 9 }, (_, n) => Math.floor(indicator / 10) * 10 + n + 1) : indicator < 45 ? [41, 42, 43, 44] : [45, 47, 46]
  return cycle[(cycle.indexOf(indicator) + (mode === 'up' ? 1 : cycle.length - 1)) % cycle.length]
}

export class BeijingLocal {
  private readonly config: Context
  private readonly indicator: number
  private readonly hun: number
  private readonly waitsMemo = new Map<string, number[]>()

  constructor(private readonly melds: string[], private readonly context: Context) {
    this.config = context.config ?? {}
    this.indicator = context.indicator ?? 0
    this.hun = beijingHun(this.indicator, this.config.hun_mode ?? 'up')
  }

  private prepared(hand: number[], ronHun = false, physical = true, complete = true): LocalHand | null {
    if (!this.hun) return null
    const policy: Policy = { jokers: [this.hun], sevenPairs: true, supply: physical ? { [this.indicator]: 3 } : Object.fromEntries(TILES.map(t => [t, 18])) }
    const value = prepare(hand, this.melds, policy, complete)
    if (value && ronHun) {
      if (!value.ghosts) return null
      value.ghosts--
      value.counts[INDEX.get(this.hun)!]++
    }
    return value
  }

  logicalWaits(hand: number[], naturalCandidate = false): number[] {
    const key = [...hand].sort((a, b) => a - b).join(',') + '/' + naturalCandidate
    if (!this.waitsMemo.has(key)) this.waitsMemo.set(key, TILES.filter(tile => canWin(this.prepared([...hand, tile], naturalCandidate && tile === this.hun, false))))
    return this.waitsMemo.get(key)!
  }

  waiting(hand: number[]): number[] {
    if (!this.prepared(hand, false, true, false)) return []
    return TILES.filter(tile => {
      const value = this.prepared([...hand, tile])
      return value && (this.config.four_hun_win && value.ghosts === 4 || canWin(value))
    })
  }

  score(hand: number[], win: number, options: Context = {}): Quote | null {
    const source = options.source ?? 'self_draw', ron = source === 'discard' || source === 'rob_kong'
    if (!['self_draw', 'discard', 'rob_kong'].includes(source) || !hand.includes(win) || options.discarded_hun) return null
    const value = this.prepared(hand, ron && win === this.hun, options.check_physical !== false)
    if (!value) return null
    const closed = this.melds.every(m => m[0] === 'G')
    if (ron && (!closed || this.config.three_hun_self_draw && value.ghosts >= 3)) return null
    if (options.four_hun) return !ron && this.config.four_hun_win && hand.filter(t => t === this.hun).length === 4
      ? { fan: 2, raw_fan: 2, points: 4, fan_values: { '混儿杠': 2 }, substitutions: 0, shape: 'four_hun', hun: this.hun } : null
    const before = [...hand]; before.splice(before.indexOf(win), 1)
    const waits = this.logicalWaits(before), strictWaits = win === 15 ? this.logicalWaits(before, true) : [], common: Record<string, number> = {}
    const add = (name: string, enabled: any, count = 1) => { if (enabled && count) common[name] = count }
    add('门清', closed)
    add('杠上开花', !ron && options.after_kong)
    add('海底捞月', options.last_tile)
    add('抢杠和', source === 'rob_kong')
    add('天和', !ron && options.heavenly, 2)
    add('天听', options.tian_ting)
    add('地和', options.earthly)
    add('自摸', this.config.fan_zimo && !ron)
    add('庄', this.config.fan_dealer && options.dealer)
    add('连庄', this.config.fan_repeat && options.repeat > 0, options.repeat)
    add('混儿悠', this.config.fan_all_waits && waits.length === 34)
    add('杠', this.config.fan_kong, this.melds.filter(m => 'gG'.includes(m[0])).length)
    const exposed = this.melds.flatMap(m => meldTiles(m)!)
    let best: Quote | null = null
    for (const shape of shapes(value)) {
      const logical = [...shapeTiles(shape), ...exposed]
      const counts = new Map<number, number>()
      for (const tile of logical) counts.set(tile, (counts.get(tile) ?? 0) + 1)
      const naturalHun = Number(ron && win === this.hun)
      const substitutions = Math.max(0, value.ghosts - Math.max(0, (counts.get(this.hun) ?? 0) - naturalHun))
      const fans = { ...common }, codes = [...shape.closed, ...this.melds]
      if (!substitutions) fans['素'] = 1
      if (shape.kind === 'seven_pairs') {
        fans['七对'] = 1
        const luxury = [...counts.values()].reduce((n, count) => n + Math.floor(count / 4), 0)
        if (luxury) fans['豪华七对'] = luxury
      } else {
        if ([1, 2, 3].some(suit => [2, 5, 8].every(rank => codes.some(m => m.toLowerCase() === 's' + (suit * 10 + rank))))) fans['一条龙'] = 1
        if (this.config.fan_all_pungs && codes.filter(m => m[0] !== 'q').every(m => !'sS'.includes(m[0]))) fans['碰碰和'] = 1
      }
      const suits = new Set(logical.map(tile => Math.floor(tile / 10)))
      if (suits.size === 1 && [...suits][0] < 4) fans['清一色'] = 1
      if (win === 15 && strictWaits.length === 1 && strictWaits[0] === 15 && counts.has(15)) fans['捉五魁'] = 1
      const raw = Object.values(fans).reduce((a, b) => a + b, 0)
      const total = this.config.half_hun && raw >= 1 ? Math.max(1, raw - substitutions) : raw
      if (!best || total > best.fan || total === best.fan && (substitutions < best.substitutions || substitutions === best.substitutions && raw > best.raw_fan)) {
        best = { fan: total, raw_fan: raw, points: 2 ** total, fan_values: fans, substitutions, shape: shape.kind === 'seven_pairs' ? 'seven' : 'standard', hun: this.hun }
      }
    }
    return best
  }

  describe(hand: number[], win: number, cut: number | null = null): Context {
    const c = this.context, blocked = c.blocked || c.discarded_hun || cut === this.hun
    const common = {
      discarded_hun: blocked, last_tile: c.remaining === 0, tian_ting: !!c.tian_ting && (cut === null || cut === c.drawn_tile),
      dealer: c.seat === 0, repeat: c.repeat ?? 0,
    }
    const ron = this.score(hand, win, { ...common, source: 'discard', earthly: c.seat !== 0 && c.first_draw && !c.has_draw_slot && cut === null })
    let self = this.score(hand, win, { ...common, source: 'self_draw', after_kong: c.has_draw_slot && c.after_kong && cut === null, heavenly: c.seat === 0 && c.opening && cut === null, earthly: c.seat !== 0 && c.first_draw && cut === null })
    if (this.config.four_hun_win && hand.filter(t => t === this.hun).length === 4) {
      const special = this.score(hand, win, { ...common, source: 'self_draw', four_hun: true })
      if (special && (!self || special.points > self.points)) self = special
    }
    const ronPoints = ron ? ron.points * (this.config.ron_all_pay ? 3 : 1) : 0
    const selfPoints = (self?.points ?? 0) * 3
    const points = ron ? ronPoints : selfPoints
    return {
      tile: win, points, ron_points: ronPoints, self_points: selfPoints, fan: ron?.fan ?? self?.fan ?? 0,
      can_ron: !!ron, can_self: !!self, kind: ron ? self ? 'dianhe' : 'ron_only' : self ? 'zimo' : 'wuyi',
      label: ron ? self ? `${points}点` : `仅点和\n${points}点` : self ? `仅自摸\n${points}点` : '未满足',
    }
  }
}
