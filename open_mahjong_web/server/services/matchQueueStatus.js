const { randomUUID } = require('crypto');
const config = require('../config/config');

const CACHE_TTL_MS = 4_000;
const STALE_TTL_MS = 30_000;
const REQUEST_TIMEOUT_MS = Math.min(5_000, config.calcServer.timeoutMs);

let cachedStatus = null;
let cachedAt = 0;
let pendingRequest = null;

function gameWebSocketUrl() {
  const url = new URL(config.calcServer.baseUrl);
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  url.pathname = `/game/web-public-${randomUUID()}`;
  url.search = '';
  url.hash = '';
  return url.toString();
}

function sanitizeQueueStatus(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('游戏服返回了无效的匹配人数');
  }
  const status = {};
  for (const [queueType, counts] of Object.entries(value)) {
    if (!/^[a-z0-9_]{1,64}$/.test(queueType) || !counts || typeof counts !== 'object') continue;
    const waiting = Number(counts.waiting);
    const playing = Number(counts.playing);
    status[queueType] = {
      waiting: Number.isFinite(waiting) ? Math.max(0, Math.trunc(waiting)) : 0,
      playing: Number.isFinite(playing) ? Math.max(0, Math.trunc(playing)) : 0,
    };
  }
  if (Object.keys(status).length === 0) throw new Error('游戏服未返回匹配队列');
  return status;
}

function requestQueueStatus() {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(gameWebSocketUrl());
    let settled = false;
    const finish = (error, value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try { socket.close(); } catch (_) { /* already closed */ }
      if (error) reject(error);
      else resolve(value);
    };
    const timer = setTimeout(
      () => finish(new Error('读取匹配人数超时')),
      REQUEST_TIMEOUT_MS,
    );

    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({ type: 'match/get_queue_status' }));
    });
    socket.addEventListener('message', (event) => {
      try {
        const message = JSON.parse(String(event.data));
        if (message.type !== 'match/queue_status') return;
        if (message.success === false) {
          finish(new Error(message.message || '读取匹配人数失败'));
          return;
        }
        finish(null, sanitizeQueueStatus(message.queue_status));
      } catch (error) {
        finish(error);
      }
    });
    socket.addEventListener('error', () => {
      finish(new Error('无法连接游戏服读取匹配人数'));
    });
    socket.addEventListener('close', () => {
      if (!settled) finish(new Error('游戏服在返回匹配人数前断开'));
    });
  });
}

async function getPublicQueueStatus() {
  const now = Date.now();
  if (cachedStatus && now - cachedAt < CACHE_TTL_MS) return cachedStatus;
  if (pendingRequest) return pendingRequest;

  pendingRequest = requestQueueStatus()
    .then((status) => {
      cachedStatus = status;
      cachedAt = Date.now();
      return status;
    })
    .catch((error) => {
      if (cachedStatus && Date.now() - cachedAt < STALE_TTL_MS) return cachedStatus;
      throw error;
    })
    .finally(() => {
      pendingRequest = null;
    });
  return pendingRequest;
}

module.exports = {
  getPublicQueueStatus,
  sanitizeQueueStatus,
};
