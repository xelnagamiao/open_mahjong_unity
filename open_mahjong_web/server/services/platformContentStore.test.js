const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createStore, normalize, publicDocument, imageInfo } = require('./platformContentStore');
const seed = require('../content/platform-content.seed.json');
const clone = x => structuredClone(x);
function temporary(t) { const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'platform-content-')); t.after(() => fs.rmSync(dir, { recursive: true, force: true })); return { dir, store: createStore(dir) }; }
test('published seed communities and existing thanks are seeded once', t => {
  const { store } = temporary(t); const d = store.read();
  assert.deepEqual(d.communities.map(x => x.id), seed.communities.map(x => x.id)); assert.equal(d.credits.length, 8);
  assert.deepEqual(d.communities.filter(x => x.kind === 'qq').map(x => x.number), ['906497522', '389832188', '1015232788', '330875568']);
  assert.equal(d.communities.find(x => x.kind === 'wechat').qrIsExample, true);
  assert.equal(d.communities.find(x => x.kind === 'telegram').linkUrl, 'https://t.me/open_mahjong_unity');
  assert.equal(d.communities.find(x => x.kind === 'discord').linkUrl, 'https://discord.gg/RqvxDDgdFH');
  assert.equal(store.read().revision, 1);
});
test('publication survives restart, creates history and rejects stale revisions', t => {
  const { store, dir } = temporary(t); const d = store.read(); d.appearance = { style: 'urban', palette: 'ivory' };
  const saved = store.publish(d); assert.equal(saved.revision, 2); assert.equal(createStore(dir).read().appearance.palette, 'ivory');
  assert.equal(JSON.parse(fs.readFileSync(path.join(dir, 'history/1.json'))).revision, 1);
  assert.throws(() => store.publish(d), e => e.status === 409); assert.equal(store.read().revision, 2);
});
test('disabled and reordered rows are projected consistently, empty publication remains empty', t => {
  const { store, dir } = temporary(t); const d = store.read(); d.communities[0].enabled = false; d.communities[5].sort = -1; d.credits[0].enabled = false;
  const pub = publicDocument(store.publish(d)); assert.equal(pub.communities[0].id, 'discord'); assert.equal(pub.communities.length, seed.communities.length - 1); assert.equal(pub.credits.length, 7); assert.ok(pub.communities.every(x => !('enabled' in x)));
  const empty = store.read(); empty.communities = []; empty.credits = []; empty.thanksClosing = ''; store.publish(empty);
  assert.deepEqual(createStore(dir).read().communities, []); assert.deepEqual(publicDocument(store.read()).credits, []);
});
test('QR image upload is content addressed, shared assets persist, traversal is rejected', t => {
  const { store } = temporary(t); const sample = fs.readFileSync(path.join(__dirname, '../content/community-wechat-sample.png'));
  assert.ok(imageInfo(sample).width >= 64); const a = store.upload(sample); const b = store.upload(sample); assert.equal(a.url, b.url);
  const d = store.read(); d.communities[1].qrUrl = a.url; d.communities[1].qrIsExample = false; assert.equal(store.publish(d).communities[1].qrUrl, a.url);
  assert.throws(() => store.assetPath('../../secret'), e => e.status === 404);
  assert.throws(() => store.upload(sample.subarray(0, 24))); assert.throws(() => store.upload(Buffer.from('<svg/>')));
  const oversized = Buffer.from(sample); oversized.writeUInt32BE(4096, 16); assert.throws(() => store.upload(oversized));
});
for (const url of ['javascript:alert(1)', 'http://t.me/example', 'https://t.me.evil.test/a', 'https://user:pass@t.me/a', 'https://discord.gg:8443/a']) {
  test('rejects unsafe invitation ' + url, () => { const d = clone(seed); d.communities[0].linkUrl = url; assert.throws(() => normalize(d), e => e.status === 400); });
}
test('invalid fields, duplicate ids, missing assets, and oversized content leave publication untouched', t => {
  const { store } = temporary(t); const original = store.read();
  for (const change of [d => d.communities[0].number = 'abc', d => d.communities[0].id = d.communities[1].id, d => d.communities[0].description = 'x'.repeat(481), d => d.appearance.style = 'bad', d => d.communities[1].qrUrl = '/api/platform/content/assets/' + '0'.repeat(64) + '.png']) {
    const d = clone(original); change(d); assert.throws(() => store.publish(d)); assert.equal(store.read().revision, 1);
  }
  const d = clone(seed); d.credits = Array.from({ length: 24 }, (_, i) => ({ id: 'large_' + i, title: '姓名', text: '字'.repeat(5000), enabled: true, sort: i })); assert.throws(() => normalize(d));
});
test('corrupt content is reported without silently replacing published data', t => {
  const { store, dir } = temporary(t); store.read(); fs.writeFileSync(path.join(dir, 'content.json'), '{broken'); assert.throws(() => store.read()); assert.equal(fs.readFileSync(path.join(dir, 'content.json'), 'utf8'), '{broken');
});

test('optional English community content survives publish, restart and public projection', t => {
  const { store, dir } = temporary(t); const draft = store.read(); const original = clone(draft.communities[0]);
  Object.assign(draft.communities[0], { titleEn: 'Player Community', descriptionEn: 'Discuss rules and find a table.', actionLabelEn: 'Copy group number', closedMessageEn: 'Temporarily unavailable' });
  const published = store.publish(draft); const restored = createStore(dir).read();
  for (const field of ['titleEn', 'descriptionEn', 'actionLabelEn', 'closedMessageEn']) {
    assert.equal(restored.communities[0][field], draft.communities[0][field]);
    assert.equal(publicDocument(published).communities.find(x => x.id === original.id)[field], draft.communities[0][field]);
  }
  for (const field of ['title', 'description', 'number', 'joinMode', 'linkUrl', 'qrUrl']) assert.equal(restored.communities[0][field], original[field]);
  const legacy = clone(seed); for (const row of legacy.communities) for (const key of ['titleEn', 'descriptionEn', 'actionLabelEn', 'closedMessageEn']) delete row[key];
  assert.equal(normalize(legacy).communities[0].descriptionEn, '');
});

test('invalid English content cannot change the publication', t => {
  const { store } = temporary(t); const original = store.read();
  for (const [field, limit] of [['titleEn',96],['descriptionEn',960],['actionLabelEn',48],['closedMessageEn',128]]) {
    for (const value of ['x'.repeat(limit + 1), 123, 'invalid\u0000text']) {
      const draft = clone(original); draft.communities[0][field] = value;
      assert.throws(() => store.publish(draft), e => e.status === 400);
      assert.deepEqual(store.read(), original);
    }
  }
});

test('Hongque and Chai follow Discord without moving the other community positions', () => {
  const document = normalize(seed);
  const rows = publicDocument(document).communities;
  const discord = rows.findIndex(x => x.id === 'discord');
  assert.equal(rows[discord + 1].id, 'hongque');
  assert.equal(rows[discord + 2].id, 'qingque');
  assert.equal(rows[discord + 2].number, '1015232788');
});
