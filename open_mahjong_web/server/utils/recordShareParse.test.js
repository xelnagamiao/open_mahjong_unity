const assert = require('node:assert/strict');
const test = require('node:test');
const { parseRecordShareInput, classicReplayLinks } = require('./recordShareParse');

test('parses 2D and 3D share URLs including round and node', () => {
  assert.deepEqual(
    parseRecordShareInput('https://salasasa.cn/2d/record/V9V0a25tKe?round=5&node=11'),
    { game_id: 'V9V0a25tKe', round: 5, node: 11 }
  );
  assert.deepEqual(
    parseRecordShareInput('https://salasasa.cn/game-unity?recordId=V9V0a25tKe&round=5&node=11'),
    { game_id: 'V9V0a25tKe', round: 5, node: 11 }
  );
  assert.deepEqual(parseRecordShareInput('V9V0a25tKe'), {
    game_id: 'V9V0a25tKe',
    round: null,
    node: null,
  });
});

test('uses the first matching line when two URLs are pasted together', () => {
  const pasted = [
    'https://salasasa.cn/2d/record/V9V0a25tKe?round=5&node=11',
    'https://salasasa.cn/game-unity?recordId=V9V0a25tKe&round=5&node=11',
  ].join('\n');
  assert.equal(parseRecordShareInput(pasted).game_id, 'V9V0a25tKe');
});

test('builds 2D and 3D replay links from a classic record', () => {
  assert.deepEqual(
    classicReplayLinks({ game_id: 'V9V0a25tKe', round: 5, node: 11 }),
    {
      url_2d: '/2d/record/V9V0a25tKe?round=5&node=11',
      url_3d: '/game-unity?recordId=V9V0a25tKe&round=5&node=11',
    }
  );
});
