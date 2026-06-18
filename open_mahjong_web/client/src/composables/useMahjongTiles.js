// 国标麻将牌号 / 文本 / Unicode 字符的统一映射
// 牌号编码：11-19 万 / 21-29 饼/筒 / 31-39 条/索 / 41-44 东南西北 / 45-47 中白发 / 51-58 花牌

// Unicode 麻将字符（U+1F000 区段）
export const TILE_UNICODE = {
  11: '\u{1F007}', 12: '\u{1F008}', 13: '\u{1F009}', 14: '\u{1F00A}', 15: '\u{1F00B}',
  16: '\u{1F00C}', 17: '\u{1F00D}', 18: '\u{1F00E}', 19: '\u{1F00F}',
  21: '\u{1F019}', 22: '\u{1F01A}', 23: '\u{1F01B}', 24: '\u{1F01C}', 25: '\u{1F01D}',
  26: '\u{1F01E}', 27: '\u{1F01F}', 28: '\u{1F020}', 29: '\u{1F021}',
  31: '\u{1F010}', 32: '\u{1F011}', 33: '\u{1F012}', 34: '\u{1F013}', 35: '\u{1F014}',
  36: '\u{1F015}', 37: '\u{1F016}', 38: '\u{1F017}', 39: '\u{1F018}',
  41: '\u{1F000}', 42: '\u{1F001}', 43: '\u{1F002}', 44: '\u{1F003}',
  45: '\u{1F004}', 46: '\u{1F006}', 47: '\u{1F005}',
  51: '\u{1F022}', 52: '\u{1F023}', 53: '\u{1F024}', 54: '\u{1F025}',
  55: '\u{1F026}', 56: '\u{1F027}', 57: '\u{1F028}', 58: '\u{1F029}'
};

export const TILE_NAME = {
  11: '一万', 12: '二万', 13: '三万', 14: '四万', 15: '五万',
  16: '六万', 17: '七万', 18: '八万', 19: '九万',
  21: '一饼', 22: '二饼', 23: '三饼', 24: '四饼', 25: '五饼',
  26: '六饼', 27: '七饼', 28: '八饼', 29: '九饼',
  31: '一条', 32: '二条', 33: '三条', 34: '四条', 35: '五条',
  36: '六条', 37: '七条', 38: '八条', 39: '九条',
  41: '东', 42: '南', 43: '西', 44: '北',
  45: '中', 46: '白', 47: '发',
  51: '春', 52: '夏', 53: '秋', 54: '冬',
  55: '梅', 56: '兰', 57: '竹', 58: '菊'
};

/** 单张牌转为 m/p/s/z 简写（与 tilesToNotationText 字序一致：z 为 1东2南3西4北5白6发7中） */
export function tileIdToNotation(id) {
  if (id == null || Number.isNaN(id)) return '?'
  if (id >= 11 && id <= 19) return `${id - 10}m`
  if (id >= 21 && id <= 29) return `${id - 20}p`
  if (id >= 31 && id <= 39) return `${id - 30}s`
  const z = { 41: '1z', 42: '2z', 43: '3z', 44: '4z', 46: '5z', 47: '6z', 45: '7z' }
  if (z[id]) return z[id]
  if (id >= 51 && id <= 58) return `${id - 50}花`
  return `#${id}`
}

// 简短文本（一行）
export const TILE_SHORT = {
  41: '东', 42: '南', 43: '西', 44: '北',
  45: '中', 46: '白', 47: '发',
  51: '春', 52: '夏', 53: '秋', 54: '冬',
  55: '梅', 56: '兰', 57: '竹', 58: '菊'
};

// 数牌花色色调（用于卡片背景）
export function tileColor(id) {
  if (id >= 11 && id <= 19) return '#3a7afe';
  if (id >= 21 && id <= 29) return '#0db368';
  if (id >= 31 && id <= 39) return '#e55353';
  if (id >= 41 && id <= 44) return '#7755c4';
  if (id === 45) return '#d63b3b';
  if (id === 46) return '#1e88e5';
  if (id === 47) return '#2e7d32';
  if (id >= 51 && id <= 58) return '#d8a32a';
  return '#666';
}

