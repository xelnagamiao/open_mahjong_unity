import {shangraoInfoAt} from '../../utils/shangraoReplay.js'
import { nanchangInfoAt } from '../../utils/nanchangReplay.js'
import { fuzhouInfoAt } from '../../utils/fuzhouReplay.js'
import { wenzhouInfoAt } from '../../utils/wenzhouReplay.js'

/** Physical Salasasa ids only: white substitutes and Wuhan skins are ordinary tiles. */
export function recordHandJokers(detail, round, node) {
  const title = detail?.record?.game_title
  const rule = title?.rule || detail?.rule
  const subRule = title?.sub_rule || detail?.sub_rule
  let tiles = []
  if (rule === 'guobiao' && subRule === 'guobiao/four_jokers') tiles = [59]
  else if (rule === 'guobiao' && subRule === 'guobiao/huahun') tiles = [51,52,53,54]
  else if (rule === 'fuzhou') tiles = [fuzhouInfoAt(round,node)?.gold_tile]
  else if (rule === 'shangrao') tiles = [shangraoInfoAt(round,node)?.bao]
  else if (rule === 'malaysia') tiles = [81]
  else if (rule === 'hangzhou') tiles = [46]
  else if (rule === 'hongzhong') tiles = [45]
  else if (rule === 'guangdong' && subRule === 'guangdong/mil2023') tiles = [55, 56, 57, 58]
  else if (rule === 'nanchang') {
    const info = nanchangInfoAt(round, node)
    tiles = [info?.indicator, info?.secondary]
  } else if (rule === 'wenzhou') tiles = [wenzhouInfoAt(round, node)?.caishen]
  else if (rule === 'wuhan') {
    // The joker is unknown until the indicator/state event at the current replay step.
    for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
      if (tick[0] !== 'wuhan') continue
      if (tick[1] === 'indicator') {
        const indicator = Number(tick[2])
        const honors = [41, 42, 43, 44, 47, 46]
        const index = honors.indexOf(indicator === 45 ? 44 : indicator)
        tiles = indicator >= 11 && indicator <= 39 && indicator % 10 >= 1 && indicator % 10 <= 9
          ? [Math.floor(indicator / 10) * 10 + indicator % 10 % 9 + 1]
          : index >= 0 ? [honors[(index + 1) % honors.length]] : []
      } else if (tick[1] === 'state') {
        let info = tick[2]
        if (typeof info === 'string') {
          try { info = JSON.parse(info) } catch { continue }
        }
        tiles = [info?.joker]
      }
    }
  }
  return [...new Set(tiles.filter(tile => Number.isInteger(tile) && (tile >= 11 && tile <= 59 || tile === 81)))]
}

/** Shared by the hand row and settlement; ids and ordinary ordering stay intact. */
export function jokerFirstSortKey(tile, jokers, ordinaryKey) {
  return ordinaryKey(tile) - (jokers.includes(tile) ? 10000 : 0)
}
