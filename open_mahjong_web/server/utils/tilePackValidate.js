const path = require('path');
const { readZip } = require('./zipEntries');

const MAX_IMAGE_EDGE = 1024;
const MAX_PNG_BYTES = 500 * 1024;
const MAX_UNCOMPRESSED = 20 * 1024 * 1024;
const MAX_BG_PNG_BYTES = 8 * 1024 * 1024;
const EXPECTED_FORMAT = 'om-tilepack';
const EXPECTED_FAMILY = 'standard';
const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const PREVIEW_FACE_IDS = [11, 15, 19, 21, 25, 29, 31, 35, 39, 41, 45, 46, 47, 51, 105];

function isStandardFaceId(tileId) {
  if (tileId === 2 || tileId === 105 || tileId === 205 || tileId === 305) return true;
  const suit = Math.floor(tileId / 10);
  const rank = tileId % 10;
  if (suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9) return true;
  if (suit === 4 && rank >= 1 && rank <= 7) return true;
  if (suit === 5 && rank >= 1 && rank <= 8) return true;
  return false;
}

function pathHasFolder(fullPath, folder) {
  const full = String(fullPath || '').replace(/\\/g, '/').replace(/^\/+/, '');
  const needle = String(folder || '').replace(/^\/+|\/+$/g, '');
  if (!full || !needle) return false;
  const lowerFull = full.toLowerCase();
  const lowerNeedle = needle.toLowerCase();
  return lowerFull.startsWith(`${lowerNeedle}/`) || lowerFull.includes(`/${lowerNeedle}/`);
}

function isHandFolder(fullPath) {
  return pathHasFolder(fullPath, 'hand') || pathHasFolder(fullPath, '手牌牌面');
}

function isTableFolder(fullPath) {
  return (
    pathHasFolder(fullPath, 'table')
    || pathHasFolder(fullPath, '3D牌面')
    || pathHasFolder(fullPath, '3d牌面')
  );
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

function validateManifest(text) {
  const raw = String(text || '').trim();
  if (!raw) return null;
  let manifest;
  try {
    manifest = JSON.parse(raw);
  } catch {
    return 'manifest.json 无法解析';
  }
  if (!manifest || typeof manifest !== 'object') return null;
  if (manifest.format && String(manifest.format).toLowerCase() !== EXPECTED_FORMAT) {
    return 'manifest.format 必须是 om-tilepack';
  }
  if (manifest.family && String(manifest.family).toLowerCase() !== EXPECTED_FAMILY) {
    return '仅支持 family=standard 的牌面包（虹雀不可自定义）';
  }
  return null;
}

function pushWarning(warnings, text) {
  if (warnings.length < 40) warnings.push(text);
}

function previewName(label) {
  return String(label).replace(/[\\/]+/g, '_');
}

function validateTileFace(zipBuffer) {
  const entries = readZip(zipBuffer, { maxUncompressed: MAX_UNCOMPRESSED });
  const hand = new Map();
  const table = new Map();
  const warnings = [];
  const previews = [];

  const manifest = entries.find((e) => fileName(e.name).toLowerCase() === 'manifest.json');
  if (manifest) {
    const manifestError = validateManifest(manifest.data.toString('utf8'));
    if (manifestError) return { error: manifestError };
  }

  for (const entry of entries) {
    const full = entry.name.replace(/\\/g, '/').replace(/^\/+/, '');
    const name = fileName(full);
    if (name.toLowerCase() === 'manifest.json') continue;
    if (name.toLowerCase() === 'preview.png') {
      if (isPng(entry.data)) {
        previews.push({ name: 'preview.png', label: 'preview.png', data: entry.data });
      }
      continue;
    }
    if (!name.toLowerCase().endsWith('.png')) {
      if (!name.startsWith('.')) pushWarning(warnings, `已忽略非 PNG: ${name}`);
      continue;
    }
    if (entry.data.length > MAX_PNG_BYTES) {
      pushWarning(warnings, `单张超过 500KB，已跳过: ${name}`);
      continue;
    }
    const idPart = name.replace(/\.png$/i, '');
    const tileId = Number.parseInt(idPart, 10);
    if (!Number.isInteger(tileId) || !isStandardFaceId(tileId)) {
      pushWarning(warnings, `无法识别的牌面文件名: ${name}`);
      continue;
    }
    if (!isPng(entry.data)) {
      pushWarning(warnings, `不是 PNG: ${name}`);
      continue;
    }
    const size = readPngSize(entry.data);
    if (!size || size.width > MAX_IMAGE_EDGE || size.height > MAX_IMAGE_EDGE) {
      pushWarning(warnings, `尺寸不合法（需 ≤1024）: ${name}`);
      continue;
    }
    const isTable = isTableFolder(full);
    const isHand = isHandFolder(full);
    if (!isTable && !isHand) {
      pushWarning(warnings, `牌面 PNG 必须放在 hand/ 或 table/（也可用 手牌牌面/、3D牌面/）: ${full}`);
      continue;
    }
    const target = isTable ? table : hand;
    target.set(tileId, entry.data);
  }

  if (hand.size === 0 || table.size === 0) {
    return {
      error: '压缩包必须同时包含 hand/ 和 table/ 两个文件夹（也可用「手牌牌面」「3D牌面」）',
    };
  }

  let missing = 0;
  for (const id of standardFaceIds()) {
    if (!hand.has(id) && !table.has(id)) missing += 1;
  }
  if (missing > 0) {
    pushWarning(warnings, `缺 ${missing} 张，对局中将回退官方牌面`);
  }

  for (const id of PREVIEW_FACE_IDS) {
    if (hand.has(id)) {
      previews.push({ name: previewName(`hand/${id}.png`), label: `hand/${id}.png`, data: hand.get(id) });
    }
    if (table.has(id)) {
      previews.push({ name: previewName(`table/${id}.png`), label: `table/${id}.png`, data: table.get(id) });
    }
  }

  return {
    meta: {
      hand_count: hand.size,
      table_count: table.size,
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
  ids.push(105, 205, 305, 2);
  return ids;
}

function validateTileBackground(zipBuffer) {
  const entries = readZip(zipBuffer, { maxUncompressed: MAX_UNCOMPRESSED });
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
