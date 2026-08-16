import type { River } from './River'
import type { Tile } from './Tile'

/**
 * Unity Card3DHoverManager 的 2D 对应：悬停/选中手牌时，高亮四家牌河中的同类牌。
 */
export class RiverMatchHighlight {
  private rivers: readonly River[] = []
  private currentTid = 0

  bind(rivers: readonly River[]): void {
    this.currentTid = 0
    this.rivers = rivers
    for (const river of rivers) {
      river.onTileRegistered = (tile) => this.syncTile(tile)
    }
  }

  highlight(tid: number): void {
    const key = tid > 0 ? tid : 0
    if (this.currentTid === key) {
      this.apply()
      return
    }
    this.currentTid = key
    this.apply()
  }

  clear(): void {
    if (this.currentTid === 0) {
      this.apply()
      return
    }
    this.currentTid = 0
    this.apply()
  }

  syncTile(tile: Tile): void {
    if (tile.destroyed) return
    tile.setMatchHighlight(this.currentTid > 0 && tile.tid === this.currentTid)
  }

  private apply(): void {
    for (const river of this.rivers) {
      for (const tile of river.tileList) {
        this.syncTile(tile)
      }
    }
  }
}
