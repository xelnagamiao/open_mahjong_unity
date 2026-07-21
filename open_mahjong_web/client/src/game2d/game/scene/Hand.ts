import { Container, FederatedPointerEvent } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_SEP, SCALE_FACTOR,
  ANIMATION_TIME, SELF_HAND_SCALE, IS_MOBILE_PHONE,
} from './constants'
import { Tile, TILE_HOVER_TINT } from './Tile'
import { River } from './River'
import { DiscardHelper } from './DiscardHelper'
import { WaitDisplay } from './WaitDisplay'

export type MeldType = 'chow' | 'pung' | 'kong'

type SnapshotMeldSpec = {
  chowMode?: number
  meldFromRel?: number
  concealed?: boolean
  claimedFromDrawnDiscard?: boolean
  addedFromDrawnTile?: boolean
  concealedFromDrawnTile?: boolean
}

export class Hand extends Container {
  readonly direction: number
  readonly leftList: Tile[] = []
  rightList: Tile[] = []
  drawnTile: Tile | null = null
  leftListLength = 0
  discardIndex = -1
  private readonly river: River | null
  readonly waitDisplay: WaitDisplay | null
  private readonly discardHelperList: DiscardHelper[] = []
  private readonly handScale: number
  private readonly replayStyle: boolean

  constructor(
    direction: number,
    parent: Container,
    river: River | null = null,
    assocWaitDisplay: WaitDisplay | null = null,
    replayStyle = false,
  ) {
    super()
    this.direction = direction
    this.river = river
    this.waitDisplay = assocWaitDisplay
    this.handScale = direction === 0 ? SELF_HAND_SCALE : 1.0
    this.replayStyle = replayStyle

    const offsets: readonly [number, number][] = [
      [0, SCALE_FACTOR / 2 - TILE_HEIGHT / 2],
      [SCALE_FACTOR / 2 - TILE_HEIGHT / 2, 0],
      [0, -SCALE_FACTOR / 2 + TILE_HEIGHT / 2],
      [-SCALE_FACTOR / 2 + TILE_HEIGHT / 2, 0],
    ]
    this.x = offsets[direction]?.[0] ?? 0
    this.y = offsets[direction]?.[1] ?? 0
    this.rotation = (-Math.PI / 2) * direction
    if (direction !== 0) this.zIndex = -1

    this.eventMode = 'passive'
    this.on('pointerout', () => {
      this.waitDisplay?.reset()
    })
    parent.addChild(this)
  }

  // ── Tile lookup ──────────────────────────────────────────────────

  private popFromHand(tid: number, checkDrawn: boolean = false): Tile | null {
    if (checkDrawn && this.drawnTile && this.drawnTile.tid === tid) {
      const tile = this.drawnTile
      this.drawnTile = null
      this.discardIndex = -1
      return tile
    }
    let idx = -1
    if (this.discardIndex >= 0) {
      if (this.rightList[this.discardIndex]?.tid === tid) {
        idx = this.discardIndex
      }
      this.discardIndex = -1
    }
    if (idx < 0) {
      idx = this.rightList.findIndex((t) => t.tid === tid)
    }

    if (idx < 0 && this.direction !== 0) {
      // randomly pick one from all not shown tiles in hand
      const candidates = this.rightList.filter((t) => !t.shown)
      if (candidates.length > 0) {
        idx = this.rightList.indexOf(candidates[Math.floor(Math.random() * candidates.length)])
      }
    }

    if (idx < 0) {
      return null
    }
    return this.rightList.splice(idx, 1)[0] ?? null
  }

  private popMultiple(tid: number, count: number): Tile[] {
    const result: Tile[] = []
    for (let i = 0; i < count; i += 1) {
      const tile = this.popFromHand(tid)
      if (!tile) break
      result.push(tile)
    }
    return result
  }

  private restoreHand(tiles: Tile[]): void {
    for (const t of tiles) this.rightList.push(t)
  }

  private static getChowTilesFromCentral(centralTid: number, chowMode: number): [number, number, number] | null {
    switch (chowMode) {
      case 1:
        return [centralTid - 1, centralTid, centralTid + 1]
      case 2:
        return [centralTid, centralTid - 1, centralTid + 1]
      case 3:
        return [centralTid + 1, centralTid - 1, centralTid]
      default:
        return null
    }
  }

