const express = require('express');
const router = express.Router();
const config = require('../config/config');

const CALC_BASE_URL = config.calcServer.baseUrl.replace(/\/$/, '');
const CALC_TIMEOUT_MS = config.calcServer.timeoutMs;

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

module.exports = router;
