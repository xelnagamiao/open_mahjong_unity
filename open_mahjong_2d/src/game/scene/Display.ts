import { Container, Graphics, Text, TextStyle, FederatedPointerEvent } from 'pixi.js'
import {
  TILE_WIDTH, TILE_HEIGHT, TILE_RADIUS, LINE_WIDTH,
  SCALE_FACTOR, FRONT_COLOR, BORDER_COLOR,
  IS_MOBILE_ANY,
} from './constants'
import type { WaitDisplay } from './WaitDisplay'

// ── MyText ────────────────────────────────────────────────────────────

/** Thin wrapper around `Text` with anchor at center. */
export class MyText extends Container {
  readonly textObj: Text

  constructor(text: string, style: Partial<TextStyle>) {
    super()
    this.textObj = new Text({ text, style })
    this.textObj.anchor.set(0.5)
    this.addChild(this.textObj)
  }
}

// ── Display ───────────────────────────────────────────────────────────

const SCORE_POSITIONS: [number, number, number][] = [
  [0, 0.8, 0],    // dir 0 – bottom center
  [0.8, 0, 1],    // dir 1 – right
  [0, -0.8, 2],   // dir 2 – top
  [-0.8, 0, 3],   // dir 3 – left
]

/**
 * A central information panel. Shows round, scores, remaining tiles,
 * current-turn indicator, queue, and win results.
 */
export class Display extends Container {
  private readonly textEntries = new Map<string, MyText | Graphics>()
  private indicatorList: Graphics[] = []

  constructor(parent: Container) {
    super()

    const bg = new Graphics()
    bg.roundRect(-TILE_WIDTH * 3, -TILE_WIDTH * 3, TILE_WIDTH * 6, TILE_WIDTH * 6, TILE_RADIUS)
    bg.fill({ color: FRONT_COLOR })
    bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(bg)

    parent.addChild(this)
  }

  // ── Text management ──────────────────────────────────────────────

  addText(
    key: string, text: string,
    x = 0, y = 0, rot = 0,
    fontSize = 200, math = false, color = 0x000000,
    simfang = false, maxLength = 5.1,
  ): void {
    this.removeText(key)

    const processed = math ? text.replace(/-/g, '\u2212') : text
    const defaultScale = IS_MOBILE_ANY ? 4 : 1
    const fontFamily = simfang
      ? 'CmuSerif, SimFang, sans-serif'
      : 'CmuSerif, SimKai, sans-serif'

    const label = new MyText(processed, {
      fontFamily, fontSize: fontSize / defaultScale, fill: color, align: 'center',
    })
    label.scale.set(defaultScale)
    label.rotation = -rot * Math.PI / 2
    label.x = x * TILE_WIDTH * 3
    label.y = y * TILE_WIDTH * 3

    // Auto-shrink if too wide
    if (label.width > TILE_WIDTH * maxLength) {
      label.scale.set(TILE_WIDTH * maxLength * defaultScale / label.width)
    }

    this.addChild(label)
    this.textEntries.set(key, label)
  }

  removeText(key: string): void {
    const entry = this.textEntries.get(key)
    if (!entry) return
    this.removeChild(entry)
    entry.destroy({ children: true })
    this.textEntries.delete(key)
  }

  clear(): void {
    for (const key of this.textEntries.keys()) {
      this.removeText(key)
    }
  }

  // ── Score & round ────────────────────────────────────────────────

  setScore(name: string, score: number, direction: number, offline = false, maxLength = 4.0): void {
    const scoreStr = score > 0 ? `+${score}` : `${score}`
    const [x, y, rot] = SCORE_POSITIONS[direction] ?? [0, 0.8, 0]
    this.addText(
      `score${direction}`, `${name} ${scoreStr}`,
      x, y, rot, 230, true, offline ? 0x888888 : 0x000000, false, maxLength,
    )
  }

  setRound(roundCounter: number): void {
    if (roundCounter === -1) {
      this.addText('round', '结束', 0, 0, 0, 390, false, 0x000000, true)
      return
    }
    const roundIndex = Math.max(0, roundCounter - 1)
    const wind = ['東', '南', '西', '北'][Math.floor(roundIndex / 4) % 4] ?? '東'
    const num = (roundIndex % 4) + 1 + 4 * Math.floor(roundIndex / 16)
    const label = `${wind} ${num}`
    this.addText('round', label, 0, -0.12, 0, label.length > 3 ? 360 : 450)
  }

