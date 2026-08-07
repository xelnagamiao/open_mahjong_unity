import { Application, Assets, Container, Graphics, Sprite, Text, TextStyle, Texture, FederatedPointerEvent } from 'pixi.js'
import { isMobile } from 'pixi.js'
import {
  SCALE_FACTOR, WINDOW_SCALE, TILE_HEIGHT, TILE_WIDTH,
  TILE_RADIUS, LINE_WIDTH, BORDER_COLOR, MELD_OPT_SCALE,
  FRONT_COLOR, IS_MOBILE_ANY,
} from './constants'
import {
  DEFAULT_SCENE_APPEARANCE,
  hexColorToNumber,
  normalizeSceneAppearanceSettings,
  pickTileCoverColor,
  type SceneAppearanceSettings,
} from '../../lib/sceneAppearance'
import { ensureTexturesLoaded, setTileThemes } from './textures'
import {
  getGameFontFamily,
  setGameFontTheme,
  setGameLatinFontTheme,
  waitForGameFonts,
} from '../fontLoader'
import { FROM_DRAWN_TINT, Tile } from './Tile'
import { River } from './River'
import { WaitDisplay, type WaitInfoData } from './WaitDisplay'
import { Hand } from './Hand'
import { Display, Countdown, DirLabel, TempLabel, TenpaiTipButton } from './Display'
import { MeldChoices, type MeldViewerSnapshot } from './MeldChoices'
import type { ActiveSessionSnapshot, SeatSnapshot } from './types'
import { hasSingleFanAtLeast } from '../../../constants/guessFanCatalog'
import {
  DEFAULT_ASSIST_SETTINGS,
  normalizeAssistSettings,
  type AssistSettings,
} from '../../lib/assistSettings'
import { tr } from '../../../i18n'

export type { WaitInfoData, AssistSettings }

// 仅当番种明细里存在单个 >=32 番的番种时播放敲锣音；总番数不参与判断。
const DUANG_CUTOFF = 32
const AUTO_WIN_DELAY_MS = 1600
const AUTO_DISCARD_DELAY_MS = 500
const PASS_DEBOUNCE_MS = 200

type ViewerSyncContext = {
  category: string
  kind: string
  actorSeat: number
  drawSource?: string
}

type ScenePresentationMode = 'game' | 'replay'

/** Direction conversion helper: absolute seat → viewer-relative. */
function transDir(absoluteDir: number, selfDir: number): number {
  return (absoluteDir + 4 - selfDir) % 4
}

function backendMeldFromRel(actorSeat: number, targetSeat: number): number {
  return (actorSeat - targetSeat + 4) % 4
}

function displayPlayerName(name: string | null | undefined): string {
  return typeof name === 'string' && name.trim().length > 0 ? name : '(COM)'
}

function readCurrentPlayer(state: Record<string, any>): number | null {
  if (typeof state.current_player === 'number' && state.current_player >= 0) return state.current_player
  if (typeof state.last_actor === 'number' && state.last_actor >= 0) return state.last_actor
  return null
}

function readWaitingDiscarderSeat(snapshot: ActiveSessionSnapshot): number | null {
  const state = snapshot.state as Record<string, any>
  const lastEventKind = typeof state.last_event_kind === 'string' ? state.last_event_kind : null
  if (lastEventKind === 'discard_tile' && typeof state.last_actor === 'number') {
    return state.last_actor
  }
  if (snapshot.result_event?.kind === 'discard_win' && typeof state.result_source_actor === 'number') {
    return state.result_source_actor
  }
  return null
}

function readResultSourceSeat(
  kind: string,
  state: Record<string, any>,
  fallbackSeat: number,
): number {
  if (kind === 'discard_win' || kind === 'rob_added_kong_win') {
    if (typeof state.result_source_actor === 'number') {
      return state.result_source_actor
    }
    return fallbackSeat
  }

  if (typeof state.last_actor === 'number') {
    return state.last_actor
  }

  return fallbackSeat
}

/**
 * The complete PixiJS mahjong scene. Owns all sub-components and
 * exposes methods the React page calls when backend messages arrive.
 */
export class MahjongScene {
  // ── Pixi ──────────────────────────────────────────────────────────
  private app: Application | null = null
  private readonly backgroundLayer = new Container()
  private readonly backgroundFill = new Graphics()
  private backgroundImageSprite: Sprite | null = null
  private readonly center = new Container()

  // ── Sub-components ────────────────────────────────────────────────
  private waitDisplay!: WaitDisplay
  private rivers!: [River, River, River, River]
  private hands!: [Hand, Hand, Hand, Hand]
  private stateDisplay!: Display
  private tempDisplay!: Display
  private volDisplay!: Display
  private directionLabels!: [DirLabel, DirLabel, DirLabel, DirLabel]
  private tenpaiTipButton!: TenpaiTipButton
  private countdown!: Countdown
  private meldChoicesPanel: MeldChoices | null = null
  private latencyIndicator: Container | null = null
  private latencyIndicatorBg: Graphics | null = null
  private latencyIndicatorText: Text | null = null

  // ── State ─────────────────────────────────────────────────────────
  private selfDir = 0
  private names: [string, string, string, string] = ['', '', '', '']
  private ranks: [string, string, string, string] = ['', '', '', '']
  private scores: [number, number, number, number] = [0, 0, 0, 0]
  private voiceIds: [number, number, number, number] = [1, 1, 1, 1]
  private present: [boolean, boolean, boolean, boolean] = [true, true, true, true]
  private currentDir = 0
  private lastDiscarderSeat = 0
  private round = 0
  private remainingTiles = 0
  private currentStageCounter = 0
  // private lastEventUi64Value = 0
  private currentViewerActions: Array<Record<string, any>> = []
  private currentPendingStatus = 'none'
  private inputEnabled = false
  private pendingPassAckStageCounter: number | null = null
  private openingReplacementTile: Tile | null = null
  private lastPassAttemptAtMs = 0
  private roundEnded = false
  private scoreDifferenceVisible = false
  private scoreDifferenceTimeout: ReturnType<typeof setTimeout> | null = null
  waitInfoData: WaitInfoData = null

  // ── Settings ──────────────────────────────────────────────────────
  private assist: AssistSettings = normalizeAssistSettings(DEFAULT_ASSIST_SETTINGS)
  volume = 0.5

  get autoDiscard(): boolean { return this.assist.autoDiscard }
  getAssistSettings(): AssistSettings {
    return normalizeAssistSettings(this.assist)
  }
  setAssistSettings(next: Partial<AssistSettings> | AssistSettings): void {
    this.assist = normalizeAssistSettings({ ...this.assist, ...next })
  }

  // ── Interactive state ─────────────────────────────────────────────
  canDraw = false

  // ── Volume ────────────────────────────────────────────────────────
  private readonly sounds = new Map<string, HTMLAudioElement>()
  private readonly primedSounds = new WeakSet<HTMLAudioElement>()
  private readonly primingSounds = new WeakMap<HTMLAudioElement, Promise<void>>()
  private readonly activeSoundInstances = new Set<HTMLAudioElement>()

  get globalVolume(): number { return this.volume }
  set globalVolume(value: number) {
    this.volume = Math.max(0, Math.min(1, value))
    for (const audio of this.sounds.values()) {
      audio.volume = this.volume
    }
    for (const audio of this.activeSoundInstances) {
      audio.volume = this.volume
    }
  }

  // ── Network callback ──────────────────────────────────────────────
  private readonly sendToServer: (type: string, payload: Record<string, unknown>) => void

  // ── Resize ────────────────────────────────────────────────────────
  private resizeFrame: number | null = null
  /** 当前垂直滑动偏移（加到 center.y 上，正=向下）。 */
  private currentSlide = 0
  /** 每次 layout 重新计算的 center.y 基准值。 */
  private baseCenterY = 0
  private pendingChoicesTimeout: ReturnType<typeof setTimeout> | null = null
  private autoActionTimeout: ReturnType<typeof setTimeout> | null = null
  private predrawSortTimeout: ReturnType<typeof setTimeout> | null = null
  private pendingButton: Container | null = null
  private deferredSnapshot: ActiveSessionSnapshot | null = null
  private deferredPending: {
    seats: { seat_index: number; ready: boolean; username: string | null }[]
    ownReady: boolean
    onToggleReady: () => void
  } | null = null
  private lastPongMs: number | null = null
  private missedPongCount = 0
  private latencyHideTimeout: ReturnType<typeof setTimeout> | null = null
  private pingIntervalId: ReturnType<typeof setInterval> | null = null
  private readonly latencyPingTimeouts = new Map<number, ReturnType<typeof setTimeout>>()
  private readonly latencyPingSentAt = new Map<number, number>()
  private latencyPingCounter = 0
  private onConnectionLost: (() => void) | null = null
  private mountGeneration = 0
  private destroyed = false
  private hostElement: HTMLElement | null = null
  private presentationMode: ScenePresentationMode = 'game'
  private replayRecordVersion = 0
  private replayWaitTipsEnabled = false
  private replayMoqieHintEnabled = true
  private replayClaimLabel: TempLabel | null = null
  private appearance: SceneAppearanceSettings = DEFAULT_SCENE_APPEARANCE
  private backgroundImageSource: string | null = null
  private backgroundImageLoadToken = 0
  private activeCoverRound = -1
  private activeCoverColor = hexColorToNumber(DEFAULT_SCENE_APPEARANCE.tileCoverColors[0])
  private lastLeftClickAtMs = 0
  private static readonly DOUBLE_CLICK_MS = 350

  constructor(sendToServer: (type: string, payload: Record<string, unknown>) => void) {
    this.sendToServer = sendToServer
    this.backgroundLayer.addChild(this.backgroundFill)
  }

  setPresentationMode(mode: ScenePresentationMode): void {
    this.presentationMode = mode
  }

  setReplayRecordVersion(version: number): void {
    this.replayRecordVersion = version
  }

  setReplayMoqieHintEnabled(enabled: boolean): void {
    this.replayMoqieHintEnabled = enabled
  }

  setReplayWaitTips(data: WaitInfoData): void {
    this.replayWaitTipsEnabled = true
    if (!this.waitDisplay) return
    this.tenpaiTipButton?.bindWaitDisplay(this.waitDisplay)
    this.setViewerWaitInfo(data)
    this.waitDisplay.visible = true
  }

  /**
   * Apply Unity-style record danger highlights to the currently rendered
   * closed hands. The map is keyed by absolute seat index.
   */
  setReplayDangerTiles(dangerBySeat: Map<number, Set<number>> | null): void {
    for (let localDir = 0; localDir < this.hands.length; localDir += 1) {
      const absoluteSeat = (localDir + this.selfDir) % 4
      const danger = dangerBySeat?.get(absoluteSeat) ?? new Set<number>()
      const hand = this.hands[localDir]
      for (const tile of [...hand.rightList, ...(hand.drawnTile ? [hand.drawnTile] : [])]) {
        tile.setRecordDangerHighlighted(danger.has(tile.tid))
      }
    }
  }

  setOnConnectionLost(cb: () => void): void {
    this.onConnectionLost = cb
  }

  setAppearance(appearance: SceneAppearanceSettings): void {
    const next = normalizeSceneAppearanceSettings(appearance)
    const fontChanged =
      next.fontTheme !== this.appearance.fontTheme
      || next.latinFontTheme !== this.appearance.latinFontTheme
    const coverColorsChanged =
      next.tileCoverColors.join(',') !== this.appearance.tileCoverColors.join(',')
    this.appearance = next
    setGameFontTheme(next.fontTheme)
    setGameLatinFontTheme(next.latinFontTheme)
    // Editing/reordering the palette updates the selected cover in place.
    // Changing only the rotation strategy takes effect next round and must not
    // reroll the cover currently shown on the table.
    if (coverColorsChanged && this.activeCoverRound === this.round) {
      const index = Math.max(0, Math.min(next.tileCoverColors.length - 1, next.lastTileCoverIndex))
      this.activeCoverColor = hexColorToNumber(next.tileCoverColors[index])
    }
    this.redrawBackground()
    this.updateBackgroundImagePresentation()
    this.applyTileCoverPalette()
    this.applyFlowerAreaAppearance()
    this.applyTileFaceTheme()
    this.refreshRoundLabel()
    if (fontChanged) this.applyFontTheme()
  }

  refreshRoundLabel(): void {
    if (!this.mounted) return
    this.stateDisplay.setRound(this.round, this.appearance.roundLabelFormat)
  }

