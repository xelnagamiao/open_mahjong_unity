const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { imageInfo } = require('./platformContentStore');
const { dataRoot } = require('../utils/runtimeData');
const PREFIX = '/api/venue-icons/';
const NAME = /^[a-f0-9]{64}\.(png|jpg)$/;
const directory = () => path.join(dataRoot(), 'venue-icons');
function normalizeTeamIcon(value, fallback = '') {
  if (value === undefined) return { value: fallback || '' };
  if (value === '' || value === null) return { value: '' };
  if (typeof value !== 'string') return { error: '队徽格式错误' };
  if (value.startsWith(PREFIX) && NAME.test(value.slice(PREFIX.length)) && fs.existsSync(path.join(directory(), value.slice(PREFIX.length)))) return { value };
  const match = /^data:image\/(png|jpeg);base64,([A-Za-z0-9+/]+={0,2})$/.exec(value);
  if (!match || value.length > 90000) return { error: '请选择 PNG/JPEG 队徽，处理后不能超过 64KB' };
  try {
    const buffer = Buffer.from(match[2], 'base64');
    if (buffer.length > 65536 || buffer.toString('base64') !== match[2]) throw new Error();
    const info = imageInfo(buffer);
    if (info.width < 1 || info.height < 1 || info.width > 512 || info.height > 512) throw new Error();
    const name = crypto.createHash('sha256').update(buffer).digest('hex') + '.' + info.ext;
    fs.mkdirSync(directory(), { recursive: true });
    const file = path.join(directory(), name);
    if (!fs.existsSync(file)) { try { fs.writeFileSync(file, buffer, { flag: 'wx' }); } catch (e) { if (e.code !== 'EEXIST') throw e; } }
    return { value: PREFIX + name };
  } catch (_) { return { error: '队徽图片无效，请选择不超过 512 像素、64KB 的 PNG/JPEG 图片' }; }
}
function serve(req, res) {
  if (!NAME.test(req.params.name)) return res.sendStatus(404);
  res.set('Cache-Control', 'public, max-age=31536000, immutable');
  res.set('X-Content-Type-Options', 'nosniff');
  res.sendFile(path.join(directory(), req.params.name), err => { if (err && !res.headersSent) res.sendStatus(404); });
}
module.exports = { normalizeTeamIcon, serve };