// 标准 34 种麻将牌（不含花牌），用于面板
export const STANDARD_TILES = [
  11, 12, 13, 14, 15, 16, 17, 18, 19,
  21, 22, 23, 24, 25, 26, 27, 28, 29,
  31, 32, 33, 34, 35, 36, 37, 38, 39,
  41, 42, 43, 44, 45, 46, 47
];

export const FLOWER_TILES = [51, 52, 53, 54, 55, 56, 57, 58];

// 副露文字描述（用于结果展示）
export function combinationLabel(code) {
  if (!code || code.length < 3) return code;
  const prefix = code[0];
  const tileId = parseInt(code.slice(1), 10);
  const name = TILE_NAME[tileId] || code.slice(1);
  switch (prefix) {
    case 's': return `顺子 ${name}-${TILE_NAME[tileId - 1] || ''}-${TILE_NAME[tileId + 1] || ''}`;
    case 'S': return `暗顺 ${name}（中心）`;
    case 'k': return `明刻 ${name}`;
    case 'K': return `暗刻 ${name}`;
    case 'g': return `明杠 ${name}`;
    case 'G': return `暗杠 ${name}`;
    case 'q': return `雀头 ${name}`;
    case 'z': return `组合龙`;
    default: return code;
  }
}

// 手牌按规则排序：万 < 饼 < 条 < 风 < 三元 < 花
export function sortTiles(tiles) {
  return [...tiles].sort((a, b) => a - b);
}

// 把项目内部牌号转为「数字+花色字母」简写（万 m / 筒 p / 索 s / 字 z）
// z：1东 2南 3西 4北 5白 6发 7中
export function tilesToNotationText(tiles) {
  if (!tiles || tiles.length === 0) return ''
  const groups = { m: [], p: [], s: [], z: [] }
  const sorted = [...tiles].sort((a, b) => a - b)
  for (const id of sorted) {
    if (id >= 11 && id <= 19) groups.m.push(id - 10)
    else if (id >= 21 && id <= 29) groups.p.push(id - 20)
    else if (id >= 31 && id <= 39) groups.s.push(id - 30)
    else if (id === 41) groups.z.push(1)
    else if (id === 42) groups.z.push(2)
    else if (id === 43) groups.z.push(3)
    else if (id === 44) groups.z.push(4)
    else if (id === 46) groups.z.push(5)
    else if (id === 47) groups.z.push(6)
    else if (id === 45) groups.z.push(7)
  }
  let out = ''
  for (const suit of ['m', 'p', 's', 'z']) {
    if (groups[suit].length === 0) continue
    out += groups[suit].join('') + suit
  }
  return out
}

// 解析「数字+花色字母」简写为项目内部牌号数组；解析失败抛出 Error
export function parseNotationText(input) {
  if (!input) return []
  const text = input.replace(/\s+/g, '').toLowerCase()
  if (!text) return []
  const result = []
  const buffer = []
  for (const ch of text) {
    if (ch >= '0' && ch <= '9') {
      buffer.push(ch)
      continue
    }
    if (ch === 'm' || ch === 'p' || ch === 's' || ch === 'z') {
      if (buffer.length === 0) {
        throw new Error(`「${ch}」前缺少数字`)
      }
      for (const numChar of buffer) {
        const num = parseInt(numChar, 10)
        if (ch === 'z') {
          if (num < 1 || num > 7) throw new Error(`字牌仅允许 1-7：z${num}`)
          // 1东 2南 3西 4北 5白 6发 7中
          const map = { 1: 41, 2: 42, 3: 43, 4: 44, 5: 46, 6: 47, 7: 45 }
          result.push(map[num])
        } else {
          if (num === 0) {
            // 0m / 0p / 0s 视为赤 5：万/饼/条 的 5
            const base = ch === 'm' ? 10 : ch === 'p' ? 20 : 30
            result.push(base + 5)
          } else {
            const base = ch === 'm' ? 10 : ch === 'p' ? 20 : 30
            result.push(base + num)
          }
        }
      }
      buffer.length = 0
    } else {
      throw new Error(`非法字符：${ch}`)
    }
  }
  if (buffer.length > 0) throw new Error('数字之后必须紧跟花色字母 m/p/s/z')
  return result
}

