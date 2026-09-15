const fs = require('fs');
const path = require('path');

const WEB_ROOT = path.resolve(__dirname, '../..');
const WEB_DATA = path.join(WEB_ROOT, 'data');
const PRODUCTION_DATA_ROOT = '/var/open-mahjong/data';

function loadDeployConfig() {
  try {
    return JSON.parse(fs.readFileSync(path.join(WEB_ROOT, 'deploy.config.json'), 'utf8'));
  } catch {
    return {};
  }
}

function resolveDataRoot() {
  if (process.env.OM_DATA_DIR) {
    return path.resolve(process.env.OM_DATA_DIR);
  }
  if (process.env.NODE_ENV === 'production' && process.platform !== 'win32') {
    return PRODUCTION_DATA_ROOT;
  }
  return WEB_DATA;
}

function dataRoot() {
  const dir = resolveDataRoot();
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function resolveSubdir(envName, subdir) {
  if (process.env[envName]) {
    return path.resolve(process.env[envName]);
  }
  return path.join(dataRoot(), subdir);
}

function userContentDir() {
  const dir = resolveSubdir('USER_CONTENT_DIR', 'user-content');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function activityAssetsDir() {
  const dir = resolveSubdir('ACTIVITY_ASSETS_DIR', 'activity-assets');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function activityCatalogDir() {
  const dir = resolveSubdir('ACTIVITY_CATALOG_DIR', 'activities');
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

function copyMissingTree(src, dest) {
  if (!src || !fs.existsSync(src)) return 0;
  if (path.resolve(src) === path.resolve(dest)) return 0;
  let copied = 0;
  fs.mkdirSync(dest, { recursive: true });
  for (const name of fs.readdirSync(src)) {
    if (name.endsWith('.tmp') || name === '.gitkeep') continue;
    const from = path.join(src, name);
    const to = path.join(dest, name);
    let stat;
    try {
      stat = fs.statSync(from);
    } catch {
      continue;
    }
    if (stat.isDirectory()) {
      copied += copyMissingTree(from, to);
      continue;
    }
    if (fs.existsSync(to)) continue;
    fs.copyFileSync(from, to);
    copied += 1;
  }
  return copied;
}

function legacyActivityAssetDirs() {
  const dirs = [
    path.join(WEB_ROOT, 'client/public/activity-assets'),
    path.join(WEB_DATA, 'activity-assets'),
  ];
  const deploy = loadDeployConfig();
  const staticRoot = deploy.staticRoot || '/www/wwwroot/salasasa.cn/dist';
  dirs.push(path.join(staticRoot, 'activity-assets'));
  return dirs;
}

function migrateLegacyRuntimeData() {
  const copied = {
    activities: copyMissingTree(path.join(WEB_DATA, 'activities'), activityCatalogDir()),
    activityAssets: 0,
    userContent: copyMissingTree(path.join(WEB_DATA, 'user-content'), userContentDir()),
  };
  const destAssets = activityAssetsDir();
  for (const src of legacyActivityAssetDirs()) {
    copied.activityAssets += copyMissingTree(src, destAssets);
  }
  return copied;
}

module.exports = {
  PRODUCTION_DATA_ROOT,
  WEB_DATA,
  resolveDataRoot,
  dataRoot,
  userContentDir,
  activityAssetsDir,
  activityCatalogDir,
  migrateLegacyRuntimeData,
};