  setRemaining(rem: number): void {
    this.addText('remaining', `余 ${rem} 枚`, 0, 0.3, 0, 170)
  }

  setPresent(direction: number, present: boolean): void {
    const entry = this.textEntries.get(`score${direction}`)
    if (entry instanceof MyText) {
      entry.textObj.style.fill = present ? 0x000000 : 0x888888
    }
  }

  // ── Current-turn indicator ───────────────────────────────────────

  setCurrent(direction: number): void {
    // Fade out old indicator
    for (const indicator of this.indicatorList) {
      indicator.visible = false
      this.removeChild(indicator)
      indicator.destroy()
    }
    this.indicatorList.length = 0

    // New indicator
    const beginX = [-TILE_WIDTH * 1.6, TILE_WIDTH * 1.8, TILE_WIDTH * 1.6, -TILE_WIDTH * 1.8][direction] ?? 0
    const beginY = [TILE_WIDTH * 1.8, TILE_WIDTH * 1.6, -TILE_WIDTH * 1.8, -TILE_WIDTH * 1.6][direction] ?? 0
    const endX = [TILE_WIDTH * 1.6, TILE_WIDTH * 1.8 - LINE_WIDTH * 0.7, -TILE_WIDTH * 1.6, -TILE_WIDTH * 1.8 + LINE_WIDTH * 0.7][direction] ?? 0
    const endY = [TILE_WIDTH * 1.8 - LINE_WIDTH * 0.7, -TILE_WIDTH * 1.6, -TILE_WIDTH * 1.8 + LINE_WIDTH * 0.7, TILE_WIDTH * 1.6][direction] ?? 0

    const indicator = new Graphics()
    indicator.rect(
      Math.min(beginX, endX), Math.min(beginY, endY),
      Math.abs(endX - beginX), Math.abs(endY - beginY),
    )
    indicator.fill({ color: 0x909090 })
    this.addChild(indicator)
    this.indicatorList.push(indicator)
  }

  // ── Queue (pending phase) ────────────────────────────────────────

  displayQueue(entries: (string | { label: string; color?: number })[]): void {
    this.clear()
    if (!entries || entries.length === 0) return
    for (let i = 0; i < entries.length; i += 1) {
      const e = entries[i]
      if (e == null) continue
      const label = typeof e === 'string' ? e : e.label
      const color = typeof e === 'string' ? 0x000000 : (e.color ?? 0x000000)
      this.addText(`queue${i}`, label, 0, -0.8 + 0.28 * (i + 0.5), 0, 190, false, color)
    }
    this.addText('queueCount', `等待中:  ${entries.length}/4`, 0, 0.8, 0, 190)
  }

  // ── Win result ───────────────────────────────────────────────────

  handleWin(
    selfDrawn: boolean, winName: string, shootName: string,
    eachLoss: number, shooterLoss: number,
    fan: number, fans: string[],
  ): void {
    const sc = TILE_WIDTH * 3

    // Top divider
    const topLine = new Graphics()
    topLine.rect(-sc * 0.85, -sc * 0.65, sc * 1.7, LINE_WIDTH * 0.7)
    topLine.fill({ color: BORDER_COLOR })
    this.addChild(topLine)
    this.textEntries.set('divider_top', topLine)

    // Title
    const title = selfDrawn ? `${winName} 自摸和` : `${winName} 和,  ${shootName} 铳`
    this.addText('title', title, 0, -0.8, 0, 190, false, 0x000000, false, 5.5)

    // Fan list
    let finalY: number
    if (fans.length <= 4) {
      for (let i = 0; i < fans.length; i += 1) {
        this.addText(`fan${i}`, fans[i], 0,
          -0.58 + 0.22 * (i + 0.5 + (fans.length === 1 ? 0.25 : 0)),
          0, 190, false, 0x000000, true)
      }
      finalY = -0.54 + 0.22 * Math.max(1.55, fans.length)
    } else {
      const textSize = fans.length > 8 ? 140 : 170
      const lineSpace = fans.length > 8 ? 0.18 : 0.22
      for (let i = 0; i < fans.length; i += 1) {
        const left = i * 2 < fans.length
        this.addText(`fan${i}`, fans[i],
          left ? -0.4 : 0.4,
          -0.58 + lineSpace * (left ? i + 0.5 : i - Math.ceil(fans.length / 2) + 0.5),
          0, textSize, false, 0x000000, true)
      }
      finalY = -0.54 + lineSpace * Math.ceil(fans.length / 2)
    }

    // Bottom divider
    const botLine = new Graphics()
    botLine.rect(-sc * 0.85, finalY * sc, sc * 1.7, LINE_WIDTH * 0.7)
    botLine.fill({ color: BORDER_COLOR })
    this.addChild(botLine)
    this.textEntries.set('divider_bot', botLine)

    // Summary
    const fanStr = fan.toFixed(2)
    const endText = selfDrawn
      ? `共 ${fanStr} 番 (各 ${eachLoss}')`
      : `共 ${fanStr} 番 (${shooterLoss}')`
    this.addText('summary', endText, 0, finalY + 0.15, 0, 170)
  }