  private static normalizeOpenMeldFromRel(meldFromRel: number): 1 | 2 | 3 | null {
    if (meldFromRel >= 5 && meldFromRel <= 7) {
      return (meldFromRel - 4) as 1 | 2 | 3
    }
    if (meldFromRel >= 1 && meldFromRel <= 3) {
      return meldFromRel as 1 | 2 | 3
    }
    return null
  }

  private applyTileStyle(tile: Tile): void {
    tile.setHoverVisualEnabled(!this.replayStyle)
  }

  private appendOpenTriplet(claimed: Tile, handA: Tile, handB: Tile, meldFromRel: number): void {
    const normalized = Hand.normalizeOpenMeldFromRel(meldFromRel)
    switch (normalized) {
      case 1:
        this.appendLeftList(claimed, true)
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        return
      case 2:
        this.appendLeftList(handA, false)
        this.appendLeftList(claimed, true)
        this.appendLeftList(handB, false)
        return
      case 3:
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        this.appendLeftList(claimed, true)
        return
      default:
        return
    }
  }

  private appendAddedKong(
    tid: number,
    meldFromRel: number,
    claimedFromDrawnDiscard = false,
    addedFromDrawnTile = false,
  ): void {
    const normalized = Hand.normalizeOpenMeldFromRel(meldFromRel)
    if (!normalized) return

    const claimed = Tile.newInvisible(tid)
    const handA = Tile.newInvisible(tid)
    const handB = Tile.newInvisible(tid)
    const stacked = Tile.newInvisible(tid)
    if (claimedFromDrawnDiscard) {
      claimed.setPersistentTint(TILE_HOVER_TINT)
    }
    if (addedFromDrawnTile) {
      stacked.setPersistentTint(TILE_HOVER_TINT)
    }
    const baseIndex = this.leftList.length

    switch (normalized) {
      case 1:
        this.appendLeftList(claimed, true)
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        this.addKongOn(stacked, baseIndex)
        return
      case 2:
        this.appendLeftList(handA, false)
        this.appendLeftList(claimed, true)
        this.appendLeftList(handB, false)
        this.addKongOn(stacked, baseIndex + 1)
        return
      case 3:
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        this.appendLeftList(claimed, true)
        this.addKongOn(stacked, baseIndex + 2)
        return
      default:
        return
    }
  }

  private appendMeldedKong(
    tid: number,
    meldFromRel: number,
    claimedFromDrawnDiscard = false,
  ): void {
    const normalized = Hand.normalizeOpenMeldFromRel(meldFromRel)
    if (!normalized) return

    const claimed = Tile.newInvisible(tid)
    const handA = Tile.newInvisible(tid)
    const handB = Tile.newInvisible(tid)
    const handC = Tile.newInvisible(tid)
    if (claimedFromDrawnDiscard) {
      claimed.setPersistentTint(TILE_HOVER_TINT)
    }

    switch (normalized) {
      case 1:
        this.appendLeftList(claimed, true)
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        this.appendLeftList(handC, false)
        return
      case 2:
        this.appendLeftList(handA, false)
        this.appendLeftList(claimed, true)
        this.appendLeftList(handB, false)
        this.appendLeftList(handC, false)
        return
      case 3:
        this.appendLeftList(handA, false)
        this.appendLeftList(handB, false)
        this.appendLeftList(handC, false)
        this.appendLeftList(claimed, true)
        return
      default:
        return
    }
  }

  // ── Melds (left list) building ───────────────────────────────────

  appendLeftList(tile: Tile, rotated: boolean): void {
    this.applyTileStyle(tile)
    tile.pos = this.leftListLength + 0.5 * (rotated ? TILE_HEIGHT : TILE_WIDTH)
    tile.rotation = rotated ? -Math.PI / 2 : 0
    this.leftListLength += rotated ? TILE_HEIGHT : TILE_WIDTH
    tile.posy = rotated ? TILE_HEIGHT / 2 - TILE_WIDTH / 2 : 0
    this.leftList.push(tile)
  }

  addKongOn(tile: Tile, idx: number): void {
    const base = this.leftList[idx]
    if (!base) return
    this.applyTileStyle(tile)
    tile.pos = base.pos
    tile.posy = base.posy - TILE_WIDTH
    tile.rotation = base.rotation
    this.leftList.push(tile)
  }

