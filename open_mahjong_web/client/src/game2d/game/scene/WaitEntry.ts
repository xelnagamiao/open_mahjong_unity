import { Container, Text } from 'pixi.js'
import { TILE_WIDTH, TILE_HEIGHT } from './constants'
import { Tile } from './Tile'
import { getGameFontFamily } from '../fontLoader'
import { locale, tr } from '../../../i18n'

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
  ) {
    super()
    this.x = x
    this.y = y
    this.scale.set(0.7)

    const selfDrawnOnly = selfDrawnF > 0 && baseF <= 0
    const notEnough = selfDrawnF <= 0 && baseF <= 0
    const fanLabel = tr(baseF > 0 ? `${Math.round(baseF)}${unit}` : selfDrawnOnly ? '仅自摸' : '未起和')
    const fanText = new Text({
      text: fanLabel,
      style: {
        fontFamily: getGameFontFamily(),
        fontSize: fanLabel.length > 3 ? 142 : 185,
        fill: notEnough ? 0x8b2f2f : selfDrawnOnly ? 0x777777 : 0x000000,
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
    tile.alpha = notEnough || remainingCount <= 0 ? 0.58 : selfDrawnOnly ? 0.78 : 1.0
    this.addChild(tile)

    if (parent) parent.addChild(this)
  }
}
