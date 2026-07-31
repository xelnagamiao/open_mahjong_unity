import { AlphaFilter, Container, Graphics, Rectangle, Text } from 'pixi.js'
import type { ContainerChild } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_SEP, WAIT_SEP,
  TILE_RADIUS, LINE_WIDTH, BORDER_COLOR,
} from './constants'
import { Tile } from './Tile'
import type { FlowerAreaDisplay } from '../../lib/sceneAppearance'
import { getGameFontFamily } from '../fontLoader'
import { tr } from '../../../i18n'

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
const FLOWER_AREA_WIDTH = FLOWER_GAP_X * (FLOWER_COLUMNS - 1) + TILE_WIDTH
const FLOWER_AREA_HEIGHT = FLOWER_GAP_Y + TILE_HEIGHT
const FLOWER_FADE_DURATION_MS = 3000

/**
 * A player's discard pile, rendered as a grid of face-up tiles.
 */
export class River extends Container {
  readonly direction: number
  readonly tileList: Tile[] = []
  readonly flowerList: Tile[] = []
  private readonly flowerLayer = new Container()
  private readonly flowerFadeFilter = new AlphaFilter({ alpha: 1 })
  private readonly flowerAreaBackground = new Graphics()
  private readonly flowerAreaInfoLayer = new Container()
  private readonly flowerAreaRank: Text
  private readonly flowerAreaName: Text
  private readonly flowerAreaCountRow = new Container()
  private readonly flowerAreaCountLabel: Text
  private readonly flowerAreaCountValue: Text
  private flowerAreaDisplay: FlowerAreaDisplay = 'always'
  private flowerAreaColor = 0x000000
  private flowerAreaAlpha = 0.85
  private flowerAreaLabelColor = 0xffffff
  private flowerAreaCountColor = 0xffa726
  private flowerAreaLabelScale = 1
  private flowerAreaNameOffline = false
  private flowerRank = ''
  private flowerCount = 0
  private flowerFadeFrame: number | null = null
  private flowerAreaHovered = false
  num = 0
  waiting = false

  constructor(
    direction: number,
    parent: Container,
    flowerAreaDisplay: FlowerAreaDisplay = 'always',
    flowerAreaColor = 0x000000,
    flowerAreaAlpha = 0.85,
    flowerAreaLabelColor = 0xffffff,
    flowerAreaCountColor = 0xffa726,
    flowerAreaLabelScale = 1,
  ) {
    super()
    this.direction = direction
    this.x = RIVER_X[direction] ?? 0
    this.y = RIVER_Y[direction] ?? 0
    this.rotation = (-Math.PI / 2) * direction
    this.flowerAreaDisplay = flowerAreaDisplay
    this.flowerAreaColor = flowerAreaColor
    this.flowerAreaAlpha = Math.max(0, Math.min(1, flowerAreaAlpha))
    this.flowerAreaLabelColor = flowerAreaLabelColor
    this.flowerAreaCountColor = flowerAreaCountColor
    this.flowerAreaLabelScale = Math.max(0.5, Math.min(1.8, flowerAreaLabelScale))
    const infoTextStyle = {
        fontFamily: getGameFontFamily(),
        fontSize: 190,
        fill: this.flowerAreaLabelColor,
        align: 'center',
      } as const
    this.flowerAreaRank = new Text({
      text: '',
      style: infoTextStyle,
    })
    this.flowerAreaName = new Text({
      text: '',
      style: { ...infoTextStyle, fontSize: 220 },
    })
    this.flowerAreaCountLabel = new Text({
      text: tr('花牌：'),
      style: infoTextStyle,
    })
    this.flowerAreaCountValue = new Text({
      text: '0',
      style: { ...infoTextStyle, fill: this.flowerAreaCountColor },
    })
    for (const text of [this.flowerAreaRank, this.flowerAreaName]) {
      text.anchor.set(0.5)
      text.x = FLOWER_START_X + FLOWER_GAP_X * (FLOWER_COLUMNS - 1) / 2
      text.alpha = 1
      text.eventMode = 'none'
      this.flowerAreaInfoLayer.addChild(text)
    }
    this.flowerAreaCountLabel.anchor.set(0, 0.5)
    this.flowerAreaCountValue.anchor.set(0, 0.5)
    this.flowerAreaCountLabel.eventMode = 'none'
    this.flowerAreaCountValue.eventMode = 'none'
    this.flowerAreaCountRow.x = FLOWER_START_X + FLOWER_GAP_X * (FLOWER_COLUMNS - 1) / 2
    this.flowerAreaCountRow.addChild(this.flowerAreaCountLabel, this.flowerAreaCountValue)
    this.flowerAreaInfoLayer.addChild(this.flowerAreaCountRow)
    // Move the complete information layer upward by about 2 CSS px at desktop scale.
    this.flowerAreaInfoLayer.y = -20
    this.flowerAreaRank.y = FLOWER_START_Y + TILE_HEIGHT * 0.06
    this.flowerAreaName.y = FLOWER_START_Y + FLOWER_GAP_Y * 0.62
    // The scene is scaled to roughly 10% at desktop size; 20 scene units is about 2 CSS px.
    this.flowerAreaCountRow.y = FLOWER_START_Y + FLOWER_GAP_Y + TILE_HEIGHT * 0.18 - 20
    this.redrawFlowerAreaBackground()
    this.updateFlowerAreaVisibility()
    this.flowerLayer.filters = [this.flowerFadeFilter]
    super.addChild(this.flowerAreaBackground)
    super.addChild(this.flowerAreaInfoLayer)
    super.addChild(this.flowerLayer)
    this.eventMode = 'static'
    this.hitArea = new Rectangle(
      FLOWER_START_X - TILE_WIDTH / 2,
      FLOWER_START_Y - TILE_HEIGHT / 2,
      FLOWER_AREA_WIDTH,
      FLOWER_AREA_HEIGHT,
    )
    this.on('pointerenter', () => {
      this.flowerAreaHovered = true
      this.revealFlowers()
      this.redrawFlowerAreaBackground()
      this.updateFlowerAreaVisibility()
    })
    this.on('pointerleave', () => {
      this.flowerAreaHovered = false
      this.hideFlowers()
      this.redrawFlowerAreaBackground()
      this.updateFlowerAreaVisibility()
    })
    parent.addChild(this)
  }