  setActiveTileCoverIndex(index: number): void {
    const colors = this.appearance.tileCoverColors
    if (colors.length === 0) return
    const selected = Math.max(0, Math.min(colors.length - 1, Math.trunc(index)))
    this.appearance = normalizeSceneAppearanceSettings({
      ...this.appearance,
      lastTileCoverIndex: selected,
    })
    this.activeCoverRound = this.round
    this.activeCoverColor = hexColorToNumber(colors[selected])
    this.applyTileCoverPalette()
  }

  private applyFontTheme(): void {
    if (!this.center) return
    const family = getGameFontFamily(this.appearance.fontTheme, this.appearance.latinFontTheme)
    const visit = (container: Container): void => {
      for (const child of container.children) {
        if (child instanceof Text) child.style.fontFamily = family
        if (child instanceof Container) visit(child)
      }
    }
    visit(this.center)
  }

  setBackgroundImage(source: string | null): void {
    if (this.backgroundImageSource === source) {
      this.updateBackgroundImagePresentation()
      return
    }
    this.backgroundImageSource = source
    this.refreshBackgroundImage()
  }

  // ══════════════════════════════════════════════════════════════════
  //  Lifecycle
  // ══════════════════════════════════════════════════════════════════

  private mounted = false

  async mount(hostElement: HTMLElement): Promise<boolean> {
    if (this.app) return true

    this.destroyed = false
    this.hostElement = hostElement
    const generation = ++this.mountGeneration

    await waitForGameFonts()
    if (this.destroyed || generation !== this.mountGeneration || !hostElement.isConnected) {
      return false
    }
    await ensureTexturesLoaded()
    if (this.destroyed || generation !== this.mountGeneration || !hostElement.isConnected) {
      return false
    }

    const { width: initialWidth, height: initialHeight } = this.getViewportSize()
    const app = new Application()
    await app.init({
      background: this.appearance.backgroundColorTable,
      width: initialWidth,
      height: initialHeight,
      resolution: Math.max(window.devicePixelRatio || 1, 2),
      autoDensity: true,
      antialias: true,
      powerPreference: 'high-performance',
    })

    if (this.destroyed || generation !== this.mountGeneration || !hostElement.isConnected) {
      app.destroy(true, { children: true, texture: true })
      return false
    }

    this.app = app

    this.app.stage.sortableChildren = true
    hostElement.replaceChildren(this.app.canvas)
    this.layout()
    window.addEventListener('resize', this.handleResize)

    this.createSubComponents()
    this.mounted = true
    this.startPeriodicPing()

    if (this.deferredSnapshot) {
      const snapshot = this.deferredSnapshot
      this.deferredSnapshot = null
      this.flushFromSnapshot(snapshot)
    } else if (this.deferredPending) {
      const pending = this.deferredPending
      this.deferredPending = null
      this.showPending(pending.seats, pending.ownReady, pending.onToggleReady)
    }

    // Right-click: discard drawn tile, or pass/final-pass
    if (!isMobile.any) {
      document.addEventListener('contextmenu', this.handleRightClick)
      document.addEventListener('pointerdown', this.handleLeftDoubleClickShortcut)
    }

    // Scroll wheel: in replay, advance/retreat 1 event; in game, do nothing
    document.addEventListener('wheel', this.handleWheel, { passive: false })
    document.addEventListener('pointerdown', this.handleUserGesture)
    document.addEventListener('keydown', this.handleUserGesture)

    // When window refocuses, force layout update to fix animation glitches
    document.addEventListener('visibilitychange', this.handleVisibilityChange)

    return true
  }

  destroy(): void {
    this.destroyed = true
    this.mountGeneration += 1
    this.pendingPassAckStageCounter = null
    if (this.resizeFrame !== null) {
      window.cancelAnimationFrame(this.resizeFrame)
      this.resizeFrame = null
    }
    this.clearPendingChoicesTimeout()
    this.clearAutoActionTimeout()
    this.clearPredrawSortTimeout()
    this.clearScoreDifferenceTimeout()
    this.stopLatencyMeasurement()
    window.removeEventListener('resize', this.handleResize)
    document.removeEventListener('contextmenu', this.handleRightClick)
    document.removeEventListener('pointerdown', this.handleLeftDoubleClickShortcut)
    document.removeEventListener('wheel', this.handleWheel)
    document.removeEventListener('pointerdown', this.handleUserGesture)
    document.removeEventListener('keydown', this.handleUserGesture)
    document.removeEventListener('visibilitychange', this.handleVisibilityChange)
    this.clearPendingButton()
    this.stopActiveSounds()
    if (this.app) {
      this.app.destroy(true, { children: true, texture: true })
      this.app = null
    }
    this.hostElement = null
    this.mounted = false
  }

  private handleRightClick = (e: MouseEvent): void => {
    e.preventDefault()
    if (!this.inputEnabled || !this.canAct() || this.currentStageCounter <= 0) return

    const moqieMode = this.appearance.moqieShortcutMode
    const passMode = this.appearance.passShortcutMode

    if (moqieMode === 1 && this.tryShortcutMoqie()) return
    if (passMode === 0 && this.tryShortcutPass()) return
  }

  private handleLeftDoubleClickShortcut = (e: PointerEvent): void => {
    if (e.button !== 0) return
    if (!this.inputEnabled || !this.canAct() || this.currentStageCounter <= 0) return
    if (!this.isPointerInsideStage(e)) return

    const now = performance.now()
    const isDouble = now - this.lastLeftClickAtMs <= MahjongScene.DOUBLE_CLICK_MS
    this.lastLeftClickAtMs = now
    if (!isDouble) return

    const moqieMode = this.appearance.moqieShortcutMode
    const passMode = this.appearance.passShortcutMode
    // Unity Update 路径：非手牌区域双击摸切（手牌单击已用于出牌，避免冲突）
    if (moqieMode === 0 && !this.isPointerOverSelfHand(e) && this.tryShortcutMoqie()) return
    if (passMode === 1 && this.tryShortcutPass()) return
  }

  private isPointerInsideStage(e: PointerEvent): boolean {
    const canvas = this.app?.canvas
    if (!canvas) return false
    const rect = canvas.getBoundingClientRect()
    return e.clientX >= rect.left && e.clientX <= rect.right
      && e.clientY >= rect.top && e.clientY <= rect.bottom
  }

  private isPointerOverSelfHand(e: PointerEvent): boolean {
    const hand = this.hands?.[0]
    const canvas = this.app?.canvas
    if (!hand || !canvas) return false
    try {
      const rect = canvas.getBoundingClientRect()
      // Stage 坐标与 CSS 像素一致（resolution 只影响 backing store）
      const x = ((e.clientX - rect.left) / Math.max(rect.width, 1)) * this.app!.screen.width
      const y = ((e.clientY - rect.top) / Math.max(rect.height, 1)) * this.app!.screen.height
      return hand.getBounds().contains(x, y)
    } catch {
      return false
    }
  }

  private tryShortcutMoqie(): boolean {
    if (!this.hasAction('discard_tile')) return false
    const hand = this.hands?.[0]
    if (!hand) return false
    // Prefer the draw-slot tile, then the last opening replacement after it has
    // been merged for display; otherwise discard the rightmost hand tile.
    const openingReplacement = this.openingReplacementTile
      && hand.rightList.includes(this.openingReplacementTile)
      ? this.openingReplacementTile
      : null
    const target = hand.drawnTile ?? openingReplacement ?? hand.rightList[0]
    if (!target || target.tid <= 0) return false
    const useDrawnTile = target === hand.drawnTile
    this.clearMeldChoices()
    hand.unwaitDiscard()
    this.countdown.stop()
    this.sendGameInput({
      kind: 'discard_tile',
      tile: target.tid,
      use_drawn_tile: useDrawnTile,
    })
    return true
  }

  private tryShortcutPass(): boolean {
    if (this.hasAction('pass')) {
      this.hands[0].unwaitDiscard()
      this.countdown.stop()
      this.requestPassAction()
      return true
    }
    if (this.hasAction('final_pass')) {
      this.clearMeldChoices()
      this.hands[0].unwaitDiscard()
      this.countdown.stop()
      this.sendGameInput({ kind: 'final_pass' })
      return true
    }
    return false
  }

  /** Stop local action UI while a custom-room vote has suspended the server loop. */
  suspendForVote(): void {
    this.currentPendingStatus = 'none'
    this.currentViewerActions = []
    this.pendingPassAckStageCounter = null
    this.clearPendingChoicesTimeout()
    this.clearAutoActionTimeout()
    this.clearMeldChoices()
    this.hands[0]?.unwaitDiscard()
    this.countdown.stop()
  }

  private clearPendingChoicesTimeout(): void {
    if (this.pendingChoicesTimeout !== null) {
      clearTimeout(this.pendingChoicesTimeout)
      this.pendingChoicesTimeout = null
    }
    this.inputEnabled = false
  }

  private clearAutoActionTimeout(): void {
    if (this.autoActionTimeout !== null) {
      clearTimeout(this.autoActionTimeout)
      this.autoActionTimeout = null
    }
  }

  private clearPredrawSortTimeout(): void {
    if (this.predrawSortTimeout !== null) {
      clearTimeout(this.predrawSortTimeout)
      this.predrawSortTimeout = null
    }
  }

  private clearPendingButton(): void {
    this.pendingButton?.destroy({ children: true })
    this.pendingButton = null
  }

  private clearLatencyPingTimeouts(): void {
    for (const timeoutId of this.latencyPingTimeouts.values()) {
      clearTimeout(timeoutId)
    }
    this.latencyPingTimeouts.clear()
    this.latencyPingSentAt.clear()
  }

  private updateLatencyIndicator(value: string): void {
    if (!this.latencyIndicatorBg || !this.latencyIndicatorText) return

    this.latencyIndicatorText.text = tr(`延迟：${value}`)
    const width = Math.max(TILE_WIDTH * 3.4, this.latencyIndicatorText.width + TILE_WIDTH * 1.2)
    const height = TILE_WIDTH * 0.9

    this.latencyIndicatorBg.clear()
    this.latencyIndicatorBg.roundRect(-width / 2, -height / 2, width, height, TILE_RADIUS)
    this.latencyIndicatorBg.fill({ color: FRONT_COLOR })
    this.latencyIndicatorBg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
  }

  private setLatencyIndicatorVisible(visible: boolean): void {
    if (!this.latencyIndicator) return
    this.latencyIndicator.visible = visible && this.presentationMode === 'game'
  }

  private stopLatencyMeasurement(): void {
    this.clearLatencyPingTimeouts()
    this.stopPeriodicPing()
    this.lastPongMs = null
    this.missedPongCount = 0
    this.updateLatencyIndicator('\u2014')
    this.setLatencyIndicatorVisible(false)
  }

  private startPeriodicPing(): void {
    this.stopPeriodicPing()
    if (this.presentationMode !== 'game') return
    this.pingIntervalId = setInterval(() => {
      this.sendLatencyPing()
    }, 5000)
  }

  private stopPeriodicPing(): void {
    if (this.pingIntervalId !== null) {
      clearInterval(this.pingIntervalId)
      this.pingIntervalId = null
    }
  }

  private sendLatencyPing(): void {
    if (this.presentationMode !== 'game') return

    const identifier = ++this.latencyPingCounter
    this.latencyPingSentAt.set(identifier, Date.now())
    const timeoutId = setTimeout(() => {
      this.latencyPingTimeouts.delete(identifier)
      this.latencyPingSentAt.delete(identifier)
      ++this.missedPongCount
      if (this.missedPongCount >= 2) {
        // Stop pinging immediately to prevent a spiral:
        // during reconnection pings get dropped but their
        // timeouts still fire, which would trigger another
        // onConnectionLost → close → reconnect → repeat.
        this.stopPeriodicPing()
        this.clearLatencyPingTimeouts()
        this.missedPongCount = 0
        this.onConnectionLost?.()
      }
    }, 3000)
    this.latencyPingTimeouts.set(identifier, timeoutId)
    this.sendToServer('ping', { identifier })
  }

    /** Restart the periodic latency-measurement ping.
   *  Called by GamePage after a successful reconnect so the
   *  latency indicator recovers without triggering another
   *  disconnect spiral. */
  restartPeriodicPing(): void {
    this.startPeriodicPing()
  }

