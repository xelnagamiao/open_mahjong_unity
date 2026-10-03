const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { funDataDir } = require('../utils/runtimeData');
const { parseRecordShareInput, classicReplayLinks, GAME_ID_RE } = require('../utils/recordShareParse');

const ID_RE = /^cls_[a-z0-9]{12}$/;
const DESCRIPTION_MAX = 80;
const ITEMS_MAX = 100;
const SEED_ID = 'cls_seed_renhe';
const SEED_RECORD = {
  id: SEED_ID,
  game_id: 'V9V0a25tKe',
  round: 5,
  node: 11,
  description: '人和不够番',
  sort: 100,
};

function catalogPath() {
  return path.join(funDataDir(), 'classic-records.json');
}

function writeJsonAtomic(filePath, data) {
  const dir = path.dirname(filePath);
  fs.mkdirSync(dir, { recursive: true });
  const tmp = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tmp, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
  try {
    fs.renameSync(tmp, filePath);
  } catch {
    fs.copyFileSync(tmp, filePath);
    fs.unlinkSync(tmp);
  }
}

function readJson(filePath, fallback) {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return fallback;
  }
}

function nowIso() {
  return new Date().toISOString();
}

function newId() {
  return `cls_${crypto.randomBytes(6).toString('hex')}`;
}

function emptyCatalog() {
  return { seeded: [], items: [] };
}

function loadCatalog() {
  const data = readJson(catalogPath(), emptyCatalog());
  if (!Array.isArray(data.items)) data.items = [];
  if (!Array.isArray(data.seeded)) data.seeded = [];
  return data;
}

function saveCatalog(catalog) {
  writeJsonAtomic(catalogPath(), {
    seeded: catalog.seeded || [],
    items: catalog.items || [],
  });
}

function clampSort(value, fallback = 0) {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(-9999, Math.min(9999, Math.round(n)));
}

function normalizeDescription(value) {
  const text = String(value || '').trim().replace(/\s+/g, ' ');
  if (!text) {
    throw Object.assign(new Error('请填写描述'), { status: 400 });
  }
  if (text.length > DESCRIPTION_MAX) {
    throw Object.assign(new Error(`描述不超过 ${DESCRIPTION_MAX} 字`), { status: 400 });
  }
  return text;
}

function normalizePosition(value, min) {
  if (value == null || value === '') return null;
  const n = Number(value);
  if (!Number.isFinite(n) || n < min) return null;
  return Math.floor(n);
}

function pickPosition(bodyValue, parsedValue, min) {
  if (bodyValue == null || bodyValue === '') return parsedValue ?? null;
  const normalized = normalizePosition(bodyValue, min);
  return normalized == null ? parsedValue ?? null : normalized;
}

function recordKey(item) {
  return `${item.game_id}#${item.round == null ? '' : item.round}#${item.node == null ? '' : item.node}`;
}

function decorate(item) {
  if (!item) return item;
  return {
    ...item,
    ...classicReplayLinks(item),
  };
}

function sortItems(items) {
  return [...items].sort((a, b) => {
    const sortDelta = (Number(b.sort) || 0) - (Number(a.sort) || 0);
    if (sortDelta) return sortDelta;
    return String(b.created_at || '').localeCompare(String(a.created_at || ''));
  });
}

function ensureSeed() {
  const catalog = loadCatalog();
  if (catalog.seeded.includes(SEED_ID)) return catalog;
  if (!catalog.items.some((item) => item && item.id === SEED_ID)) {
    catalog.items.push({
      ...SEED_RECORD,
      created_at: nowIso(),
      updated_at: nowIso(),
    });
  }
  catalog.seeded.push(SEED_ID);
  saveCatalog(catalog);
  return catalog;
}

function listAll() {
  const catalog = ensureSeed();
  return sortItems(catalog.items).map(decorate);
}

function listPublic() {
  return listAll();
}

