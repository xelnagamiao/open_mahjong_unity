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
  return path.join(__dirname, '../../client/public/activity-assets');
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
  const tmp = `${filePath}.${process.pid}.tmp`;
  fs.writeFileSync(tmp, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
  fs.renameSync(tmp, filePath);
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

function rebuildPublicIndex(catalog) {
  const items = (catalog.items || [])
    .filter((item) => item && item.published)
    .sort((a, b) => {
      const sortA = Number(a.sort) || 0;
      const sortB = Number(b.sort) || 0;
      if (sortA !== sortB) return sortA - sortB;
      return String(b.updated_at || '').localeCompare(String(a.updated_at || ''));
    })
    .map((item) => ({
      id: item.id,
      title: item.title,
      cover_url: item.cover_url || '',
      updated_at: item.updated_at,
      sort: Number(item.sort) || 0,
    }));
  writeJsonAtomic(indexPath(), {
    updated_at: nowIso(),
    items,
  });
}

function listAll() {
  const catalog = loadCatalog();
  return catalog.items
    .slice()
    .sort((a, b) => {
      const sortA = Number(a.sort) || 0;
      const sortB = Number(b.sort) || 0;
      if (sortA !== sortB) return sortA - sortB;
      return String(b.updated_at || '').localeCompare(String(a.updated_at || ''));
    });
}

function getById(id) {
  const item = loadCatalog().items.find((row) => row.id === id);
  if (!item) {
    throw Object.assign(new Error('活动不存在'), { status: 404 });
  }
  return item;
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
  writeJsonAtomic(path.join(dir, 'meta.json'), {
    id: item.id,
    title: item.title,
    body: item.body || '',
    cover_url: item.cover_url || '',
    image_urls: item.image_urls || [],
    published: !!item.published,
    sort: Number(item.sort) || 0,
    created_at: item.created_at,
    updated_at: item.updated_at,
  });
}

function createActivity({ title, body = '', sort = 0, published = false }) {
  const catalog = loadCatalog();
  const item = {
    id: newId(),
    title: normalizeTitle(title),
    body: normalizeBody(body),
    cover_url: '',
    image_urls: [],
    published: !!published,
    sort: Number(sort) || 0,
    created_at: nowIso(),
    updated_at: nowIso(),
  };
  catalog.items.push(item);
  writeMeta(item);
  saveCatalog(catalog);
  return item;
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
  if (patch.published !== undefined) item.published = !!patch.published;
  if (Array.isArray(patch.image_urls)) {
    item.image_urls = patch.image_urls.filter((url) => typeof url === 'string');
  }
  item.updated_at = nowIso();
  writeMeta(item);
  saveCatalog(catalog);
  return item;
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
    saveCatalog(catalog);
  }
  return getById(id);
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
    saveCatalog(catalog);
  }
  return getById(id);
}

function ensureSeedFiles() {
  const dir = assetsDir();
  catalogDir();
  if (!fs.existsSync(catalogPath())) {
    const legacy = readJson(legacyCatalogPath(), { items: [] });
    writeJsonAtomic(catalogPath(), legacy);
  }
  if (!fs.existsSync(indexPath())) {
    rebuildPublicIndex(loadCatalog());
  }
  return dir;
}

module.exports = {
  PUBLIC_PREFIX,
  assetsDir,
  ensureSeedFiles,
  listAll,
  getById,
  createActivity,
  updateActivity,
  deleteActivity,
  saveCover,
  addBodyImage,
  removeBodyImage,
};
