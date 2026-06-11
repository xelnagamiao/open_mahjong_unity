// 随机种子验证工具：在浏览器内 1:1 复现服务端的洗牌过程
//
// 服务端逻辑（open_mahjong_server/server/gamestate/public/random_seed_manager.py）：
//   commitment  = SHA256(format(master_seed, '064x') + salt)
//   round_seed  = SHA256(format(master_seed, '064x') + str(round_number))
//   随机座位     = random.Random(master_seed).shuffle(入场顺序玩家列表)
//   每局洗牌     = random.seed(round_seed); random.shuffle(tiles_list)
//
// 因此本模块实现了与 CPython random 模块一致的 MT19937（init_by_array 大整数播种、
// getrandbits 拒绝采样、Fisher-Yates shuffle），保证字节级一致。

// ---------------------------------------------------------------- SHA-256

const SHA256_K = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
]

/** 计算 UTF-8 字符串的 SHA-256，返回 64 位 hex 字符串 */
export function sha256Hex(message) {
  const bytes = new TextEncoder().encode(message)
  const bitLen = bytes.length * 8
  // 填充：0x80 + 0x00... + 64 位大端长度
  const padded = new Uint8Array(((bytes.length + 8) >> 6 << 6) + 64)
  padded.set(bytes)
  padded[bytes.length] = 0x80
  const view = new DataView(padded.buffer)
  view.setUint32(padded.length - 8, Math.floor(bitLen / 0x100000000))
  view.setUint32(padded.length - 4, bitLen >>> 0)

  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a
  let h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19
  const w = new Array(64)

  for (let offset = 0; offset < padded.length; offset += 64) {
    for (let i = 0; i < 16; i++) w[i] = view.getUint32(offset + i * 4)
    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3)
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10)
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7
    for (let i = 0; i < 64; i++) {
      const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)
      const ch = (e & f) ^ (~e & g)
      const temp1 = (h + S1 + ch + SHA256_K[i] + w[i]) >>> 0
      const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)
      const maj = (a & b) ^ (a & c) ^ (b & c)
      const temp2 = (S0 + maj) >>> 0
      h = g; g = f; f = e; e = (d + temp1) >>> 0
      d = c; c = b; b = a; a = (temp1 + temp2) >>> 0
    }
    h0 = (h0 + a) >>> 0; h1 = (h1 + b) >>> 0; h2 = (h2 + c) >>> 0; h3 = (h3 + d) >>> 0
    h4 = (h4 + e) >>> 0; h5 = (h5 + f) >>> 0; h6 = (h6 + g) >>> 0; h7 = (h7 + h) >>> 0
  }
  return [h0, h1, h2, h3, h4, h5, h6, h7].map(x => x.toString(16).padStart(8, '0')).join('')
}

function rotr(x, n) {
  return ((x >>> n) | (x << (32 - n))) >>> 0
}

// ------------------------------------------------- MT19937（与 CPython 一致）

const N = 624
const M = 397
const MATRIX_A = 0x9908b0df
const UPPER_MASK = 0x80000000
const LOWER_MASK = 0x7fffffff

/** 32 位模乘（a、b 均视作 uint32） */
function mul32(a, b) {
  return ((((a >>> 16) * b) << 16) + (a & 0xffff) * b) >>> 0
}

/** 与 random.Random(seed) / random.seed(seed) 行为一致的随机数发生器（seed 为 BigInt） */
export class PyRandom {
  constructor(seedBigInt) {
    this.mt = new Array(N)
    this.mti = N + 1
    this._seedBigInt(seedBigInt)
  }

  _initGenrand(s) {
    const mt = this.mt
    mt[0] = s >>> 0
    for (let i = 1; i < N; i++) {
      const prev = mt[i - 1] ^ (mt[i - 1] >>> 30)
      mt[i] = (mul32(prev, 1812433253) + i) >>> 0
    }
    this.mti = N
  }

  /** CPython random_seed：大整数取绝对值按 32 位小端拆分后 init_by_array */
  _seedBigInt(n) {
    if (n < 0n) n = -n
    const key = []
    if (n === 0n) key.push(0)
    while (n > 0n) {
      key.push(Number(n & 0xffffffffn))
      n >>= 32n
    }
    this._initByArray(key)
  }

  _initByArray(key) {
    const mt = this.mt
    this._initGenrand(19650218)
    let i = 1, j = 0
    let k = Math.max(N, key.length)
    for (; k; k--) {
      const prev = mt[i - 1] ^ (mt[i - 1] >>> 30)
      mt[i] = (((mt[i] ^ mul32(prev, 1664525)) >>> 0) + key[j] + j) >>> 0
      i++; j++
      if (i >= N) { mt[0] = mt[N - 1]; i = 1 }
      if (j >= key.length) j = 0
    }
    for (k = N - 1; k; k--) {
      const prev = mt[i - 1] ^ (mt[i - 1] >>> 30)
      mt[i] = (((mt[i] ^ mul32(prev, 1566083941)) >>> 0) - i) >>> 0
      i++
      if (i >= N) { mt[0] = mt[N - 1]; i = 1 }
    }
    mt[0] = 0x80000000
  }

