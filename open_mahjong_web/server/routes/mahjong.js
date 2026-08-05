const express = require('express');
const router = express.Router();
const config = require('../config/config');
const hongque = require('../calc/hongque');

const CALC_BASE_URL = config.calcServer.baseUrl.replace(/\/$/, '');
const CALC_TIMEOUT_MS = config.calcServer.timeoutMs;
const TZIAKCHA_BASE_URL = 'https://tziakcha.net';
const TZIAKCHA_TIMEOUT_MS = 12_000;
const TZIAKCHA_ID_RE = /^[A-Za-z0-9_-]{1,128}$/;

function parseTziakchaInput(value) {
  const input = String(value || '').trim();
  if (!input || input.length > 512) throw new Error('请输入雀渣牌谱链接或牌谱 ID');
  if (!input.includes('/') && !input.includes('?')) {
    if (!TZIAKCHA_ID_RE.test(input)) throw new Error('雀渣牌谱 ID 格式不正确');
    return { id: input, kind: 'unknown' };
  }

  let url;
  try {
    url = new URL(input, TZIAKCHA_BASE_URL);
  } catch {
    throw new Error('雀渣牌谱链接格式不正确');
  }
  if (url.hostname !== 'tziakcha.net' && url.hostname !== 'www.tziakcha.net') {
    throw new Error('仅支持 tziakcha.net 的牌谱链接');
  }
  const recordMatch = url.pathname.match(/^\/record\/([^/?#]+)/);
  const isRecordPath = /^\/record\/?$/.test(url.pathname) || Boolean(recordMatch);
  const id = recordMatch?.[1] || url.searchParams.get('id');
  if (!id || !TZIAKCHA_ID_RE.test(id)) throw new Error('链接中没有可识别的雀渣牌谱 ID');
  return { id, kind: isRecordPath ? 'record' : 'session' };
}

async function fetchTziakchaJson(path, init) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TZIAKCHA_TIMEOUT_MS);
  try {
    const response = await fetch(`${TZIAKCHA_BASE_URL}${path}`, {
      ...init,
      headers: {
        'User-Agent': 'salasasa-record-converter/1.0',
        ...(init?.headers || {}),
      },
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`雀渣返回 HTTP ${response.status}`);
    const data = await response.json();
    if (!data || typeof data !== 'object') throw new Error('雀渣返回了无法识别的数据');
    return data;
  } finally {
    clearTimeout(timer);
  }
}

async function fetchTziakchaRound(recordId) {
  const data = await fetchTziakchaJson('/_qry/record/', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
    body: new URLSearchParams({ id: recordId }).toString(),
  });
  if (typeof data.script !== 'string' || !data.script) throw new Error('没有找到该小局的牌谱数据');
  return { ...data, id: data.id || recordId };
}

async function fetchTziakchaSession(sessionId) {
  const session = await fetchTziakchaJson(`/_qry/game/?id=${encodeURIComponent(sessionId)}`, {
    method: 'POST',
  });
  const refs = Array.isArray(session.records) ? session.records : [];
  const recordIds = refs.map((item) => item?.i || item?.id).filter((id) => TZIAKCHA_ID_RE.test(String(id))).slice(0, 64);
  if (!recordIds.length) throw new Error('没有找到该场对局的小局牌谱');
  const records = await Promise.all(recordIds.map((id) => fetchTziakchaRound(String(id))));
  return {
    session: { ...session, id: session.id || sessionId },
    records,
  };
}

async function fetchTziakchaRecordInput(parsed) {
  if (parsed.kind === 'session') return fetchTziakchaSession(parsed.id);
  try {
    const round = await fetchTziakchaRound(parsed.id);
    if (round.belongs && TZIAKCHA_ID_RE.test(String(round.belongs))) {
      try {
        return await fetchTziakchaSession(String(round.belongs));
      } catch (_) {
        // 整场信息不可用时仍允许转换当前小局。
      }
    }
    return round;
  } catch (roundError) {
    if (parsed.kind === 'record') throw roundError;
    return fetchTziakchaSession(parsed.id);
  }
}

// 国标麻将合法牌号集合（11-19 万 / 21-29 饼 / 31-39 条 / 41-44 风 / 45-47 中白发）
const VALID_TILES = new Set();
for (const base of [10, 20, 30]) {
  for (let i = 1; i <= 9; i++) VALID_TILES.add(base + i);
}
for (let i = 1; i <= 7; i++) VALID_TILES.add(40 + i);
// 花牌 51-58
const VALID_FLOWERS = new Set();
for (let i = 1; i <= 8; i++) VALID_FLOWERS.add(50 + i);

const VALID_COMBINATION_PREFIXES = new Set(['s', 'S', 'k', 'K', 'g', 'G', 'q']);

function validateGBCalcInput(body, requireGetTile) {
  const errors = [];

  if (!Array.isArray(body.hand_tiles) || body.hand_tiles.length === 0) {
    errors.push('hand_tiles 必须是非空整数数组');
  } else {
    for (const t of body.hand_tiles) {
      if (!Number.isInteger(t) || (!VALID_TILES.has(t) && !VALID_FLOWERS.has(t))) {
        errors.push(`非法牌号: ${t}`);
        break;
      }
    }
  }

  const combos = Array.isArray(body.tiles_combination) ? body.tiles_combination : [];
  for (const c of combos) {
    if (typeof c !== 'string' || c.length < 3 || !VALID_COMBINATION_PREFIXES.has(c[0])) {
      errors.push(`非法副露/暗刻格式: ${c}`);
      break;
    }
    const tileId = parseInt(c.slice(1), 10);
    if (!VALID_TILES.has(tileId)) {
      errors.push(`非法副露牌号: ${c}`);
      break;
    }
  }

  if (requireGetTile) {
    if (!Number.isInteger(body.get_tile) || !VALID_TILES.has(body.get_tile)) {
      errors.push('get_tile 必须是合法牌号');
    }
  }

  const flowers = Array.isArray(body.flower_tiles) ? body.flower_tiles : [];
  for (const f of flowers) {
    if (!Number.isInteger(f) || !VALID_FLOWERS.has(f)) {
      errors.push(`非法花牌: ${f}`);
      break;
    }
  }

  return errors;
}

// 根据雀渣牌谱链接读取公开牌谱；上游地址固定，用户输入只用于牌谱 ID。
router.post('/tziakcha-record', async (req, res) => {
  try {
    const parsed = parseTziakchaInput(req.body?.input);
    const data = await fetchTziakchaRecordInput(parsed);
    return res.json({ success: true, data });
  } catch (error) {
    const message = error?.name === 'AbortError'
      ? '读取雀渣牌谱超时，请稍后重试'
      : error?.message || '无法读取雀渣牌谱';
    return res.status(message.includes('格式') || message.includes('请输入') || message.includes('仅支持') ? 400 : 502).json({
      success: false,
      message,
    });
  }
});

// 调用 Python FastAPI 计算服务并透传响应
async function proxyToCalcServer(path, body) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), CALC_TIMEOUT_MS);
  try {
    const resp = await fetch(`${CALC_BASE_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
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

// 国标算分接口
router.post('/gb/score', async (req, res) => {
  const errors = validateGBCalcInput(req.body, true);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const { status, data } = await proxyToCalcServer('/calc/gb/score', {
      hand_tiles: req.body.hand_tiles,
      tiles_combination: req.body.tiles_combination || [],
      way_to_hepai: req.body.way_to_hepai || [],
      get_tile: req.body.get_tile,
      flower_tiles: req.body.flower_tiles || [],
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data.detail || data.message || '计算服务返回错误'
      });
    }
    return res.json({
      success: true,
      data: data,
    });
  } catch (error) {
    console.error('国标算分代理错误:', error);
    return res.status(502).json({
      success: false,
      message: '无法连接到计算服务'
    });
  }
});

// 国标拆解接口
router.post('/gb/decompose', async (req, res) => {
  const errors = validateGBCalcInput(req.body, true);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const { status, data } = await proxyToCalcServer('/calc/gb/decompose', {
      hand_tiles: req.body.hand_tiles,
      tiles_combination: req.body.tiles_combination || [],
      way_to_hepai: req.body.way_to_hepai || [],
      get_tile: req.body.get_tile,
      flower_tiles: req.body.flower_tiles || [],
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data.detail || data.message || '计算服务返回错误'
      });
    }
    return res.json({
      success: true,
      data: data,
    });
  } catch (error) {
    console.error('国标拆解代理错误:', error);
    return res.status(502).json({
      success: false,
      message: '无法连接到计算服务'
    });
  }
});

// 国标听牌待牌接口
router.post('/gb/tingpai', async (req, res) => {
  const errors = validateGBCalcInput(req.body, false);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const { status, data } = await proxyToCalcServer('/calc/gb/tingpai', {
      hand_tiles: req.body.hand_tiles,
      tiles_combination: req.body.tiles_combination || [],
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data.detail || data.message || '计算服务返回错误'
      });
    }
    return res.json({
      success: true,
      data: data,
    });
  } catch (error) {
    console.error('国标听牌代理错误:', error);
    return res.status(502).json({
      success: false,
      message: '无法连接到计算服务'
    });
  }
});

// 牌理：14 张切牌后向听 / 进张分析
router.post('/paili', async (req, res) => {
  const errors = validateGBCalcInput(req.body, false);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const { status, data } = await proxyToCalcServer('/calc/paili', {
      hand_tiles: req.body.hand_tiles,
      tiles_combination: req.body.tiles_combination || [],
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data.detail || data.message || '计算服务返回错误'
      });
    }
    return res.json({
      success: true,
      data: data,
    });
  } catch (error) {
    console.error('牌理代理错误:', error);
    return res.status(502).json({
      success: false,
      message: '无法连接到计算服务'
    });
  }
});

// ===========================================================================
// 虹雀²（Hongque 2 v1.6）和牌/算分：纯 Node 实现，不依赖 Python 计算服务。
// 测试路径（隐藏入口，不出现在导航中）：/hongque-calc
// 请求体示例：
// {
//   "hand": ["AX1","AX2","AX3","BX4","BX5","BX6"],
//   "open_melds": [{"tiles":["CX7","DX7","EX7"]}],
//   "self_draw": true,
//   "before_first_discard": false,
//   "wall_empty": false
// }
// ===========================================================================

const HQ_TILE_RE = /^[A-Ga-g][XxYy][1-9]$/;

function validateHongqueInput(body) {
  const errors = [];
  if (!Array.isArray(body.hand) || body.hand.length === 0) {
    errors.push('hand 必须是非空牌码数组');
  } else {
    const seen = new Set();
    for (const code of body.hand) {
      if (typeof code !== 'string' || !HQ_TILE_RE.test(code.trim())) {
        errors.push(`非法虹雀牌码: ${code}`);
        break;
      }
      const normalized = code.trim().toUpperCase();
      if (seen.has(normalized)) {
        errors.push(`虹雀牌不可重复: ${normalized}`);
        break;
      }
      seen.add(normalized);
    }
  }

  const melds = Array.isArray(body.open_melds) ? body.open_melds : [];
  for (const meld of melds) {
    if (!meld || !Array.isArray(meld.tiles) || meld.tiles.length < 3) {
      errors.push('open_melds 中每个牌组必须包含 tiles 数组（至少 3 张）');
      break;
    }
    const seen = new Set();
    for (const code of meld.tiles) {
      if (typeof code !== 'string' || !HQ_TILE_RE.test(code.trim())) {
        errors.push(`非法虹雀牌码: ${code}`);
        break;
      }
      const normalized = code.trim().toUpperCase();
      if (seen.has(normalized)) {
        errors.push(`虹雀牌不可重复: ${normalized}`);
        break;
      }
      seen.add(normalized);
    }
  }
  return errors;
}

function hongqueFlags(body) {
  return {
    selfDraw: Boolean(body.self_draw),
    beforeFirstDiscard: Boolean(body.before_first_discard),
    wallEmpty: Boolean(body.wall_empty),
  };
}

function normalizeHongqueHand(hand) {
  return hand.map((code) => code.trim().toUpperCase());
}

function normalizeHongqueMelds(melds) {
  return melds.map((meld) => ({
    ...meld,
    tiles: meld.tiles.map((code) => code.trim().toUpperCase()),
  }));
}

// 虹雀算分：返回最优和牌拆解与番种
router.post('/hongque/score', (req, res) => {
  const errors = validateHongqueInput(req.body);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const hand = normalizeHongqueHand(req.body.hand);
    const openMelds = normalizeHongqueMelds(Array.isArray(req.body.open_melds) ? req.body.open_melds : []);
    const flags = hongqueFlags(req.body);
    const result = hongque.bestWinResult(hand, openMelds, flags);
    return res.json({
      success: true,
      data: {
        is_hepai: result !== null,
        hand,
        open_melds: openMelds,
        flags,
        result,
      },
    });
  } catch (error) {
    console.error('虹雀算分错误:', error);
    return res.status(400).json({ success: false, message: error.message || '虹雀算分失败' });
  }
});

// 虹雀全部和牌拆解（按分数从高到低排序）
router.post('/hongque/decompose', (req, res) => {
  const errors = validateHongqueInput(req.body);
  if (errors.length > 0) {
    return res.status(400).json({ success: false, message: errors.join('; ') });
  }

  try {
    const hand = normalizeHongqueHand(req.body.hand);
    const openMelds = normalizeHongqueMelds(Array.isArray(req.body.open_melds) ? req.body.open_melds : []);
    const flags = hongqueFlags(req.body);
    const results = hongque.allWinResults(hand, openMelds, flags);
    return res.json({
      success: true,
      data: {
        is_hepai: results.length > 0,
        hand,
        open_melds: openMelds,
        flags,
        decompositions: results,
      },
    });
  } catch (error) {
    console.error('虹雀拆解错误:', error);
    return res.status(400).json({ success: false, message: error.message || '虹雀拆解失败' });
  }
});

module.exports = router;
