import { classifyMeld, parseTile } from './index.ts'

const mod = (n: number) => ((n % 14) + 14) % 14
const colour = (code: string) => parseTile(code).colour
const number = (code: string) => parseTile(code).number
const earlier = (a: string[], b: string[] | null) => !b || a.join(',') < b.join(',')

/** Display only: preserve protocol/scoring order and choose a continuous run in either direction. */
export function meldDisplayOrder(codes: string[]): string[] {
  const source = [...codes]
  const shape = classifyMeld(source)
  if (!shape) return source
  if (shape.baseKind === 'sequence') {
    const ascending = source.sort((a, b) => number(a) - number(b))
    const descending = [...ascending].reverse()
    return earlier(ascending, descending) ? ascending : descending
  }
  let best: string[] | null = null
  const byColour = new Map(source.map(code => [colour(code), code]))
  for (const start of source) {
    for (const direction of [1, -1]) {
      const candidate = source.map((_, i) => byColour.get(mod(colour(start) + direction * shape.colourStep * i)))
      if (candidate.every((code): code is string => code !== undefined) && earlier(candidate, best)) best = candidate
    }
  }
  return best ?? source
}

export function handDisplayOrder(codes: string[]): string[] {
  if (!codes.length) return []
  if (classifyMeld(codes)) return meldDisplayOrder(codes)
  const natural = [...codes].sort()
  const colours = [...new Set(codes.map(colour))].sort((a, b) => a - b)
  if (colours.length < 2) return natural
  const gaps = colours.map((c, i) => mod(colours[(i + 1) % colours.length] - c))
  const maxGap = Math.max(...gaps)
  if (14 + colours[0] - colours[colours.length - 1] === maxGap) return natural
  let best: string[] | null = null
  for (let i = 0; i < colours.length; i++) {
    if (gaps[i] !== maxGap) continue
    const left = colours[i], right = colours[(i + 1) % colours.length]
    const forward = [...codes].sort((a, b) => mod(colour(a) - right) - mod(colour(b) - right) || number(a) - number(b))
    const backward = [...codes].sort((a, b) => mod(left - colour(a)) - mod(left - colour(b)) || number(a) - number(b))
    if (earlier(forward, best)) best = forward
    if (earlier(backward, best)) best = backward
  }
  return best ?? natural
}

export function sortHandCodes(codes: string[]): string[] {
  codes.splice(0, codes.length, ...handDisplayOrder(codes))
  return codes
}

export function sortMeldCodes(codes: string[]): string[] {
  codes.splice(0, codes.length, ...meldDisplayOrder(codes))
  return codes
}