  setPlayerInfo(rank: string, name: string, offline = false): void {
    this.flowerRank = rank
    this.flowerAreaRank.text = tr(rank)
    this.flowerAreaName.text = name
    this.flowerAreaNameOffline = offline
    this.resizeFlowerAreaInfo()
  }

  setPlayerName(name: string, offline = false): void {
    this.setPlayerInfo(this.flowerRank, name, offline)
  }

  private resizeFlowerAreaInfo(): void {
    const maxWidth = FLOWER_AREA_WIDTH - TILE_WIDTH * 0.35
    const baseSizes = [310, 205]
    const labels = [this.flowerAreaRank, this.flowerAreaName]
    labels.forEach((text, index) => {
      text.style.fontSize = baseSizes[index] * this.flowerAreaLabelScale
      text.style.fill = this.flowerAreaNameOffline ? 0xb0b0b0 : this.flowerAreaLabelColor
      text.scale.set(1)
      text.scale.set(Math.min(1, maxWidth / Math.max(text.width, 1)))
    })
    const countLabelFontSize = 170 * this.flowerAreaLabelScale
    const countValueFontSize = 270 * this.flowerAreaLabelScale
    this.flowerAreaCountLabel.style.fontSize = countLabelFontSize
    this.flowerAreaCountLabel.style.fill = this.flowerAreaNameOffline ? 0xb0b0b0 : this.flowerAreaLabelColor
    this.flowerAreaCountValue.style.fontSize = countValueFontSize
    this.flowerAreaCountValue.style.fill = this.flowerAreaCountColor
    const countGap = TILE_SEP * 0.12 * this.flowerAreaLabelScale
    this.flowerAreaCountLabel.x = 0
    this.flowerAreaCountValue.x = this.flowerAreaCountLabel.width + countGap
    this.flowerAreaCountRow.scale.set(1)
    const countWidth = this.flowerAreaCountValue.x + this.flowerAreaCountValue.width
    const countScale = Math.min(1, maxWidth / Math.max(countWidth, 1))
    this.flowerAreaCountRow.scale.set(countScale)
    this.flowerAreaCountRow.x = FLOWER_START_X
      + FLOWER_GAP_X * (FLOWER_COLUMNS - 1) / 2
      - countWidth * countScale / 2
  }

  setFlowerAreaAppearance(
    mode: FlowerAreaDisplay,
    color: number,
    alpha: number,
    labelColor: number,
    countColor: number,
    labelScale: number,
  ): void {
    this.flowerAreaDisplay = mode
    this.flowerAreaColor = color
    this.flowerAreaAlpha = Math.max(0, Math.min(1, alpha))
    this.flowerAreaLabelColor = labelColor
    this.flowerAreaCountColor = countColor
    this.flowerAreaLabelScale = Math.max(0.5, Math.min(1.8, labelScale))
    this.resizeFlowerAreaInfo()
    this.redrawFlowerAreaBackground()
    this.updateFlowerAreaVisibility()
  }

  private redrawFlowerAreaBackground(): void {
    this.flowerAreaBackground.clear()
    this.flowerAreaBackground.roundRect(
      FLOWER_START_X - TILE_WIDTH / 2,
      FLOWER_START_Y - TILE_HEIGHT / 2,
      FLOWER_AREA_WIDTH,
      FLOWER_AREA_HEIGHT,
      TILE_RADIUS,
    )
    this.flowerAreaBackground.fill({ color: this.flowerAreaColor, alpha: this.flowerAreaAlpha })
    this.flowerAreaBackground.stroke({
      color: this.flowerAreaHovered ? this.flowerAreaLabelColor : BORDER_COLOR,
      alpha: this.flowerAreaHovered ? 1 : this.flowerAreaAlpha,
      width: this.flowerAreaHovered ? LINE_WIDTH * 2 : LINE_WIDTH,
      alignment: this.flowerAreaHovered ? 0 : 0.5,
    })
  }

