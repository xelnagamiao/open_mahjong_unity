const MAX_CFG_BYTES = 64 * 1024;
const IDENTITY = () => ({ x: 0, y: 0, scale: 100 });

// Percentages. Per-tile values are composed inside the corresponding 2D/3D group.
function parseTilePackConfig(text, isFaceId) {
  const groups = { '2d': IDENTITY(), '3d': IDENTITY() };
  const tiles = { '2d': {}, '3d': {} };
  const sections = new Set();
  let target = null;
  let keys = new Set();
  const lines = String(text).replace(/^\uFEFF/, '').split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].split('#', 1)[0].trim();
    if (!line) continue;
    const fail = (detail) => { throw new Error(`牌面配置第 ${index + 1} 行：${detail}`); };
    const section = /^\[(2d|3d)(?:\.([0-9]+))?\]$/i.exec(line);
    if (section) {
      const mode = section[1].toLowerCase();
      const id = section[2] == null ? null : Number(section[2]);
      if (id != null && (String(id) !== section[2] || !isFaceId(id))) fail('无效的牌面代号');
      const name = id == null ? mode : `${mode}.${id}`;
      if (sections.has(name)) fail('配置段重复');
      sections.add(name);
      target = id == null ? groups[mode] : (tiles[mode][id] = IDENTITY());
      keys = new Set();
      continue;
    }
    const pair = /^(x|y|scale)\s*=\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*%?$/i.exec(line);
    if (!target || !pair) fail('使用 [2d]、[3d]、[2d.11] 或 [3d.11]，参数为 x、y、scale');
    const key = pair[1].toLowerCase();
    const value = Number(pair[2]);
    if (keys.has(key)) fail('参数重复');
    const low = key === 'scale' ? 25 : -100;
    const high = key === 'scale' ? 200 : 100;
    if (!Number.isFinite(value) || value < low || value > high) fail(`${key} 范围为 ${low}%～${high}%`);
    keys.add(key);
    target[key] = value;
  }
  return { groups, tiles };
}

module.exports = { MAX_CFG_BYTES, parseTilePackConfig };