  private handleCountdownLatencyClick = (): void => {
    if (this.presentationMode !== 'game') return
    this.setLatencyIndicatorVisible(true)
    if (this.lastPongMs !== null) {
      this.updateLatencyIndicator(`${this.lastPongMs} ms`)
    } else {
      this.updateLatencyIndicator('\u2014')
    }
    if (this.latencyHideTimeout !== null) clearTimeout(this.latencyHideTimeout)
    this.latencyHideTimeout = setTimeout(() => {
      this.latencyHideTimeout = null
      this.setLatencyIndicatorVisible(false)
    }, 1000)
  }

  private handleWheel = (e: WheelEvent): void => {
    // 侧栏 / 弹窗等 UI 需要原生滚轮滚动；勿全局 preventDefault
    const target = e.target
    if (target instanceof Element) {
      if (target.closest('.game-felt-rail, .game-page__sidebar, .el-overlay, .el-dialog, .el-message-box')) {
        return
      }
      const overStage = Boolean(
        this.hostElement?.contains(target)
        || this.app?.canvas?.contains(target),
      )
      if (!overStage) return
    }
    e.preventDefault()
  }

  private handleVisibilityChange = (): void => {
    if (document.visibilityState === 'visible' && this.mounted) {
      // Force layout refresh after window refocus to fix animation glitches
      this.resizeAndLayout()
    }
  }

  private handleUserGesture = (): void => {
    void this.primeLoadedSounds()
  }

  private rememberViewer(viewer: Record<string, any>): void {
    this.currentPendingStatus = typeof viewer.pending === 'string' ? viewer.pending : 'none'
    this.currentViewerActions = Array.isArray(viewer.available_actions)
      ? viewer.available_actions as Array<Record<string, any>>
      : []
  }

  private canAct(): boolean {
    return this.currentPendingStatus !== 'none' && this.currentPendingStatus !== 'slept'
  }

  private hasAction(kind: string): boolean {
    return this.currentViewerActions.some((action) => action.kind === kind)
  }

  private sendGameInput(payload: Record<string, unknown>): void {
    if (this.currentStageCounter <= 0) return
    this.sendToServer('game.input', {
      ...payload,
      stage_counter: this.currentStageCounter,
    })
  }

  private requestPassAction(applyDebounce: boolean = true): void {
    const now = Date.now()
    if (this.currentStageCounter <= 0) return
    if (applyDebounce && now - this.lastPassAttemptAtMs < PASS_DEBOUNCE_MS) return
    this.lastPassAttemptAtMs = now
    this.pendingPassAckStageCounter = this.currentStageCounter
    this.sendGameInput({ kind: 'pass' })
  }

  private syncDecisionTimer(viewer: Record<string, any>): void {
    const dt = typeof viewer.decision_timer_ms === 'number' ? viewer.decision_timer_ms : null
    if (this.canAct() && dt !== null && dt > 0) {
      this.countdown.onExpire = () => {
        this.inputEnabled = false
        this.clearMeldChoices()
        this.hands[0].unwaitDiscard()
      }
      this.countdown.setTimeMillis(dt, this.currentStageCounter)
      this.countdown.visible = true
    } else {
      this.countdown.stop()
      this.countdown.visible = true
    }
  }

  private setViewerWaitInfo(data: WaitInfoData): void {
    this.waitInfoData = data
    this.waitDisplay.setData(data)
    if (!data) {
      this.waitDisplay.reset()
    }
    const isStableTenpai = Boolean(data && data.type === 'waits' && data.details.length > 0)
    this.tenpaiTipButton?.setTenpaiActive(
      isStableTenpai && (this.presentationMode !== 'replay' || this.replayWaitTipsEnabled),
    )
  }

  private seatWindLabel(localDir: number): string {
    return ['東', '南', '西', '北'][(localDir + this.selfDir) % 4] ?? ''
  }

  private applyCenterScores(): void {
    for (let i = 0; i < 4; i += 1) {
      this.rivers[i]?.setPlayerInfo(this.ranks[i], this.names[i], !this.present[i])
      this.stateDisplay.setScore(
        this.names[i],
        this.scores[i],
        i,
        !this.present[i],
        4.0,
        this.seatWindLabel(i),
      )
    }
  }

  private clearScoreDifferenceTimeout(): void {
    if (this.scoreDifferenceTimeout !== null) {
      clearTimeout(this.scoreDifferenceTimeout)
      this.scoreDifferenceTimeout = null
    }
  }

  private renderScoreDifferences(): void {
    const selfScore = Number(this.scores[0] ?? 0)
    for (let direction = 0; direction < 4; direction += 1) {
      if (direction === 0) {
        const text = selfScore > 0 ? `+${selfScore}` : `${selfScore}`
        this.stateDisplay.setScoreText(
          text,
          direction,
          this.present[direction] ? 0x000000 : 0x888888,
        )
        continue
      }

      const difference = selfScore - Number(this.scores[direction] ?? 0)
      const text = difference > 0 ? `+${difference}` : `${difference}`
      const color = !this.present[direction]
        ? 0x888888
        : difference > 0
          ? 0x008c35
          : difference < 0
            ? 0xd02020
            : 0x000000
      this.stateDisplay.setScoreText(text, direction, color)
    }
  }

  private showScoreDifferences = (): void => {
    if (!this.mounted || !this.stateDisplay.visible) return

    this.scoreDifferenceVisible = true
    this.renderScoreDifferences()
    this.clearScoreDifferenceTimeout()
    this.scoreDifferenceTimeout = setTimeout(() => {
      this.scoreDifferenceTimeout = null
      this.scoreDifferenceVisible = false
      if (this.mounted) this.applyCenterScores()
    }, 2000)
  }

  private findAvailableAction(kinds: string[]): Record<string, any> | null {
    return this.currentViewerActions.find((action) => kinds.includes(action.kind)) ?? null
  }

  private triggerAutoAction(action: Record<string, any>): boolean {
    if (action.kind === 'pass') {
      this.clearAutoActionTimeout()
      this.countdown.stop()
      this.clearMeldChoices()
      this.hands[0].unwaitDiscard()
      this.waitDisplay.visible = false
      this.requestPassAction(false)
      return true
    }
    this.clearAutoActionTimeout()
    this.countdown.stop()
    this.clearMeldChoices()
    this.hands[0].unwaitDiscard()
    this.waitDisplay.visible = false
    this.sendGameInput({
      kind: action.kind,
      tile: action.tile,
      use_drawn_tile: action.use_drawn_tile,
      ui64_value: action.ui64_value,
    })
    return true
  }

  private scheduleAutoWinAction(action: Record<string, any>): boolean {
    this.clearAutoActionTimeout()
    this.countdown.stop()
    this.clearMeldChoices()
    this.hands[0].unwaitDiscard()
    this.waitDisplay.visible = false
    this.autoActionTimeout = setTimeout(() => {
      this.autoActionTimeout = null
      const liveAction = this.findAvailableAction([action.kind])
      if (!liveAction) {
        return
      }
      this.triggerAutoAction(liveAction)
    }, AUTO_WIN_DELAY_MS)
    return true
  }

  private tryAutoHelperAction(context: ViewerSyncContext | null, reactionTile: number | null = null): boolean {
    // 杠后补牌必须保留完整的手动决策窗口：不自动和牌、补花或摸切。
    if (context?.drawSource === 'deal_gang_tile' && context.actorSeat === this.selfDir) {
      return false
    }

    const selfWinAction = this.findAvailableAction(['self_drawn_win'])
    const claimRonAction = this.findAvailableAction(['discard_win'])
    const claimRobAction = this.findAvailableAction(['rob_added_kong_win'])
    const flowerAction = this.findAvailableAction(['flower'])
    const selfKongAction = this.findAvailableAction(['added_kong', 'concealed_kong'])
    const passAction = this.findAvailableAction(['pass', 'final_pass'])
    const silent = new Set(this.assist.silentTiles)

    // 1) 自动补花
    if (this.assist.autoFlower && flowerAction) {
      return this.triggerAutoAction(flowerAction)
    }

    // 2) 选中牌命中河牌/加杠：不询问任何操作（含荣和）→ pass
    if (
      passAction
      && reactionTile
      && silent.has(reactionTile)
      && this.findAvailableAction(['chow', 'pung', 'melded_kong', 'discard_win', 'rob_added_kong_win'])
    ) {
      return this.triggerAutoAction(passAction)
    }

    // 3) 自动和牌（受不点和 / 不抢杠 / 不自摸 / 选中牌不自动自摸 约束）
    if (this.assist.autoWin) {
      if (claimRobAction && !this.assist.noRobKong) {
        return this.scheduleAutoWinAction(claimRobAction)
      }
      if (claimRonAction && !this.assist.noRon) {
        return this.scheduleAutoWinAction(claimRonAction)
      }
      if (selfWinAction && !this.assist.noTsumo) {
        const winTile = Number(selfWinAction.tile ?? this.hands[0]?.drawnTile?.tid ?? 0)
        const skipSelectedTsumo = this.assist.silentSkipTsumo && winTile > 0 && silent.has(winTile)
        if (!skipSelectedTsumo) {
          return this.scheduleAutoWinAction(selfWinAction)
        }
      }
    }

    // 4) 鸣牌过滤后无可操作项 → 自动 pass（Unity ShouldAutoPassMingPaiAsk）
    if (passAction && this.shouldAutoPassAfterMeldFilter()) {
      return this.triggerAutoAction(passAction)
    }

    // 5) 自动摸切
    if (
      this.autoDiscard
      && this.hasAction('discard_tile')
      && !selfWinAction
      && !selfKongAction
      && !flowerAction
      && this.hands[0].drawnTile
      && (
        (context?.category === 'transition' && context.kind === 'draw_tile' && context.actorSeat === this.selfDir)
        || context?.kind === 'hand_prompt'
        || context == null
      )
    ) {
      const drawnTile = this.hands[0].drawnTile
      this.clearAutoActionTimeout()
      this.clearMeldChoices()
      // Match Unity WaitAutoCut: keep the real decision timer and hand input
      // active during the 0.5 s grace period so a manual discard can override.
      this.hands[0].waitDiscard((tid, useDrawn) => {
        this.clearAutoActionTimeout()
        this.countdown.stop()
        this.hands[0].unwaitDiscard()
        this.sendGameInput({
          kind: 'discard_tile',
          tile: tid,
          use_drawn_tile: useDrawn,
        })
      })
      this.autoActionTimeout = setTimeout(() => {
        this.autoActionTimeout = null
        if (!this.hasAction('discard_tile') || !drawnTile || this.hands[0].drawnTile !== drawnTile) {
          return
        }
        this.countdown.stop()
        this.hands[0].unwaitDiscard()
        this.waitDisplay.reset()
        this.sendGameInput({
          kind: 'discard_tile',
          tile: drawnTile.tid,
          use_drawn_tile: true,
        })
      }, AUTO_DISCARD_DELAY_MS)
      return true
    }
    return false
  }

  /** Remaining non-pass actions after 不吃/不碰/不明杠/不点和 filters. */
  private remainingActionsAfterMeldFilter(): Array<Record<string, any>> {
    const actions = this.currentViewerActions.filter((action) => action.kind !== 'pass' && action.kind !== 'final_pass')
    return actions.filter((action) => {
      if (this.assist.passChi && action.kind === 'chow') return false
      if (this.assist.passPeng && action.kind === 'pung') return false
      if (this.assist.passMingGang && action.kind === 'melded_kong') return false
      // 不点和仅剔除 discard_win；不抢杠/不自摸不参与自动过牌筛除（对齐 Unity）。
      if (this.shouldFilterRonForAutoPass() && action.kind === 'discard_win') return false
      return true
    })
  }

  private hasUnblockedMeldOption(): boolean {
    if (!this.assist.passPeng && this.hasAction('pung')) return true
    if (!this.assist.passChi && this.hasAction('chow')) return true
    if (!this.assist.passMingGang && this.hasAction('melded_kong')) return true
    return false
  }

  /** Unity ShouldFilterRonForAutoPass: 不点和 removes ron unless another unblocked meld remains. */
  private shouldFilterRonForAutoPass(): boolean {
    if (!this.assist.noRon || !this.hasAction('discard_win')) return false
    return !this.hasUnblockedMeldOption()
  }

  private shouldAutoPassAfterMeldFilter(): boolean {
    const offered = this.currentViewerActions.some((action) => action.kind !== 'pass' && action.kind !== 'final_pass')
    if (!offered) return false
    return this.remainingActionsAfterMeldFilter().length === 0
  }

