import { Container, Text } from 'pixi.js'
import { TILE_WIDTH, TILE_HEIGHT } from './constants'
import { Tile } from './Tile'
import { getGameFontFamily } from '../fontLoader'
import { locale, tr } from '../../../i18n'
import { resolveFourJokersHint, type FourJokersHintRequest } from '../../calc/guobiao/fourJokers'

/**
 * One entry in the wait-info bar. Shows a tile with its base fan,
 * self-drawn fan, and remaining count.
 */
export class WaitEntry extends Container {
  constructor(
    parent: Container | null,
    x: number,
    y: number,
    tid: number,
    baseF: number,
    selfDrawnF: number,
    remainingCount: number,
    unit = '番',
    hint?: {label?: string; kind?: 'dianhe' | 'ron_only' | 'zimo' | 'wuyi'; four_jokers_request?: FourJokersHintRequest},
  ) {
    super()
    this.x = x
    this.y = y
    this.scale.set(0.7)

    const selfDrawnOnly = hint?.kind ? hint.kind === 'zimo' : selfDrawnF > 0 && baseF <= 0
    const ronOnly = hint?.kind === 'ron_only'
    const singleMethod = selfDrawnOnly || ronOnly
    const notEnough = hint?.kind ? hint.kind === 'wuyi' : selfDrawnF <= 0 && baseF <= 0
    const fanLabel = tr(hint?.label ?? (ronOnly ? '仅点和' : baseF > 0 ? `${Math.round(baseF)}${unit}` : selfDrawnOnly ? '仅自摸' : '未起和'))
    const fanText = new Text({
      text: fanLabel,
      style: {
        fontFamily: getGameFontFamily(),
        fontSize: fanLabel.length > 3 ? 142 : 185,
        fill: notEnough ? 0x8b2f2f : singleMethod ? 0x777777 : 0x000000,
        align: 'center',
      },
    })
    fanText.anchor.set(0.5)
    if (locale.value === 'fr' && fanText.width > TILE_WIDTH * 1.15) {
      fanText.scale.set(TILE_WIDTH * 1.15 / fanText.width)
    }
    fanText.y = TILE_HEIGHT / 2 + 150
    this.addChild(fanText)

    // Remaining count
    const remText = new Text({
      text: `${remainingCount}`,
      style: { fontFamily: getGameFontFamily(), fontSize: 170, fill: remainingCount <= 0 ? 0x8b2f2f : 0x000000, align: 'center' },
    })
    remText.anchor.set(0.5)
    remText.x = TILE_WIDTH / 2 + 90
    remText.y = -TILE_HEIGHT / 2 + 90
    this.addChild(remText)

    // Tile preview
    const tile = new Tile(tid, true)
    tile.eventMode = 'passive'
    tile.cursor = 'default'
    tile.alpha = notEnough || remainingCount <= 0 ? 0.58 : singleMethod ? 0.78 : 1.0
    this.addChild(tile)

    if (hint?.four_jokers_request) {
      tile.alpha = remainingCount <= 0 ? 0.58 : 1
      fanText.style.fill = 0x777777
      void resolveFourJokersHint(hint.four_jokers_request).then(result => {
        if (this.destroyed || fanText.destroyed) return
        fanText.text = tr(result?.label ?? '提示暂不可用')
        fanText.style.fontSize = fanText.text.length > 3 ? 142 : 185
        fanText.style.fill = result?.kind === 'wuyi' ? 0x8b2f2f : result?.kind === 'dianhe' ? 0x000000 : 0x777777
        tile.alpha = remainingCount <= 0 || result?.kind === 'wuyi' ? 0.58 : result?.kind === 'zimo' || result?.kind === 'ron_only' ? 0.78 : 1
      })
    }

    if (parent) parent.addChild(this)
  }
}