  private bindWaitHover(tile: Tile): void {
    if (this.direction !== 0 || !this.waitDisplay) return
    tile.setHoverCallbacks(
      () => this.waitDisplay?.loadData(tile.tid),
      () => {
        this.waitDisplay?.reset()
      },
    )
  }

  addRightList(tile: Tile): void {
    this.applyTileStyle(tile)
    this.bindWaitHover(tile)
    this.rightList.push(tile)
  }

  // ── Display geometry ─────────────────────────────────────────────

  getDisplayLength(): number {
    const leftPart = this.leftListLength === 0
      ? 0 : this.leftListLength + TILE_SEP * (this.handScale * 5 - 4)
    const rightPart = TILE_WIDTH * (this.rightList.length - 1) * this.handScale
    return leftPart + rightPart
  }

  private get bias(): number {
    return this.direction === 0 && this.rightList.length % 3 === 1
      ? (TILE_WIDTH + TILE_SEP) * this.handScale * 0.5 : 0
  }

  getXLeft(pos: number): number {
    return pos - this.getDisplayLength() / 2 - TILE_WIDTH / 2 - this.bias
  }

  getXRight(idx: number): number {
    return this.getDisplayLength() / 2 - TILE_WIDTH * idx * this.handScale - this.bias
  }

  sortRightList(): void {
    this.rightList.sort((a, b) => {
      const oa = (a.tid & 0b11100000) === 0b10100000 ? 1000 : 0
      const ob = (b.tid & 0b11100000) === 0b10100000 ? 1000 : 0
      return b.tid + ob - (a.tid + oa)
    })
  }

  revealHand(tiles: number[], drawnTile: number | null = null): void {
    this.discardIndex = -1
    const sorted = [...tiles].sort((left, right) => {
      const leftOffset = (left & 0b11100000) === 0b10100000 ? 1000 : 0
      const rightOffset = (right & 0b11100000) === 0b10100000 ? 1000 : 0
      return right + rightOffset - (left + leftOffset)
    })

    while (this.rightList.length > sorted.length) {
      const extra = this.rightList.pop()
      if (!extra) break
      extra.removeFromParent()
      extra.destroy({ children: true })
    }
    while (this.rightList.length < sorted.length) {
      this.addRightList(Tile.newInvisible(0))
    }

    for (let i = 0; i < sorted.length; i += 1) {
      const tile = this.rightList[i]
      tile.updateTid(sorted[i])
      tile.show()
    }

    if (drawnTile != null) {
      if (!this.drawnTile) {
        const tile = Tile.newInvisible(drawnTile)
        tile.updateTid(drawnTile)
        tile.show()
        this.applyTileStyle(tile)
        this.bindWaitHover(tile)
        this.drawnTile = tile
      } else {
        this.drawnTile.updateTid(drawnTile)
        this.drawnTile.show()
      }
    } else if (this.drawnTile) {
      this.drawnTile.removeFromParent()
      this.drawnTile.destroy({ children: true })
      this.drawnTile = null
    }

    this.updateDisplay(false, false, false, false, false)
  }

  // ── Display update ───────────────────────────────────────────────