  private applyViewerInteractions(reactionTile: number | null, context: ViewerSyncContext | null): void {
    this.inputEnabled = this.canAct()
    if (!this.inputEnabled) {
      this.clearMeldChoices()
      this.hands[0].unwaitDiscard()
      return
    }

    if (this.tryAutoHelperAction(context, reactionTile)) {
      return
    }

    const visibleActions = this.viewerActionsAfterMeldFilter()
    const nonDiscard = visibleActions.filter((action) => action.kind !== 'discard_tile')
    if (nonDiscard.length > 0) {
      this.showMeldChoices(
        { available_actions: visibleActions as MeldViewerSnapshot['available_actions'] },
        reactionTile,
      )
    } else {
      this.clearMeldChoices()
    }

    if (this.hasAction('discard_tile')) {
      this.hands[0].waitDiscard((tid, useDrawn) => {
        this.countdown.stop()
        this.clearMeldChoices()
        this.sendGameInput({
          kind: 'discard_tile',
          tile: tid,
          use_drawn_tile: useDrawn,
        })
      })
    } else {
      this.hands[0].unwaitDiscard()
    }
  }

  private syncViewerControls(
    viewer: Record<string, any>,
    reactionTile: number | null = null,
    context: ViewerSyncContext | null = null,
  ): void {
    if (this.presentationMode === 'replay') {
      this.currentPendingStatus = 'none'
      this.currentViewerActions = []
      this.inputEnabled = false
      this.clearPendingChoicesTimeout()
      this.clearAutoActionTimeout()
      this.clearMeldChoices()
      this.hands[0].unwaitDiscard()
      this.setViewerWaitInfo(null)
      this.waitDisplay.visible = false
      this.countdown.stop()
      this.countdown.visible = false
      return
    }

    this.rememberViewer(viewer)
    this.clearPendingChoicesTimeout()
    this.clearAutoActionTimeout()
    this.clearMeldChoices()
    this.hands[0].unwaitDiscard()

    // After a win or drawn game, suppress wait options until next kStart
    if (this.roundEnded) {
      this.setViewerWaitInfo(null)
      this.waitDisplay.visible = false
      this.countdown.stop()
      this.countdown.visible = true
      return
    }

    this.setViewerWaitInfo((viewer.wait_data ?? null) as WaitInfoData)

    const pendingDelay = Math.max(0, Number(viewer.pending_start_timer_remaining_ms ?? 0))
    if (pendingDelay > 0 && this.canAct()) {
      this.countdown.stop()
      this.countdown.visible = true
      this.pendingChoicesTimeout = setTimeout(() => {
        this.pendingChoicesTimeout = null
        this.syncDecisionTimer(viewer)
        this.applyViewerInteractions(reactionTile, context)
      }, pendingDelay)
      return
    }

    this.syncDecisionTimer(viewer)
    this.applyViewerInteractions(reactionTile, context)
  }

  handlePassAck(payload: { stage_counter?: number | null }): void {
    if (!this.mounted) return
    const stageCounter = typeof payload.stage_counter === 'number' ? payload.stage_counter : null
    if (stageCounter === null || stageCounter !== this.currentStageCounter) return
    if (this.pendingPassAckStageCounter !== stageCounter) return

    this.pendingPassAckStageCounter = null
    this.currentPendingStatus = 'slept'
    this.currentViewerActions = []
    this.inputEnabled = false
    this.countdown.stop()
    this.clearMeldChoices()
    this.hands[0].unwaitDiscard()
  }

  showRatingUpdate(message: string): void {
    if (!this.mounted || !this.app) return
    this.clearPendingButton()

    const style = new TextStyle({
      fontFamily: getGameFontFamily(),
      fontSize: 28,
      fill: 0x000000,
      align: 'center',
    })
    const text = new Text({ text: message, style })
    text.anchor.set(0.5)

    const bg = new Graphics()
    const paddingX = 40
    const paddingY = 16
    bg.roundRect(-text.width / 2 - paddingX, -text.height / 2 - paddingY,
      text.width + paddingX * 2, text.height + paddingY * 2, 16)
    bg.fill({ color: FRONT_COLOR, alpha: 0.92 })

    const container = new Container()
    container.addChild(bg)
    container.addChild(text)
    container.x = this.app!.screen.width / 2
    container.y = this.app!.screen.height / 2 + 120

    this.center.addChild(container)
    this.pendingButton = container

    // Auto-dismiss after 6 seconds
    setTimeout(() => {
      this.clearPendingButton()
    }, 6000)
  }

  // ══════════════════════════════════════════════════════════════════
  //  Layout
  // ══════════════════════════════════════════════════════════════════

  private handleResize = (): void => {
    this.scheduleResizeAndLayout()
  }

  private scheduleResizeAndLayout(): void {
    if (this.resizeFrame !== null) return
    this.resizeFrame = window.requestAnimationFrame(() => {
      this.resizeFrame = null
      this.resizeAndLayout()
    })
  }

  private resizeAndLayout(): void {
    if (!this.app) return
    const { width, height } = this.getViewportSize()
    const nextResolution = Math.max(window.devicePixelRatio || 1, 2)
    if (this.app.screen.width !== width || this.app.screen.height !== height) {
      this.app.renderer.resize(width, height)
    }
    if (this.app.renderer.resolution !== nextResolution) {
      this.app.renderer.resolution = nextResolution
    }
    this.redrawBackground()
    this.layoutBackgroundImage()
    this.layout()
    // 尺寸变化后按新的可视范围重新夹紧滑动偏移
    this.setVerticalSlide(this.currentSlide)
  }

  private redrawBackground(): void {
    if (!this.app) {
      return
    }
    const { width, height } = this.getViewportSize()
    this.backgroundFill.clear()
    this.backgroundFill.rect(0, 0, width, height)
    this.backgroundFill.fill({ color: hexColorToNumber(this.appearance.backgroundColorTable) })
  }

  private refreshBackgroundImage(): void {
    this.backgroundImageLoadToken += 1
    const loadToken = this.backgroundImageLoadToken

    if (this.backgroundImageSprite) {
      this.backgroundLayer.removeChild(this.backgroundImageSprite)
      this.backgroundImageSprite.destroy()
      this.backgroundImageSprite = null
    }

    if (!this.app || !this.backgroundImageSource) {
      return
    }

    void Assets.load(this.backgroundImageSource)
      .then((asset) => {
        if (!this.app || loadToken !== this.backgroundImageLoadToken || !this.backgroundImageSource) {
          return
        }

        const texture = asset as Texture
        const sprite = new Sprite(texture)
        sprite.eventMode = 'none'
        this.backgroundLayer.addChild(sprite)
        this.backgroundImageSprite = sprite
        this.layoutBackgroundImage()
        this.updateBackgroundImagePresentation()
      })
      .catch(() => {})
  }

  private layoutBackgroundImage(): void {
    if (!this.app || !this.backgroundImageSprite) {
      return
    }

    const { width, height } = this.getViewportSize()
    const textureWidth = this.backgroundImageSprite.texture.width || width
    const textureHeight = this.backgroundImageSprite.texture.height || height
    const scale = Math.max(width / textureWidth, height / textureHeight)

    this.backgroundImageSprite.width = textureWidth * scale
    this.backgroundImageSprite.height = textureHeight * scale
    this.backgroundImageSprite.x = (width - this.backgroundImageSprite.width) / 2
    this.backgroundImageSprite.y = (height - this.backgroundImageSprite.height) / 2
  }

  private updateBackgroundImagePresentation(): void {
    if (!this.backgroundImageSprite) {
      return
    }

    this.backgroundImageSprite.visible = this.appearance.backgroundImageEnabled && !!this.backgroundImageSource
    this.backgroundImageSprite.alpha = this.appearance.backgroundImageAlpha
  }

  private resolveTileCoverColor(): number {
    if (this.activeCoverRound === this.round) {
      return this.activeCoverColor
    }
    const picked = pickTileCoverColor(this.appearance, this.round)
    this.activeCoverRound = this.round
    this.activeCoverColor = hexColorToNumber(picked.color)
    if (picked.index !== this.appearance.lastTileCoverIndex) {
      this.appearance = normalizeSceneAppearanceSettings({
        ...this.appearance,
        lastTileCoverIndex: picked.index,
      })
      this.sendToServer('appearance.lastTileCoverIndex', { index: picked.index })
    }
    return this.activeCoverColor
  }

  private applyTileCoverPalette(): void {
    if (!this.mounted) {
      return
    }

    const coverColor = this.resolveTileCoverColor()
    for (const river of this.rivers) {
      for (const tile of river.tileList) {
        tile.setCoverColor(coverColor)
      }
      for (const tile of river.flowerList) {
        tile.setCoverColor(coverColor)
      }
    }

    for (const hand of this.hands) {
      for (const tile of hand.leftList) {
        tile.setCoverColor(coverColor)
      }
      for (const tile of hand.rightList) {
        tile.setCoverColor(coverColor)
      }
      hand.drawnTile?.setCoverColor(coverColor)
    }
  }

  private applyFlowerAreaAppearance(): void {
    if (!this.mounted || !this.rivers) return
    for (const river of this.rivers) {
      river.setFlowerAreaAppearance(
        this.appearance.flowerAreaDisplay,
        hexColorToNumber(this.appearance.flowerAreaColor),
        this.appearance.flowerAreaAlpha,
        hexColorToNumber(this.appearance.flowerAreaLabelColor),
        hexColorToNumber(this.appearance.flowerAreaCountColor),
        this.appearance.flowerAreaLabelScale,
      )
    }
  }

  private applyTileFaceTheme(): void {
    setTileThemes(this.appearance.tileFaceTheme, this.appearance.flowerFaceTheme)
    if (!this.mounted) return
    const refresh = (container: Container): void => {
      for (const child of container.children) {
        if (child instanceof Tile) child.refreshTexture()
        if (child instanceof Container) refresh(child)
      }
    }
    refresh(this.center)
  }

  private layout(): void {
    if (!this.app) return
    const { width: sw, height: sh } = this.getViewportSize()

    if (isMobile.any) {
      if (sw > sh) {
        this.center.x = sh / 2 + 1.4 * TILE_HEIGHT * sh * WINDOW_SCALE / SCALE_FACTOR
        this.center.y = sh / 2
        this.center.rotation = -Math.PI / 2
        this.center.scale.set(sh * WINDOW_SCALE / SCALE_FACTOR)
      } else {
        this.center.x = sw / 2
        this.center.y = sw / 2 + 1.4 * TILE_HEIGHT * sw * WINDOW_SCALE / SCALE_FACTOR
        this.center.rotation = 0
        this.center.scale.set(sw * WINDOW_SCALE / SCALE_FACTOR)
      }
    } else {
      this.center.x = sw / 2
      this.center.y = sh / 2
      this.center.rotation = 0
      this.center.scale.set(Math.min(sw, sh) * WINDOW_SCALE / SCALE_FACTOR)
    }

    // 竖屏手机才允许垂直滑动；横屏/桌面回到基准位置
    if (!isMobile.any || sw > sh) {
      this.currentSlide = 0
    }
    this.baseCenterY = this.center.y
    this.center.y = this.baseCenterY + this.currentSlide
  }

  private getViewportSize(): { width: number; height: number } {
    const bounds = this.hostElement?.getBoundingClientRect()
    if (bounds && bounds.width > 0 && bounds.height > 0) {
      return { width: bounds.width, height: bounds.height }
    }
    return { width: window.innerWidth, height: window.innerHeight }
  }

  private updateDirectionLabels(): void {
    for (let localDir = 0; localDir < 4; localDir += 1) {
      this.directionLabels[localDir].setDir(this.selfDir)
    }
  }

  private setDirectionLabelsVisible(_visible: boolean): void {
    // Corner wind disks retired: seat winds render beside center-panel names.
    for (let localDir = 0; localDir < 4; localDir += 1) {
      this.directionLabels[localDir].visible = false
    }
  }

  private createHand(direction: number, parent: Container, river: River, waitDisplay: WaitDisplay | null): Hand {
    return new Hand(direction, parent, river, waitDisplay, this.presentationMode === 'replay')
  }

