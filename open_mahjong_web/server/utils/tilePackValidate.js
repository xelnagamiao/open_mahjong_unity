const path = require('path');
const { readZip } = require('./zipEntries');
const { MAX_CFG_BYTES, parseTilePackConfig } = require('./tilePackConfig');

const MAX_IMAGE_EDGE = 4096;
const MAX_PNG_BYTES = 4 * 1024 * 1024;
const MAX_UNCOMPRESSED = 128 * 1024 * 1024;
const MAX_BG_PNG_BYTES = 8 * 1024 * 1024;
const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const PREVIEW_FACE_IDS = [11, 15, 19, 21, 25, 29, 31, 35, 39, 41, 45, 46, 47, 51, 105];

function isStandardFaceId(tileId) {
  if (tileId === 59 || tileId === 2 || tileId === 105 || tileId === 205 || tileId === 305) return true;
  const suit = Math.floor(tileId / 10);
  const rank = tileId % 10;
  if (suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9) return true;
  if (suit === 4 && rank >= 1 && rank <= 7) return true;
  if (suit === 5 && rank >= 1 && rank <= 8) return true;
  return false;
}

function isPng(bytes) {
  return Buffer.isBuffer(bytes) && bytes.length >= 8 && bytes.subarray(0, 8).equals(PNG_SIG);
}

function readPngSize(bytes) {
  if (!isPng(bytes) || bytes.length < 33) return null;
  if (bytes[8] !== 0 || bytes[9] !== 0 || bytes[10] !== 0 || bytes[11] !== 13) return null;
  if (bytes[12] !== 0x49 || bytes[13] !== 0x48 || bytes[14] !== 0x44 || bytes[15] !== 0x52) return null;
  const width = bytes.readUInt32BE(16);
  const height = bytes.readUInt32BE(20);
  if (width <= 0 || height <= 0) return null;
  return { width, height };
}

function fileName(full) {
  return path.posix.basename(String(full || '').replace(/\\/g, '/'));
}

function isHandBgFileName(name) {
  const lower = fileName(name).toLowerCase();
  return lower.includes('hand-bg') || lower.includes('handbg') || lower === 'front.png';
}

function isHandBackFileName(name) {
  const lower = fileName(name).toLowerCase();
  if (isHandBgFileName(lower)) return false;
  return (
    lower.includes('hand-back')
    || lower.includes('handback')
    || lower.includes('hand_back')
    || lower === '0.png'
    || lower === 'back.png'
    || lower.includes('ura-back')
  );
}

function isTableBgFileName(name) {
  const lower = fileName(name).toLowerCase();
  if (isHandBgFileName(lower)) return false;
  return (
    lower.includes('table-bg')
    || lower.includes('tablebg')
    || lower.includes('table_bg')
    || lower === 'table.png'
    || lower.includes('3d-bg')
  );
}

function pushWarning(warnings, text) {
  if (warnings.length < 40) warnings.push(text);
}

