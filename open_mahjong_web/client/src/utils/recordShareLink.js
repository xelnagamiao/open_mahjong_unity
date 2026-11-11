const RECORD_ID_RE = /^[0-9A-Za-z]{1,16}$/
const SHARE_GAME_ID_RE = /(?:(?:^|\/)2d\/record\/|[?&]recordId=|salasasa:\/\/record\/)([0-9A-Za-z]{1,16})(?=[/?#&]|$)/i

function winShareNode(win) {
  return win.shareNode != null ? win.shareNode : win.node
}

export function shareInteger(value, min = 0, max = 2147483647) {
  if (typeof value !== 'string' && typeof value !== 'number') return undefined
  if (String(value).trim() === '') return undefined
  const n = Number(value)
  return Number.isInteger(n) && n >= min && n <= max ? n : undefined
}

/** player 为用户 ID，playerIndex 为开局席位（机器人和外部牌谱也能唯一定位）。 */
export function parseRecordShareInput(raw) {
  const text = String(raw || '').trim().replace(/^['"]|['"]$/g, '')
  if (!text) return null
  const gameId = RECORD_ID_RE.test(text) ? text : text.match(SHARE_GAME_ID_RE)?.[1]
  if (!gameId) return null
  const queryText = text.includes('?') ? text.slice(text.indexOf('?') + 1).split('#')[0] : ''
  const query = new URLSearchParams(queryText)
  return {
    gameId,
    round: shareInteger(query.get('round'), 1),
    node: shareInteger(query.get('node')),
    player: shareInteger(query.get('player')),
    playerIndex: shareInteger(query.get('playerIndex'), 0, 3),
  }
}

export function sharePathForWin(win) {
  if (!win?.game_id) return ''
  return `/2d/record/${encodeURIComponent(win.game_id)}?round=${win.round}&node=${winShareNode(win)}`
}

export function sharePathForWin3d(win) {
  if (!win?.game_id) return ''
  return `/game-unity?recordId=${encodeURIComponent(win.game_id)}&round=${win.round}&node=${winShareNode(win)}`
}

export function replaySharePath(kind, gameId, round, node, player, playerIndex) {
  const id = encodeURIComponent(gameId)
  const is3d = kind === '3d' || kind === '3d-node'
  const query = new URLSearchParams(is3d ? { recordId: gameId } : {})
  if (kind === '3d-node' || kind === '2d-node') {
    query.set('round', String(round))
    query.set('node', String(node))
  }
  if (shareInteger(player) != null) query.set('player', String(player))
  if (shareInteger(playerIndex, 0, 3) != null) query.set('playerIndex', String(playerIndex))
  return `${is3d ? '/game-unity' : `/2d/record/${id}`}${query.size ? `?${query}` : ''}`
}
