const test = require('node:test');
const assert = require('node:assert/strict');
const { Writable } = require('node:stream');
const { randomBytes } = require('node:crypto');
const { inflateRawSync } = require('node:zlib');
const crc32 = require('crc-32');

// Use the real compressor, with fault injection only for error-path tests.
const archiverPath = require.resolve('archiver');
const realArchiver = require(archiverPath);
let fault;
require.cache[archiverPath].exports = (...args) => {
  const archive = realArchiver(...args);
  if (fault === 'append') archive.append = () => { throw new Error('append failed'); };
  if (fault === 'error') archive.append = () => { queueMicrotask(() => archive.emit('error', new Error('zip failed'))); };
  if (fault === 'finalize') archive.finalize = () => Promise.reject(new Error('finalize failed'));
  return archive;
};
const { streamRecordZip, RECORD_BATCH_SIZE, DOWNLOAD_CANCELLED } = require('./recordDownloadZip');
require.cache[archiverPath].exports = realArchiver;

class Response extends Writable {
  constructor({ hold = false } = {}) {
    super({ highWaterMark: 1024 });
    this.headers = {};
    this.headersSent = false;
    this.chunks = [];
    this.hold = hold;
    this.blocked = new Promise((resolve) => { this.onBlocked = resolve; });
  }
  setHeader(key, value) { this.headers[key] = value; }
  _write(chunk, _encoding, callback) {
    this.headersSent = true;
    this.chunks.push(Buffer.from(chunk));
    if (this.hold) { this.pending = callback; this.onBlocked(); }
    else callback();
  }
  release() { this.hold = false; this.pending?.(); this.pending = null; }
  zip() { return Buffer.concat(this.chunks); }
}

// Read the ZIP central directory, inflate each entry and verify its CRC. Tests
// validate the actual download bytes, not just calls made to archiver.append.
function entries(zip) {
  const end = zip.lastIndexOf(Buffer.from('504b0506', 'hex'));
  assert.ok(end >= 0, 'ZIP must have its final central directory');
  assert.equal(end + 22, zip.length);
  const count = zip.readUInt16LE(end + 10);
  let offset = zip.readUInt32LE(end + 16);
  const result = [];
  for (let i = 0; i < count; i++) {
    assert.equal(zip.readUInt32LE(offset), 0x02014b50);
    const method = zip.readUInt16LE(offset + 10);
    const size = zip.readUInt32LE(offset + 20);
    const nameSize = zip.readUInt16LE(offset + 28);
    const local = zip.readUInt32LE(offset + 42);
    assert.equal(zip.readUInt32LE(local), 0x04034b50);
    const start = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    const compressed = zip.subarray(start, start + size);
    const body = method === 0 ? compressed : inflateRawSync(compressed);
    assert.equal(body.length, zip.readUInt32LE(offset + 24));
    assert.equal(crc32.buf(body) >>> 0, zip.readUInt32LE(offset + 16));
    result.push({ name: zip.subarray(offset + 46, offset + 46 + nameSize).toString(), body: body.toString() });
    offset += 46 + nameSize + zip.readUInt16LE(offset + 30) + zip.readUInt16LE(offset + 32);
  }
  assert.equal(offset, end);
  return result;
}

function database(record = (id) => ({ game_id: id, game_title: { rule: 'hongkong' } })) {
  const calls = [];
  return { calls, async query(sql, [ids]) {
    calls.push([...ids]);
    assert.ok(ids.length <= RECORD_BATCH_SIZE);
    assert.match(sql, /access_wall\.is_unlocked IS DISTINCT FROM TRUE/);
    // PG can return rows in any order. Visibility is rechecked for each batch.
    return { rows: [...new Set(ids)].reverse().map((id) => ({ game_id: id, record: record(id) })) };
  } };
}

test('multi-batch ZIP preserves ID order, raw JSON text, objects and duplicate names', { timeout: 5000 }, async () => {
  const ids = Array.from({ length: 47 }, (_, i) => `game${i}`);
  ids.splice(21, 0, ids[0]);
  const record = (id) => id === 'game1' ? '{ "game_title": "原始 JSON" }' : { game_id: id, text: '牌谱' };
  const db = database(record);
  const res = new Response();
  await streamRecordZip(db, ids, 100, res);
  assert.deepEqual(db.calls.map((batch) => batch.length), [20, 20, 8]);
  assert.equal(res.headers['Content-Type'], 'application/zip');
  assert.equal(res.headers['Content-Disposition'], 'attachment; filename="player_100_records.zip"');
  assert.deepEqual(entries(res.zip()), ids.map((id) => ({ name: `${id}.json`, body: typeof record(id) === 'string' ? record(id) : JSON.stringify(record(id)) })));
  assert.equal(res.listenerCount('close'), 0);
});

