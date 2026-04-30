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
