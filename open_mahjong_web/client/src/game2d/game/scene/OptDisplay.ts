import { Container, Graphics, Text, Rectangle, FederatedPointerEvent } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS, TILE_SEP, LINE_WIDTH,
  SCALE_FACTOR, FRONT_COLOR, BORDER_COLOR, IS_MOBILE_PHONE,
} from './constants'
import { DirLabel } from './Display'

export type AutoWinMode = 0 | 1 | 2

export interface OptDisplayCallbacks {
  onCycleAutoWin: () => void
  onToggleNoMeld: () => void
  onToggleAutoDiscard: () => void
}

/**
 * Option toggle panel (desktop: hover-reveal; mobile: always visible).
 * Contains toggles for auto-win, no-meld, and auto-discard.
 */
export class OptDisplay extends Container {
  private readonly buttons: { gfx: Graphics; text: Text }[] = []

  constructor(
    parent: Container,
    autoWinLabel: string,
    noMeldLabel: string,
    autoDiscardLabel: string,
    callbacks: OptDisplayCallbacks,
    trigger?: DirLabel,
  ) {
    super()

    if (IS_MOBILE_PHONE) {
      // Flat horizontal layout
      this.x = 0
      this.y = -2 * TILE_SEP - 0.7 * TILE_HEIGHT - SCALE_FACTOR / 2
      const bg = new Graphics()
      bg.roundRect(-TILE_WIDTH * 10, -TILE_WIDTH * 0.75, TILE_WIDTH * 20, TILE_WIDTH * 1.5, TILE_RADIUS)
      bg.fill({ color: FRONT_COLOR })
      bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
      bg.visible = false
      this.addChild(bg)

      this.addButton(-TILE_WIDTH * 5, 0, autoWinLabel, callbacks.onCycleAutoWin)
      this.addButton(0, 0, noMeldLabel, callbacks.onToggleNoMeld)
      this.addButton(TILE_WIDTH * 5, 0, autoDiscardLabel, callbacks.onToggleAutoDiscard)
    } else {
      // Vertical box layout
      const bg = new Graphics()
      bg.roundRect(-TILE_WIDTH * 3, -TILE_WIDTH * 3, TILE_WIDTH * 6, TILE_WIDTH * 6, TILE_RADIUS)
      bg.fill({ color: FRONT_COLOR })
      bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
      this.addChild(bg)

      this.addButton(0, -TILE_WIDTH * 3 / 2, autoWinLabel, callbacks.onCycleAutoWin)
      this.addButton(0, 0, noMeldLabel, callbacks.onToggleNoMeld)
      this.addButton(0, TILE_WIDTH * 3 / 2, autoDiscardLabel, callbacks.onToggleAutoDiscard)

      this.alpha = 0

      if (trigger) {
        // Tablet mode: DirLabel toggles visibility
        trigger.eventMode = 'static'
        trigger.cursor = 'pointer'
        trigger.on('pointerdown', () => {
          if (this.alpha < 0.5) {
            this.alpha = 1
            this.buttons.forEach((b) => { b.gfx.visible = true })
          } else {
            this.alpha = 0
            this.buttons.forEach((b) => { b.gfx.visible = false })
          }
        })
      } else {
        // Desktop hover
        this.eventMode = 'static'
        this.cursor = 'pointer'
        this.hitArea = new Rectangle(-TILE_WIDTH * 3, -TILE_WIDTH * 3, TILE_WIDTH * 6, TILE_WIDTH * 6)
        this.buttons.forEach((b) => { b.gfx.visible = false })
        this.on('pointerover', () => {
          this.alpha = 1
          this.buttons.forEach((b) => { b.gfx.visible = true })
        })
        this.on('pointerout', () => {
          this.alpha = 0
          this.buttons.forEach((b) => { b.gfx.visible = false })
        })
      }
    }

    parent.addChild(this)
  }

  private addButton(x: number, y: number, labelText: string, onClick: () => void): void {
    const gfx = new Graphics()
    gfx.roundRect(-TILE_WIDTH * 2, -TILE_WIDTH * 0.5, TILE_WIDTH * 4, TILE_WIDTH, TILE_RADIUS)
    gfx.fill({ color: FRONT_COLOR })
    gfx.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    gfx.x = x
    gfx.y = y
    gfx.eventMode = 'static'
    gfx.cursor = 'pointer'
    gfx.on('pointerover', () => { gfx.tint = 0xe0e0e0 })
    gfx.on('pointerout', () => { gfx.tint = 0xffffff })
    gfx.on('pointerdown', (e: FederatedPointerEvent) => {
      if (e.button !== 0) return
      onClick()
    })

    const text = new Text({
      text: labelText,
      style: { fontFamily: 'CmuSerif, SimFang, sans-serif', fontSize: 170, fill: 0x000000, align: 'center' },
    })
    text.anchor.set(0.5)
    text.x = 0
    text.y = 0
    gfx.addChild(text)

    this.addChild(gfx)
    this.buttons.push({ gfx, text })
  }

  updateLabels(autoWin: string, noMeld: string, autoDiscard: string): void {
    if (this.buttons[0]) this.buttons[0].text.text = autoWin
    if (this.buttons[1]) this.buttons[1].text.text = noMeld
    if (this.buttons[2]) this.buttons[2].text.text = autoDiscard
  }
}
