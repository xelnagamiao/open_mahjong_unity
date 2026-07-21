import { Container, Graphics, Sprite } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS, LINE_WIDTH,
  FRONT_COLOR, BACK_COLOR, BORDER_COLOR, ANIMATION_TIME,
} from './constants'
import { getTexture } from './textures'

export const TILE_HOVER_TINT = 0xe0e0e0
export const FROM_DRAWN_TINT = 0xcccccc

function shouldUseTimerFallback(): boolean {
  return typeof document !== 'undefined'
    && (document.visibilityState === 'hidden' || !document.hasFocus())
}

function queueAnimationTick(tick: () => void): void {
  if (shouldUseTimerFallback()) {
    window.setTimeout(tick, 16)
    return
  }
  requestAnimationFrame(() => tick())
}

function runTimedAnimation(durationMs: number, apply: (t: number) => void, finish: () => void): void {
  if (durationMs <= 0) {
    apply(1)
    finish()
    return
  }

  let elapsedMs = 0
  let lastTickMs = performance.now()

  const tick = () => {
    const nowMs = performance.now()
    elapsedMs += Math.min(Math.max(0, nowMs - lastTickMs), 34)
    lastTickMs = nowMs

    const t = Math.min(elapsedMs / durationMs, 1)
    apply(t)
    if (t < 1) {
      queueAnimationTick(tick)
      return
    }
    finish()
  }

  queueAnimationTick(tick)
}

/** A single mahjong tile — can be face-up or face-down. */
export class Tile extends Container {
  tid: number
  shown: boolean

  /** Cumulative position in the left list (for meld layout). */
  pos = 0
  /** Stack offset for added-kong tiles. */
  posy = 0

  private readonly bg: Graphics
  private readonly cover: Graphics
  private readonly sprite: Sprite | null
  private coverColor = BACK_COLOR
  private onHoverIn: (() => void) | null = null
  private onHoverOut: (() => void) | null = null
  private persistentTint = 0xffffff
  private hoverTint: number | null = null
  private hoverTintColor: number | null = TILE_HOVER_TINT
  private hoverVisualEnabled = true
  private inputEnabled = true
  private hoverWhileDisabled = false

  constructor(tid: number, shown = true) {
    super()
    this.tid = tid
    this.shown = shown && tid > 0

    // Background
    this.bg = new Graphics()
    this.bg.roundRect(-TILE_WIDTH / 2, -TILE_HEIGHT / 2, TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS)
    this.bg.fill({ color: FRONT_COLOR })
    this.bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.bg.visible = this.shown
    this.addChild(this.bg)

    // Texture
    const texture = getTexture(tid)
    if (texture) {
      this.sprite = new Sprite(texture)
      this.sprite.anchor.set(0.5)
      this.sprite.visible = this.shown
      this.addChild(this.sprite)
    } else {
      this.sprite = null
    }

    // Cover (face-down)
    this.cover = new Graphics()
    this.redrawCover()
    this.cover.visible = !this.shown
    this.addChild(this.cover)

    // Interactive (hover + click)
    this.on('pointerover', () => {
      this.hoverTint = this.hoverVisualEnabled ? this.hoverTintColor : null
      this.applyTint()
      this.onHoverIn?.()
    })
    this.on('pointerout', () => {
      this.hoverTint = null
      this.applyTint()
      this.onHoverOut?.()
    })
    this.setHoverEnabled(this.shown)
  }

  private setTint(tint: number): void {
    this.bg.tint = tint
    this.cover.tint = tint
    if (this.sprite) this.sprite.tint = tint
  }

  private redrawCover(): void {
    this.cover.clear()
    this.cover.roundRect(-TILE_WIDTH / 2, -TILE_HEIGHT / 2, TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS)
    this.cover.fill({ color: this.coverColor })
    this.cover.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
  }

  private applyTint(): void {
    this.setTint(this.hoverTint ?? this.persistentTint)
  }

  setCoverColor(color: number): void {
    this.coverColor = color
    this.redrawCover()
    this.applyTint()
  }

  setPersistentTint(tint: number | null): void {
    this.persistentTint = tint ?? 0xffffff
    this.applyTint()
  }

  setHoverTintColor(tint: number | null): void {
    this.hoverTintColor = tint
    if (tint == null) {
      this.hoverTint = null
      this.applyTint()
    }
  }

