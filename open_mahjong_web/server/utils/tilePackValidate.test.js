const assert = require('node:assert/strict');
const test = require('node:test');
const archiver = require('archiver');
const { validateZip } = require('./tilePackValidate');
const { isZip } = require('./zipEntries');
const { renderTilePackPreview } = require('./tilePackPreview');
const sharp = require('sharp');

const PNG_1x1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64'
);

function zipFiles(files) {
  return new Promise((resolve, reject) => {
    const archive = archiver('zip', { zlib: { level: 0 } });
    const chunks = [];
    archive.on('data', (c) => chunks.push(c));
    archive.on('end', () => resolve(Buffer.concat(chunks)));
    archive.on('error', reject);
    for (const [name, data] of Object.entries(files)) {
      archive.append(data, { name });
    }
    archive.finalize();
  });
}

test('tile face zip accepts one shared image set and layout', async () => {
  const buf = await zipFiles({
    '11.png': PNG_1x1,
    'tiles.cfg': Buffer.from('[2d]\nx=2\n[3d.11]\nscale=95'),
  });
  assert.equal(isZip(buf), true);
  const result = validateZip('tile_face', buf);
  assert.equal(result.error, undefined);
  assert.equal(result.meta.shared_count, 1);
  assert.equal(result.meta.layout.groups['2d'].x, 2);
  assert.equal(result.meta.layout.tiles['3d'][11].scale, 95);
  assert.equal(result.meta.format_version, 2);
  assert.deepEqual(result.meta.previews, [{ name: '11.png', label: '11.png' }]);
});

test('tile face zip rejects independent image folders', async () => {
  for (const folder of ['hand', 'table', '手牌牌面', '3D牌面']) {
    const buf = await zipFiles({
      [`${folder}/45.png`]: PNG_1x1,
      'tiles.cfg': Buffer.from('[2d]\nscale=100'),
    });
    const result = validateZip('tile_face', buf);
    assert.match(result.error, /一套牌号 PNG/);
  }
});

test('tile face zip requires cfg', async () => {
  const buf = await zipFiles({
    '11.png': PNG_1x1,
  });
  const result = validateZip('tile_face', buf);
  assert.match(result.error, /tiles.cfg/);
});

test('full appearance requires its embedded images and accepts explicit canvas', async () => {
  const cfg = Buffer.from('[2d.11]\noffset_aspect=0.6992287918\nscale=107.5\n[hand]\ny=-2\nscale=109\n[appearance]\nhand_background=hand-bg.png\nback=0.2,0.3,0.4,1\nuse_background=false');
  const missing = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, 'tiles.cfg': cfg }));
  assert.match(missing.error, /缺少背景图片/);
  const result = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, 'tiles.cfg': cfg, 'hand-bg.png': PNG_1x1 }));
  assert.equal(result.error, undefined);
  assert.equal(result.meta.layout.tiles['2d'][11].offset_aspect, 0.6992287918);
  assert.equal(result.meta.layout.groups.hand.scale, 109);
  assert.equal(result.meta.layout.appearance.back, '0.2,0.3,0.4,1');
});

test('rejects ambiguous or invalid settings without losing images', async () => {
  for (const config of ['[2d]\nx=NaN', '[3d]\nscale=201', '[2d]\nx=1\nx=2', '[2d.0105]\nscale=100', '[appearance]\ntable_back=../../file.png', '[appearance]\nside=0,0,2,1', '[appearance]\nside=0x0,0,0,1', '[appearance]\nback_brightness=0x1', '[2d]\nimage_scale=90', '[2d.11]\nimage_scale=0']) {
    const result = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, 'tiles.cfg': Buffer.from(config) }));
    assert.ok(result.error, config);
  }
  const result = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, 'nested/11.png': PNG_1x1, 'tiles.cfg': Buffer.from('[2d]') }));
  assert.match(result.error, /代号重复/);
});

test('exported image basis preserves adjustments at the slider limits', async () => {
  const cfg = Buffer.from('[2d.11]\nx=100\ny=-100\nscale=25\nimage_x=0.4\nimage_y=-1.8\nimage_scale=71.25\n[3d.11]\nscale=200\nimage_scale=114.875\n[appearance]\nhand_back_follow=true');
  const result = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, 'tiles.cfg': cfg }));
  assert.equal(result.error, undefined);
  assert.equal(result.meta.layout.tiles['2d'][11].scale, 25);
  assert.equal(result.meta.layout.tiles['2d'][11].image_scale, 71.25);
  assert.equal(result.meta.layout.tiles['3d'][11].image_scale, 114.875);
  assert.equal(result.meta.layout.appearance.hand_back_follow, 'true');
});

test('only the canonical UTF-8 cfg is accepted', async () => {
  for (const [name, data] of [['layout.cfg', Buffer.from('[2d]')], ['tiles.cfg', Buffer.from('\ufeff[2d]', 'utf16le')]]) {
    const result = validateZip('tile_face', await zipFiles({ '11.png': PNG_1x1, [name]: data }));
    assert.ok(result.error);
  }
});

test('tile background zip accepts hand-bg', async () => {
  const buf = await zipFiles({
    'hand-bg.png': PNG_1x1,
  });
  const result = validateZip('tile_background', buf);
  assert.equal(result.error, undefined);
  assert.deepEqual(result.meta.files, ['hand-bg.png']);
});

test('tile background zip without known files is rejected', async () => {
  const buf = await zipFiles({
    'readme.txt': Buffer.from('x'),
  });
  const result = validateZip('tile_background', buf);
  assert.equal(result.error, '压缩包需包含 hand-back.png / hand-bg.png 或 table-bg.png');
});

test('catalog uses the four hand faces in display order and fills missing faces from the official pack', async () => {
  const ids = [11, 21, 31, 41];
  const colors = ['#111111', '#353535', '#797979', '#bbbbbb'];
  const images = await Promise.all(colors.map(background =>
    sharp({ create: { width: 24, height: 34, channels: 3, background } }).png().toBuffer()));
  const entries = ids.map((id, i) => ({ name: `hand/${id}.png`, data: images[i] })).reverse();
  entries.push({ name: 'table/31.png', data: images[0] });
  const full = await renderTilePackPreview(entries);
  const partial = await renderTilePackPreview([{ name: '41.png', data: images[3] }]);
  const official = await renderTilePackPreview([]);
  const cell = (image, i) => sharp(image).extract({ left: i * 102, top: 0, width: 96, height: 136 }).raw().toBuffer();
  for (const [i, color] of colors.entries()) {
    const sample = await cell(full, i);
    assert.deepEqual([...sample.subarray(0, 3)], Array(3).fill(parseInt(color.slice(1, 3), 16)));
    assert.deepEqual(await cell(partial, i), await cell(i === 3 ? full : official, i));
  }
});