async function attachRecordMeta(items) {
  const gameIds = [...new Set((items || []).map((item) => item.game_id).filter(Boolean))];
  if (!gameIds.length) return items || [];
  const pool = require('../config/database');
  const [records, players] = await Promise.all([
    pool.query(
      `SELECT game_id, created_at FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    ),
    pool.query(
      `SELECT game_id, user_id, username, score, rank, rule
         FROM game_player_records
        WHERE game_id = ANY($1::varchar[])
        ORDER BY rank`,
      [gameIds]
    ),
  ]);
  const createdAt = new Map(records.rows.map((row) => [row.game_id, row.created_at]));
  const playersByGame = new Map();
  for (const row of players.rows) {
    if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
    playersByGame.get(row.game_id).push({
      user_id: Number(row.user_id),
      username: row.username,
      score: Number(row.score) || 0,
      rank: Number(row.rank) || 0,
      rule: row.rule || null,
    });
  }
  return items.map((item) => {
    const table = playersByGame.get(item.game_id) || [];
    const rule = table[0]?.rule || null;
    return {
      ...item,
      created_at_game: createdAt.get(item.game_id) || null,
      rule,
      show_2d: !rule || rule === 'guobiao',
      missing: !createdAt.has(item.game_id),
      players: table,
    };
  });
}

function parseClassicInput(body = {}) {
  const parsed = parseRecordShareInput(body.url || body.game_id || body.gameId);
  const gameId = String(parsed?.game_id || body.game_id || body.gameId || '').trim();
  if (!GAME_ID_RE.test(gameId)) {
    throw Object.assign(new Error('请粘贴有效的 2D / 3D 牌谱链接或牌谱 ID'), { status: 400 });
  }
  const round = pickPosition(body.round, parsed?.round, 1);
  const node = pickPosition(body.node, parsed?.node, 0);
  return {
    game_id: gameId,
    round: round == null ? null : round,
    node: node == null ? null : node,
    description: normalizeDescription(body.description),
    sort: clampSort(body.sort, 0),
  };
}

function findDuplicate(catalog, fields, exceptId = null) {
  const key = recordKey(fields);
  return catalog.items.find((item) => item && item.id !== exceptId && recordKey(item) === key);
}

function createClassicRecord(body) {
  const catalog = ensureSeed();
  if (catalog.items.length >= ITEMS_MAX) {
    throw Object.assign(new Error(`经典牌谱最多 ${ITEMS_MAX} 条`), { status: 400 });
  }
  const fields = parseClassicInput(body);
  if (findDuplicate(catalog, fields)) {
    throw Object.assign(new Error('这条牌谱已经添加过了'), { status: 409 });
  }
  const now = nowIso();
  const item = {
    id: newId(),
    ...fields,
    created_at: now,
    updated_at: now,
  };
  catalog.items.push(item);
  saveCatalog(catalog);
  return decorate(item);
}

function updateClassicRecord(id, body) {
  if (!ID_RE.test(id) && id !== SEED_ID) {
    throw Object.assign(new Error('无效的经典牌谱 ID'), { status: 400 });
  }
  const catalog = ensureSeed();
  const index = catalog.items.findIndex((item) => item && item.id === id);
  if (index < 0) {
    throw Object.assign(new Error('经典牌谱不存在'), { status: 404 });
  }
  const current = catalog.items[index];
  const fields = parseClassicInput({
    url: body.url,
    game_id: body.game_id || body.gameId || current.game_id,
    round: body.round !== undefined ? body.round : current.round,
    node: body.node !== undefined ? body.node : current.node,
    description: body.description != null ? body.description : current.description,
    sort: body.sort !== undefined ? body.sort : current.sort,
  });
  if (findDuplicate(catalog, fields, id)) {
    throw Object.assign(new Error('这条牌谱已经添加过了'), { status: 409 });
  }
  const item = {
    ...current,
    ...fields,
    updated_at: nowIso(),
  };
  catalog.items[index] = item;
  saveCatalog(catalog);
  return decorate(item);
}

function deleteClassicRecord(id) {
  if (!ID_RE.test(id) && id !== SEED_ID) {
    throw Object.assign(new Error('无效的经典牌谱 ID'), { status: 400 });
  }
  const catalog = ensureSeed();
  const next = catalog.items.filter((item) => item && item.id !== id);
  if (next.length === catalog.items.length) {
    throw Object.assign(new Error('经典牌谱不存在'), { status: 404 });
  }
  catalog.items = next;
  saveCatalog(catalog);
}

module.exports = {
  SEED_ID,
  SEED_RECORD,
  DESCRIPTION_MAX,
  ensureSeed,
  listAll,
  listPublic,
  createClassicRecord,
  updateClassicRecord,
  deleteClassicRecord,
  parseClassicInput,
  attachRecordMeta,
};
