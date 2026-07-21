import { Container, Graphics, Text, FederatedPointerEvent } from 'pixi.js'
import {
  TILE_WIDTH, TILE_RADIUS, LINE_WIDTH,
  FRONT_COLOR, BORDER_COLOR, MELD_OPT_SCALE,
} from './constants'
import { Tile } from './Tile'

export type MeldButtonType = 'chow' | 'pung' | 'ckong' | 'hkong' | 'rkong' | 'hwin' | 'rkwin' | 'flower' | 'pass' | 'final_pass'

const MELD_LABELS: Partial<Record<MeldButtonType, string>> = {
  chow: '吃', pung: '碰', ckong: '杠', hkong: '杠', rkong: '杠', hwin: '和', rkwin: '和',
  pass: '过', final_pass: '弃',
}

export interface MeldAction {
  kind: string
  indices: number[]
  chowMode?: number
  tile?: number
}

/**
 * A single meld-choice button. Shows the action name and preview tile(s).
 */
export class MeldButton extends Container {
  private readonly bg: Graphics

  constructor(
    parent: Container,
    x: number,
    y: number,
    tid: number,
    meldType: MeldButtonType,
    chowMode: number,
    onPress: () => void,
    small: number = 0,
  ) {
    super()
    
    const isPass = meldType === 'pass' || meldType === 'final_pass'
    const isSimple = isPass || meldType === 'flower'

    this.bg = new Graphics()
    if (small === -1) {
      this.bg.roundRect(-TILE_WIDTH * 1.2, -TILE_WIDTH / 2, TILE_WIDTH, TILE_WIDTH, TILE_RADIUS)
    } else if (small === 1) {
      this.bg.roundRect(TILE_WIDTH * 0.2, -TILE_WIDTH / 2, TILE_WIDTH, TILE_WIDTH, TILE_RADIUS)
    } else {
      this.bg.roundRect(-TILE_WIDTH * 1.2, -TILE_WIDTH / 2, TILE_WIDTH * 2.4, TILE_WIDTH, TILE_RADIUS)
    }

    this.bg.fill({ color: FRONT_COLOR, alpha: isPass ? 1 : 0.8 })
    this.bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(this.bg)

    // Label
    const label = new Text({
      text: meldType === 'flower' ? '花' : (MELD_LABELS[meldType] ?? '?'),
      style: { fontFamily: 'SimFang, sans-serif', fontSize: 200, fill: 0x000000, align: 'center' },
    })
    label.anchor.set(0.5)
    label.x = -TILE_WIDTH * 0.7
    label.y = -20
    this.addChild(label)

    if (!isSimple) {
      if (meldType === 'chow') {
        // Two tiles: tid+offset based on chowMode
        const t1 = Tile.newInvisible(tid + [2, 1, -1][chowMode])
        t1.x = TILE_WIDTH * 0.7; t1.y = 0; t1.scale.set(0.57)
        t1.visible = true
        t1.eventMode = 'passive'; t1.cursor = 'default'
        t1.off('pointerover'); t1.off('pointerout')
        this.addChild(t1)

        const t2 = Tile.newInvisible(tid + [1, -1, -2][chowMode])
        t2.x = TILE_WIDTH * (0.7 - 0.57); t2.y = 0; t2.scale.set(0.57)
        t2.visible = true
        t2.eventMode = 'passive'; t2.cursor = 'default'
        t2.off('pointerover'); t2.off('pointerout')
        this.addChild(t2)
      } else {
        const t1 = Tile.newInvisible(tid)
        t1.x = TILE_WIDTH * 0.7; t1.y = 0; t1.scale.set(0.57)
        t1.visible = true
        t1.eventMode = 'passive'; t1.cursor = 'default'
        t1.off('pointerover'); t1.off('pointerout')
        t1.off('pointerdown')
        this.addChild(t1)
      }
    }
    // For pass/final_pass, center the label
    if (isSimple) {
      if (small === -1) {
        label.x = -TILE_WIDTH * 0.7
      } else if (small === 1) {
        label.x = TILE_WIDTH * 0.7
      } else {
        label.x = 0
      }
    }

    this.x = x
    this.y = y
    this.scale.set(MELD_OPT_SCALE)

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointerover', () => { this.bg.tint = 0xe0e0e0 })
    this.on('pointerout', () => { this.bg.tint = 0xffffff })
    this.on('pointerdown', (e: FederatedPointerEvent) => {
      if (e.button !== 0) return
      onPress()
    })

    parent.addChild(this)
  }
}
