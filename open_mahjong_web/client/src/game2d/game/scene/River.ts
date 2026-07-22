import { Container, Graphics } from 'pixi.js'
import type { ContainerChild } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_SEP, WAIT_SEP,
  TILE_RADIUS, LINE_WIDTH, FRONT_COLOR, BORDER_COLOR,
} from './constants'
import { Tile } from './Tile'
import type { FlowerAreaDisplay } from '../../lib/sceneAppearance'

const RIVER_X = [0, TILE_SEP + TILE_WIDTH * 3 + TILE_HEIGHT / 2, 0, -TILE_SEP - TILE_WIDTH * 3 - TILE_HEIGHT / 2] as const
const RIVER_Y = [TILE_SEP + TILE_WIDTH * 3 + TILE_HEIGHT / 2, 0, -TILE_SEP - TILE_WIDTH * 3 - TILE_HEIGHT / 2, 0] as const
const FLOWER_COLUMNS = 4
const RIVER_RIGHT_EDGE = TILE_WIDTH * 3
const FLOWER_AREA_GAP = TILE_SEP * 1.35
const FLOWER_START_X = RIVER_RIGHT_EDGE + FLOWER_AREA_GAP + TILE_WIDTH
// The first flower row shares the baseline of the river's second row.
const FLOWER_START_Y = TILE_HEIGHT
const FLOWER_GAP_X = TILE_WIDTH + TILE_SEP * 0.12
const FLOWER_GAP_Y = TILE_HEIGHT + TILE_SEP * 0.12

/**
 * A player's discard pile, rendered as a grid of face-up tiles.
 */
export class River extends Container {
  readonly direction: number
  readonly tileList: Tile[] = []
  readonly flowerList: Tile[] = []
  private readonly flowerLayer = new Container()
  private readonly flowerAreaBackground = new Graphics()
  private flowerAreaDisplay: FlowerAreaDisplay = 'always'
  private flowerAreaColor = FRONT_COLOR
  private flowerAreaAlpha = 0.82
  num = 0
  waiting = false

  constructor(
    direction: number,
    parent: Container,
    flowerAreaDisplay: FlowerAreaDisplay = 'always',
    flowerAreaColor = FRONT_COLOR,
    flowerAreaAlpha = 0.82,
  ) {
    super()
    this.direction = direction
    this.x = RIVER_X[direction] ?? 0
    this.y = RIVER_Y[direction] ?? 0
    this.rotation = (-Math.PI / 2) * direction
    this.flowerAreaDisplay = flowerAreaDisplay
    this.flowerAreaColor = flowerAreaColor
    this.flowerAreaAlpha = Math.max(0, Math.min(1, flowerAreaAlpha))
    this.redrawFlowerAreaBackground()
    this.updateFlowerAreaVisibility()
    super.addChild(this.flowerAreaBackground)
    super.addChild(this.flowerLayer)
    parent.addChild(this)
  }

  setFlowerAreaAppearance(mode: FlowerAreaDisplay, color: number, alpha: number): void {
    this.flowerAreaDisplay = mode
    this.flowerAreaColor = color
    this.flowerAreaAlpha = Math.max(0, Math.min(1, alpha))
    this.redrawFlowerAreaBackground()
    this.updateFlowerAreaVisibility()
  }

  private redrawFlowerAreaBackground(): void {
    const flowerAreaWidth = FLOWER_GAP_X * (FLOWER_COLUMNS - 1) + TILE_WIDTH
    const flowerAreaHeight = FLOWER_GAP_Y + TILE_HEIGHT
    this.flowerAreaBackground.clear()
    this.flowerAreaBackground.roundRect(
      FLOWER_START_X - TILE_WIDTH / 2,
      FLOWER_START_Y - TILE_HEIGHT / 2,
      flowerAreaWidth,
      flowerAreaHeight,
      TILE_RADIUS,
    )
    this.flowerAreaBackground.fill({ color: this.flowerAreaColor, alpha: this.flowerAreaAlpha })
    this.flowerAreaBackground.stroke({
      color: BORDER_COLOR,
      alpha: this.flowerAreaAlpha,
      width: LINE_WIDTH,
    })
  }