  private updateFlowerAreaVisibility(): void {
    this.flowerAreaBackground.visible = this.flowerAreaHovered
      || this.flowerAreaDisplay === 'always'
      || (this.flowerAreaDisplay === 'when-present' && this.flowerCount > 0)
  }

  /**
   * Update only the summarized flower count. Replay seeks use this so
   * rebuilding a snapshot does not lay every historical flower onto the table.
   */
  setFlowerCount(count: number): void {
    this.flowerCount = Math.max(0, Math.trunc(Number(count) || 0))
    this.flowerAreaCountValue.text = String(this.flowerCount)
    this.resizeFlowerAreaInfo()
    this.updateFlowerAreaVisibility()
  }

  /**
   * Restore replay flower identities without showing the historical tiles
   * immediately. Hovering the flower area reveals the full-size faces.
   */
  setReplayFlowers(tids: number[]): void {
    this.cancelFlowerFade()
    for (const tile of this.flowerList) {
      tile.removeFromParent()
      tile.destroy({ children: true })
    }
    this.flowerList.length = 0
    this.flowerLayer.removeChildren()

    tids.forEach((tid, index) => {
      const tile = Tile.newInvisible(tid)
      tile.updateTid(tid)
      tile.show()
      tile.off('pointerdown')
      tile.setHoverCallbacks(null, null)
      tile.setHoverWhileDisabled(true)
      tile.setInputEnabled(false)
      tile.x = FLOWER_START_X + (index % FLOWER_COLUMNS) * FLOWER_GAP_X
      tile.y = FLOWER_START_Y + Math.floor(index / FLOWER_COLUMNS) * FLOWER_GAP_Y
      tile.rotation = 0
      tile.scale.set(1)
      tile.visible = true
      this.flowerList.push(tile)
      this.flowerLayer.addChild(tile)
    })

    this.setFlowerCount(tids.length)
    this.flowerFadeFilter.alpha = this.flowerAreaHovered ? 1 : 0
  }

  private cancelFlowerFade(): void {
    if (this.flowerFadeFrame !== null) {
      cancelAnimationFrame(this.flowerFadeFrame)
      this.flowerFadeFrame = null
    }
  }

  private animateFlowerAlpha(target: number, durationMs: number): void {
    if (this.flowerList.length === 0) return
    if (this.flowerFadeFrame !== null) cancelAnimationFrame(this.flowerFadeFrame)
    const from = this.flowerFadeFilter.alpha
    const startedAt = performance.now()
    const tick = (now: number) => {
      const progress = Math.min(1, (now - startedAt) / Math.max(1, durationMs))
      this.flowerFadeFilter.alpha = from + (target - from) * progress
      if (progress < 1) {
        this.flowerFadeFrame = requestAnimationFrame(tick)
      } else {
        this.flowerFadeFrame = null
      }
    }
    this.flowerFadeFrame = requestAnimationFrame(tick)
  }

  private revealFlowers(): void {
    this.cancelFlowerFade()
    this.flowerFadeFilter.alpha = 1
  }

  private hideFlowers(): void {
    this.cancelFlowerFade()
    this.flowerFadeFilter.alpha = 0
  }

  private beginFlowerFade(): void {
    if (this.flowerAreaHovered || this.flowerList.length === 0) return
    this.cancelFlowerFade()
    this.animateFlowerAlpha(0, FLOWER_FADE_DURATION_MS)
  }

  /** Place full-size flower tiles outside the river, with a fixed clear gap. */
  addFlower(tile: Tile, animate = false): void {
    const index = this.flowerCount
    const x = FLOWER_START_X + (index % FLOWER_COLUMNS) * FLOWER_GAP_X
    const y = FLOWER_START_Y + Math.floor(index / FLOWER_COLUMNS) * FLOWER_GAP_Y
    tile.off('pointerdown')
    tile.setHoverCallbacks(null, null)
    tile.setHoverWhileDisabled(true)
    tile.setInputEnabled(false)
    this.flowerList.push(tile)
    this.setFlowerCount(this.flowerCount + 1)
    this.cancelFlowerFade()
    this.flowerFadeFilter.alpha = 1
    if (animate && tile.parent) {
      const movement = tile.generalMove(this.flowerLayer, x, y, 0)
      tile.scale.set(1)
      const fadeAfterPlacement = () => {
        this.revealFlowers()
        this.beginFlowerFade()
      }
      movement.then(fadeAfterPlacement).catch(fadeAfterPlacement)
    } else {
      this.flowerLayer.addChild(tile)
      tile.x = x
      tile.y = y
      tile.rotation = 0
      tile.scale.set(1)
      tile.visible = true
      this.beginFlowerFade()
    }
  }

  override destroy(options?: any): void {
    this.cancelFlowerFade()
    super.destroy(options)
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