function validateTileFace(zipBuffer) {
  const entries = readZip(zipBuffer, { maxUncompressed: MAX_UNCOMPRESSED });
  const shared = new Map();
  const warnings = [];
  const previews = [];
  const configs = entries.filter((e) => /\.cfg$/i.test(fileName(e.name)));
  if (configs.length !== 1 || fileName(configs[0].name).toLowerCase() !== 'tiles.cfg') return { error: '牌面包需包含一个 tiles.cfg 文件' };
  const surfaces = new Map();
  let config = null;
  if (configs.length) {
    if (configs[0].data.length > MAX_CFG_BYTES) return { error: '牌面配置超过 64KB' };
    try {
      const text = new TextDecoder('utf-8', { fatal: true }).decode(configs[0].data);
      config = parseTilePackConfig(text, isStandardFaceId);
    } catch (err) { return { error: err.message }; }
  }
  for (const entry of entries) {
    const full = entry.name.replace(/\\/g, '/').replace(/^\/+/, '');
    const name = fileName(full);
    if (full.split('/').some((part) => /^(hand|table|手牌牌面|3d牌面)$/i.test(part))) {
      return { error: '牌面包需使用一套牌号 PNG 和 tiles.cfg' };
    }
    if (/\.cfg$/i.test(name) || name.toLowerCase() === 'manifest.json') continue;
    if (!name.toLowerCase().endsWith('.png')) {
      if (!name.startsWith('.')) pushWarning(warnings, `已忽略非 PNG: ${name}`);
      continue;
    }
    const surface = /^(hand-bg|hand-back|table-bg|table-back)\.png$/i.test(name);
    if (!surface && entry.data.length > MAX_PNG_BYTES) {
      pushWarning(warnings, `单张超过 4MB，已跳过: ${name}`);
      continue;
    }
    if (surface) {
      if (entry.data.length > MAX_BG_PNG_BYTES) return { error: `背景图片超过 8MB: ${name}` };
      if (surfaces.has(name.toLowerCase())) return { error: `背景图片重复: ${name}` };
      const size = readPngSize(entry.data);
      if (!size || size.width > MAX_IMAGE_EDGE || size.height > MAX_IMAGE_EDGE) return { error: `背景 PNG 无效: ${name}` };
      surfaces.set(name.toLowerCase(), entry.data);
      continue;
    }
    const idPart = name.replace(/\.png$/i, '');
    const tileId = Number(idPart);
    if (!/^[0-9]+$/.test(idPart) || String(tileId) !== idPart || !isStandardFaceId(tileId)) {
      pushWarning(warnings, `无法识别的牌面文件名: ${name}`);
      continue;
    }
    const size = readPngSize(entry.data);
    if (!size || size.width > MAX_IMAGE_EDGE || size.height > MAX_IMAGE_EDGE) {
      pushWarning(warnings, `尺寸不合法（需 ≤4096）: ${name}`);
      continue;
    }
    const target = shared;
    if (target.has(tileId)) return { error: `牌面代号重复: ${full}` };
    target.set(tileId, entry.data);
  }
  if (!shared.size) return { error: '压缩包内没有可识别的 PNG 牌面' };
  for (const key of ['hand_background', 'hand_back', 'table_background', 'table_back']) {
    const name = config?.appearance?.[key];
    if (name && !surfaces.has(name)) return { error: `牌面包缺少背景图片: ${name}` };
  }
  const missing = standardFaceIds().filter((id) => !shared.has(id)).length;
  if (missing) pushWarning(warnings, `缺 ${missing} 张，对局中将回退官方牌面`);
  for (const id of PREVIEW_FACE_IDS) if (shared.has(id))
    previews.push({ name: `${id}.png`, label: `${id}.png`, data: shared.get(id) });
  return {
    meta: {
      format_version: 2,
      shared_count: shared.size,
      config_file: configs[0] ? fileName(configs[0].name) : null,
      layout: config,
      warnings,
      previews: previews.map((p) => ({ name: p.name, label: p.label })),
    },
    previews,
  };
}

function standardFaceIds() {
  const ids = [];
  for (let suit = 1; suit <= 3; suit += 1) {
    for (let rank = 1; rank <= 9; rank += 1) ids.push(suit * 10 + rank);
  }
  for (let rank = 1; rank <= 7; rank += 1) ids.push(40 + rank);
  for (let rank = 1; rank <= 8; rank += 1) ids.push(50 + rank);
  ids.push(59, 105, 205, 305, 2);
  return ids;
}

function validateTileBackground(zipBuffer) {
  const entries = readZip(zipBuffer, { maxUncompressed: 20 * 1024 * 1024 });
  let handBg = null;
  let handBack = null;
  let tableBg = null;
  for (const entry of entries) {
    const name = fileName(entry.name);
    if (!name.toLowerCase().endsWith('.png')) continue;
    if (isHandBgFileName(name)) handBg = entry;
    else if (isHandBackFileName(name)) handBack = entry;
    else if (isTableBgFileName(name)) tableBg = entry;
  }
  if (!handBg && !handBack && !tableBg) {
    return { error: '压缩包需包含 hand-back.png / hand-bg.png 或 table-bg.png' };
  }

  const picked = [
    handBg && { key: 'hand-bg.png', label: fileName(handBg.name), data: handBg.data },
    handBack && { key: 'hand-back.png', label: fileName(handBack.name), data: handBack.data },
    tableBg && { key: 'table-bg.png', label: fileName(tableBg.name), data: tableBg.data },
  ].filter(Boolean);

  for (const item of picked) {
    if (item.data.length > MAX_BG_PNG_BYTES) {
      return { error: '图片超过 8MB，请压缩后再上传' };
    }
    if (!isPng(item.data)) {
      return { error: `不是 PNG: ${item.label}` };
    }
    const size = readPngSize(item.data);
    if (!size) {
      return { error: `不是 PNG: ${item.label}` };
    }
  }

  const files = picked.map((p) => p.label);
  const previews = picked.map((p) => ({ name: p.key, label: p.label, data: p.data }));
  return {
    meta: {
      files,
      warnings: [],
      previews: previews.map((p) => ({ name: p.name, label: p.label })),
    },
    previews,
  };
}

function validateZip(kind, zipBuffer) {
  if (kind === 'tile_background') return validateTileBackground(zipBuffer);
  return validateTileFace(zipBuffer);
}

module.exports = {
  MAX_UNCOMPRESSED,
  validateZip,
};
