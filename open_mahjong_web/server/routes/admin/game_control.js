const express = require('express');
const router = express.Router();
const config = require('../../config/config');
const { writeAudit } = require('../../utils/audit');

const GAME_SERVER_BASE_URL = config.calcServer.baseUrl.replace(/\/$/, '');
const GAME_SERVER_TIMEOUT_MS = config.calcServer.timeoutMs;

async function proxyToGameServer(path, body, method = 'POST') {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), GAME_SERVER_TIMEOUT_MS);
  try {
    const resp = await fetch(`${GAME_SERVER_BASE_URL}${path}`, {
      method,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });
    const text = await resp.text();
    let data;
    try {
      data = text ? JSON.parse(text) : {};
    } catch (_) {
      data = { detail: text };
    }
    return { status: resp.status, data };
  } finally {
    clearTimeout(timer);
  }
}

// 进行中的对局列表（实时，来自游戏服内存）
router.get('/list', async (req, res) => {
  try {
    const { status, data } = await proxyToGameServer('/admin/game/list', null, 'GET');
    if (status >= 400) {
      return res.status(status).json({ success: false, message: data?.detail || '获取对局列表失败' });
    }
    res.json({ success: true, data });
  } catch (err) {
    console.error('admin game list:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

async function callGameControl(path, gameId, adminUser, actionLabel) {
  const { status, data } = await proxyToGameServer(path, { gamestate_id: gameId });
  if (status >= 400) {
    return { status, body: { success: false, message: data?.detail || `${actionLabel}失败` } };
  }
  await writeAudit({
    adminUserId: adminUser.userId,
    action: `game.${actionLabel}`,
    targetType: 'game',
    targetId: gameId,
    payload: { gamestate_id: gameId },
  });
  return { status: 200, body: { success: true, message: data?.message || actionLabel } };
}

router.post('/pause', async (req, res) => {
  const gameId = (req.body?.gamestate_id || '').trim();
  if (!gameId) return res.status(400).json({ success: false, message: '缺少 gamestate_id' });
  try {
    const { status, body } = await callGameControl('/admin/game/pause', gameId, req.admin, 'pause');
    res.status(status).json(body);
  } catch (err) {
    console.error('admin game pause:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

router.post('/resume', async (req, res) => {
  const gameId = (req.body?.gamestate_id || '').trim();
  if (!gameId) return res.status(400).json({ success: false, message: '缺少 gamestate_id' });
  try {
    const { status, body } = await callGameControl('/admin/game/resume', gameId, req.admin, 'resume');
    res.status(status).json(body);
  } catch (err) {
    console.error('admin game resume:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

router.post('/end', async (req, res) => {
  const gameId = (req.body?.gamestate_id || '').trim();
  if (!gameId) return res.status(400).json({ success: false, message: '缺少 gamestate_id' });
  try {
    const { status, body } = await callGameControl('/admin/game/end', gameId, req.admin, 'end');
    res.status(status).json(body);
  } catch (err) {
    console.error('admin game end:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

module.exports = router;