  // ── Volume ───────────────────────────────────────────────────────

  displayVolume(volume: number): void {
    this.addText('volume', `音量：${Math.round(volume * 100)}%`, 0, 0, 0, 230, false, 0x000000, true)
  }
}

// ── Countdown ─────────────────────────────────────────────────────────

/**
 * A countdown timer displayed in the bottom-right corner.
 * Click to draw when the player is the next to draw.
 */
export class Countdown extends Container {
  private expireTime = 0
  private timerId: ReturnType<typeof setTimeout> | null = null
  private readonly bg: Graphics
  private readonly timeText: Text
  /** Optional callback when the player clicks to draw. */
  onDrawClick: (() => void) | null = null
  onExpire: (() => void) | null = null
  onLatencyClick: (() => void) | null = null

  constructor(parent: Container) {
    super()

    this.bg = new Graphics()
    this.bg.roundRect(-TILE_HEIGHT * 0.5, -TILE_HEIGHT * 0.5, TILE_HEIGHT, TILE_HEIGHT, TILE_RADIUS)
    this.bg.fill({ color: FRONT_COLOR })
    this.bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(this.bg)

    this.timeText = new Text({
      text: '\u2212',
      style: { fontFamily: 'CmuSerif, SimFang, sans-serif', fontSize: 270, fill: 0x000000, align: 'center' },
    })
    this.timeText.anchor.set(0.5)
    this.addChild(this.timeText)

    this.x = SCALE_FACTOR / 2 - TILE_HEIGHT / 2
    this.y = SCALE_FACTOR / 2 - TILE_HEIGHT / 2

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointerover', () => {
      this.bg.tint = 0xe0e0e0
    })
    this.on('pointerout', () => {
      this.bg.tint = 0xffffff
    })
    this.on('pointerdown', (e: FederatedPointerEvent) => {
      if (e.button !== 0) return
      if (!this.onDrawClick) {
        this.onLatencyClick?.()
      }
    })

    parent.addChild(this)
  }

  setTimeMillis(ms: number): void {
    this.expireTime = Date.now() + ms
    this.visible = true
    this.tick()
  }

  stop(): void {
    if (this.timerId !== null) {
      clearTimeout(this.timerId)
      this.timerId = null
    }
    this.expireTime = 0
    this.bg.tint = 0xffffff
    this.timeText.text = '\u2212'
    this.onDrawClick = null
    this.onExpire = null
  }

  /** Enable click-to-draw mode. */
  waitDraw(onDraw: () => void): void {
    this.onDrawClick = onDraw
    this.on('pointerdown', (e: FederatedPointerEvent) => {
      if (e.button !== 0) return
      this.stop()
      onDraw()
    })
  }

  private tick(): void {
    if (this.timerId !== null) clearTimeout(this.timerId)
    const remaining = this.expireTime - Date.now()
    if (remaining <= 0) {
      this.timerId = null
      this.bg.tint = 0xffffff
      this.timeText.text = '\u2212'
      this.off('pointerdown')
      this.onDrawClick = null
      this.onLatencyClick = null
      const onExpire = this.onExpire
      this.onExpire = null
      onExpire?.()
      return
    }
    this.timeText.text = `${Math.ceil(remaining / 1000)}`
    this.timerId = setTimeout(() => this.tick(), 100)
  }
}