test('missing, null and newly locked rows are excluded without dropping later batches', { timeout: 5000 }, async () => {
  const ids = Array.from({ length: 45 }, (_, i) => `game${i}`);
  const db = database((id) => id === 'game1' ? null : { id });
  const query = db.query;
  db.query = async (...args) => {
    const result = await query(...args);
    result.rows = result.rows.filter((row) => row.game_id !== 'game21');
    return result;
  };
  const res = new Response();
  await streamRecordZip(db, ids, 101, res);
  assert.deepEqual(entries(res.zip()).map((entry) => entry.name), ids.filter((id) => !['game1', 'game21'].includes(id)).map((id) => `${id}.json`));
});

test('an empty query result still completes a valid empty ZIP', { timeout: 5000 }, async () => {
  const res = new Response();
  await streamRecordZip({ query: async () => ({ rows: [] }) }, ['gone'], 1, res);
  assert.deepEqual(entries(res.zip()), []);
});

test('a slow client stops future batches until output drains', { timeout: 10000 }, async () => {
  const ids = Array.from({ length: 100 }, (_, i) => `game${i}`);
  const noise = randomBytes(32 * 1024).toString('base64');
  const db = database((id) => ({ id, noise }));
  const res = new Response({ hold: true });
  const downloading = streamRecordZip(db, ids, 1, res);
  await res.blocked;
  await new Promise((resolve) => setTimeout(resolve, 80));
  assert.equal(db.calls.length, 1, 'blocked output must not fetch more complete records');
  res.release();
  await downloading;
  assert.equal(entries(res.zip()).length, ids.length);
});

test('disconnect during compressed output cancels instead of waiting for drain', { timeout: 5000 }, async () => {
  const db = database(() => ({ noise: randomBytes(32 * 1024).toString('base64') }));
  const res = new Response({ hold: true });
  const downloading = streamRecordZip(db, Array.from({ length: 100 }, (_, i) => `${i}`), 1, res);
  const rejected = assert.rejects(downloading, { code: DOWNLOAD_CANCELLED });
  await res.blocked;
  res.destroy();
  await rejected;
  res.release();
  assert.equal(db.calls.length, 1);
  assert.equal(res.listenerCount('drain'), 0);
});

test('disconnect during SQL finishes only the current query and starts no compression', { timeout: 5000 }, async () => {
  let finishQuery;
  let calls = 0;
  const db = { query: () => { calls++; return new Promise((resolve) => { finishQuery = resolve; }); } };
  const res = new Response();
  const downloading = streamRecordZip(db, Array.from({ length: 50 }, (_, i) => `${i}`), 1, res);
  const rejected = assert.rejects(downloading, { code: DOWNLOAD_CANCELLED });
  await new Promise((resolve) => setImmediate(resolve));
  res.destroy();
  finishQuery({ rows: [{ game_id: '0', record: { hello: 'world' } }] });
  await rejected;
  assert.equal(calls, 1);
  assert.equal(res.headersSent, false);
});

test('first query failure leaves the response available for a JSON error', async () => {
  const res = new Response();
  await assert.rejects(streamRecordZip({ query: async () => { throw new Error('SQL failed'); } }, ['a'], 1, res), /SQL failed/);
  assert.equal(res.headersSent, false);
  assert.equal(res.destroyed, false);
});

test('later query failure aborts the partial download', { timeout: 5000 }, async () => {
  const db = database();
  const query = db.query;
  db.query = (...args) => db.calls.length ? Promise.reject(new Error('SQL failed')) : query(...args);
  const res = new Response();
  await assert.rejects(streamRecordZip(db, Array.from({ length: 21 }, (_, i) => `${i}`), 1, res), /SQL failed/);
  assert.equal(res.headersSent, true);
  assert.equal(res.destroyed, true);
});

for (const kind of ['append', 'error', 'finalize']) {
  test(`${kind} failure settles all pending waits`, { timeout: 5000 }, async () => {
    fault = kind;
    try {
      await assert.rejects(streamRecordZip(database(), ['a'], 1, new Response()), /failed/);
    } finally { fault = undefined; }
  });
}