  _genrandUint32() {
    const mt = this.mt
    if (this.mti >= N) {
      for (let kk = 0; kk < N - M; kk++) {
        const y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK)
        mt[kk] = (mt[kk + M] ^ (y >>> 1) ^ ((y & 1) ? MATRIX_A : 0)) >>> 0
      }
      for (let kk = N - M; kk < N - 1; kk++) {
        const y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK)
        mt[kk] = (mt[kk + (M - N)] ^ (y >>> 1) ^ ((y & 1) ? MATRIX_A : 0)) >>> 0
      }
      const y = (mt[N - 1] & UPPER_MASK) | (mt[0] & LOWER_MASK)
      mt[N - 1] = (mt[M - 1] ^ (y >>> 1) ^ ((y & 1) ? MATRIX_A : 0)) >>> 0
      this.mti = 0
    }
    let y = mt[this.mti++]
    y ^= y >>> 11
    y = (y ^ ((y << 7) & 0x9d2c5680)) >>> 0
    y = (y ^ ((y << 15) & 0xefc60000)) >>> 0
    y ^= y >>> 18
    return y >>> 0
  }

  /** random.getrandbits(k)，k ∈ [1, 32] */
  getrandbits(k) {
    return this._genrandUint32() >>> (32 - k)
  }

  /** random._randbelow_with_getrandbits(n) */
  randbelow(n) {
    if (n <= 0) return 0
    const k = 32 - Math.clz32(n)
    let r = this.getrandbits(k)
    while (r >= n) r = this.getrandbits(k)
    return r
  }

  /** random.shuffle(x)：Fisher-Yates，自尾向前 */
  shuffle(arr) {
    for (let i = arr.length - 1; i >= 1; i--) {
      const j = this.randbelow(i + 1)
      const tmp = arr[i]
      arr[i] = arr[j]
      arr[j] = tmp
    }
  }
}

// -------------------------------------------------------------- 业务封装

/** 解析主种子输入：支持十进制或 hex（可带 0x 前缀；64 位纯 hex 也可） */
export function parseMasterSeed(input) {
  const text = String(input || '').trim().toLowerCase().replace(/\s+/g, '')
  if (!text) throw new Error('请输入主种子')
  if (text.startsWith('0x')) return BigInt(text)
  if (/^[0-9]+$/.test(text)) return BigInt(text)
  if (/^[0-9a-f]+$/.test(text)) return BigInt('0x' + text)
  throw new Error('主种子格式错误：仅支持十进制或十六进制')
}

function masterSeedHex64(masterSeed) {
  return masterSeed.toString(16).padStart(64, '0')
}

/** derive_round_seed(master_seed, round_number) */
export function deriveRoundSeed(masterSeed, roundNumber) {
  return BigInt('0x' + sha256Hex(masterSeedHex64(masterSeed) + String(roundNumber)))
}

/** compute_commitment(master_seed, salt)，返回 64 位 hex */
export function computeCommitmentHex(masterSeed, salt) {
  return sha256Hex(masterSeedHex64(masterSeed) + String(salt || '').trim())
}

/** 开局随机座位：random.Random(master_seed).shuffle(入场顺序列表)，返回座位 0-3 的元素 */
export function shuffleSeats(masterSeed, entryList) {
  const seats = [...entryList]
  new PyRandom(masterSeed).shuffle(seats)
  return seats
}

/**
 * 构建初始牌堆（与服务端 init_tiles 一致的初始顺序）：
 * 万/饼/条/字各 4 张连续排列；国标追加 8 张花牌；立直赤宝牌按 remove+append 替换。
 */
export function buildBaseTiles(rule, redDora) {
  const order = []
  for (let t = 11; t <= 19; t++) order.push(t)
  for (let t = 21; t <= 29; t++) order.push(t)
  for (let t = 31; t <= 39; t++) order.push(t)
  order.push(41, 42, 43, 44, 45, 46, 47)
  const tiles = []
  for (const t of order) tiles.push(t, t, t, t)
  if (rule === 'guobiao') {
    tiles.push(51, 52, 53, 54, 55, 56, 57, 58)
  }
  if (rule === 'riichi' && redDora) {
    for (const [aka, normal] of [[105, 15], [205, 25], [305, 35]]) {
      const idx = tiles.indexOf(normal)
      if (idx >= 0) {
        tiles.splice(idx, 1)
        tiles.push(aka)
      }
    }
  }
  return tiles
}

/**
 * 复现单局洗牌与发牌。
 * 返回 { roundSeedHex, wallAfterShuffle, hands: [4][], wallRemaining, riichi?: { doraIndicator, uraDoraIndicator } }
 */
export function simulateRound(rule, masterSeed, roundNumber, redDora) {
  const roundSeed = deriveRoundSeed(masterSeed, roundNumber)
  const tiles = buildBaseTiles(rule, redDora)
  new PyRandom(roundSeed).shuffle(tiles)
  const wallAfterShuffle = [...tiles]

  // 发牌：每人 13 张依次从牌堆头部抓取；国标庄家额外 1 张
  const hands = [[], [], [], []]
  for (let p = 0; p < 4; p++) {
    for (let n = 0; n < 13; n++) hands[p].push(tiles.shift())
  }
  if (rule === 'guobiao') {
    hands[0].push(tiles.shift())
  }

  const result = {
    roundSeedHex: roundSeed.toString(16).padStart(64, '0'),
    wallAfterShuffle,
    hands,
    wallRemaining: tiles,
  }
  if (rule === 'riichi') {
    // 王牌区为牌山最后 14 张；倒数第 6 张为宝牌指示牌，倒数第 5 张为里宝牌指示牌
    result.riichi = {
      doraIndicator: tiles[tiles.length - 6],
      uraDoraIndicator: tiles[tiles.length - 5],
    }
  }
  return result
}
