const crypto = require('crypto');

function base64UrlEncode(obj) {
  return Buffer.from(JSON.stringify(obj))
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

function base64UrlDecode(str) {
  const pad = 4 - (str.length % 4);
  const b64 = str.replace(/-/g, '+').replace(/_/g, '/') + (pad < 4 ? '='.repeat(pad) : '');
  return JSON.parse(Buffer.from(b64, 'base64').toString('utf8'));
}

function signToken(payload, secret, expiresInSec) {
  const header = base64UrlEncode({ alg: 'HS256', typ: 'JWT' });
  const exp = Math.floor(Date.now() / 1000) + expiresInSec;
  const body = base64UrlEncode({ ...payload, exp });
  const sig = crypto
    .createHmac('sha256', secret)
    .update(`${header}.${body}`)
    .digest('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `${header}.${body}.${sig}`;
}

function verifyToken(token, secret) {
  if (!token || typeof token !== 'string') {
    return null;
  }
  const parts = token.split('.');
  if (parts.length !== 3) {
    return null;
  }
  const [header, body, sig] = parts;
  const expected = crypto
    .createHmac('sha256', secret)
    .update(`${header}.${body}`)
    .digest('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  const sigBuf = Buffer.from(sig);
  const expBuf = Buffer.from(expected);
  if (sigBuf.length !== expBuf.length || !crypto.timingSafeEqual(sigBuf, expBuf)) {
    return null;
  }
  const payload = base64UrlDecode(body);
  if (!payload.exp || payload.exp < Math.floor(Date.now() / 1000)) {
    return null;
  }
  return payload;
}

module.exports = { signToken, verifyToken };
