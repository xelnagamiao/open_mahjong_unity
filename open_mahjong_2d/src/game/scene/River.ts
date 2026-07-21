import { Container } from 'pixi.js'
import type { ContainerChild } from 'pixi.js'
import { TILE_WIDTH, TILE_HEIGHT, TILE_SEP, WAIT_SEP } from './constants'
import { Tile } from './Tile'

const RIVER_X = [0, TILE_SEP + TILE_WIDTH * 3 + TILE_HEIGHT / 2, 0, -TILE_SEP - TILE_WIDTH * 3 - TILE_HEIGHT / 2] as const
const RIVER_Y = [TILE_SEP + TILE_WIDTH * 3 + TILE_HEIGHT / 2, 0, -TILE_SEP - TILE_WIDTH * 3 - TILE_HEIGHT / 2, 0] as const

/**
 * A player's discard pile, rendered as a grid of face-up tiles.
 */
export class River extends Container {
  readonly direction: number
  readonly tileList: Tile[] = []
  num = 0
  waiting = false

  constructor(direction: number, parent: Container) {
    super()
    this.direction = direction
    this.x = RIVER_X[direction] ?? 0
    this.y = RIVER_Y[direction] ?? 0
    this.rotation = (-Math.PI / 2) * direction
    parent.addChild(this)
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
