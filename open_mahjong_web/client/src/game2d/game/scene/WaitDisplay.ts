import { Container, Graphics } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_SEP, TILE_RADIUS, LINE_WIDTH,
  SCALE_FACTOR, SELF_HAND_SCALE, FRONT_COLOR, BORDER_COLOR, SUIT_HONOR,
} from './constants'
import { WaitEntry } from './WaitEntry'

export interface WaitDetail {
  tile: number
  base_f: number
  selfdrawn_f: number
  remaining_count: number
}

export interface WaitAddDetail {
  tile: number
  base_f: number
  selfdrawn_f: number
  remaining_count: number
}

export interface WaitsMessage {
  type: 'waits'
  details: WaitDetail[]
}

export interface WaitsAllMessage {
  type: 'waits_all'
  details: {
    discard_tile: number
    adds: WaitAddDetail[]
  }[]
}

export type WaitInfoData = WaitsMessage | WaitsAllMessage | null

/** Match the legacy wait ordering: lower tile keys render first. */
function sortByTileKeyAsc(a: { tile: number }, b: { tile: number }): number {
  const ka = a.tile + ((a.tile & 0b11100000) === SUIT_HONOR ? 1000 : 0)
  const kb = b.tile + ((b.tile & 0b11100000) === SUIT_HONOR ? 1000 : 0)
  return ka - kb
}

/**
 * A horizontal bar that appears above the self hand, showing
 * wait information (tile, base fan, self-drawn fan, remaining count).
 */
export class WaitDisplay extends Container {
  private entryList: WaitEntry[] = []
  private backgroundGraphic: Graphics | null = null
  private currentData: WaitInfoData = null

  constructor(parent: Container) {
    super()
    this.x = 0
    this.y = SCALE_FACTOR / 2 - TILE_HEIGHT * 3.2
    this.scale.set(SELF_HAND_SCALE)
    this.zIndex = 100000
    this.visible = false
    parent.addChild(this)
  }

  setData(data: WaitInfoData, activeTid: number = -1): void {
    const previousType = this.currentData?.type ?? null
    this.currentData = data
    if (!data) {
      this.reset()
      return
    }

    if (previousType !== null && previousType !== data.type) {
      this.reset()
    }

    if (data.type === 'waits' && activeTid === 0) {
      this.loadData(0, data)
      return
    }
    if (data.type === 'waits_all' && activeTid > 0) {
      this.loadData(activeTid, data)
    }
  }

  /**
   * Load wait data. If `data` is null, uses the latest viewer wait payload.
   * @param tid - if >0, only `waits_all` is processed; if 0, only `waits`.
   */
  loadData(tid: number, data: WaitInfoData = this.currentData): void {
    this.reset()
    if (!data) return

    if (data.type === 'waits') {
      if (tid > 0) return
      const sorted = [...data.details].sort(sortByTileKeyAsc)
      for (const entry of sorted) {
        this.addEntry(new WaitEntry(null, 0, 0, entry.tile, entry.base_f, entry.selfdrawn_f, entry.remaining_count))
      }
    } else if (data.type === 'waits_all') {
      if (tid === 0) return
      for (const entry of data.details) {
        if (entry.discard_tile !== tid) continue
        const sorted = [...entry.adds].sort(sortByTileKeyAsc)
        for (const add of sorted) {
          this.addEntry(new WaitEntry(null, 0, 0, add.tile, add.base_f, add.selfdrawn_f, add.remaining_count))
        }
      }
    }

    this.layout()
  }

  private addEntry(entry: WaitEntry): void {
    this.entryList.push(entry)
  }

  private layout(): void {
    if (this.entryList.length === 0) return

    const size = this.entryList.length
    const itemSep = TILE_SEP * 2.9
    const totalWidth = size * TILE_WIDTH * 0.7 + size * itemSep

    // Background
    this.backgroundGraphic = new Graphics()
    this.backgroundGraphic.roundRect(
      -totalWidth / 2 - itemSep / 2, -TILE_HEIGHT * 0.52,
      totalWidth + itemSep, TILE_HEIGHT * 1.3,
      TILE_RADIUS,
    )
    this.backgroundGraphic.fill({ color: FRONT_COLOR, alpha: 0.8 })
    this.backgroundGraphic.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(this.backgroundGraphic)

    // Position entries
    this.entryList.forEach((entry, i) => {
      entry.x = -totalWidth / 2 + (i + 0.5) * (TILE_WIDTH * 0.7 + itemSep)
      entry.y = 0
      this.addChild(entry)
    })

    this.visible = true
  }

  reset(): void {
    this.visible = false
    for (const entry of this.entryList) {
      this.removeChild(entry)
      entry.destroy({ children: true })
    }
    this.entryList.length = 0
    if (this.backgroundGraphic) {
      this.removeChild(this.backgroundGraphic)
      this.backgroundGraphic.destroy()
      this.backgroundGraphic = null
    }
  }
}
