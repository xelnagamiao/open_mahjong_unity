const GAME_ID_RE = /^[0-9A-Za-z]{1,16}$/;
const SHARE_GAME_ID_RE = /(?:(?:^|\/)2d\/record\/|[?&]recordId=|salasasa:\/\/record\/)([0-9A-Za-z]{1,16})(?=[/?#&]|$)/i;

function shareInteger(value, min = 0, max = 2147483647) {
  if (typeof value !== 'string' && typeof value !== 'number') return null;
  if (String(value).trim() === '') return null;
  const n = Number(value);
  return Number.isInteger(n) && n >= min && n <= max ? n : null;
}

function parseQueryPosition(text) {
  const queryText = text.includes('?') ? text.slice(text.indexOf('?') + 1).split('#')[0] : '';
  const query = new URLSearchParams(queryText);
  const roundText = query.get('round');
  const nodeText = query.get('node');
  const roundRaw = Number(roundText);
  const nodeRaw = Number(nodeText);
  return {
    ...(['main','open','closed'].includes(query.get('table')) && { table: query.get('table') }),
    ...(query.get('cursor') === 'v2' && { cursor: 'v2' }),
    player: shareInteger(query.get('player')),
    playerIndex: shareInteger(query.get('playerIndex'), 0, 3),
    round: roundText != null && roundText !== '' && Number.isFinite(roundRaw) && roundRaw >= 1
      ? Math.floor(roundRaw)
      : null,
    node: nodeText != null && nodeText !== '' && Number.isFinite(nodeRaw) && nodeRaw >= 0
      ? Math.floor(nodeRaw)
      : null,
  };
}

function parseOneRecordShare(raw) {
  const text = String(raw || '').trim().replace(/^['"]|['"]$/g, '');
  if (!text) return null;
  const gameId = GAME_ID_RE.test(text) ? text : text.match(SHARE_GAME_ID_RE)?.[1];
  if (!gameId) return null;
  const position = parseQueryPosition(text);
  return {
    game_id: gameId,
    ...position,
  };
}

/** 从纯 ID、2D 或 3D 分享链接中提取牌谱 ID 以及可选的 round/node。 */
function parseRecordShareInput(raw) {
  const text = String(raw || '').trim();
  if (!text) return null;
  const whole = parseOneRecordShare(text);
  if (whole) return whole;
  for (const line of text.split(/\r?\n/)) {
    const parsed = parseOneRecordShare(line);
    if (parsed) return parsed;
  }
  return null;
}

function replayQuery(round, node, player, playerIndex, table, cursor) {
  const params = new URLSearchParams();
  if (round != null) params.set('round', String(round));
  if (node != null) params.set('node', String(node));
  if (shareInteger(player) != null) params.set('player', String(player));
  if (shareInteger(playerIndex, 0, 3) != null) params.set('playerIndex', String(playerIndex));
  if (table) params.set('table', table);
  if (cursor) params.set('cursor', cursor);
  const query = params.toString();
  return query ? `?${query}` : '';
}

function classicReplayLinks(item) {
  const id = encodeURIComponent(item.game_id);
  const query = replayQuery(item.round, item.node, item.protagonist_user_id, item.protagonist_player_index, item.table, item.cursor);
  const unityQuery = new URLSearchParams();
  unityQuery.set('recordId', item.game_id);
  if (item.round != null) unityQuery.set('round', String(item.round));
  if (item.node != null) unityQuery.set('node', String(item.node));
  if (shareInteger(item.protagonist_user_id) != null) unityQuery.set('player', String(item.protagonist_user_id));
  if (shareInteger(item.protagonist_player_index, 0, 3) != null) unityQuery.set('playerIndex', String(item.protagonist_player_index));
  if (item.table) unityQuery.set('table', item.table);
  if (item.cursor) unityQuery.set('cursor', item.cursor);
  return {
    url_2d: `/2d/record/${id}${query}`,
    url_3d: `/game-unity?${unityQuery.toString()}`,
  };
}

module.exports = {
  GAME_ID_RE,
  shareInteger,
  parseRecordShareInput,
  classicReplayLinks,
};
