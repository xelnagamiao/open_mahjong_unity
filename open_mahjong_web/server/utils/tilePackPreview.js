const path = require('node:path');
const sharp = require('sharp');

const FACES = ['11.png', '21.png', '31.png', '41.png'];
const PNG_SIGNATURE = Buffer.from('89504e470d0a1a0a', 'hex');
const WIDTH = 96;
const HEIGHT = 136;
const GAP = 6;
const TILE_COLOR = '#f5f5ef';
const defaults = path.join(__dirname, '../content/tile-preview-defaults');

// Older archives can have separate hand and table artwork. Prefer their hand faces.
async function renderTilePackPreview(entries) {
  const faces = new Map();
  for (const { name, data } of entries) {
    const key = path.posix.basename(name).toLowerCase();
    if (!FACES.includes(key) || !data.subarray(0, 8).equals(PNG_SIGNATURE)) continue;
    if (!/(?:^|\/)(?:table|3d牌面)\//i.test(name) || !faces.has(key)) faces.set(key, data);
  }
  const tiles = [];
  for (const name of FACES) {
    const source = faces.get(name) || path.join(defaults, name);
    const input = await sharp(source, { limitInputPixels: 4096 * 4096 })
      .resize(WIDTH, HEIGHT, { fit: 'contain', background: TILE_COLOR })
      .flatten({ background: TILE_COLOR }).png().toBuffer();
    tiles.push({ input, left: tiles.length * (WIDTH + GAP), top: 0 });
  }
  return sharp({ create: {
    width: tiles.length * (WIDTH + GAP) - GAP, height: HEIGHT, channels: 4, background: '#00000000',
  } }).composite(tiles).png().toBuffer();
}

module.exports = { renderTilePackPreview };