  updateDisplay(
    mergeDraw = false, setVisible = false,
    gradualAppear = false, flush = false, sort = true, delayRight = false, delayLeft = false
  ): void {
    const yRight = -TILE_HEIGHT * 0.5 * (this.handScale - 1.0)
    const movementTime = gradualAppear || flush ? 0 : ANIMATION_TIME
    const rightListDelay = delayRight ? movementTime * 4 : 0
    const drawnTileDelay = delayRight ? movementTime * 4 : 0
    const leftListDelay = delayLeft ? movementTime * 4 : 0

    if (mergeDraw && this.drawnTile) {
      // push drawnTile to front of the right list (container operation only)
      this.rightList.unshift(this.drawnTile)
      this.drawnTile = null
    }
    if (sort) {
      this.sortRightList()
    }

    if (leftListDelay === 0) {
      for (const t of this.leftList) {
        t.scale.set(1.0)
        t.generalMove(this, this.getXLeft(t.pos), t.posy,
          t.posy === 0 ? 0 : -Math.PI / 2,
          movementTime, setVisible)
          .then(() => { if (gradualAppear) t.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
      }
    } else setTimeout(() => {
      for (const t of this.leftList) {
        t.scale.set(1.0)
        t.generalMove(this, this.getXLeft(t.pos), t.posy,
          t.posy === 0 ? 0 : -Math.PI / 2,
          movementTime, setVisible)
          .then(() => { if (gradualAppear) t.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
      }
    }, leftListDelay)

    if (rightListDelay === 0) {
      for (let i = 0; i < this.rightList.length; i += 1) {
        const t = this.rightList[i]; t.scale.set(this.handScale)
        t.generalMove(this, this.getXRight(i), yRight, 0,
          movementTime, setVisible)
          .then(() => { if (gradualAppear) t.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
        t.pos = -i
      }
    } else setTimeout(() => {
      for (let i = 0; i < this.rightList.length; i += 1) {
        const t = this.rightList[i]; t.scale.set(this.handScale)
        t.generalMove(this, this.getXRight(i), yRight, 0,
          movementTime, setVisible)
          .then(() => { if (gradualAppear) t.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
        t.pos = -i
      }
    }, rightListDelay)

    if (drawnTileDelay === 0) {
      if (this.drawnTile) {
        this.drawnTile.scale.set(this.handScale)
        this.drawnTile.generalMove(this,
          this.getXRight(0) + (TILE_WIDTH + TILE_SEP) * this.handScale,
          yRight, 0, movementTime, setVisible)
          .then(() => { if (gradualAppear) this.drawnTile!.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
        this.drawnTile.pos = -1000
      }
    } else setTimeout(() => {
      if (this.drawnTile) {
        this.drawnTile.scale.set(this.handScale)
        this.drawnTile.generalMove(this,
          this.getXRight(0) + (TILE_WIDTH + TILE_SEP) * this.handScale,
          yRight, 0, movementTime, setVisible)
          .then(() => { if (gradualAppear) this.drawnTile!.gradualAppear(ANIMATION_TIME, flush) })
          .catch(() => {})
        this.drawnTile.pos = -1000
      }
    }, drawnTileDelay)
  }

  // ── Discard interaction ──────────────────────────────────────────

  waitDiscard(onDiscard: (tid: number, useDrawnTile: boolean) => void): void {
    this.unwaitDiscard()

    if (this.drawnTile) {
      const dt = this.drawnTile
      this.bindClick(dt, () => {
        this.discardIndex = -1
        onDiscard(dt.tid, true)
      },
        this.getXRight(0) + TILE_WIDTH + TILE_SEP, TILE_HEIGHT / 2 + TILE_WIDTH * 4)
    }

    for (let i = 0; i < this.rightList.length; i += 1) {
      const t = this.rightList[i]
      let hy = TILE_HEIGHT / 2 + TILE_WIDTH * 1.5
      if (i % 2 === 1) hy += 2.5 * TILE_WIDTH
      this.bindClick(t, () => {
        this.discardIndex = i
        onDiscard(t.tid, false)
      }, this.getXRight(i), hy)
    }
  }

  private bindClick(tile: Tile, fire: () => void, hx: number, hy: number): void {
    if (IS_MOBILE_PHONE) {
      const h = new DiscardHelper(this, hx, hy)
      this.discardHelperList.push(h)
      h.eventMode = 'static'; h.cursor = 'pointer'
      h.on('pointerdown', (e: FederatedPointerEvent) => {
        if (e.button !== 0) return
        this.unwaitDiscard(); fire()
      })
    } else {
      tile.eventMode = 'static'; tile.cursor = 'pointer'
      tile.on('pointerdown', (e: FederatedPointerEvent) => {
        if (e.button !== 0) return
        this.unwaitDiscard(); fire()
      })
    }
  }

  unwaitDiscard(): void {
    for (const t of this.rightList) { t.off('pointerdown'); t.setHoverEnabled(t.shown) }
    if (this.drawnTile) { this.drawnTile.off('pointerdown'); this.drawnTile.setHoverEnabled(this.drawnTile.shown) }
    for (const h of this.discardHelperList) { h.visible = false; this.removeChild(h); h.destroy() }
    this.discardHelperList.length = 0
  }

  // ── Meld from snapshot ───────────────────────────────────────────

  addMeld(type: MeldType, middleTid: number, spec: SnapshotMeldSpec = {}): void {
    const chowMode = spec.chowMode ?? 0
    const meldFromRel = spec.meldFromRel ?? 0
    const concealed = spec.concealed ?? false
    const claimedFromDrawnDiscard = spec.claimedFromDrawnDiscard ?? false
    const addedFromDrawnTile = spec.addedFromDrawnTile ?? false
    const concealedFromDrawnTile = spec.concealedFromDrawnTile ?? false

    // added_kong: backend sets meldFromRel >= 4 to indicate this was a pung
    // that was later upgraded to a kong. Render as pung + stacked tile.
    const isAddedKong = type === 'kong' && meldFromRel >= 4 && meldFromRel <= 7

    if (type === 'chow') {
      const chowTiles = Hand.getChowTilesFromCentral(middleTid, chowMode)
      if (!chowTiles) return
      const [claimedTid, handTidA, handTidB] = chowTiles
      const claimed = Tile.newInvisible(claimedTid)
      if (claimedFromDrawnDiscard) {
        claimed.setPersistentTint(TILE_HOVER_TINT)
      }
      this.appendLeftList(claimed, true)
      this.appendLeftList(Tile.newInvisible(handTidA), false)
      this.appendLeftList(Tile.newInvisible(handTidB), false)
      return
    }
    if (type === 'pung') {
      if (!Hand.normalizeOpenMeldFromRel(meldFromRel)) return
      const claimed = Tile.newInvisible(middleTid)
      if (claimedFromDrawnDiscard) {
        claimed.setPersistentTint(TILE_HOVER_TINT)
      }
      this.appendOpenTriplet(
        claimed,
        Tile.newInvisible(middleTid),
        Tile.newInvisible(middleTid),
        meldFromRel,
      )
      return
    }
    if (isAddedKong) {
      this.appendAddedKong(middleTid, meldFromRel, claimedFromDrawnDiscard, addedFromDrawnTile)
      return
    }
    if (concealed) {
      const firstShown = Tile.newInvisible(middleTid)
      const lastShown = Tile.newInvisible(middleTid)
      if (concealedFromDrawnTile) {
        lastShown.setPersistentTint(TILE_HOVER_TINT)
      }
      this.appendLeftList(firstShown, false)
      this.appendLeftList(Tile.newInvisible(0), false)
      this.appendLeftList(Tile.newInvisible(0), false)
      this.appendLeftList(lastShown, false)
      return
    }
    this.appendMeldedKong(middleTid, meldFromRel, claimedFromDrawnDiscard)
  }

  // ── Runtime melds (tid-based) ────────────────────────────────────

  chowFromRiver(river: River, centralTid: number, chowMode: number): void {
    const chowTiles = Hand.getChowTilesFromCentral(centralTid, chowMode)
    if (!chowTiles) return

    const [claimedTid, handTidA, handTidB] = chowTiles
    const claimed = river.tileList[river.num - 1] ?? null
    const handA = this.popFromHand(handTidA)
    const handB = this.popFromHand(handTidB)

    if (!claimed || !handA || !handB) {
      if (handA) this.restoreHand([handA])
      if (handB) this.restoreHand([handB])
      return
    }

    claimed.updateTid(claimedTid)
    claimed.show()
    handA.updateTid(handTidA)
    handA.show()
    handB.updateTid(handTidB)
    handB.show()

    this.appendLeftList(claimed, true)
    this.appendLeftList(handA, false)
    this.appendLeftList(handB, false)
    this.updateDisplay(true)
  }

  pungFromRiver(river: River, meldFromRel: number, tid: number): void {
    if (!Hand.normalizeOpenMeldFromRel(meldFromRel)) return

    const claimed = river.tileList[river.num - 1] ?? null
    const tiles = this.popMultiple(tid, 2)
    if (!claimed || tiles.length < 2) {
      this.restoreHand(tiles)
      return
    }
    const [handA, handB] = tiles
    for (const current of [claimed, handA, handB]) {
      current.updateTid(tid)
      current.show()
    }

    this.appendOpenTriplet(claimed, handA, handB, meldFromRel)
    this.updateDisplay(true)
  }

  meldedKongFromRiver(river: River, meldFromRel: number, tid: number): void {
    const normalized = Hand.normalizeOpenMeldFromRel(meldFromRel)
    if (!normalized) return

    const hts = this.popMultiple(tid, 3)
    if (hts.length < 3) { this.restoreHand(hts); return }
    const [handA, handB, handC] = hts
    const claimed = river.tileList[river.num - 1] ?? null
    if (!claimed) { this.restoreHand(hts); return }
    for (const current of [handA, handB, handC, claimed]) {
      current.updateTid(tid)
      current.show()
    }

    if (normalized === 1) {
      this.appendLeftList(claimed, true)
      this.appendLeftList(handA, false)
      this.appendLeftList(handB, false)
      this.appendLeftList(handC, false)
    } else if (normalized === 2) {
      this.appendLeftList(handA, false)
      this.appendLeftList(claimed, true)
      this.appendLeftList(handB, false)
      this.appendLeftList(handC, false)
    } else if (normalized === 3) {
      this.appendLeftList(handA, false)
      this.appendLeftList(handB, false)
      this.appendLeftList(handC, false)
      this.appendLeftList(claimed, true)
    } else {
      this.restoreHand(hts)
      return
    }

    this.updateDisplay(true)
  }

  cKongFromHand(tid: number, useDrawnTile: boolean): void {
    const need = useDrawnTile ? 3 : 4
    let fd: Tile | null = null
    if (useDrawnTile && this.drawnTile) {
      fd = this.drawnTile
      this.drawnTile = null
    }
    const fh = this.popMultiple(tid, need)
    if (fh.length < need || (useDrawnTile && !fd)) { this.restoreHand(fh); if (fd) this.drawnTile = fd; return }
    this.unwaitDiscard()
    const all = fd ? [fd, ...fh] : [...fh]
    for (const t of all) t.updateTid(tid)
    if (this.replayStyle && useDrawnTile && fd) {
      fd.setPersistentTint(TILE_HOVER_TINT)
    }
    all[0].show(); all[1]?.hide(); all[2]?.hide(); all[3]?.show()
    this.appendLeftList(all[3], false); this.appendLeftList(all[2], false)
    this.appendLeftList(all[1], false); this.appendLeftList(all[0], false)
    this.updateDisplay(true)
  }

  mKongFromHand(tid: number, useDrawnTile: boolean): void {
    let tile: Tile | null = null
    if (useDrawnTile && this.drawnTile) { tile = this.drawnTile; this.drawnTile = null }
    else tile = this.popFromHand(tid)
    if (!tile) return
    this.unwaitDiscard(); tile.updateTid(tid); tile.show()
    if (this.replayStyle && useDrawnTile) {
      tile.setPersistentTint(TILE_HOVER_TINT)
    }
    let ti = -1
    for (let i = 0; i < this.leftList.length; i += 1) {
      if (this.leftList[i].tid === tid && this.leftList[i].posy !== 0) { ti = i; break }
    }
    if (ti >= 0) this.addKongOn(tile, ti)
    this.updateDisplay(true)
  }

  // ── Draw / Discard ───────────────────────────────────────────────

  drawTile(tile: Tile): void {
    this.discardIndex = -1
    this.applyTileStyle(tile)
    this.bindWaitHover(tile)
    this.drawnTile = tile
    this.updateDisplay(false, false, true)
  }

  discardTile(tid: number, useDrawnTile: boolean): void {
    const river = this.river; if (!river) return
    if (useDrawnTile && this.drawnTile) {
      this.discardIndex = -1
      const dt = this.drawnTile  // capture before nulling
      dt.updateTid(tid); dt.show(); dt.scale.set(1.0)
      river.waiting = true
      const [rx, ry] = [river.getX(river.num), river.getY(river.num)]
      this.drawnTile = null
      dt.generalMove(river as Container, rx, ry, 0).catch(() => {})
    } else {
      const tile = this.popFromHand(tid); if (!tile) return
      tile.updateTid(tid); tile.show(); tile.scale.set(1.0)
      river.waiting = true
      const [rx, ry] = [river.getX(river.num), river.getY(river.num)]
      tile.generalMove(river as Container, rx, ry, 0).catch(() => {})
    }
    this.updateDisplay(true, false, false, false, true, true, true)
  }
}
