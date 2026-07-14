import { reactive, ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import {
  TILE_NAME,
  parseNotationText,
  tilesToNotationText,
  parseMeldSlotInput,
  meldDisplayTiles,
  tileIdToNotation,
} from '@/composables/useMahjongTiles'

export const FULU_SLOT_COUNT = 4
export const FULU_SLOT_HINTS = ['123m', '333p', '3333s', '1111z']

export function createEmptyFuluSlot() {
  return {
    input: '',
    options: [],
    locked: null,
  }
}

/** 把点选的牌序列转成副露简写（同花色连写） */
export function tilesToMeldNotation(tiles) {
  if (!tiles?.length) return ''
  try {
    return tilesToNotationText(tiles)
  } catch {
    return tiles.map(tileIdToNotation).join('')
  }
}

export function tryParseMeldTiles(text) {
  if (!text?.trim()) return []
  try {
    return parseNotationText(text)
  } catch {
    return null
  }
}

/**
 * @param {object} [opts]
 * @param {(meld: object, excludeIdx: number) => number|null} [opts.checkOverflow]
 *   返回超限的 tileId，或 null
 * @param {() => void} [opts.onLocked]
 */
export function useFuluSlots(opts = {}) {
  const slots = reactive(
    Array.from({ length: FULU_SLOT_COUNT }, () => createEmptyFuluSlot())
  )
  const activeIdx = ref(-1)

  const lockedList = computed(() =>
    slots.filter((s) => s.locked).map((s) => s.locked)
  )
  const lockedCount = computed(() => lockedList.value.length)

  const buildLockedMeld = (opt, input) => ({
    kind: opt.kind,
    tileId: opt.tileId,
    label: opt.label,
    code: `${opt.kind}${opt.tileId}`,
    input,
    displayTiles: meldDisplayTiles(opt.kind, opt.tileId),
  })

  const clearSlot = (idx) => {
    const slot = slots[idx]
    slot.input = ''
    slot.options = []
    slot.locked = null
    if (activeIdx.value === idx) activeIdx.value = idx
  }

  const resetAll = () => {
    for (let i = 0; i < FULU_SLOT_COUNT; i++) clearSlot(i)
    activeIdx.value = -1
  }

  const activate = (idx) => {
    if (slots[idx].locked) return
    activeIdx.value = idx
  }

  const activateHand = () => {
    activeIdx.value = -1
  }

  const lockSlot = (idx, opt) => {
    const slot = slots[idx]
    const meld = buildLockedMeld(opt, slot.input)
    if (opts.checkOverflow) {
      const overflow = opts.checkOverflow(meld, idx)
      if (overflow) {
        ElMessage.warning(`牌 ${TILE_NAME[overflow]} 已达 4 张上限`)
        return false
      }
    }
    slot.locked = meld
    slot.options = []
    if (activeIdx.value === idx) activeIdx.value = -1
    opts.onLocked?.()
    return true
  }

  const refreshSlotParse = (idx) => {
    const slot = slots[idx]
    if (slot.locked) return
    const { auto, options } = parseMeldSlotInput(slot.input)
    slot.options = options
    if (auto) lockSlot(idx, auto)
  }

  const onSlotInput = (idx) => {
    refreshSlotParse(idx)
  }

  /** 向当前激活副露槽追加一张牌；成功返回 true */
  const appendTileToActive = (tileId) => {
    if (tileId >= 51 && tileId <= 58) {
      ElMessage.warning('副露不能使用花牌')
      return false
    }
    const idx = activeIdx.value
    if (idx < 0 || idx >= FULU_SLOT_COUNT) return false
    const slot = slots[idx]
    if (slot.locked) return false

    let tiles = tryParseMeldTiles(slot.input)
    if (tiles == null) {
      ElMessage.warning('请先清空当前副露简写，再点选牌面')
      return true
    }
    if (tiles.length >= 4) {
      ElMessage.warning('副露最多 4 张')
      return true
    }
    tiles = [...tiles, tileId]
    slot.input = tilesToMeldNotation(tiles)
    refreshSlotParse(idx)
    return true
  }

  const removeDraftTile = (idx, tileIdx) => {
    const slot = slots[idx]
    if (slot.locked) return
    const tiles = tryParseMeldTiles(slot.input)
    if (!tiles || tileIdx < 0 || tileIdx >= tiles.length) return
    tiles.splice(tileIdx, 1)
    slot.input = tilesToMeldNotation(tiles)
    refreshSlotParse(idx)
  }

  /** 已锁定副露中点掉一张：解锁并回到草稿 */
  const removeLockedTile = (idx, tileIdx) => {
    const slot = slots[idx]
    if (!slot.locked) return
    const tiles = [...(slot.locked.displayTiles || [])]
    if (tileIdx < 0 || tileIdx >= tiles.length) return
    tiles.splice(tileIdx, 1)
    slot.locked = null
    slot.options = []
    slot.input = tilesToMeldNotation(tiles)
    activeIdx.value = idx
    refreshSlotParse(idx)
  }

  return {
    slots,
    activeIdx,
    lockedList,
    lockedCount,
    activate,
    activateHand,
    clearSlot,
    resetAll,
    lockSlot,
    onSlotInput,
    appendTileToActive,
    removeDraftTile,
    removeLockedTile,
    FULU_SLOT_HINTS,
  }
}
