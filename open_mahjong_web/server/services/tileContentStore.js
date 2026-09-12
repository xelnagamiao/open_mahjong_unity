const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { validateZip } = require('../utils/tilePackValidate');

const PREVIEW_NAME_RE = /^[A-Za-z0-9._-]+$/;

function resolveContentDir() {
  if (process.env.USER_CONTENT_DIR) {
    return path.resolve(process.env.USER_CONTENT_DIR);
  }
  return path.join(__dirname, '../../data/user-content');
}

function contentDir() {
  const dir = resolveContentDir();
  fs.mkdirSync(dir, { recursive: true });
  return dir;
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

function saveZip({ kind, zipBuffer, storageKey }) {
  const parsed = validateZip(kind, zipBuffer);
  if (parsed.error) {
    const err = new Error(parsed.error);
    err.status = 400;
    throw err;
  }
  const key = storageKey || newStorageKey();
  const dir = packDir(key);
  fs.mkdirSync(dir, { recursive: true });
  writeAtomic(packZipPath(key), zipBuffer);

  const previews = previewDir(key);
  fs.rmSync(previews, { recursive: true, force: true });
  fs.mkdirSync(previews, { recursive: true });
  for (const item of parsed.previews || []) {
    if (!PREVIEW_NAME_RE.test(item.name)) continue;
    fs.writeFileSync(path.join(previews, item.name), item.data);
  }
  return {
    storageKey: key,
    fileSize: zipBuffer.length,
    meta: parsed.meta,
  };
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
  removePack,
};
