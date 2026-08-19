const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const config = require('../config/config');

const PUBLIC_PREFIX = '/activity-assets';
const ID_RE = /^act_[a-f0-9]{12}$/;
const ALLOWED_EXT = new Set(['.jpg', '.jpeg', '.png', '.webp', '.gif']);
const MIME_EXT = {
  'image/jpeg': '.jpg',
  'image/png': '.png',
  'image/webp': '.webp',
  'image/gif': '.gif',
};

const TITLE_MAX = 80;
const BODY_MAX = 20000;
const MAX_BODY_IMAGES = 20;

function loadDeployConfig() {
  try {
    return JSON.parse(
      fs.readFileSync(path.join(__dirname, '../../deploy.config.json'), 'utf8')
    );
  } catch {
    return {};
  }
}

function resolveAssetsDir() {
  if (process.env.ACTIVITY_ASSETS_DIR) {
    return path.resolve(process.env.ACTIVITY_ASSETS_DIR);
  }
  if (config.isProduction) {
    const deploy = loadDeployConfig();
    const staticRoot = deploy.staticRoot || '/www/wwwroot/salasasa.cn/dist';
    return path.join(staticRoot, 'activity-assets');
  }
  // 开发环境不要写进 Vite public/：改文件会触发整页刷新，
  // 浏览器会把已成功的创建/保存请求当成失败。
  return path.join(__dirname, '../../data/activity-assets');
}

function resolveCatalogDir() {
  if (process.env.ACTIVITY_CATALOG_DIR) {
    return path.resolve(process.env.ACTIVITY_CATALOG_DIR);
  }
  return path.join(__dirname, '../../data/activities');
}