  private createRiver(direction: number, parent: Container): River {
    return new River(
      direction,
      parent,
      this.appearance.flowerAreaDisplay,
      hexColorToNumber(this.appearance.flowerAreaColor),
      this.appearance.flowerAreaAlpha,
      hexColorToNumber(this.appearance.flowerAreaLabelColor),
      hexColorToNumber(this.appearance.flowerAreaCountColor),
      this.appearance.flowerAreaLabelScale,
    )
  }

  private createSceneTile(tid: number): Tile {
    const tile = Tile.newInvisible(tid)
    if (this.presentationMode === 'replay') {
      tile.setHoverVisualEnabled(false)
    }
    return tile
  }

  private clearReplayClaimLabel(): void {
    if (!this.replayClaimLabel) {
      return
    }
    const label = this.replayClaimLabel
    this.replayClaimLabel = null
    label.dismiss()
  }

  private showReplayClaimLabel(actorDir: number, text: string, autoHide: boolean): void {
    this.clearReplayClaimLabel()
    const label = new TempLabel(
      this.center,
      actorDir,
      text,
      autoHide ? 1000 : null,
    )
    if (!autoHide) {
      this.replayClaimLabel = label
    }
  }

  // ══════════════════════════════════════════════════════════════════
  //  Sub-component creation
  // ══════════════════════════════════════════════════════════════════

  private createSubComponents(): void {
    const c = this.center
    c.sortableChildren = true

    this.waitDisplay = new WaitDisplay(c)

    this.rivers = [
      this.createRiver(0, c),
      this.createRiver(1, c),
      this.createRiver(2, c),
      this.createRiver(3, c),
    ]

    this.hands = [
      this.createHand(0, c, this.rivers[0], this.waitDisplay),
      this.createHand(1, c, this.rivers[1], null),
      this.createHand(2, c, this.rivers[2], null),
      this.createHand(3, c, this.rivers[3], null),
    ]

    this.stateDisplay = new Display(c)
    this.stateDisplay.setPanelClickHandler(this.showScoreDifferences)
    this.tempDisplay = new Display(c)
    this.volDisplay = new Display(c)
    this.directionLabels = [
      new DirLabel(c, 0, null),
      new DirLabel(c, 1, null),
      new DirLabel(c, 2, null),
      new DirLabel(c, 3, null),
    ]
    this.tenpaiTipButton = new TenpaiTipButton(c)
    this.tenpaiTipButton.bindWaitDisplay(
      this.presentationMode === 'replay' && !this.replayWaitTipsEnabled ? null : this.waitDisplay,
    )
    this.countdown = new Countdown(c)
    this.countdown.onLatencyClick = this.handleCountdownLatencyClick
    this.countdown.onUrgentSecond = () => {
      this.playSound('decision-countdown-tick')
    }

    const latencyIndicator = new Container()
    latencyIndicator.x = 5 * TILE_HEIGHT
    latencyIndicator.y = -6 * TILE_HEIGHT
    latencyIndicator.scale.set(MELD_OPT_SCALE)
    latencyIndicator.visible = false

    const latencyBg = new Graphics()
    latencyIndicator.addChild(latencyBg)

    const latencyText = new Text({
      text: tr('延迟：\u2014'),
      style: { fontFamily: getGameFontFamily(), fontSize: 160, fill: 0x000000, align: 'center' },
    })
    latencyText.anchor.set(0.5)
    latencyIndicator.addChild(latencyText)
    c.addChild(latencyIndicator)

    this.latencyIndicator = latencyIndicator
    this.latencyIndicatorBg = latencyBg
    this.latencyIndicatorText = latencyText
    this.updateLatencyIndicator('\u2014')

    this.setDirectionLabelsVisible(false)
    this.countdown.visible = false
    this.setLatencyIndicatorVisible(false)
    this.volDisplay.visible = false
    this.waitDisplay.visible = this.presentationMode !== 'replay' || this.replayWaitTipsEnabled

    if (!this.app) return
    this.redrawBackground()
    this.app.stage.addChild(this.backgroundLayer)
    this.refreshBackgroundImage()
    this.app.stage.addChild(this.center)
  }

  private resetAssistOptions(): void {
    // Assist preferences are user settings (persisted in Vue); do not wipe on round start.
  }

  private schedulePredrawSort(): void {
    this.clearPredrawSortTimeout()
    this.predrawSortTimeout = setTimeout(() => {
      this.predrawSortTimeout = null
      if (!this.mounted) return
      this.hands[0].updateDisplay(false, false, false, false, true)
      this.hands[1].updateDisplay(false, false, false, false, true)
      this.hands[2].updateDisplay(false, false, false, false, true)
      this.hands[3].updateDisplay(false, false, false, false, true)
    }, 500)
  }

  // ══════════════════════════════════════════════════════════════════
  //  Public API — called by React page
  // ══════════════════════════════════════════════════════════════════

  /** Full reset from an active snapshot (reconnect or initial state). */
  flushFromSnapshot(snapshot: ActiveSessionSnapshot): void {
    if (!this.mounted) {
      this.deferredPending = null
      this.deferredSnapshot = snapshot
      return
    }

    const { viewer, seats, state } = snapshot
    const revealAllHands = Boolean(snapshot.reveal_all_hands)
    this.pendingPassAckStageCounter = null
    this.openingReplacementTile = null
    this.selfDir = viewer.seat_index
    this.currentStageCounter = state.stage_counter
    this.rememberViewer(viewer as Record<string, any>)
    this.deferredPending = null
    this.deferredSnapshot = null
    this.clearPendingChoicesTimeout()
    this.clearAutoActionTimeout()
    this.clearPredrawSortTimeout()
    this.clearPendingButton()
    this.clearReplayClaimLabel()
    this.inputEnabled = false

    // Destroy old hands & rivers
    this.clearMeldChoices()

    this.stateDisplay.clear()

    for (let i = 0; i < 4; i += 1) {
      this.rivers[i].visible = false
      this.hands[i].visible = false
      this.center.removeChild(this.rivers[i])
      this.center.removeChild(this.hands[i])
      this.rivers[i].destroy({ children: true })
      this.hands[i].destroy({ children: true })
    }

    // Recreate
    const c = this.center
    this.rivers = [
      this.createRiver(0, c),
      this.createRiver(1, c),
      this.createRiver(2, c),
      this.createRiver(3, c),
    ]
    this.hands = [
      this.createHand(0, c, this.rivers[0], this.waitDisplay),
      this.createHand(1, c, this.rivers[1], null),
      this.createHand(2, c, this.rivers[2], null),
      this.createHand(3, c, this.rivers[3], null),
    ]

    this.updateDirectionLabels()
    this.countdown.stop()
    this.setViewerWaitInfo((viewer.wait_data ?? null) as WaitInfoData)

    this.round = state.round_counter
    this.remainingTiles = state.remaining_tile_count
    // If the snapshot shows a completed round (result_event present or session ended),
    // suppress wait options until the next kStart arrives.
    // A game_start snapshot is also used for every following round.  Reset the
    // previous result guard or round two will render normally but reject every
    // tile/button input until the server times out.
    this.roundEnded = Boolean(state.ended || snapshot.result_event)
    const currentPlayer = readCurrentPlayer(state as Record<string, any>)
    if (currentPlayer !== null) {
      this.currentDir = currentPlayer
    }

    // Populate seats
    const localSeats: (SeatSnapshot | null)[] = [null, null, null, null]
    for (const seat of seats) {
      const localDir = transDir(seat.seat_index, this.selfDir)
      localSeats[localDir] = seat
    }

    for (let localDir = 0; localDir < 4; localDir += 1) {
      const seat = localSeats[localDir]
      if (!seat) continue

      this.names[localDir] = displayPlayerName(seat.username)
      this.ranks[localDir] = seat.rank || '—'
      this.scores[localDir] = seat.score
      this.voiceIds[localDir] = seat.voice_id === 2 ? 2 : 1
      this.present[localDir] = (this.presentationMode === 'replay' && this.replayRecordVersion < 6) ? true : !(seat.afk || seat.disconnected)

      // River tiles
      for (const tid of seat.discard_pile) {
        const tile = this.createSceneTile(tid)
        tile.updateTid(tid)
        tile.show()
        this.rivers[localDir].addTile(tile)
      }
      const discardDrawnFlags = Array.isArray(seat.discard_drawn_flags) ? seat.discard_drawn_flags : []
      discardDrawnFlags.forEach((usedDrawnTile, discardIndex) => {
        if (!usedDrawnTile || this.presentationMode !== 'replay' || !this.replayMoqieHintEnabled) return
        const riverTile = this.rivers[localDir].tileList[discardIndex]
        riverTile?.setPersistentTint(FROM_DRAWN_TINT)
      })

      // Melds — use backend snapshot fields directly.
      for (const meld of seat.melds) {
        const meldType = meld.type === 'sequence' ? 'chow' : meld.type === 'triplet' ? 'pung' : 'kong'
        this.hands[localDir].addMeld(meldType as 'chow' | 'pung' | 'kong', meld.tile, {
          chowMode: meld.chow_mode,
          meldFromRel: meld.meld_from_rel,
          concealed: meld.concealed ?? false,
          claimedFromDrawnDiscard: meld.claimed_from_drawn_discard ?? false,
          addedFromDrawnTile: meld.added_from_drawn_tile ?? false,
          concealedFromDrawnTile: meld.concealed_from_drawn_tile ?? false,
        })
      }
      if (this.presentationMode === 'replay') {
        this.rivers[localDir].setReplayFlowers(seat.flower_tiles ?? [])
      } else {
        for (const flowerTile of seat.flower_tiles ?? []) {
          this.hands[localDir].addFlower(flowerTile)
        }
      }

      // Hand tiles
      if (localDir === 0 || (revealAllHands && Array.isArray(seat.hand_tiles))) {
        for (const tid of seat.hand_tiles ?? []) {
          const tile = Tile.newInvisible(tid)
          tile.updateTid(tid)
          tile.show()
          this.hands[localDir].addRightList(tile)
        }
        if (seat.drawn_tile != null && seat.drawn_tile !== undefined) {
          const tile = Tile.newInvisible(seat.drawn_tile)
          tile.updateTid(seat.drawn_tile)
          tile.show()
          this.hands[localDir].drawTile(tile)
        }
      } else {
        for (let j = 0; j < seat.hand_tile_count; j += 1) {
          const tile = Tile.newInvisible(0)
          tile.hide()
          this.hands[localDir].addRightList(tile)
        }
        if (seat.has_drawn_tile) {
          const tile = Tile.newInvisible(0)
          tile.hide()
          this.hands[localDir].drawTile(tile)
        }
      }
      const nosort = state.last_event_kind === 'predraw'
      if (this.presentationMode === 'replay') {
        this.hands[localDir].updateDisplay(false, true, false, true, !nosort)
      } else {
        this.hands[localDir].updateDisplay(false, false, true, false, !nosort)
      }
    }

    this.applyTileCoverPalette()

    const waitingDiscarderSeat = readWaitingDiscarderSeat(snapshot)
    if (waitingDiscarderSeat !== null) {
      this.lastDiscarderSeat = waitingDiscarderSeat
      this.rivers[transDir(waitingDiscarderSeat, this.selfDir)].setWaitingState(true, false)
    }

    // Display
    this.stateDisplay.setRound(this.round, this.appearance.roundLabelFormat)
    this.applyCenterScores()
    this.stateDisplay.setRemaining(this.remainingTiles)
    this.stateDisplay.setCurrent(transDir(this.currentDir, this.selfDir))
    this.tempDisplay.clear()
    this.tempDisplay.visible = false
    if (state.ended && Array.isArray(state.final_scores)) {
      this.showFinalScores(state.final_scores)
    } else if (snapshot.result_event) {
      this.applySnapshotResult(
        snapshot.result_event,
        typeof state.result_source_actor === 'number' ? state.result_source_actor : null,
      )
    }

    // Reveal game elements
    this.setDirectionLabelsVisible(true)
    this.waitDisplay.visible = this.presentationMode !== 'replay' || this.replayWaitTipsEnabled
    if (this.presentationMode === 'replay') {
      this.countdown.stop()
      this.countdown.visible = false
    }

    this.syncViewerControls(viewer as Record<string, any>)
  }