/** 解析副露槽位简写，返回可自动锁定或待选的副露类型 */
export function parseMeldSlotInput(text) {
  const raw = (text || '').replace(/\s+/g, '').toLowerCase()
  if (!raw) return { auto: null, options: [] }

  const numSuit = raw.match(/^(\d+)([mps])$/)
  if (numSuit) {
    const [, digits, suit] = numSuit
    const nums = [...digits].map((d) => parseInt(d, 10))
    const base = suit === 'm' ? 10 : suit === 'p' ? 20 : 30

    if (nums.length === 3 && nums[0] + 1 === nums[1] && nums[1] + 1 === nums[2]) {
      const center = base + nums[1]
      if (center % 10 >= 2 && center % 10 <= 8) {
        return { auto: { kind: 's', tileId: center, label: '明顺' }, options: [] }
      }
    }
    if (nums.length === 3 && nums.every((n) => n === nums[0]) && nums[0] >= 1 && nums[0] <= 9) {
      const tileId = base + nums[0]
      return { auto: null, options: [{ kind: 'k', tileId, label: '明刻' }] }
    }
    if (nums.length === 4 && nums.every((n) => n === nums[0]) && nums[0] >= 1 && nums[0] <= 9) {
      const tileId = base + nums[0]
      return {
        auto: null,
        options: [
          { kind: 'g', tileId, label: '明杠' },
          { kind: 'G', tileId, label: '暗杠' },
        ],
      }
    }
  }

  const zSuit = raw.match(/^(\d+)(z)$/)
  if (zSuit) {
    const [, digits] = zSuit
    const nums = [...digits].map((d) => parseInt(d, 10))
    const zMap = { 1: 41, 2: 42, 3: 43, 4: 44, 5: 46, 6: 47, 7: 45 }
    if (nums.length === 3 && nums.every((n) => n === nums[0]) && zMap[nums[0]]) {
      return { auto: null, options: [{ kind: 'k', tileId: zMap[nums[0]], label: '明刻' }] }
    }
    if (nums.length === 4 && nums.every((n) => n === nums[0]) && zMap[nums[0]]) {
      const tileId = zMap[nums[0]]
      return {
        auto: null,
        options: [
          { kind: 'g', tileId, label: '明杠' },
          { kind: 'G', tileId, label: '暗杠' },
        ],
      }
    }
  }

  return { auto: null, options: [] }
}

/** 副露展示用牌面列表 */
export function meldDisplayTiles(kind, tileId) {
  if (kind === 's' || kind === 'S') return [tileId - 1, tileId, tileId + 1]
  if (kind === 'g' || kind === 'G') return [tileId, tileId, tileId, tileId]
  return [tileId, tileId, tileId]
}

/** 从完整 136 张牌墙中随机抽取 count 张（每种牌至多 4 张） */
export function randomHandTiles(count) {
  const wall = []
  for (let d = 0; d < 4; d++) {
    for (let t = 11; t <= 19; t++) wall.push(t)
    for (let t = 21; t <= 29; t++) wall.push(t)
    for (let t = 31; t <= 39; t++) wall.push(t)
    for (const t of [41, 42, 43, 44, 45, 46, 47]) wall.push(t)
  }
  for (let i = wall.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1))
    ;[wall[i], wall[j]] = [wall[j], wall[i]]
  }
  return wall.slice(0, count)
}

