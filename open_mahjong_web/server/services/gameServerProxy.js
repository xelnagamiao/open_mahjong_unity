const config = require('../config/config');
const crypto = require('crypto');

function roomAdminHeaders(path, method, payload, secret, stamp = String(Math.floor(Date.now() / 1000))) {
  const digest = crypto.createHash('sha256').update(payload).digest('hex');
  const message = `room-admin\n${stamp}\n${method}\n${path}\n${digest}`;
  return { 'x-room-admin-time': stamp, 'x-room-admin-signature': crypto.createHmac('sha256', secret).update(message).digest('hex') };
}

async function proxyToGameServer(path, body, method = 'POST') {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), config.calcServer.timeoutMs);
  try {
    const payload = body ? JSON.stringify(body) : '';
    const headers = { 'Content-Type': 'application/json' };
    if (path === '/admin/rooms' || path.startsWith('/admin/rooms/')) {
      Object.assign(headers, roomAdminHeaders(path, method, payload, config.playerAuth.jwtSecret));
    }
    const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}${path}`, {
      method, headers, body: body ? payload : undefined, signal: controller.signal,
    });
    return { status: response.status, data: await response.json() };
  } finally {
    clearTimeout(timer);
  }
}

module.exports = { proxyToGameServer, roomAdminHeaders };
