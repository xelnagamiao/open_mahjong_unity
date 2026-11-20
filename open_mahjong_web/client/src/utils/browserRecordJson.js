import { sha256Hex } from './seedVerify.js'

export function requireData(condition, reason) {
  if (!condition) throw new Error('牌谱迁移异常（' + reason + '）')
}

export function canonical(value) {
  if (Array.isArray(value)) return '[' + value.map((item) => canonical(item) ?? 'null').join(',') + ']'
  if (value && typeof value === 'object')
    return (
      '{' +
      Object.keys(value)
        .filter((key) => value[key] !== undefined)
        .sort()
        .map((key) => JSON.stringify(key) + ':' + canonical(value[key]))
        .join(',') +
      '}'
    )
  if (typeof value === 'bigint') return value.toString()
  if (typeof value !== 'number') return JSON.stringify(value)
  requireData(Number.isFinite(value), '非有限数字')
  if (Number.isInteger(value)) return BigInt(value).toString()
  const [mantissa, exponent] = Math.abs(value).toPrecision(17).toLowerCase().split('e')
  const point = (mantissa.includes('.') ? mantissa.indexOf('.') : mantissa.length) + Number(exponent || 0)
  const digits = mantissa.replace('.', '')
  let text = point <= 0 ? '0.' + '0'.repeat(-point) + digits : digits.padEnd(point, '0')
  if (point > 0 && point < digits.length) text = digits.slice(0, point) + '.' + digits.slice(point)
  return (value < 0 ? '-' : '') + (text.includes('.') ? text.replace(/0+$/, '').replace(/\.$/, '') : text)
}

export const digest = (value) => sha256Hex(canonical(value))
const namespace = 'c2c84d54322a4fb49ad054aee59b1ce3'
const fromHex = (hex) => Uint8Array.from(hex.match(/../g), (octet) => parseInt(octet, 16))

// SHA-1 for deterministic UUIDv5 identifiers. Synchronous conversion keeps
// IndexedDB's versionchange transaction active, also on offline HTTP origins.
export function identity(...parts) {
  const name = new TextEncoder().encode(canonical(parts))
  const size = 16 + name.length
  const bytes = new Uint8Array(Math.ceil((size + 9) / 64) * 64)
  bytes.set(fromHex(namespace))
  bytes.set(name, 16)
  bytes[size] = 128
  const view = new DataView(bytes.buffer)
  view.setUint32(bytes.length - 4, size * 8)
  const h = [0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476, 0xc3d2e1f0]
  const rot = (x, n) => (x << n) | (x >>> (32 - n))
  const words = new Uint32Array(80)
  for (let offset = 0; offset < bytes.length; offset += 64) {
    let [a, b, c, d, e] = h
    for (let i = 0; i < 80; i++) {
      words[i] =
        i < 16
          ? view.getUint32(offset + 4 * i)
          : rot(words[i - 3] ^ words[i - 8] ^ words[i - 14] ^ words[i - 16], 1)
      const f = i < 20 ? (b & c) | (~b & d) : i < 40 || i >= 60 ? b ^ c ^ d : (b & c) | (b & d) | (c & d)
      const k = [0x5a827999, 0x6ed9eba1, 0x8f1bbcdc, 0xca62c1d6][Math.floor(i / 20)]
      const sum = (rot(a, 5) + f + e + k + words[i]) >>> 0
      e = d
      d = c
      c = rot(b, 30)
      b = a
      a = sum
    }
    for (const [i, n] of [a, b, c, d, e].entries()) h[i] = (h[i] + n) >>> 0
  }
  const hex = h
    .slice(0, 4)
    .map((n) => n.toString(16).padStart(8, '0'))
    .join('')
  return [
    hex.slice(0, 8),
    hex.slice(8, 12),
    '5' + hex.slice(13, 16),
    ((parseInt(hex[16], 16) & 3) | 8).toString(16) + hex.slice(17, 20),
    hex.slice(20),
  ].join('-')
}

export function pairedId(matchId) {
  const bits = [...fromHex(sha256Hex(matchId))].map((n) => n.toString(2).padStart(8, '0')).join('')
  return (
    'P' +
    bits
      .slice(0, 75)
      .match(/.{5}/g)
      .map((chunk) => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'[parseInt(chunk, 2)])
      .join('')
  )
}

// JSON.parse normally rounds large integer seeds before its reviver runs.
export function parseReplayJSON(text) {
  const token = /"(?:\\.|[^"\\])*"|true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/y
  let pos = 0
  function spaces() {
    while (pos < text.length && /[\t\r\n ]/.test(text[pos])) pos++
  }
  function read() {
    spaces()
    const start = text[pos]
    if (start === '[' || start === '{') {
      const object = start === '{',
        end = object ? '}' : ']'
      const value = object ? {} : []
      pos++
      spaces()
      if (text[pos] === end) {
        pos++
        return value
      }
      for (;;) {
        if (object) {
          const key = read()
          spaces()
          requireData(typeof key === 'string' && text[pos++] === ':', 'JSON 键')
          Object.defineProperty(value, key, {
            value: read(),
            enumerable: true,
            writable: true,
            configurable: true,
          })
        } else value.push(read())
        spaces()
        if (text[pos] === end) {
          pos++
          return value
        }
        requireData(text[pos++] === ',', 'JSON 分隔符')
      }
    }
    token.lastIndex = pos
    const match = token.exec(text)
    requireData(match, 'JSON 数值或字符串')
    pos = token.lastIndex
    const word = match[0]
    if (/^-?\d+$/.test(word) && !Number.isSafeInteger(Number(word))) return BigInt(word)
    return JSON.parse(word)
  }
  const result = read()
  spaces()
  requireData(pos === text.length, 'JSON 末尾')
  return result
}

export const stringifyReplayJSON = (value) => canonical(value)
export const decoded = (value) => (typeof value === 'string' ? parseReplayJSON(value) : value)