  /**
   * 国标局中错和续打：本局并未结束，ready 结束后服务端不会下发新局快照，
   * 而是直接继续本局。清除 roundEnded 封禁，并把和牌展示翻回正常状态：
   * 荣和错和的和牌张实际仍在河牌，需从摸牌区移除；他家手牌翻回牌背。
   */
  resumeAfterMidRoundResult(winnerSeat: number, isSelfDraw: boolean): void {
    if (!this.mounted) return
    this.roundEnded = false
    this.countdown.stop()
    const winnerDir = transDir(Number(winnerSeat) || 0, this.selfDir)
    const hand = this.hands[winnerDir]
    if (!hand) return
    if (!isSelfDraw && hand.drawnTile) {
      // 荣和错和：和牌张未真正进入手牌（服务端 hand_tiles 未追加），
      // 展示结束后从摸牌区移除，牌仍保留在河牌。
      const winTile = hand.drawnTile
      hand.drawnTile = null
      winTile.removeFromParent()
      winTile.destroy({ children: true })
    }
    if (winnerDir !== 0) {
      // 他家手牌翻回牌背；自家手牌本身明牌，无需隐藏。
      for (const tile of hand.rightList) tile.hide()
      if (hand.drawnTile) hand.drawnTile.hide()
    }
    this.setViewerWaitInfo(null)
    this.waitDisplay.visible = this.presentationMode !== 'replay' || this.replayWaitTipsEnabled
  }

  /**
   * Handle a game.event payload from the backend.
   * Two-phase dispatch per the spec document Section 24.
   */
  handleEvent(payload: Record<string, any>): void {
    if (!this.mounted) return

    const category: string = payload.category ?? 'transition'
    const event: Record<string, any> = payload.event ?? {}
    const viewer: Record<string, any> = payload.viewer ?? {}
    const seatStatus: Array<Record<string, any>> = payload.seat_status ?? []
    const state: Record<string, any> = payload.state ?? {}
    const kind: string = event.kind ?? ''
    const revealAllHands = Boolean(payload.reveal_all_hands)

    if (this.presentationMode === 'replay') {
      this.clearReplayClaimLabel()
    }

    if (typeof state.stage_counter === 'number' && state.stage_counter > 0) {
      if (this.pendingPassAckStageCounter !== null && this.pendingPassAckStageCounter !== state.stage_counter) {
        this.pendingPassAckStageCounter = null
      }
      this.currentStageCounter = state.stage_counter
    }
    // if (typeof event.ui64_value === 'number') {
    //   this.lastEventUi64Value = event.ui64_value
    // }
    this.rememberViewer(viewer)

    const actorSeat: number = event.actor_seat ?? 0
    const actorDir = transDir(actorSeat, this.selfDir)
    const tile: number | undefined = event.tile

    // Opening flower replacements are part of the initial hand on the server.
    // Merge every replacement tile after the flower round so all 14 dealer
    // tiles use the same layout, matching the no-flower opening path.
    if (kind === 'hand_prompt' && event.opening_buhua_complete) {
      const selfCanDiscard = Array.isArray(viewer.available_actions)
        && viewer.available_actions.some((action: { kind?: string }) => action.kind === 'discard_tile')
      this.openingReplacementTile = selfCanDiscard ? this.hands[0].drawnTile : null
      for (const hand of this.hands) hand.settleDrawnTile()
    }

    // ── Phase A: Board mutation (TRANSITION only) ──────────────────
    if (category === 'transition') {
      switch (kind) {
        case 'start': {
          // New round: clear all tiles and reposition based on the new seat wind
          this.roundEnded = false
          this.pendingPassAckStageCounter = null
          this.openingReplacementTile = null
          this.currentPendingStatus = 'none'
          this.currentViewerActions = []
          this.inputEnabled = false
          this.lastDiscarderSeat = 0
          this.clearPredrawSortTimeout()
          this.clearPendingButton()
          this.clearReplayClaimLabel()
          this.clearMeldChoices()
          this.stateDisplay.clear()
          this.tempDisplay.clear()
          this.tempDisplay.visible = false
          this.countdown.stop()
          this.clearPendingChoicesTimeout()
          this.clearAutoActionTimeout()
          this.hands[0].unwaitDiscard()
          this.setViewerWaitInfo(null)

          // Destroy and recreate hands & rivers
          for (let i = 0; i < 4; i += 1) {
            this.rivers[i].visible = false
            this.hands[i].visible = false
            this.center.removeChild(this.rivers[i])
            this.center.removeChild(this.hands[i])
            this.rivers[i].destroy({ children: true })
            this.hands[i].destroy({ children: true })
          }
          const c = this.center
          this.rivers = [
            this.createRiver(0, c),
            this.createRiver(1, c),
            this.createRiver(2, c),
            this.createRiver(3, c),
          ]
          this.hands = [
            this.createHand(0, c, this.rivers[0], this.waitDisplay),
            this.createHand(1, c, this.rivers[1], null),
            this.createHand(2, c, this.rivers[2], null),
            this.createHand(3, c, this.rivers[3], null),
          ]

          // Viewer seat_index already reflects the new seat after shuffle/rotation
          this.selfDir = viewer.seat_index as number ?? this.selfDir
          this.updateDirectionLabels()

          // Reset scores (names will be repopulated in Phase B from seat_status)
          for (let i = 0; i < 4; i += 1) {
            this.scores[i] = 0
            this.present[i] = true
          }
          this.resetAssistOptions()
          this.playSound('25-xchg')
          break
        }
        case 'predraw': {
          const drawnTiles: number[] = event.drawn_tiles ?? []
          const stage: number = event.ui64_value ?? 0
          const count = stage < 12 ? 4 : 1
          for (let i = 0; i < count; i += 1) {
            const tid = drawnTiles[i] ?? 0
            const tileObj = actorDir === 0 || revealAllHands ? Tile.newInvisible(tid) : Tile.newInvisible(0)
            if (actorDir === 0 || revealAllHands) { tileObj.updateTid(tid); tileObj.show() }
            else { tileObj.hide() }
            this.hands[actorDir].addRightList(tileObj)
          }
          this.hands[actorDir].updateDisplay(false, false, true, false, false)
          if (stage === 15) {
            this.schedulePredrawSort()
          }
          this.playSound('05-draw')
          break
        }
        case 'draw_tile': {
          this.remainingTiles = Math.max(0, this.remainingTiles - 1)
          this.stateDisplay.setRemaining(this.remainingTiles)
          if ((actorDir === 0 || revealAllHands) && tile !== undefined) {
            const t = Tile.newInvisible(tile); t.updateTid(tile); t.show()
            this.hands[actorDir].drawTile(t, Boolean(event.settle_drawn_tile))
          } else {
            const t = Tile.newInvisible(0); t.hide()
            this.hands[actorDir].drawTile(t, Boolean(event.settle_drawn_tile))
          }
          this.countdown.stop()
          this.clearMeldChoices()
          for (const r of this.rivers) r.unwait()
          this.playSound('05-draw')
          break
        }
        case 'discard_tile': {
          this.lastDiscarderSeat = actorSeat
          const useDrawn = event.use_drawn_tile ?? false
          if (actorDir === 0 && tile !== undefined) {
            this.openingReplacementTile = null
            this.hands[0].discardTile(tile, useDrawn)
          } else if (tile !== undefined) {
            this.hands[actorDir].discardTile(tile, useDrawn)
          }
          if (useDrawn && this.presentationMode === 'replay' && this.replayMoqieHintEnabled) {
            const riverTile = this.rivers[actorDir].tileList[this.rivers[actorDir].num - 1]
            riverTile?.setPersistentTint(FROM_DRAWN_TINT)
          }
          this.countdown.stop()
          this.playSound('06-discard')
          break
        }
        case 'flower': {
          if (tile !== undefined) {
            this.hands[actorDir].flowerFromHand(tile, event.use_drawn_tile ?? false)
          }
          this.countdown.stop()
          this.clearMeldChoices()
          if (!event.silent) this.playCallSound('buhua', actorDir, false)
          break
        }
        case 'chow': {
          const discarderRelDir = transDir(this.lastDiscarderSeat, this.selfDir)
          const centralTile = tile ?? 0
          const chowMode = event.ui64_value ?? 0
          this.hands[actorDir].chowFromRiver(this.rivers[discarderRelDir], centralTile, chowMode)
          if (this.presentationMode === 'replay' && !event.silent) {
            this.showReplayClaimLabel(actorDir, tr('吃'), true)
          }
          if (!event.silent) this.playCallSound('chi', actorDir)
          break
        }
        case 'pung': {
          const discarderRelDir = transDir(this.lastDiscarderSeat, this.selfDir)
          const meldFromRel = backendMeldFromRel(actorSeat, this.lastDiscarderSeat)
          const t = tile ?? 0
          this.hands[actorDir].pungFromRiver(this.rivers[discarderRelDir], meldFromRel, t)
          if (this.presentationMode === 'replay' && !event.silent) {
            this.showReplayClaimLabel(actorDir, tr('碰'), true)
          }
          if (!event.silent) this.playCallSound('peng', actorDir)
          break
        }
        case 'melded_kong': {
          const discarderRelDir = transDir(this.lastDiscarderSeat, this.selfDir)
          const t = tile ?? 0
          const meldFromRel = backendMeldFromRel(actorSeat, this.lastDiscarderSeat)
          this.hands[actorDir].meldedKongFromRiver(this.rivers[discarderRelDir], meldFromRel, t)
          if (this.presentationMode === 'replay' && !event.silent) {
            this.showReplayClaimLabel(actorDir, tr('杠'), true)
          }
          if (!event.silent) this.playCallSound('gang', actorDir)
          break
        }
        case 'added_kong': {
          const t = tile ?? 0
          const useDrawn = event.use_drawn_tile ?? false
          if (actorDir === 0) {
            this.hands[0].mKongFromHand(t, useDrawn)
          } else {
            this.hands[actorDir].mKongFromHand(t, useDrawn)
          }
          if (this.presentationMode === 'replay' && !event.silent) {
            this.showReplayClaimLabel(actorDir, tr('杠'), true)
          }
          if (!event.silent) this.playCallSound('gang', actorDir)
          break
        }
        case 'concealed_kong': {
          const t = tile ?? 0
          const useDrawn = event.use_drawn_tile ?? false
          if (actorDir === 0) {
            this.hands[0].cKongFromHand(t, useDrawn)
          } else {
            this.hands[actorDir].cKongFromHand(t, useDrawn)
          }
          if (this.presentationMode === 'replay' && !event.silent) {
            this.showReplayClaimLabel(actorDir, tr('杠'), true)
          }
          if (!event.silent) this.playCallSound('gang', actorDir)
          break
        }
        case 'self_drawn_win': {
          this.roundEnded = true
          if (Array.isArray(event.revealed_hand_tiles)) {
            this.hands[actorDir].revealWinningHand(event.revealed_hand_tiles as number[], tile ?? null)
          }
          if (actorDir === 0) this.waitDisplay.reset()
          this.countdown.stop()
          this.clearMeldChoices()
          if (!event.silent) {
            if (this.presentationMode === 'replay') this.showReplayClaimLabel(actorDir, tr('和'), true)
            else new TempLabel(this.center, actorDir, tr('和'), 1000)
            this.playCallSound('hu', actorDir)
          }
          break
        }
        case 'discard_win':
        case 'rob_added_kong_win': {
          this.roundEnded = true
          if (Array.isArray(event.revealed_hand_tiles)) {
            this.hands[actorDir].revealWinningHand(event.revealed_hand_tiles as number[], tile ?? null)
          }
          if (actorDir === 0) {
            this.waitDisplay.reset()
          }
          this.countdown.stop()
          this.clearMeldChoices()
          if (!event.silent) {
            if (this.presentationMode === 'replay') this.showReplayClaimLabel(actorDir, tr('和'), true)
            else new TempLabel(this.center, actorDir, tr('和'), 1000)
            this.playCallSound('hu', actorDir)
          }
          break
        }
        case 'drawn_game': {
          this.roundEnded = true
          for (const r of this.rivers) r.unwait()
          this.countdown.stop()
          this.clearMeldChoices()
          // Salasasa uses the Vue settlement panel; skip the Pixi center "流局" text.
          if (!event.suppress_result_display) {
            this.tempDisplay.clear()
            this.tempDisplay.addText('drawnGame', tr('流局'), 0, 0, 0, 390, false, 0x000000, true)
            this.tempDisplay.visible = true
          }
          break
        }
        case 'end': {
          // for (const r of this.rivers) r.unwait()
          this.countdown.stop()
          this.clearMeldChoices()
          this.hands[0].unwaitDiscard()
          this.waitDisplay.reset()
          break
        }
      }
    }

    // ── Phase A-alt: TempLabel for CLAIM events ────────────────────
    if (category === 'claim') {
      const labelMap: Record<string, string> = {
        chow: '吃', pung: '碰', melded_kong: '杠', added_kong: '杠',
        concealed_kong: '杠', discard_win: '和', rob_added_kong_win: '和',
        self_drawn_win: '和',
      }
      const text = tr(labelMap[kind])
      if (text) {
        if (this.presentationMode === 'replay') {
          this.showReplayClaimLabel(actorDir, text, true)
        } else {
          new TempLabel(this.center, actorDir, text, 1000)
        }
        if (!event.silent) {
          if (kind === 'chow') this.playCallSound('chi', actorDir, false)
          else if (kind === 'pung') this.playCallSound('peng', actorDir, false)
          else if (kind.includes('kong')) this.playCallSound('gang', actorDir, false)
          else if (kind.includes('win')) this.playCallSound('hu', actorDir, false)
        }
      }
    }

    // ── Phase B: UI update (BOTH categories) ───────────────────────
    // Seat status
    for (const ss of seatStatus) {
      const dir = transDir(ss.seat_index as number, this.selfDir)
      this.names[dir] = displayPlayerName(ss.username)
      if (typeof ss.rank === 'string') this.ranks[dir] = ss.rank || '—'
      this.scores[dir] = ss.score as number ?? 0
      const isActorPresenceEvent =
        (kind === 'player_left' || kind === 'player_resumed') &&
        (ss.seat_index as number) === actorSeat
      if (!isActorPresenceEvent) {
        this.present[dir] = (this.presentationMode === 'replay' && this.replayRecordVersion < 6)
          ? true
          : !((ss.afk as boolean) || Boolean(ss.disconnected))
      }
      this.stateDisplay.setScore(
        this.names[dir],
        this.scores[dir],
        dir,
        !this.present[dir],
        4.0,
        this.seatWindLabel(dir),
      )
      this.rivers[dir]?.setPlayerInfo(this.ranks[dir], this.names[dir], !this.present[dir])
    }
    if (this.scoreDifferenceVisible) {
      this.renderScoreDifferences()
    }
    if (kind === 'player_left') this.handlePlayerLeft(actorSeat)
    if (kind === 'player_resumed') this.handlePlayerResumed(actorSeat)
    // Current direction comes directly from backend state.
    const currentPlayer = readCurrentPlayer(state)
    if (currentPlayer !== null) {
      this.currentDir = currentPlayer
      this.stateDisplay.setCurrent(transDir(this.currentDir, this.selfDir))
    }
    // Round & remaining
    const rc: number = state.round_counter ?? -1
    if (rc >= 0 && rc !== this.round) {
      this.round = rc
      this.stateDisplay.setRound(this.round, this.appearance.roundLabelFormat)
    }
    this.stateDisplay.setRemaining(state.remaining_tile_count ?? this.remainingTiles)
    this.applyTileCoverPalette()
    // Meld choices
    if (category === 'transition') {
      this.clearMeldChoices()
    }
    // For claim events where the viewer is the claimer, clear choices
    if (category === 'claim' && actorDir === 0) {
      this.clearMeldChoices()
    }

    // Don't update viewer controls for player status events (leave/resume),
    // as they don't affect meld choices or available actions.
    if (kind !== 'player_left' && kind !== 'player_resumed') {
      this.syncViewerControls(viewer, tile ?? null, {
        category,
        kind,
        actorSeat,
        drawSource: typeof event.draw_source === 'string' ? event.draw_source : undefined,
      })
    }
    if (kind === 'end' && category === 'transition') {
      this.showFinalScores(Array.isArray(event.scores) ? event.scores as number[] : [])
    }
    // Win result display
    if (event.win && category !== 'claim' && !event.suppress_result_display) {
      this.tempDisplay.clear()
      const win = event.win as Record<string, any>
      const selfDrawn = kind === 'self_drawn_win'
      const winnerName = this.names[transDir(event.actor_seat ?? 0, this.selfDir)] ?? ''
      const shooterSeat = readResultSourceSeat(kind, state, this.lastDiscarderSeat)
      const shooterName = selfDrawn ? '' : (this.names[transDir(shooterSeat, this.selfDir)] ?? '')
      const fan: number = win.win_fan ?? 0
      const fans: string[] = win.win_fans ?? []
      const bp: number = win.win_base_point ?? 0
      // In self-drawn: each loser pays bp; in discard win: shooter pays bp*3
      const eachLoss = selfDrawn ? bp : 0
      const shooterLoss = selfDrawn ? 0 : bp * 3
      this.tempDisplay.handleWin(selfDrawn, winnerName, shooterName, eachLoss, shooterLoss, fan, fans)
      this.tempDisplay.visible = true
      if (hasSingleFanAtLeast(fans, DUANG_CUTOFF, 'guobiao')) {
        setTimeout(() => this.playSound('01-start'), 50)
      }
    }
  }

