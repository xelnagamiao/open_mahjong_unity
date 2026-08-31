const RECORD_ID_RE = /^[0-9A-Za-z]{1,16}$/
const SHARE_GAME_ID_RE = /(?:(?:^|\/)2d\/record\/|[?&]recordId=|salasasa:\/\/record\/)([0-9A-Za-z]{1,16})(?=[/?#&]|$)/i

function winShareNode(win) {
  return win.shareNode != null ? win.shareNode : win.node
}

/** 从纯 ID、2D 或 3D 分享链接中提取牌谱 ID 以及可选的 round/node。 */
export function parseRecordShareInput(raw) {
  const text = String(raw || '').trim().replace(/^['"]|['"]$/g, '')
  if (!text) return null
  const gameId = RECORD_ID_RE.test(text) ? text : text.match(SHARE_GAME_ID_RE)?.[1]
  if (!gameId) return null
  const queryText = text.includes('?') ? text.slice(text.indexOf('?') + 1).split('#')[0] : ''
  const query = new URLSearchParams(queryText)
  const roundRaw = Number(query.get('round'))
  const nodeRaw = Number(query.get('node'))
  return {
    gameId,
    round: Number.isFinite(roundRaw) && roundRaw >= 1 ? Math.floor(roundRaw) : undefined,
    node: Number.isFinite(nodeRaw) && nodeRaw >= 0 ? Math.floor(nodeRaw) : undefined,
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

export function replaySharePath(kind, gameId, round, node) {
  const id = encodeURIComponent(gameId)
  if (kind === '3d-node') return `/game-unity?recordId=${id}&round=${round}&node=${node}`
  if (kind === '3d') return `/game-unity?recordId=${id}`
  if (kind === '2d-node') return `/2d/record/${id}?round=${round}&node=${node}`
  return `/2d/record/${id}`
}
