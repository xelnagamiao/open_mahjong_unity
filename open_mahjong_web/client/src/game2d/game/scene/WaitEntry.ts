import { Container, Text } from 'pixi.js'
import { TILE_WIDTH, TILE_HEIGHT } from './constants'
import { Tile } from './Tile'

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
  ) {
    super()
    this.x = x
    this.y = y
    this.scale.set(0.7)

    const sameF = Math.abs(baseF - selfDrawnF) < 1e-3
    const selfDrawnOnly = selfDrawnF > 0 && baseF < 0.1
    const occasionalOnly = selfDrawnF < 0.1 && baseF < 0.1

    // Base fan
    const baseText = new Text({
      text: occasionalOnly ? '\u2014' : baseF.toFixed(1),
      style: { fontFamily: 'CmuSerif', fontSize: sameF ? 200 : 160, fill: 0x000000, align: 'center' },
    })
    baseText.anchor.set(0.5)
    baseText.x = sameF ? 0 : -168
    baseText.y = TILE_HEIGHT / 2 + 150
    if (selfDrawnOnly) baseText.visible = false
    this.addChild(baseText)

    // Self-drawn fan
    const selfText = new Text({
      text: selfDrawnF.toFixed(1),
      style: { fontFamily: 'CmuSerif', fontSize: sameF || selfDrawnOnly ? 200 : 160, fill: 0x888888, align: 'center' },
    })
    selfText.anchor.set(0.5)
    selfText.x = sameF || selfDrawnOnly ? 0 : 168
    selfText.y = TILE_HEIGHT / 2 + 150
    if (sameF || occasionalOnly) selfText.visible = false
    this.addChild(selfText)

    // Remaining count
    const remText = new Text({
      text: `${remainingCount}`,
      style: { fontFamily: 'CmuSerif', fontSize: 170, fill: selfDrawnOnly || occasionalOnly ? 0x888888 : 0x000000, align: 'center' },
    })
    remText.anchor.set(0.5)
    remText.x = TILE_WIDTH / 2 + 90
    remText.y = -TILE_HEIGHT / 2 + 90
    this.addChild(remText)

    // Tile preview
    const tile = new Tile(tid, true)
    tile.eventMode = 'passive'
    tile.cursor = 'default'
    tile.alpha = occasionalOnly ? 0.7 : selfDrawnOnly ? 0.7 : 1.0
    this.addChild(tile)

    if (parent) parent.addChild(this)
  }
}
