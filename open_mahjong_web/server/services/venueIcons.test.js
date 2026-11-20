const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { normalizeTeamIcon } = require('./venueIcons');
test('optional icons, content-addressed upload and invalid inputs', () => {
  const old = process.env.OM_DATA_DIR;
  const dir = fs.mkdtempSync(path.join(process.env.OM_TEST_ARTIFACTS || os.tmpdir(), 'venue-icons-'));
  process.env.OM_DATA_DIR = dir;
  try {
    assert.equal(normalizeTeamIcon('').value, '');
    assert.equal(normalizeTeamIcon(undefined, '/existing.png').value, '/existing.png');
    const image = fs.readFileSync(path.join(__dirname, '../../../open_mahjong_unity/Assets/Resources/image/logo.png'));
    const input = 'data:image/png;base64,' + image.toString('base64');
    const result = normalizeTeamIcon(input);
    assert.ok(result.value?.startsWith('/api/venue-icons/'));
    assert.equal(normalizeTeamIcon(input).value, result.value);
    assert.equal(normalizeTeamIcon(result.value).value, result.value);
    assert.deepEqual(fs.readFileSync(path.join(dir, 'venue-icons', result.value.split('/').pop())), image);
    for (const invalid of ['https://example.com/a.png', '/api/venue-icons/../x', 'data:image/svg+xml;base64,PHN2Zz4=', 'data:image/png;base64,bm90IGEgcG5n', 'data:image/png;base64,' + 'A'.repeat(90000)]) assert.ok(normalizeTeamIcon(invalid).error);
  } finally { if(old === undefined) delete process.env.OM_DATA_DIR; else process.env.OM_DATA_DIR=old; }
});
