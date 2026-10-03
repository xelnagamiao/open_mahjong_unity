const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const express = require('express');
process.env.ADMIN_USER_IDS ||= '1';
process.env.ADMIN_JWT_EXPIRES_SEC ||= '3600';
process.env.ADMIN_JWT_SECRET ||= 'record-share-test-only';
process.env.BOT_API_JWT_SECRET ||= 'record-share-bot-test-only';

test('shared source survives a new client and converts into a playable record', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'record-shares-'));
  const previous = process.env.OM_DATA_DIR;
  process.env.OM_DATA_DIR = dir;
  const app = express();
  app.use('/shares', require('./recordConvertShares'));
  // Match production ordering: large share requests precede the default parser.
  app.use(express.json());
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const url = `http://127.0.0.1:${server.address().port}/shares`;
  const post = body => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  try {
    const source = JSON.stringify([
      { type: 'start_game', names: ['甲', '乙', '丙', '丁'] },
      { type: 'start_kyoku', bakaze: 'E', kyoku: 1, honba: 0, kyotaku: 0, oya: 0, scores: [25000, 25000, 25000, 25000], dora_marker: '1m', tehais: Array.from({ length: 4 }, () => ['1m','2m','3m','4m','5m','6m','7m','8m','9m','1p','2p','3p','4p']) },
      { type: 'tsumo', actor: 0, pai: '5p' },
      { type: 'dahai', actor: 0, pai: '5p', tsumogiri: true },
      { type: 'end_kyoku' }, { type: 'end_game' },
    ]);
    const response = await post({ mode: 'mjai2sala', source });
    assert.equal(response.status, 201);
    const { id } = await response.json();
    assert.match(id, /^[a-f0-9]{32}$/);
    const loaded = await (await fetch(`${url}/${id}`)).json();
    assert.deepEqual(loaded, { mode: 'mjai2sala', source });
    const { getMode } = await import('../../client/src/utils/recordConvert/index.js');
    const record = JSON.parse(await getMode(loaded.mode).convert(loaded.source));
    assert.ok(Object.keys(record.game_round).length > 0);
    assert.equal(record.game_title.rule, 'riichi');
    assert.equal((await post({ mode: 'bz2sala', source: 'x'.repeat(150_000) })).status, 201);
    assert.equal((await post({ mode: 'sala2tz', source })).status, 400);
    assert.equal((await post({ mode: 'tz2sala', source: '' })).status, 400);
    assert.equal((await post({ mode: 'tz2sala', source: 'x'.repeat(2 * 1024 * 1024 + 1) })).status, 413);
    assert.equal((await fetch(`${url}/invalid`)).status, 404);
    assert.equal((await fetch(`${url}/${'a'.repeat(32)}`)).status, 404);
  } finally {
    await new Promise(resolve => server.close(resolve));
    if (previous === undefined) delete process.env.OM_DATA_DIR;
    else process.env.OM_DATA_DIR = previous;
    await fs.rm(dir, { recursive: true, force: true });
  }
});
