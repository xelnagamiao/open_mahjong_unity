const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { dataRoot } = require('../utils/runtimeData');
const seed = require('../content/platform-content.seed.json');

const STYLES = ['original', 'urban', 'academy', 'nocturne'];
const PALETTES = ['slate', 'ivory', 'sky', 'navy', 'rose', 'graphite'];
const LINK_HOSTS = new Set(['qm.qq.com', 'discord.gg', 'discord.com', 't.me']);
const ASSET_RE = /^(?:[a-f0-9]{64}\.(?:png|jpg)|sample-wechat\.png)$/;
const ASSET_PREFIX = '/api/platform/content/assets/';
const fail = (message, status = 400) => { throw Object.assign(new Error(message), { status }); };
function text(value, max, field, required = false) {
  if (typeof value !== 'string' || value.length > max || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(value)) fail(`${field}格式错误或过长`);
  const result = value.trim();
  if (required && !result) fail(`${field}不能为空`);
  return result;
}
function common(x) {
  if (!x || typeof x !== 'object' || Array.isArray(x)) fail('条目格式错误');
  const id = text(x.id, 64, 'ID', true);
  if (!/^[a-zA-Z0-9_-]+$/.test(id)) fail('ID 格式错误');
  if (typeof x.enabled !== 'boolean') fail('启用状态必须为布尔值');
  if (!Number.isInteger(x.sort) || Math.abs(x.sort) > 9999) fail('排序必须为 -9999 至 9999 的整数');
  return { id, title: text(x.title, 64, '名称', true), sort: x.sort, enabled: x.enabled };
}
function rows(values, max, convert) {
  if (!Array.isArray(values) || values.length > max) fail('条目数量超限');
  const result = values.map(convert);
  if (new Set(result.map(x => x.id)).size !== result.length) fail('条目 ID 重复');
  return result;
}
function normalize(input) {
  if (!input || input.schemaVersion !== 1) fail('不支持的内容版本');
  const appearance = input.appearance;
  if (!appearance || !STYLES.includes(appearance.style) || !PALETTES.includes(appearance.palette)) fail('请选择有效的风格和配色');
  const result = {
    schemaVersion: 1,
    appearance: { style: appearance.style, palette: appearance.palette },
    credits: rows(input.credits, 24, x => ({ ...common(x), text: text(x.text, 5000, '鸣谢正文', true) })),
    thanksClosing: text(input.thanksClosing, 200, '结束语'),
    communities: rows(input.communities, 48, x => {
      const base = common(x);
      if (!['qq', 'wechat', 'telegram', 'discord'].includes(x.kind)) fail('社群渠道不支持');
      if (!['number', 'link', 'qr', 'closed'].includes(x.joinMode)) fail('入群方式无效');
      const number = text(x.number, 64, '群号或频道');
      if (x.kind === 'qq' && number && !/^\d{5,16}$/.test(number)) fail('QQ群号需为 5 至 16 位数字');
      const linkUrl = text(x.linkUrl, 512, '邀请地址');
      if (linkUrl) {
        let uri; try { uri = new URL(linkUrl); } catch { fail('邀请地址无效'); }
        if (uri.protocol !== 'https:' || uri.username || uri.password || uri.port || !LINK_HOSTS.has(uri.hostname)) fail('邀请地址需使用 QQ、Telegram 或 Discord 的 HTTPS 地址');
      }
      const qrUrl = text(x.qrUrl, 180, '二维码地址');
      if (qrUrl && (!qrUrl.startsWith(ASSET_PREFIX) || !ASSET_RE.test(qrUrl.slice(ASSET_PREFIX.length)))) fail('请使用后台上传的二维码');
      const expiresAt = text(x.expiresAt, 32, '失效时间');
      if (expiresAt && !Number.isFinite(Date.parse(expiresAt))) fail('失效时间格式错误');
      if (typeof x.qrIsExample !== 'boolean') fail('二维码示例标记无效');
      if (x.joinMode === 'number' && !number) fail('复制方式需要填写群号或频道');
      if (x.joinMode === 'link' && !linkUrl) fail('链接方式需要填写邀请地址');
      if (x.joinMode === 'qr' && !qrUrl) fail('二维码方式需要先上传二维码');
      return { ...base, titleEn: text(x.titleEn ?? '', 96, '英文名称'), kind: x.kind, number, tag: text(x.tag, 24, '标签'),
        description: text(x.description, 480, '简介'), descriptionEn: text(x.descriptionEn ?? '', 960, '英文简介'), joinMode: x.joinMode,
        actionLabel: text(x.actionLabel, 16, '按钮文字', true), linkUrl, qrUrl, qrIsExample: x.qrIsExample,
        actionLabelEn: text(x.actionLabelEn ?? '', 48, '英文按钮文字'),
        qrCaption: text(x.qrCaption, 160, '二维码说明'), closedMessage: text(x.closedMessage, 64, '关闭提示'),
        closedMessageEn: text(x.closedMessageEn ?? '', 128, '英文关闭提示'), expiresAt };
    }),
  };
  if (Buffer.byteLength(JSON.stringify(result), 'utf8') > 240 * 1024) fail('内容总量过大，请精简后发布');
  return result;
}
function publicDocument(document) {
  const visible = items => items.filter(x => x.enabled).sort((a, b) => a.sort - b.sort || a.id.localeCompare(b.id)).map(({ enabled, ...x }) => x);
  return { schemaVersion: 1, revision: document.revision, updatedAt: document.updatedAt, appearance: document.appearance,
    credits: visible(document.credits), thanksClosing: document.thanksClosing, communities: visible(document.communities) };
}
function imageInfo(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 24 || buffer.length > 2 * 1024 * 1024) fail('二维码图片需小于 2MB');
  if (buffer.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10])) && buffer.toString('ascii', 12, 16) === 'IHDR') {
    let offset = 8, imageData = false, ended = false;
    while (offset + 12 <= buffer.length) {
      const size = buffer.readUInt32BE(offset);
      if (offset + 12 + size > buffer.length) break;
      const kind = buffer.toString('ascii', offset + 4, offset + 8);
      if (offset === 8 && size !== 13) break;
      if (kind === 'IDAT' && size > 0) imageData = true;
      offset += 12 + size;
      if (kind === 'IEND') { ended = size === 0 && offset === buffer.length; break; }
    }
    if (imageData && ended) return { ext: 'png', width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
    fail('PNG 图片不完整，请重新上传');
  }
  if (buffer[0] === 0xff && buffer[1] === 0xd8 && buffer[buffer.length - 2] === 0xff && buffer[buffer.length - 1] === 0xd9) {
    let offset = 2;
    while (offset + 4 < buffer.length) {
      if (buffer[offset++] !== 0xff) break;
      const marker = buffer[offset++];
      if (marker === 0xd9 || marker === 0xda) break;
      const length = buffer.readUInt16BE(offset);
      if (length < 2 || offset + length > buffer.length) break;
      if ([0xc0,0xc1,0xc2].includes(marker) && length >= 7) return { ext: 'jpg', width: buffer.readUInt16BE(offset + 5), height: buffer.readUInt16BE(offset + 3) };
      offset += length;
    }
  }
  fail('仅支持有效的 PNG / JPEG 二维码图片');
}
function createStore(directory, initial = seed) {
  const file = path.join(directory, 'content.json');
  const assets = path.join(directory, 'assets');
  function atomic(value) {
    fs.mkdirSync(directory, { recursive: true });
    const temporary = `${file}.${crypto.randomUUID()}.tmp`;
    try { fs.writeFileSync(temporary, JSON.stringify(value, null, 2), 'utf8'); fs.renameSync(temporary, file); }
    finally { if (fs.existsSync(temporary)) fs.unlinkSync(temporary); }
  }
  function ensureSeed() {
    fs.mkdirSync(assets, { recursive: true });
    const sample = path.join(assets, 'sample-wechat.png');
    if (!fs.existsSync(sample)) fs.copyFileSync(path.join(__dirname, '../content/community-wechat-sample.png'), sample);
    if (!fs.existsSync(file)) atomic({ ...normalize(initial), revision: 1, updatedAt: new Date().toISOString() });
  }
  function read() { ensureSeed(); return JSON.parse(fs.readFileSync(file, 'utf8')); }
  function assetPath(filename) {
    if (!ASSET_RE.test(filename)) fail('图片不存在', 404);
    ensureSeed(); const location = path.join(assets, filename);
    if (!fs.existsSync(location)) fail('图片不存在', 404);
    return location;
  }
  function publish(value) {
    const current = read();
    if (value.revision !== current.revision) fail('内容已被其他管理员修改，请重新载入后再发布', 409);
    const next = { ...normalize(value), revision: current.revision + 1, updatedAt: new Date().toISOString() };
    for (const row of next.communities) if (row.qrUrl) assetPath(row.qrUrl.slice(ASSET_PREFIX.length));
    const history = path.join(directory, 'history'); fs.mkdirSync(history, { recursive: true });
    fs.writeFileSync(path.join(history, `${current.revision}.json`), JSON.stringify(current, null, 2));
    atomic(next); return next;
  }
  function upload(buffer) {
    const info = imageInfo(buffer);
    if (info.width < 64 || info.height < 64 || info.width > 2048 || info.height > 2048) fail('图片边长需要在 64 至 2048 像素之间');
    ensureSeed(); const name = `${crypto.createHash('sha256').update(buffer).digest('hex')}.${info.ext}`;
    const destination = path.join(assets, name);
    if (!fs.existsSync(destination)) fs.writeFileSync(destination, buffer, { flag: 'wx' });
    return { url: ASSET_PREFIX + name, width: info.width, height: info.height };
  }
  return { read, publish, upload, assetPath };
}
let instance;
function defaultStore() { return instance ||= createStore(path.join(dataRoot(), 'platform-content')); }
module.exports = { STYLES, PALETTES, normalize, publicDocument, createStore, defaultStore, imageInfo };