  playResultGong(fans: string[]): void {
    if (hasSingleFanAtLeast(fans, DUANG_CUTOFF, 'guobiao')) {
      this.playSound('01-start')
    }
  }

  /** Public wrapper for Vue settlement UI (fan reveal ticks, etc.). */
  playUiSound(alias: string): void {
    this.playSound(alias)
  }

  handlePlayerLeft(seat: number): void {
    if (this.presentationMode === 'replay' && this.replayRecordVersion < 6) return
    const dir = transDir(seat, this.selfDir)
    this.present[dir] = false
    this.stateDisplay.setPresent(dir, false)
    this.rivers[dir]?.setPlayerName(this.names[dir], true)
  }

  handlePlayerResumed(seat: number): void {
    if (this.presentationMode === 'replay' && this.replayRecordVersion < 6) return
    const dir = transDir(seat, this.selfDir)
    this.present[dir] = true
    this.stateDisplay.setPresent(dir, true)
    this.rivers[dir]?.setPlayerName(this.names[dir], false)
  }

  applyReplayCue(category: string, event: Record<string, any> | null): void {
    if (this.presentationMode !== 'replay') {
      return
    }

    this.clearReplayClaimLabel()
    if (!event) {
      return
    }

    const kind = typeof event.kind === 'string' ? event.kind : ''
    const actorSeat = typeof event.actor_seat === 'number' ? event.actor_seat : 0
    const actorDir = transDir(actorSeat, this.selfDir)

    if (category === 'claim') {
      const labelMap: Record<string, string> = {
        chow: '吃', pung: '碰', melded_kong: '杠', added_kong: '杠',
        concealed_kong: '杠', discard_win: '和', rob_added_kong_win: '和',
        self_drawn_win: '和',
      }
      const text = tr(labelMap[kind])
      if (text) {
        this.showReplayClaimLabel(actorDir, text, true)
        if (kind === 'chow') this.playCallSound('chi', actorDir, false)
        else if (kind === 'pung') this.playCallSound('peng', actorDir, false)
        else if (kind.includes('kong')) this.playCallSound('gang', actorDir, false)
        else if (kind.includes('win')) this.playCallSound('hu', actorDir, false)
      }
      return
    }

    switch (kind) {
      case 'start':
        this.playSound('25-xchg')
        break
      case 'predraw':
      case 'draw_tile':
        this.playSound('05-draw')
        break
      case 'discard_tile':
        this.playSound('06-discard')
        break
      case 'flower':
        this.playCallSound('buhua', actorDir, false)
        break
      case 'chow':
        this.playCallSound('chi', actorDir)
        break
      case 'pung':
        this.playCallSound('peng', actorDir)
        break
      case 'melded_kong':
      case 'added_kong':
      case 'concealed_kong':
        this.playCallSound('gang', actorDir)
        break
      case 'self_drawn_win':
      case 'discard_win':
      case 'rob_added_kong_win':
        this.playCallSound('hu', actorDir)
        break
      default:
        break
    }
  }

  /** Update pending display (waiting room) with a prepare/unprepare button. */
  showPending(
    seats: { seat_index: number; ready: boolean; username: string | null }[],
    ownReady: boolean,
    onToggleReady: () => void,
  ): void {
    if (!this.mounted) {
      this.deferredSnapshot = null
      this.deferredPending = { seats, ownReady, onToggleReady }
      return
    }

    this.deferredPending = null
  this.pendingPassAckStageCounter = null
    this.currentViewerActions = []
    this.currentPendingStatus = 'none'
    this.inputEnabled = false
    this.clearPendingChoicesTimeout()
    this.clearAutoActionTimeout()
    this.clearPredrawSortTimeout()
    this.clearMeldChoices()
    this.hands[0].unwaitDiscard()
    // Hide game-only elements during pending phase
    this.setDirectionLabelsVisible(false)
    this.countdown.stop()
    this.countdown.visible = false
    this.volDisplay.visible = false
    this.setViewerWaitInfo(null)
    this.waitDisplay.visible = this.presentationMode !== 'replay' || this.replayWaitTipsEnabled

    // Show player list on the temporary center overlay, matching the legacy scene.
    this.stateDisplay.clear()
    this.tempDisplay.clear()
    this.tempDisplay.visible = true
    const entries = seats
      .map((s) => ({ username: typeof s.username === 'string' ? s.username.trim() : '', ready: s.ready }))
      .filter((s) => s.username.length > 0)
      .map((s) => ({ label: s.username, color: s.ready ? 0x000000 : 0x888888 }))
    if (entries.length === 0) {
      this.tempDisplay.addText('pending', tr('等待玩家加入'), 0, 0, 0, 240, false, 0x000000, true)
    } else {
      this.tempDisplay.displayQueue(entries)
    }

    // Prepare/unprepare button at the MeldChoices position
    this.addPendingButton(ownReady, onToggleReady)
  }

  private addPendingButton(ready: boolean, onToggle: () => void): void {
    this.clearPendingButton()

    const btn = new Container()
    btn.x = 5 * TILE_HEIGHT
    btn.y = 6 * TILE_HEIGHT
    btn.scale.set(MELD_OPT_SCALE)

    const bg = new Graphics()
    bg.roundRect(-TILE_WIDTH * 1.2, -TILE_WIDTH / 2, TILE_WIDTH * 2.4, TILE_WIDTH, TILE_RADIUS)
    bg.fill({ color: ready ? 0xf0c5b8 : 0xefdf9f })
    bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    btn.addChild(bg)

    const label = new Text({
      text: tr(ready ? '取消准备' : '准备'),
      style: { fontFamily: getGameFontFamily(), fontSize: 200, fill: 0x000000, align: 'center' },
    })
    label.anchor.set(0.5)
    btn.addChild(label)

    btn.eventMode = 'static'
    btn.cursor = 'pointer'
    btn.on('pointerover', () => { bg.tint = 0xe0e0e0 })
    btn.on('pointerout', () => { bg.tint = 0xffffff })
    btn.on('pointerdown', (e: FederatedPointerEvent) => {
      if (e.button !== 0) return
      onToggle()
    })

    this.center.addChild(btn)
    this.pendingButton = btn
  }

  showRatingResult(
    rankChanged: boolean,
    oldRank: string,
    newRank: string,
    deltaMu: number,
    deltaPoints: number,
  ): void {
    this.clearPendingButton()

    const btn = new Container()
    btn.x = 3.5 * TILE_HEIGHT
    btn.y = 5.4 * TILE_HEIGHT
    btn.scale.set(MELD_OPT_SCALE)

    const twoRows = rankChanged
    const w = TILE_WIDTH * 5.0
    const h = TILE_WIDTH * (twoRows ? 1.3 : 0.72)

    const bg = new Graphics()
    bg.roundRect(-w / 2, -h / 2, w, h, TILE_RADIUS)
    bg.fill({ color: FRONT_COLOR })
    bg.stroke({ color: BORDER_COLOR, width: LINE_WIDTH })
    btn.addChild(bg)

    const textScale = IS_MOBILE_ANY ? 4 : 1

    if (twoRows) {
      const t1 = new Text({
        text: `${oldRank} → ${newRank}`,
        style: { fontFamily: getGameFontFamily(), fontSize: 160 / textScale, fill: 0x000000, align: 'center' },
      })
      t1.anchor.set(0.5, 0.5)
      t1.scale.set(textScale)
      t1.x = 0
      t1.y = -100
      btn.addChild(t1)
    }

    const line2 = `${tr('积分')} ${deltaPoints >= 0 ? '+' : '\u2212'}${Math.abs(deltaPoints).toFixed(2)} (${tr('等级分')} ${deltaMu >= 0 ? '+' : '\u2212'}${Math.abs(deltaMu).toFixed(2)})`
    const t2 = new Text({
      text: line2,
      style: { fontFamily: getGameFontFamily(), fontSize: (twoRows ? 110 : 140) / textScale, fill: twoRows ? 0x333333 : 0x000000, align: 'center' },
    })
    t2.anchor.set(0.5)
    t2.scale.set(textScale)
    t2.x = 0
    t2.y = twoRows ? 100 : 0
    btn.addChild(t2)

    this.center.addChild(btn)
    this.pendingButton = btn
  }