  private updateFlowerAreaVisibility(): void {
    this.flowerAreaBackground.visible = this.flowerAreaDisplay === 'always'
      || (this.flowerAreaDisplay === 'when-present' && this.flowerList.length > 0)
  }

  /** Place full-size flower tiles outside the river, with a fixed clear gap. */
  addFlower(tile: Tile, animate = false): void {
    const index = this.flowerList.length
    const x = FLOWER_START_X + (index % FLOWER_COLUMNS) * FLOWER_GAP_X
    const y = FLOWER_START_Y + Math.floor(index / FLOWER_COLUMNS) * FLOWER_GAP_Y
    tile.off('pointerdown')
    tile.setHoverCallbacks(null, null)
    tile.setHoverWhileDisabled(true)
    tile.setInputEnabled(false)
    this.flowerList.push(tile)
    this.updateFlowerAreaVisibility()
    if (animate && tile.parent) {
      const movement = tile.generalMove(this.flowerLayer, x, y, 0)
      tile.scale.set(1)
      movement.catch(() => {})
    } else {
      this.flowerLayer.addChild(tile)
      tile.x = x
      tile.y = y
      tile.rotation = 0
      tile.scale.set(1)
      tile.visible = true
    }
  }

  /** Override addChild to synchronously register tiles (matches old impl). */
  addChild<U extends ContainerChild[]>(...children: U): U[0] {
    for (const child of children) {
      if (child instanceof Tile) {
        child.off('pointerdown')
        child.setHoverCallbacks(null, null)
        child.setHoverWhileDisabled(true)
        child.setInputEnabled(false)
        this.num += 1
        this.tileList.push(child)
      }
    }
    return super.addChild(...children)
  }

  /** Override removeChild to synchronously unregister tiles (matches old impl). */
  removeChild<U extends ContainerChild[]>(...children: U): U[0] {
    for (const child of children) {
      if (child instanceof Tile) {
        this.num -= 1
        const idx = this.tileList.indexOf(child)
        if (idx >= 0) this.tileList.splice(idx, 1)
      }
    }
    return super.removeChild(...children)
  }

  /** X position for the nth tile (0-indexed). */
  getX(n = this.num): number {
    const w = this.waiting ? WAIT_SEP : 0
    if (n >= 24) return TILE_WIDTH * (3.5 + (n % 3)) + TILE_SEP + w
    return TILE_WIDTH * (n % 6 - 2.5) + w
  }

  /** Y position for the nth tile (0-indexed). */
  getY(n = this.num): number {
    const w = this.waiting ? WAIT_SEP : 0
    if (n >= 24) return TILE_HEIGHT * Math.floor((n - 24) / 3) + w
    return TILE_HEIGHT * Math.floor(n / 6) + w
  }

  /**
   * Register a tile that was already added (e.g. via snapshot building).
   * Does NOT call addChild — the caller is responsible for that.
   */
  registerTile(tile: Tile): void {
    // If already registered via addChild override, skip double-counting.
    if (this.tileList.includes(tile)) return
    tile.off('pointerdown')
    tile.setHoverCallbacks(null, null)
    tile.setHoverWhileDisabled(true)
    tile.setInputEnabled(false)
    this.num += 1
    this.tileList.push(tile)
  }

  /** Add a tile for snapshot building (no animation). */
  addTile(tile: Tile): void {
    tile.x = this.getX()
    tile.y = this.getY()
    tile.rotation = 0
    this.addChild(tile)
    tile.visible = true
  }

  setWaitingState(waiting: boolean, animate = false): void {
    this.waiting = waiting
    if (this.tileList.length === 0) return
    const last = this.tileList[this.num - 1]
    if (!last) return
    const x = this.getX(this.num - 1)
    const y = this.getY(this.num - 1)
    if (animate) {
      last.moveTo(x, y, 0).catch(() => {})
      return
    }
    last.x = x
    last.y = y
    last.rotation = 0
  }

  /** Remove the last tile and return it. */
  popTile(): Tile | null {
    const last = this.tileList.pop()
    if (last) {
      this.num -= 1
      super.removeChild(last)
    }
    return last ?? null
  }

  /** Stop showing the last tile as "waiting" (animated, matching old impl). */
  unwait(): void {
    this.setWaitingState(false, true)
  }
}
