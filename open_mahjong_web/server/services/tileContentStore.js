const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { MAX_UNCOMPRESSED, validateZip } = require('../utils/tilePackValidate');
const { readZip } = require('../utils/zipEntries');
const { renderTilePackPreview } = require('../utils/tilePackPreview');
const { userContentDir } = require('../utils/runtimeData');

const PREVIEW_NAME_RE = /^[A-Za-z0-9._-]+$/;
const catalogBuilds = new Map();
const CATALOG_PREVIEW = 'catalog.png';

function contentDir() {
  return userContentDir();
}

function newStorageKey() {
  return `tc_${crypto.randomBytes(12).toString('hex')}`;
}

function packDir(storageKey) {
  if (!/^tc_[a-f0-9]{24}$/.test(storageKey)) {
    const err = new Error('无效的存储键');
    err.status = 400;
    throw err;
  }
  return path.join(contentDir(), storageKey);
}

function packZipPath(storageKey) {
  return path.join(packDir(storageKey), 'pack.zip');
}

function previewDir(storageKey) {
  return path.join(packDir(storageKey), 'preview');
}

function previewPath(storageKey, filename) {
  if (!PREVIEW_NAME_RE.test(filename)) {
    const err = new Error('无效的预览文件');
    err.status = 400;
    throw err;
  }
  return path.join(previewDir(storageKey), filename);
}

function writeAtomic(filePath, data) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tmp, data);
  try {
    fs.renameSync(tmp, filePath);
  } catch {
    fs.copyFileSync(tmp, filePath);
    fs.unlinkSync(tmp);
  }
}

async function saveZip({ kind, zipBuffer }) {
  const parsed = validateZip(kind, zipBuffer);
  if (parsed.error) {
    const err = new Error(parsed.error);
    err.status = 400;
    throw err;
  }
  let catalog;
  if (kind === 'tile_face') {
    try { catalog = await renderTilePackPreview(parsed.previews); }
    catch { throw Object.assign(new Error('无法生成牌包预览，请检查 11、21、31、41 的 PNG 图片'), { status: 400 }); }
  }
  const key = newStorageKey();
  try {
    writeAtomic(packZipPath(key), zipBuffer);
    for (const item of parsed.previews || []) {
      if (PREVIEW_NAME_RE.test(item.name)) writeAtomic(previewPath(key, item.name), item.data);
    }
    if (catalog) writeAtomic(previewPath(key, CATALOG_PREVIEW), catalog);
  } catch (err) {
    removePack(key);
    throw err;
  }
  return {
    storageKey: key,
    fileSize: zipBuffer.length,
    meta: parsed.meta,
  };
}

async function catalogPreview(storageKey) {
  const filePath = previewPath(storageKey, CATALOG_PREVIEW);
  try { return await fs.promises.readFile(filePath); }
  catch (err) { if (err.code !== 'ENOENT') throw err; }
  if (!catalogBuilds.has(storageKey)) {
    const build = async () => {
      const entries = readZip(readZipFile(storageKey), { maxUncompressed: MAX_UNCOMPRESSED });
      const image = await renderTilePackPreview(entries);
      if (fs.existsSync(packZipPath(storageKey))) writeAtomic(filePath, image);
      return image;
    };
    catalogBuilds.set(storageKey, build().finally(() => catalogBuilds.delete(storageKey)));
  }
  return catalogBuilds.get(storageKey);
}

function readZipFile(storageKey) {
  const filePath = packZipPath(storageKey);
  if (!fs.existsSync(filePath)) {
    const err = new Error('文件不存在');
    err.status = 404;
    throw err;
  }
  return fs.readFileSync(filePath);
}

function readPreviewFile(storageKey, filename) {
  const filePath = previewPath(storageKey, filename);
  if (!fs.existsSync(filePath)) {
    const err = new Error('预览不存在');
    err.status = 404;
    throw err;
  }
  return fs.readFileSync(filePath);
}

function removePack(storageKey) {
  fs.rmSync(packDir(storageKey), { recursive: true, force: true });
}

module.exports = {
  contentDir,
  saveZip,
  readZipFile,
  readPreviewFile,
  catalogPreview,
  removePack,
};
