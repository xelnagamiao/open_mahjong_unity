const assert = require('node:assert/strict');
const test = require('node:test');
const { parseRecordShareInput, classicReplayLinks } = require('./recordShareParse');

test('parses 2D and 3D share URLs including round and node', () => {
  assert.deepEqual(
    parseRecordShareInput('https://salasasa.cn/2d/record/V9V0a25tKe?round=5&node=11'),
    { game_id: 'V9V0a25tKe', round: 5, node: 11, player: null, playerIndex: null }
  );
  assert.deepEqual(
    parseRecordShareInput('https://salasasa.cn/game-unity?recordId=V9V0a25tKe&round=5&node=11'),
    { game_id: 'V9V0a25tKe', round: 5, node: 11, player: null, playerIndex: null }
  );
  assert.deepEqual(parseRecordShareInput('V9V0a25tKe'), {
    game_id: 'V9V0a25tKe',
    round: null,
    node: null,
    player: null,
    playerIndex: null,
  });
});

test('player identity survives both link formats, including UID zero and original seat zero', () => {
  for (const [uid, index] of [[10001372, 2], [0, 0]]) {
    const links = classicReplayLinks({ game_id: 'ABC123', round: 3, node: 0, protagonist_user_id: uid, protagonist_player_index: index });
    for (const url of Object.values(links)) {
      assert.deepEqual(parseRecordShareInput(url), { game_id: 'ABC123', round: 3, node: 0, player: uid, playerIndex: index });
    }
  }
  for (const query of ['player=-1&playerIndex=4', 'player=abc&playerIndex=1.5', 'player=&playerIndex=', 'player=2147483648&playerIndex=-1']) {
    const parsed = parseRecordShareInput(`/2d/record/ABC123?${query}`);
    assert.equal(parsed.player, null);
    assert.equal(parsed.playerIndex, null);
  }
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
