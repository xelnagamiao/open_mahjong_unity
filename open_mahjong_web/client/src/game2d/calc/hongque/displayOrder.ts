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

/** Playing hands keep the fixed tile-table order, independent of the other tiles. */
export function handDisplayOrder(codes: string[]): string[] {
  return [...codes].sort()
}

export function sortHandCodes(codes: string[]): string[] {
  codes.splice(0, codes.length, ...handDisplayOrder(codes))
  return codes
}

export function sortMeldCodes(codes: string[]): string[] {
  codes.splice(0, codes.length, ...meldDisplayOrder(codes))
  return codes
}