  private showFinalScores(scoresBySeat: number[]): void {
    this.tempDisplay.clear()
    for (let seat = 0; seat < 4; seat += 1) {
      const dir = transDir(seat, this.selfDir)
      this.tempDisplay.setScore(
        this.names[dir] ?? '',
        scoresBySeat[seat] ?? this.scores[dir] ?? 0,
        dir,
        !this.present[dir],
        4.0,
        this.seatWindLabel(dir),
      )
    }
    this.tempDisplay.visible = true
    this.tempDisplay.setRound(-1)
  }

  private applySnapshotResult(
    event: Record<string, any>,
    sourceActorSeat: number | null,
  ): void {
    const kind = event.kind ?? ''
    const actorSeat = typeof event.actor_seat === 'number' ? event.actor_seat : 0
    const actorDir = transDir(actorSeat, this.selfDir)
    const tile = typeof event.tile === 'number' ? event.tile : null

    if (
      (kind === 'self_drawn_win' || kind === 'discard_win' || kind === 'rob_added_kong_win')
      && Array.isArray(event.revealed_hand_tiles)
    ) {
      this.hands[actorDir].revealWinningHand(event.revealed_hand_tiles as number[], tile)
    }

    if (kind === 'drawn_game') {
      if (!event.suppress_result_display) {
        this.tempDisplay.clear()
        this.tempDisplay.addText('drawnGame', tr('流局'), 0, 0, 0, 390, false, 0x000000, true)
        this.tempDisplay.visible = true
      }
      return
    }

    if (!event.win || event.suppress_result_display) {
      return
    }

    this.tempDisplay.clear()
    const win = event.win as Record<string, any>
    const selfDrawn = kind === 'self_drawn_win'
    const winnerName = this.names[actorDir] ?? displayPlayerName(null)
    const shooterIdx = sourceActorSeat ?? this.lastDiscarderSeat
    const shooterName = this.names[transDir(shooterIdx, this.selfDir)] ?? displayPlayerName(null)
    const fan: number = win.win_fan ?? 0
    const fans: string[] = win.win_fans ?? []
    const bp: number = win.win_base_point ?? 0
    const eachLoss = selfDrawn ? bp : 0
    const shooterLoss = selfDrawn ? 0 : bp * 3
    this.tempDisplay.handleWin(selfDrawn, winnerName, shooterName, eachLoss, shooterLoss, fan, fans)
    this.tempDisplay.visible = true
  }

  // ══════════════════════════════════════════════════════════════════
  //  Internal helpers
  // ══════════════════════════════════════════════════════════════════

  private showMeldChoices(
    viewer: { available_actions: Array<{ kind: string; tile?: number; use_drawn_tile?: boolean; ui64_value?: number }> },
    reactionTile: number | null = null,
  ): void {
    this.clearMeldChoices()
    const availableActions = viewer.available_actions
    const nonDiscard = availableActions.filter((a) => a.kind !== 'discard_tile')
    if (nonDiscard.length === 0) return

    this.meldChoicesPanel = new MeldChoices(
      this.center, 'discard', { available_actions: availableActions },
      reactionTile, this.hands[0],
      (action) => {
        this.countdown.stop()
        this.hands[0].unwaitDiscard()
        if (action.kind === 'pass') {
          this.requestPassAction()
          return false
        }
        this.sendGameInput({
          kind: action.kind,
          tile: action.tile,
          use_drawn_tile: action.use_drawn_tile,
          ui64_value: action.ui64_value,
        })
        return true
      },
      () => { this.meldChoicesPanel = null },
    )
    this.playSound('08-inquire')
  }

  /** Hide the meld buttons disabled in the expanded assist panel. Pass and win actions remain available. */
  private viewerActionsAfterMeldFilter(): Array<Record<string, any>> {
    return this.currentViewerActions.filter((action) => {
      if (this.assist.passChi && action.kind === 'chow') return false
      if (this.assist.passPeng && action.kind === 'pung') return false
      if (this.assist.passMingGang && action.kind === 'melded_kong') return false
      return true
    })
  }

  private clearMeldChoices(): void {
    this.meldChoicesPanel?.clear()
    this.meldChoicesPanel = null
  }

  // ══════════════════════════════════════════════════════════════════
  //  Sound
  // ══════════════════════════════════════════════════════════════════

  loadSound(alias: string, audio: HTMLAudioElement): void {
    audio.volume = this.volume
    this.sounds.set(alias, audio)
    // Muted pre-roll is allowed before audible autoplay. Start it as soon as
    // the asset is registered so opening flowers cannot outrun sound loading.
    void this.primeAudio(audio)
  }

  setVolume(volume: number): void {
    this.globalVolume = volume
  }

  /** Trigger a renderer resize (e.g. after the sidebar height changes). */
  forceResize(): void {
    this.scheduleResizeAndLayout()
  }

  // ── 牌桌垂直滑动（手机竖屏：置顶/自由拖动）──────────────────────

  /**
   * 牌桌垂直滑动的可用范围（相对 center.y 基准的偏移，正=向下）。
   * min = 置顶（内容顶边贴到牌桌区上缘），max = 置底（内容底边保持在底部保留区之上）。
   */
  getVerticalSlideLimits(): { min: number; max: number } {
    if (!this.app) {
      return { min: 0, max: 0 }
    }
    const bounds = this.center.getBounds()
    const viewHeight = this.app.screen.height
    const top = bounds.top - this.currentSlide
    const bottom = bounds.bottom - this.currentSlide
    const min = -Math.max(0, top)
    // 底部保留区：给底部操作 Dock 留出空间，避免手牌/辅助方块被盖住
    const bottomReserve = 150
    const max = Math.max(min, viewHeight - bottom - bottomReserve)
    return { min, max }
  }

  /** 设置垂直滑动偏移（自动夹紧），返回实际生效的偏移。 */
  setVerticalSlide(offset: number): number {
    if (!this.app) {
      return 0
    }
    const { min, max } = this.getVerticalSlideLimits()
    this.currentSlide = Math.min(max, Math.max(min, offset))
    this.center.y = this.baseCenterY + this.currentSlide
    return this.currentSlide
  }

  /** 当前垂直滑动偏移。 */
  getVerticalSlide(): number {
    return this.currentSlide
  }

  /** 置顶：牌桌贴到牌桌区最上边缘。 */
  pinTableToTop(): number {
    return this.setVerticalSlide(this.getVerticalSlideLimits().min)
  }

  /** 将客户端坐标换算为舞台坐标（Stage 坐标与 Canvas CSS 像素一致）。 */
  stagePointFromClient(clientX: number, clientY: number): { x: number; y: number } {
    const canvas = this.app?.canvas
    if (!canvas) {
      return { x: 0, y: 0 }
    }
    const rect = canvas.getBoundingClientRect()
    return {
      x: ((clientX - rect.left) / Math.max(rect.width, 1)) * this.app!.screen.width,
      y: ((clientY - rect.top) / Math.max(rect.height, 1)) * this.app!.screen.height,
    }
  }

  /** 该舞台点是否命中可交互对象（牌/按钮/辅助方块），用于区分“空白处拖动牌桌”。 */
  isOverInteractive(x: number, y: number): boolean {
    if (!this.app) {
      return false
    }
    try {
      const eventSystem = (this.app.renderer as unknown as {
        events?: { rootBoundary?: { hitTest?: (px: number, py: number) => unknown } }
      }).events
      if (eventSystem?.rootBoundary?.hitTest?.(x, y) != null) {
        return true
      }
    } catch {
      // fall through to hand-area check
    }
    // 自家手牌区域（含辅助方块）即使当前不可点也视为“非空白”，避免误拖牌桌
    const hand = this.hands?.[0]
    if (hand) {
      try {
        return hand.getBounds().contains(x, y)
      } catch {
        return false
      }
    }
    return false
  }

  handleLatencyPong(identifier: number | null | undefined): void {
    if (typeof identifier !== 'number') return

    const sentAt = this.latencyPingSentAt.get(identifier)
    if (sentAt === undefined) return

    const timeoutId = this.latencyPingTimeouts.get(identifier)
    if (timeoutId !== undefined) {
      clearTimeout(timeoutId)
      this.latencyPingTimeouts.delete(identifier)
    }
    this.latencyPingSentAt.delete(identifier)

    this.missedPongCount = 0
    this.lastPongMs = Math.max(0, Math.round(Date.now() - sentAt))
  }

  private primeAudio(audio: HTMLAudioElement): Promise<void> {
    if (this.primedSounds.has(audio)) return Promise.resolve()
    const pending = this.primingSounds.get(audio)
    if (pending) return pending

    const task = (async () => {
      const prevMuted = audio.muted
      try {
        audio.muted = true
        audio.volume = 0
        await audio.play()
        audio.pause()
        audio.currentTime = 0
        this.primedSounds.add(audio)
      } catch {
        // Browser autoplay policy still blocks this element until a later gesture.
      } finally {
        audio.muted = prevMuted
        audio.volume = this.globalVolume
      }
    })()
    this.primingSounds.set(audio, task)
    void task.finally(() => {
      if (this.primingSounds.get(audio) === task) this.primingSounds.delete(audio)
    })
    return task
  }

  private async primeLoadedSounds(): Promise<void> {
    await Promise.all([...this.sounds.values()].map((audio) => this.primeAudio(audio)))
  }

  private playSound(alias: string): void {
    this.playSoundNow(alias)
  }

  private playSoundNow(alias: string): void {
    const audio = this.sounds.get(alias)
    if (!audio) return
    const play = () => {
      if (this.destroyed) return
      this.playSoundInstance(audio)
    }
    const priming = this.primingSounds.get(audio)
    if (priming) void priming.then(play)
    else play()
  }

  /**
   * Play an independent instance so rapid consecutive calls never rewind one
   * another. This is especially important during the opening flower round,
   * where several buhua voices may arrive before the previous clip finishes.
   */
  private playSoundInstance(source: HTMLAudioElement): void {
    const instance = source.cloneNode(true) as HTMLAudioElement
    instance.volume = this.globalVolume
    instance.muted = false
    instance.currentTime = 0
    this.activeSoundInstances.add(instance)

    let cleaned = false
    const cleanup = () => {
      if (cleaned) return
      cleaned = true
      instance.onended = null
      instance.onerror = null
      this.activeSoundInstances.delete(instance)
    }
    instance.onended = cleanup
    instance.onerror = cleanup
    instance.play().catch(cleanup)
  }

  private stopActiveSounds(): void {
    for (const audio of this.activeSoundInstances) {
      audio.pause()
      audio.currentTime = 0
      audio.onended = null
      audio.onerror = null
    }
    this.activeSoundInstances.clear()
  }

  /** MMCR table effects remain unchanged; only the character speech uses Salasasa voices. */
  private playCallSound(voice: 'chi' | 'peng' | 'gang' | 'buhua' | 'hu', actorDir: number, tableEffect = true): void {
    const tableAudio = tableEffect ? this.sounds.get('09-cpk') : null
    const voiceId = this.voiceIds[actorDir] === 2 ? 2 : 1
    const voiceAudio = this.sounds.get(`voice-${voiceId}-${voice}`)
    if (!voiceAudio) {
      if (tableEffect) this.playSoundNow('09-cpk')
      return
    }

    const play = () => {
      if (this.destroyed) return
      if (tableAudio) this.playSoundInstance(tableAudio)
      this.playSoundInstance(voiceAudio)
    }

    const pending = [tableAudio, voiceAudio]
      .filter((audio): audio is HTMLAudioElement => Boolean(audio))
      .map((audio) => this.primingSounds.get(audio))
      .filter((promise): promise is Promise<void> => Boolean(promise))
    if (pending.length > 0) void Promise.all(pending).then(play)
    else play()
  }
}
