import { Container, Graphics } from 'pixi.js'
import { TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS, LINE_WIDTH, FRONT_COLOR, BORDER_COLOR } from './constants'

/**
 * A helper button shown above a hand tile on mobile devices,
 * making it easier for the player to tap to discard.
 */
export class DiscardHelper extends Container {
  constructor(parent: Container, xOffset: number, yOffset: number) {
    super()

    // Background square
    const bg = new Graphics()
    bg.roundRect(-TILE_WIDTH, -TILE_WIDTH, TILE_WIDTH * 2, TILE_WIDTH * 2, TILE_RADIUS)
    bg.fill({ color: FRONT_COLOR })
    bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(bg)

    // Vertical dark bar
    const fg = new Graphics()
    fg.rect(-LINE_WIDTH, -TILE_WIDTH, LINE_WIDTH * 2, TILE_HEIGHT)
    fg.fill({ color: 0x606060 })
    this.addChild(fg)

    this.x = xOffset
    this.y = yOffset
    this.visible = true
    this.alpha = 0.6
    this.eventMode = 'static'
    this.cursor = 'pointer'

    this.on('pointerover', () => { bg.tint = 0xe0e0e0 })
    this.on('pointerout', () => { bg.tint = 0xffffff })

    parent.addChild(this)
  }
}