// ── TempLabel ─────────────────────────────────────────────────────────

/**
 * A temporary label that appears when a player declares a meld,
 * e.g. "吃", "碰", "杠", "和". Auto-hides after `duration` ms.
 */
export class TempLabel extends Container {
  private expireTime: number | null
  private triggered = false

  constructor(
    parent: Container,
    dir: number,
    text: string,
    durationMs: number | null,
    onSound?: (alias: string) => void,
  ) {
    super()

    const bg = new Graphics()
    bg.roundRect(-TILE_HEIGHT * 0.5, -TILE_HEIGHT * 0.5, TILE_HEIGHT, TILE_HEIGHT, TILE_RADIUS)
    bg.fill({ color: FRONT_COLOR })
    bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(bg)

    const label = new Text({
      text,
      style: { fontFamily: 'CmuSerif, SimFang, sans-serif', fontSize: 270, fill: 0x000000, align: 'center' },
    })
    label.anchor.set(0.5)
    this.addChild(label)

    const l0 = SCALE_FACTOR / 2 - TILE_HEIGHT * 2.5
    const l1 = 6 * TILE_WIDTH
    this.x = [l1, l0, -l1, -l0][dir] ?? 0
    this.y = [l0, -l1, -l0, l1][dir] ?? 0
    this.rotation = -Math.PI * dir / 2

    this.expireTime = durationMs == null ? null : Date.now() + durationMs

    this.on('pointerover', () => { bg.tint = 0xe0e0e0 })
    this.on('pointerout', () => { bg.tint = 0xffffff })

    const soundMap: Record<string, string> = { '吃': '14-chow-m', '碰': '16-pung-m', '杠': '18-kong-m', '和': '20-win-m' }

    parent.addChild(this)

    // Trigger sound immediately, not deferred
    if (!this.triggered) {
      this.triggered = true
      onSound?.(soundMap[text] ?? '')
    }

    if (this.expireTime == null) {
      return
    }

    const tick = () => {
      const remaining = (this.expireTime ?? 0) - Date.now()
      if (remaining <= 0) {
        this.dismiss()
        return
      }
      this.alpha = 1
      setTimeout(tick, 100)
    }
    tick()
  }

  dismiss(): void {
    this.visible = false
    this.removeFromParent()
    this.destroy({ children: true })
  }
}

// ── DirLabel ──────────────────────────────────────────────────────────

export class DirLabel extends Container {
  private readonly bg: Graphics
  private readonly dirText: Text
  private readonly localDir: number
  private readonly waitDisplay: WaitDisplay | null

  constructor(parent: Container, localDir = 0, assocWaitDisplay: WaitDisplay | null = null) {
    super()

    this.localDir = localDir
    this.waitDisplay = assocWaitDisplay

    this.bg = new Graphics()
    this.bg.roundRect(-TILE_HEIGHT * 0.5, -TILE_HEIGHT * 0.5, TILE_HEIGHT, TILE_HEIGHT, TILE_RADIUS)
    this.bg.fill({ color: FRONT_COLOR })
    this.bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    this.addChild(this.bg)

    this.dirText = new Text({
      text: '',
      style: { fontFamily: 'CmuSerif, SimFang, sans-serif', fontSize: 330, fill: 0x000000, align: 'center' },
    })
    this.dirText.anchor.set(0.5)
    this.dirText.rotation = -Math.PI * localDir / 2
    this.addChild(this.dirText)

    this.x = (-SCALE_FACTOR / 2 + TILE_HEIGHT / 2) * ([1, -1, -1, 1][localDir] ?? 1)
    this.y = (SCALE_FACTOR / 2 - TILE_HEIGHT / 2) * ([1, 1, -1, -1][localDir] ?? 1)

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointerover', () => {
      this.bg.tint = 0xe0e0e0
      this.waitDisplay?.loadData(0)
    })
    this.on('pointerout', () => {
      this.bg.tint = 0xffffff
      this.waitDisplay?.reset()
    })

    parent.addChild(this)
  }

  setDir(dir: number): void {
    const actualDir = (this.localDir + dir + 4) % 4
    this.dirText.text = ['東', '南', '西', '北'][actualDir] ?? ''
  }
}
