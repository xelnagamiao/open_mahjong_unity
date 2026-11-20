const MAX_CFG_BYTES = 64 * 1024;
const IDENTITY = () => ({ x: 0, y: 0, scale: 100 });

// Percentages. Per-tile values are composed inside the corresponding 2D/3D group.
function parseTilePackConfig(text, isFaceId) {
  const groups = { '2d': IDENTITY(), '3d': IDENTITY(), hand: IDENTITY() };
  let appearance = null;
  const tiles = { '2d': {}, '3d': {} };
  const sections = new Set();
  let target = null;
  let keys = new Set();
  const lines = String(text).replace(/^\uFEFF/, '').split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].split('#', 1)[0].trim();
    if (!line) continue;
    const fail = (detail) => { throw new Error(`牌面配置第 ${index + 1} 行：${detail}`); };
    const section = /^\[(2d|3d|hand|appearance)(?:\.([0-9]+))?\]$/i.exec(line);
    if (section) {
      const mode = section[1].toLowerCase();
      const id = section[2] == null ? null : Number(section[2]);
      if (id != null && (mode === 'hand' || mode === 'appearance')) fail('无效的配置段');
      if (id != null && (String(id) !== section[2] || !isFaceId(id))) fail('无效的牌面代号');
      const name = id == null ? mode : `${mode}.${id}`;
      if (sections.has(name)) fail('配置段重复');
      sections.add(name);
      target = mode === 'appearance' ? (appearance = {}) : id == null ? groups[mode] : (tiles[mode][id] = IDENTITY());
      keys = new Set();
      continue;
    }
    if (target && target === appearance) {
      const pair = /^([a-z_]+)\s*=\s*(.*)$/i.exec(line);
      if (!pair) fail('外观参数格式错误');
      const key = pair[1].toLowerCase(), value = pair[2].trim();
      if (keys.has(key)) fail('参数重复');
      validateAppearance(key, value, fail);
      keys.add(key); target[key] = value;
      continue;
    }
    const pair = /^(x|y|scale|offset_aspect|image_x|image_y|image_scale)\s*=\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*%?$/i.exec(line);
    if (!target || !pair) fail('使用 [2d]、[3d]、[2d.11] 或 [3d.11]，参数为 x、y、scale');
    const key = pair[1].toLowerCase();
    const value = Number(pair[2]);
    if (keys.has(key)) fail('参数重复');
    if (key.startsWith('image_') && !Object.values(tiles['2d']).includes(target) && !Object.values(tiles['3d']).includes(target)) fail('图片基准仅用于单牌配置');
    const low = ['offset_aspect', 'image_scale'].includes(key) ? .01 : key === 'scale' ? 25 : -100;
    const high = key === 'image_scale' ? 1000 : key === 'offset_aspect' ? 100 : key === 'scale' ? 200 : 100;
    if (key === 'offset_aspect' && target !== groups['2d'] && !Object.values(tiles['2d']).includes(target)) fail('offset_aspect 仅用于 2D');
    if (!Number.isFinite(value) || value < low || value > high) fail(`${key} 范围为 ${low}%～${high}%`);
    keys.add(key);
    target[key] = value;
  }
  return { groups, tiles, appearance };
}

function validateAppearance(key, value, fail) {
  const number = (text, min, max) => /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(text.trim())
    && Number.isFinite(Number(text)) && Number(text) >= min && Number(text) <= max;
  if (['back', 'side', 'back_edge', 'front_edge', 'face'].includes(key)) {
    const parts = value.split(',');
    if (parts.length !== 4 || parts.some((v) => !number(v, 0, 1))) fail('颜色需为四个 0–1 数值');
  } else if (['back_brightness', 'face_brightness', 'front_edge_brightness', 'back_edge_brightness'].includes(key)) {
    if (!number(value, -100, 100)) fail('亮度范围为 -100～100');
  } else if (['back_edge_mode', 'front_edge_mode'].includes(key)) {
    if (!['0', '1', '2'].includes(value)) fail('无效的牌边模式');
  } else if (['use_background', 'use_solid', 'hand_back_follow'].includes(key)) {
    if (!['true', 'false'].includes(value)) fail('开关需为 true 或 false');
  } else {
    const images = { hand_background: 'hand-bg.png', hand_back: 'hand-back.png', table_background: 'table-bg.png', table_back: 'table-back.png' };
    if (!Object.hasOwn(images, key) || (value && value !== images[key])) fail(`无效的外观参数: ${key}`);
  }
}

module.exports = { MAX_CFG_BYTES, parseTilePackConfig };
