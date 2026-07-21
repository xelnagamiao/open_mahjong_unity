import { Container } from 'pixi.js'
import { TILE_HEIGHT, MELD_OPT_SCALE } from './constants'
import { MeldButton, type MeldButtonType } from './MeldButton'
import { Hand } from './Hand'

/** Viewer action as sent by the backend. */
export interface ViewerAction {
  kind: string
  tile?: number
  use_drawn_tile?: boolean
  ui64_value?: number
}

/** Minimal viewer snapshot for meld choices. */
export interface MeldViewerSnapshot {
  available_actions: ViewerAction[]
}

/**
 * A panel of meld-choice buttons. Appears when the player can claim
 * a tile or declare a kong/win.
 */
export class MeldChoices extends Container {
  private buttons: MeldButton[] = []
  /** Called when the player selects an action. */
  private readonly onAction: (action: ViewerAction) => boolean
  /** Called to clear this panel. */
  private readonly onClear: () => void

  constructor(
    parent: Container,
    context: 'discard' | 'draw' | 'meld' | 'hkong',
    /** Viewer snapshot with available_actions. */
    viewer: MeldViewerSnapshot,
    /** The reaction tile (discarded tile). */
    reactionTile: number | null,
    /** The player's hand (for finding tile indices). */
    hand: Hand,
    onAction: (action: ViewerAction) => boolean,
    onClear: () => void,
  ) {
    super()
    this.onAction = onAction
    this.onClear = onClear

    this.x = 5 * TILE_HEIGHT
    this.y = 6 * TILE_HEIGHT

    this.build(context, viewer, reactionTile, hand)
    parent.addChild(this)
  }

  private build(
    _context: string,
    viewer: MeldViewerSnapshot,
    reactionTile: number | null,
    _hand: Hand,
  ): void {
    let offset = 0
    let offsetLeft = 0

    // Filter and sort actions by priority
    const actionPriority: Record<string, number> = {
      flower: 9, discard_win: 10, rob_added_kong_win: 11, self_drawn_win: 12,
      melded_kong: 13, added_kong: 14, concealed_kong: 15,
      pung: 16, chow: 17, pass: 0, final_pass: 1,
    }

    const sorted = [...viewer.available_actions]
      .filter((a) => a.kind !== 'discard_tile')
      .sort((a, b) => (actionPriority[a.kind] ?? 99) - (actionPriority[b.kind] ?? 99))

    // Reverse chow order: mode 3 first (12<3>), then 2 (1<2>3), then 1 (<1>23)
    // Actually the old code puts chows last in reverse order
    const chowActions = sorted.filter((a) => a.kind === 'chow')
    const otherActions = sorted.filter((a) => a.kind !== 'chow')
    const ordered = [...otherActions, ...chowActions.reverse()]

    // Size = length of ordered list - 1('pass' and 'final_pass' both exists)
    const hasPass = ordered.some((a) => a.kind === 'pass')
    const hasFinalPass = ordered.some((a) => a.kind === 'final_pass')
    const hasBothPass = hasPass && hasFinalPass
    const showFinalPassAsPass = !hasPass && hasFinalPass
    const size = ordered.length - (hasBothPass ? 1 : 0)

    let index = 0

    for (const action of ordered) {
      const useLeft = size >= 4 && index * 2 >= size
      const x = useLeft ? -2 * this.x : 0
      const y = useLeft ? offsetLeft : offset

      const meldType = showFinalPassAsPass && action.kind === 'final_pass'
        ? 'pass'
        : actionKindToMeldType(action.kind)
      const tile = action.tile ?? reactionTile ?? 0
      const chowMode = action.kind === 'chow' ? Math.max(0, (action.ui64_value ?? 1) - 1) : 0
      
      let small = 0
      if (hasBothPass && action.kind === 'pass') {
        small = -1
      }
      if (hasBothPass && action.kind === 'final_pass') {
        small = 1
      }

      const btn = new MeldButton(this, x, y, tile, meldType, chowMode, () => {
        const shouldClear = this.onAction(action)
        if (shouldClear) {
          this.clear()
        }
      }, small)
      this.buttons.push(btn)

      if (!hasBothPass || action.kind !== 'pass') {
        if (useLeft) {
          offsetLeft -= TILE_HEIGHT * MELD_OPT_SCALE
        } else {
          offset -= TILE_HEIGHT * MELD_OPT_SCALE
        }
        index++
      }
    }
  }

  clear(): void {
    for (const btn of this.buttons) {
      btn.visible = false
      this.removeChild(btn)
      btn.destroy({ children: true })
    }
    this.buttons.length = 0
    this.visible = false
    if (this.parent) this.parent.removeChild(this)
    this.onClear()
  }
}

function actionKindToMeldType(kind: ViewerAction['kind']): MeldButtonType {
  switch (kind) {
    case 'chow': return 'chow'
    case 'pung': return 'pung'
    case 'melded_kong': return 'rkong'
    case 'concealed_kong': return 'ckong'
    case 'added_kong': return 'hkong'
    case 'discard_win': return 'rkwin'
    case 'rob_added_kong_win': return 'rkwin'
    case 'self_drawn_win': return 'hwin'
    case 'flower': return 'flower'
    case 'pass': return 'pass'
    case 'final_pass': return 'final_pass'
    default: return 'final_pass'
  }
}