  setHoverVisualEnabled(enabled: boolean): void {
    this.hoverVisualEnabled = enabled
    if (!enabled) {
      this.hoverTint = null
      this.applyTint()
    }
  }

  setInputEnabled(enabled: boolean): void {
    this.inputEnabled = enabled
    this.setHoverEnabled(this.shown)
  }

  setHoverWhileDisabled(enabled: boolean): void {
    this.hoverWhileDisabled = enabled
    this.setHoverEnabled(this.shown)
  }

  setHoverEnabled(enabled: boolean): void {
    const hoverActive = enabled && (this.inputEnabled || this.hoverWhileDisabled)
    this.eventMode = hoverActive ? 'static' : 'passive'
    this.cursor = enabled && this.inputEnabled ? 'pointer' : 'default'
    if (!hoverActive) {
      this.hoverTint = null
      this.applyTint()
    }
  }

  setHoverCallbacks(onHoverIn: (() => void) | null, onHoverOut: (() => void) | null): void {
    this.onHoverIn = onHoverIn
    this.onHoverOut = onHoverOut
  }

  // ── Visibility ────────────────────────────────────────────────────

  hide(): void {
    if (this.tid === 0) return
    this.shown = false
    if (this.sprite) this.sprite.visible = false
    this.bg.visible = false
    this.cover.visible = true
    this.setHoverEnabled(false)
  }

  show(): void {
    if (this.tid === 0) return
    this.shown = true
    if (this.sprite) this.sprite.visible = true
    this.bg.visible = true
    this.cover.visible = false
    this.setHoverEnabled(true)
  }

  // ── Texture update ────────────────────────────────────────────────

  updateTid(newTid: number): void {
    if (newTid === 0) { this.hide(); this.tid = 0; return }
    this.tid = newTid
    const texture = getTexture(newTid)
    if (texture && this.sprite) {
      this.sprite.texture = texture
      this.sprite.visible = this.shown
    }
    this.bg.visible = this.shown
    this.cover.visible = !this.shown
    this.setHoverEnabled(this.shown)
  }

  // ── Animation ─────────────────────────────────────────────────────

  /** Animate to (x, y, rotation) over `time` ms. Returns a promise. */
  moveTo(x: number, y: number, rotation: number, time = ANIMATION_TIME): Promise<void> {
    return new Promise((resolve) => {
      const sx = this.x, sy = this.y, sr = this.rotation
      const dx = x - sx, dy = y - sy, dr = rotation - sr
      if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5 && Math.abs(dr) < 0.01) {
        this.x = x; this.y = y; this.rotation = rotation
        resolve()
        return
      }
      this.setHoverEnabled(false)
      runTimedAnimation(time, (t) => {
        this.x = sx + dx * t
        this.y = sy + dy * t
        this.rotation = sr + dr * t
      }, () => {
          this.x = x
          this.y = y
          this.rotation = rotation
          this.hoverTint = null
          this.applyTint()
          this.setHoverEnabled(this.shown)
          resolve()
      })
    })
  }

  /** Move across parents with coordinate conversion, then animate. */
  generalMove(
    target: Container, tx: number, ty: number, trot: number,
    time = ANIMATION_TIME, setVisible = false,
  ): Promise<void> {
    const global = this.getGlobalPosition()
    const parentRot = this.parent?.rotation ?? 0
    const globalRot = this.rotation + parentRot
    this.parent?.removeChild(this)
    target.addChild(this)
    const local = target.toLocal(global)
    this.x = local.x
    this.y = local.y
    this.rotation = globalRot - (target.rotation ?? 0)
    return this.moveTo(tx, ty, trot, time).then(() => {
      if (setVisible) this.visible = true
    })
  }

  /** Fade in alpha 0→1 over `time` ms. */
  gradualAppear(time = ANIMATION_TIME, flush = false): Promise<void> {
    return new Promise((resolve) => {
      if (this.visible && this.alpha >= 0.9 && !flush) { resolve(); return }
      this.show()
      this.alpha = this.alpha <= 0.1 || this.alpha >= 0.9 ? 0 : this.alpha
      this.visible = true
      runTimedAnimation(time, (t) => {
        this.alpha = t
      }, () => { this.alpha = 1; resolve() })
    })
  }

  // ── Static ────────────────────────────────────────────────────────

  static newInvisible(tid: number): Tile {
    const tile = new Tile(tid, true)
    tile.visible = false
    return tile
  }
}
