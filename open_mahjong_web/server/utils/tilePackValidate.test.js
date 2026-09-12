const assert = require('node:assert/strict');
const test = require('node:test');
const archiver = require('archiver');
const { validateZip } = require('./tilePackValidate');
const { isZip } = require('./zipEntries');

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

test('tile face zip requires hand and table folders', async () => {
  const buf = await zipFiles({
    'hand/11.png': PNG_1x1,
    'table/11.png': PNG_1x1,
  });
  assert.equal(isZip(buf), true);
  const result = validateZip('tile_face', buf);
  assert.equal(result.error, undefined);
  assert.equal(result.meta.hand_count, 1);
  assert.equal(result.meta.table_count, 1);
});

test('tile face zip accepts chinese folder names', async () => {
  const buf = await zipFiles({
    '手牌牌面/45.png': PNG_1x1,
    '3D牌面/45.png': PNG_1x1,
  });
  const result = validateZip('tile_face', buf);
  assert.equal(result.error, undefined);
  assert.equal(result.meta.hand_count, 1);
  assert.equal(result.meta.table_count, 1);
});

test('tile face zip missing table folder is rejected', async () => {
  const buf = await zipFiles({
    'hand/11.png': PNG_1x1,
  });
  const result = validateZip('tile_face', buf);
  assert.match(result.error, /hand\/ 和 table\//);
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