function assetsDir() {
  const dir = resolveAssetsDir();
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function catalogDir() {
  const dir = resolveCatalogDir();
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function catalogPath() {
  return path.join(catalogDir(), 'catalog.json');
}

function legacyCatalogPath() {
  return path.join(assetsDir(), '_catalog.json');
}

function indexPath() {
  return path.join(assetsDir(), 'index.json');
}

function activityDir(id) {
  if (!ID_RE.test(id)) {
    throw Object.assign(new Error('无效的活动 ID'), { status: 400 });
  }
  return path.join(assetsDir(), id);
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
  return `act_${crypto.randomBytes(6).toString('hex')}`;
}

function publicUrl(id, filename) {
  return `${PUBLIC_PREFIX}/${id}/${filename}`;
}

function loadCatalog() {
  let data = readJson(catalogPath(), null);
  if (!data) data = readJson(legacyCatalogPath(), { items: [] });
  if (!Array.isArray(data.items)) data.items = [];
  return data;
}

function saveCatalog(catalog) {
  writeJsonAtomic(catalogPath(), catalog);
  rebuildPublicIndex(catalog);
}

const STATUSES = new Set(['draft', 'published', 'ended', 'offline']);

function normalizeStatus(item) {
  const status = String(item?.status || '');
  if (STATUSES.has(status)) return status;
  return item?.published ? 'published' : 'draft';
}

function isClientVisible(status) {
  return status === 'published' || status === 'ended';
}

function persistStatus(item) {
  const status = normalizeStatus(item);
  item.status = status;
  item.published = isClientVisible(status);
  item.ended = status === 'ended';
  return status;
}

function decorate(item) {
  if (!item) return item;
  const status = normalizeStatus(item);
  return {
    ...item,
    status,
    published: isClientVisible(status),
    ended: status === 'ended',
  };
}

function toPublicIndexItem(item) {
  const status = normalizeStatus(item);
  return {
    id: item.id,
    title: item.title,
    cover_url: item.cover_url || '',
    updated_at: item.updated_at,
    sort: Number(item.sort) || 0,
    status,
    ended: status === 'ended',
  };
}

function publishedItems(catalog) {
  return (catalog.items || [])
    .filter((item) => item && isClientVisible(normalizeStatus(item)))
    .sort((a, b) => {
      const sortA = Number(a.sort) || 0;
      const sortB = Number(b.sort) || 0;
      if (sortA !== sortB) return sortA - sortB;
      return String(b.updated_at || '').localeCompare(String(a.updated_at || ''));
    });
}

function getPublicIndex() {
  const items = publishedItems(loadCatalog()).map(toPublicIndexItem);
  return {
    updated_at: nowIso(),
    items,
  };
}

function rebuildPublicIndex(catalog) {
  const items = publishedItems(catalog).map(toPublicIndexItem);
  const prev = readJson(indexPath(), null);
  if (prev && JSON.stringify(prev.items || []) === JSON.stringify(items)) {
    return;
  }
  writeJsonAtomic(indexPath(), {
    updated_at: nowIso(),
    items,
  });
}

function listAll() {
  const catalog = loadCatalog();
  const rank = {
    draft: 0,
    offline: 1,
    published: 2,
    ended: 3,
  };
  return catalog.items
    .slice()
    .sort((a, b) => {
      const rankA = rank[normalizeStatus(a)] ?? 9;
      const rankB = rank[normalizeStatus(b)] ?? 9;
      if (rankA !== rankB) return rankA - rankB;
      return String(b.updated_at || '').localeCompare(String(a.updated_at || ''));
    })
    .map(decorate);
}

function getById(id) {
  const item = loadCatalog().items.find((row) => row.id === id);
  if (!item) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  return decorate(item);
}

function normalizeTitle(raw) {
  const title = String(raw || '').trim();
  if (!title) {
    throw Object.assign(new Error('请填写活动名称'), { status: 400 });
  }
  if (title.length > TITLE_MAX) {
    throw Object.assign(new Error(`活动名称不能超过 ${TITLE_MAX} 字`), { status: 400 });
  }
  return title;
}

function normalizeBody(raw) {
  const body = String(raw || '');
  if (body.length > BODY_MAX) {
    throw Object.assign(new Error(`正文不能超过 ${BODY_MAX} 字`), { status: 400 });
  }
  return body;
}

function writeMeta(item) {
  const dir = activityDir(item.id);
  fs.mkdirSync(dir, { recursive: true });
  const status = persistStatus(item);
  writeJsonAtomic(path.join(dir, 'meta.json'), {
    id: item.id,
    title: item.title,
    body: item.body || '',
    cover_url: item.cover_url || '',
    image_urls: item.image_urls || [],
    status,
    published: isClientVisible(status),
    ended: status === 'ended',
    sort: Number(item.sort) || 0,
    created_at: item.created_at,
    updated_at: item.updated_at,
  });
}

function createActivity({ title, body = '', sort = 0 }) {
  const catalog = loadCatalog();
  const item = {
    id: newId(),
    title: normalizeTitle(title),
    body: normalizeBody(body),
    cover_url: '',
    image_urls: [],
    status: 'draft',
    published: false,
    ended: false,
    sort: Number(sort) || 0,
    created_at: nowIso(),
    updated_at: nowIso(),
  };
  catalog.items.push(item);
  writeMeta(item);
  saveCatalog(catalog);
  return decorate(item);
}

function updateActivity(id, patch) {
  const catalog = loadCatalog();
  const item = catalog.items.find((row) => row.id === id);
  if (!item) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  if (patch.title !== undefined) item.title = normalizeTitle(patch.title);
  if (patch.body !== undefined) item.body = normalizeBody(patch.body);
  if (patch.sort !== undefined) item.sort = Number(patch.sort) || 0;
  if (Array.isArray(patch.image_urls)) {
    item.image_urls = patch.image_urls.filter((url) => typeof url === 'string');
  }
  persistStatus(item);
  item.updated_at = nowIso();
  writeMeta(item);
  saveCatalog(catalog);
  return decorate(item);
}

function setActivityStatus(id, nextStatus) {
  const status = String(nextStatus || '');
  if (!STATUSES.has(status) || status === 'draft') {
    throw Object.assign(new Error('无效的活动状态'), { status: 400 });
  }
  const catalog = loadCatalog();
  const item = catalog.items.find((row) => row.id === id);
  if (!item) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  const current = normalizeStatus(item);
  if (status === 'ended' && current !== 'published' && current !== 'ended') {
    throw Object.assign(new Error('只有已发布的活动可以结束'), { status: 400 });
  }
  if (status === 'offline' && current !== 'published' && current !== 'ended' && current !== 'offline') {
    throw Object.assign(new Error('草稿无需下架'), { status: 400 });
  }
  if (status === 'published' && current === 'published') {
    return decorate(item);
  }
  item.status = status;
  persistStatus(item);
  item.updated_at = nowIso();
  writeMeta(item);
  saveCatalog(catalog);
  return decorate(item);
}

function deleteActivity(id) {
  const catalog = loadCatalog();
  const index = catalog.items.findIndex((row) => row.id === id);
  if (index < 0) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  catalog.items.splice(index, 1);
  saveCatalog(catalog);
  const dir = activityDir(id);
  fs.rmSync(dir, { recursive: true, force: true });
}

function extForUpload(file) {
  const fromMime = MIME_EXT[file.mimetype];
  if (fromMime) return fromMime;
  const ext = path.extname(file.originalname || '').toLowerCase();
  if (ALLOWED_EXT.has(ext)) return ext === '.jpeg' ? '.jpg' : ext;
  throw Object.assign(new Error('仅支持 jpg / png / webp / gif'), { status: 400 });
}

function removeFilesByPrefix(dir, prefix) {
  if (!fs.existsSync(dir)) return;
  for (const name of fs.readdirSync(dir)) {
    if (name.startsWith(prefix)) {
      fs.unlinkSync(path.join(dir, name));
    }
  }
}

function saveCover(id, file) {
  const item = getById(id);
  const dir = activityDir(id);
  fs.mkdirSync(dir, { recursive: true });
  const ext = extForUpload(file);
  removeFilesByPrefix(dir, 'cover.');
  const filename = `cover${ext}`;
  fs.writeFileSync(path.join(dir, filename), file.buffer);
  item.cover_url = publicUrl(id, filename);
  item.updated_at = nowIso();
  writeMeta(item);
  const catalog = loadCatalog();
  const row = catalog.items.find((entry) => entry.id === id);
  if (row) {
    row.cover_url = item.cover_url;
    row.updated_at = item.updated_at;
    persistStatus(row);
    saveCatalog(catalog);
  }
  return getById(id);
}

function addBodyImage(id, file) {
  const item = getById(id);
  if ((item.image_urls || []).length >= MAX_BODY_IMAGES) {
    throw Object.assign(new Error(`正文图片最多 ${MAX_BODY_IMAGES} 张`), { status: 400 });
  }
  const dir = activityDir(id);
  fs.mkdirSync(dir, { recursive: true });
  const ext = extForUpload(file);
  const filename = `img_${crypto.randomBytes(4).toString('hex')}${ext}`;
  fs.writeFileSync(path.join(dir, filename), file.buffer);
  item.image_urls = [...(item.image_urls || []), publicUrl(id, filename)];
  item.updated_at = nowIso();
  writeMeta(item);
  const catalog = loadCatalog();
  const row = catalog.items.find((entry) => entry.id === id);
  if (row) {
    row.image_urls = item.image_urls;
    row.updated_at = item.updated_at;
    persistStatus(row);
    saveCatalog(catalog);
  }
  return getById(id);
}

function removeCover(id) {
  const catalog = loadCatalog();
  const item = catalog.items.find((row) => row.id === id);
  if (!item) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  const dir = activityDir(id);
  removeFilesByPrefix(dir, 'cover.');
  item.cover_url = '';
  persistStatus(item);
  item.updated_at = nowIso();
  writeMeta(item);
  saveCatalog(catalog);
  return decorate(item);
}

function removeBodyImage(id, filename) {
  const safe = path.basename(filename || '');
  if (!safe || safe !== filename || !safe.startsWith('img_')) {
    throw Object.assign(new Error('无效的图片文件名'), { status: 400 });
  }
  const item = getById(id);
  const targetUrl = publicUrl(id, safe);
  item.image_urls = (item.image_urls || []).filter((url) => url !== targetUrl);
  const filePath = path.join(activityDir(id), safe);
  if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
  item.updated_at = nowIso();
  writeMeta(item);
  const catalog = loadCatalog();
  const row = catalog.items.find((entry) => entry.id === id);
  if (row) {
    row.image_urls = item.image_urls;
    row.updated_at = item.updated_at;
    persistStatus(row);
    saveCatalog(catalog);
  }
  return getById(id);
}

function migrateLegacyPublicAssets(dest) {
  const legacy = path.join(__dirname, '../../client/public/activity-assets');
  if (!fs.existsSync(legacy) || path.resolve(legacy) === path.resolve(dest)) return;
  for (const name of fs.readdirSync(legacy)) {
    const from = path.join(legacy, name);
    const to = path.join(dest, name);
    if (fs.existsSync(to)) continue;
    fs.cpSync(from, to, { recursive: true });
  }
}

function ensureSeedFiles() {
  const dir = assetsDir();
  catalogDir();
  migrateLegacyPublicAssets(dir);
  if (!fs.existsSync(catalogPath())) {
    const legacy = readJson(legacyCatalogPath(), { items: [] });
    writeJsonAtomic(catalogPath(), legacy);
  }
  const catalog = loadCatalog();
  let migrated = false;
  for (const item of catalog.items) {
    if (!item) continue;
    const before = item.status;
    persistStatus(item);
    if (item.status !== before) migrated = true;
  }
  if (migrated || !fs.existsSync(indexPath())) {
    saveCatalog(catalog);
  }
  return dir;
}

module.exports = {
  PUBLIC_PREFIX,
  assetsDir,
  ensureSeedFiles,
  listAll,
  getPublicIndex,
  getById,
  createActivity,
  updateActivity,
  setActivityStatus,
  deleteActivity,
  saveCover,
  addBodyImage,
  removeCover,
  removeBodyImage,
};
