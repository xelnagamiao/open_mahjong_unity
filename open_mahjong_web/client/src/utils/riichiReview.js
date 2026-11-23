import { salasasaToMjaiRecord } from './recordConvert/mjaiRiichi.js'
import { mjaiToSalasasa } from './recordConvert/tiles.js'
import { calculatePaili } from './pailiCalculator.ts'

const calls = new Set(['chi', 'pon', 'daiminkan', 'ankan', 'kakan'])
const labels = { chi: '吃', pon: '碰', daiminkan: '明杠', ankan: '暗杠', kakan: '加杠' }
const riichiShapes = { mcrSevenPairs: false, riichiSevenPairs: true, thirteenOrphans: true, unrelatedTiles: false, combinationDragon: false }

export function isStandardFourRiichi(detail) {
  const title = detail?.record?.game_title
  return title?.rule === 'riichi' && title.sub_rule === 'riichi/standard' &&
    Number(title.player_count ?? detail.players?.length) === 4
}

function tileId(token) {
  const tile = mjaiToSalasasa(token.replace(/r$/, ''))
  if (tile == null) throw new Error('牌谱缺少可见牌信息')
  return tile
}

export function reviewTile(tile) {
  if (tile >= 41) return ['东', '南', '西', '北', '中', '白', '发'][tile - 41]
  return String(tile % 10) + ['万', '筒', '索'][Math.floor(tile / 10) - 1]
}

export function tileText(token) {
  return (token.endsWith('r') ? '赤' : '') + reviewTile(tileId(token))
}

function removeTile(tiles, tile) {
  const index = tiles.lastIndexOf(tile)
  if (index < 0) throw new Error('牌谱动作与可见牌不一致')
  tiles.splice(index, 1)
}

// Each point is attached to the position immediately before the recorded action.
// Only the selected hand and tiles already shown publicly contribute to ukeire.
export function riichiReviewChoices(record, perspective) {
  const points = []
  let hand, visible, melds, reach, round = -1
  salasasaToMjaiRecord(record, { onRoundEvent(_key, node, e) {
    const self = e.actor === perspective
    const point = { round, node: node - 1 }
    if (e.type === 'start_kyoku') {
      round++
      hand = e.tehais[perspective].map(tileId)
      visible = [tileId(e.dora_marker)]
      melds = []
      reach = false
    } else if (e.type === 'tsumo' && self) {
      hand.push(tileId(e.pai))
    } else if (e.type === 'dora') {
      visible.push(tileId(e.dora_marker))
    } else if (e.type === 'reach' && self) {
      reach = true
    } else if (e.type === 'dahai') {
      const tile = tileId(e.pai)
      visible.push(tile)
      if (self) {
        removeTile(hand, tile)
        points.push({ ...point, action: (reach ? '立直 · ' : '') + '打' + tileText(e.pai),
          shape: { handTiles: [...hand], combinations: [...melds], visibleTiles: [...visible] } })
        reach = false
      }
    } else if (calls.has(e.type)) {
      const added = e.type === 'kakan' ? [e.pai] : e.consumed
      if (!self) {
        visible.push(...added.map(tileId))
        return
      }
      for (const token of added) removeTile(hand, tileId(token))
      const tile = tileId(e.pai ?? e.consumed[0])
      if (e.type === 'kakan') {
        const pon = melds.indexOf('k' + tile)
        if (pon < 0) throw new Error('加杠缺少原碰牌')
        melds[pon] = 'g' + tile
      } else {
        if (e.type !== 'ankan') removeTile(visible, tile)
        const middle = e.type === 'chi' ? [tile, ...added.map(tileId)].sort((a,b) => a-b)[1] : tile
        const kind = e.type === 'chi' ? 's' : e.type === 'pon' ? 'k' : 'g'
        melds.push(kind + middle)
      }
      points.push({ ...point, action: labels[e.type] + tileText(e.pai ?? e.consumed[0]) })
    } else if (e.type === 'hora' && self) {
      points.push({ ...point, action: e.actor === e.target ? '自摸' : '荣和' })
    }
  } })
  return points
}

export function riichiDiscardUkeire(point) {
  return point?.shape ? calculatePaili({ ...point.shape, options: riichiShapes }) : null
}

export async function riichiReviewSignature(record) {
  const rounds = []
  salasasaToMjaiRecord(record, { onRoundEvent(_key, n, e) {
    if (e.type === 'start_kyoku') rounds.push([])
    rounds.at(-1).push({ n, e })
  } })
  const bytes = new TextEncoder().encode(JSON.stringify(rounds))
  const hash = await crypto.subtle.digest('SHA-256', bytes)
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('')
}

export function reviewActionKey(event) {
  if (event.type === 'dahai') return event.pai
  if (event.type === 'chi') {
    const position = event.consumed.filter(tile => Number(tile[0]) < Number(event.pai[0])).length
    return ['chi_low', 'chi_mid', 'chi_high'][position]
  }
  return { hora: 'agari', reach: 'riichi', daiminkan: 'kan', ankan: 'kan', kakan: 'kan' }[event.type] ?? event.type
}

export function reviewActionLabel(key) {
  const labels = { chi_low: '吃（低张）', chi_mid: '吃（中张）', chi_high: '吃（高张）', pon: '碰',
    kan: '杠', agari: '和牌', riichi: '立直', pass: '过' }
  return labels[key] ?? (/^(?:[1-9][mps]r?|[ESWNPFC])$/.test(key) ? '打' + tileText(key) : key)
}
