<template>
  <div
    ref="layerElement"
    class="replay-independent-wall"
    :class="{ 'is-compact': compact }"
    @click.stop
    @contextmenu.stop
    @wheel.stop
    @keydown.stop
  >
    <button
      ref="toggleElement"
      type="button"
      class="replay-independent-wall__toggle"
      :class="{ 'is-active': open }"
      :aria-expanded="open"
      :aria-pressed="open"
      aria-controls="replay-independent-wall-panel"
      @click="open = !open"
    >
      侧边牌山
    </button>
    <section
      v-if="open"
      id="replay-independent-wall-panel"
      ref="panelElement"
      class="replay-independent-wall__panel"
      :style="{
        transform: `translate(${position.x}px, ${position.y}px)`,
        width: `${fit.width}px`,
        maxHeight: `${fit.height}px`,
        '--wall-tile-width': `${fit.tileWidth}px`,
        '--wall-group-columns': fit.columns,
      }"
      aria-label="侧边牌山"
      @keydown.esc.prevent="open = false"
    >
      <header class="replay-independent-wall__header">
        <button
          ref="handleElement"
          type="button"
          class="replay-independent-wall__handle"
          aria-label="移动侧边牌山，方向键调整位置"
          title="拖动调整位置，也可用方向键移动"
          @pointerdown="startDrag"
          @pointermove="moveDrag"
          @pointerup="endDrag"
          @pointercancel="endDrag"
          @lostpointercapture="drag = null"
          @keydown="moveWithKeyboard"
        >
          余 {{ remaining }}/{{ tiles.length }}
        </button>
        <span v-if="hintsEnabled" class="replay-wall__legend">
          <i class="is-danger" title="铳张" aria-label="铳张">铳</i><i class="is-predicted" title="摸牌预测" aria-label="摸牌预测">预</i>
        </span>
      </header>
      <div
        ref="scrollElement"
        class="replay-independent-wall__scroll"
        role="region"
        aria-label="完整牌山，已摸牌淡化，可滚动查看"
        tabindex="0"
      >
        <div class="replay-independent-wall__rows">
          <div
            v-for="(row, rowIndex) in rows"
            :key="rowIndex"
            class="replay-wall__row"
            :aria-label="`第 ${rowIndex * 4 + 1} 至 ${rowIndex * 4 + row.length} 张`"
          >
            <span
              v-for="(item, offset) in row"
              :key="offset"
              :class="{
                'is-consumed': item.consumed,
                'is-danger': item.isDanger,
                'is-predicted': item.isPredicted,
              }"
            >
              <img :src="tileAsset(item.tile)" :alt="`第 ${rowIndex * 4 + offset + 1} 张${item.consumed ? '，已摸' : ''}`" />
            </span>
          </div>
        </div>
        <span v-if="!tiles.length" class="replay-independent-wall__empty">暂无牌山</span>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ReplayWallTile } from '@/game2d/replay/recordReplay'

const props = defineProps<{
  tiles: (ReplayWallTile & { isDanger: boolean, isPredicted: boolean })[]
  remaining: number
  hintsEnabled: boolean
  tileAsset: (tile: number) => string
  roundIndex: number
  stageElement: HTMLElement | null
}>()

const open = ref(true)
const compact = ref(false)
const fit = ref({ width: 112, height: 560, tileWidth: 20, columns: 1 })
const position = ref({ x: 0, y: 0 })
const layerElement = ref<HTMLElement | null>(null)
const panelElement = ref<HTMLElement | null>(null)
const scrollElement = ref<HTMLElement | null>(null)
const toggleElement = ref<HTMLButtonElement | null>(null)
const handleElement = ref<HTMLButtonElement | null>(null)
const PANEL_TOP = 38
const PANEL_CHROME = 44 // 30px header, 12px content padding, 2px border
const MIN_TILE_WIDTH = 16
let observer: ResizeObserver | null = null
let drag: { pointerId: number, x: number, y: number, startX: number, startY: number } | null = null

const rows = computed(() => {
  const result: typeof props.tiles[] = []
  for (let index = 0; index < props.tiles.length; index += 4) result.push(props.tiles.slice(index, index + 4))
  return result
})

function moveTo(x: number, y: number) {
  const layer = layerElement.value
  const panel = panelElement.value
  if (!layer || !panel) return
  position.value = {
    x: Math.max(0, Math.min(x, layer.clientWidth - panel.offsetWidth)),
    y: Math.max(0, Math.min(y, layer.clientHeight - PANEL_TOP - panel.offsetHeight,
      window.innerHeight - 12 - layer.getBoundingClientRect().top - PANEL_TOP - panel.offsetHeight)),
  }
}

async function updateLayout() {
  const stage = props.stageElement
  const layer = layerElement.value
  if (!stage || !layer) return
  // Use the same 95% square footprint as MahjongScene; never resize the table to fit this panel.
  const leftSpace = (stage.clientWidth - Math.min(stage.clientWidth, stage.clientHeight) * 0.95) / 2
  const nextCompact = window.matchMedia('(max-width: 560px)').matches
  if (compact.value !== nextCompact) {
    compact.value = nextCompact
    position.value = { x: 0, y: 0 }
    drag = null
  }
  const groupCount = Math.max(1, rows.value.length)
  const layerBounds = layer.getBoundingClientRect()
  const panelTop = layerBounds.top + PANEL_TOP
  const availableHeight = Math.max(0, Math.min(layer.clientHeight - PANEL_TOP, window.innerHeight - panelTop - 12))
  const availableWidth = Math.min(layer.clientWidth, nextCompact ? 660 : Math.max(112, leftSpace - 20))
  let regions = [{ width: availableWidth, height: availableHeight }]
  const tools = stage.parentElement?.querySelector<HTMLElement>('.replay-board-tools')
  if (nextCompact && tools) {
    // Keep the default panel entirely above or to the left of the existing tool buttons.
    const toolsBounds = tools.getBoundingClientRect()
    regions = [
      { width: availableWidth, height: Math.max(0, Math.min(availableHeight, toolsBounds.top - panelTop - 8)) },
      { width: Math.max(0, Math.min(availableWidth, toolsBounds.left - layerBounds.left - 8)), height: availableHeight },
    ]
  }
  let best = { tileWidth: 0, columns: 1, rowCount: groupCount, region: regions[0] }
  for (const region of regions) {
    const contentHeight = Math.max(0, region.height - PANEL_CHROME - 2)
    // On phones, spread groups across the available width to keep the floating panel shallow.
    const columnOptions = nextCompact
      ? [Math.max(1, Math.min(groupCount, Math.floor((region.width - 14 + 6) / (4 * MIN_TILE_WIDTH + 3 + 6))))]
      : [1, 2].filter(columns => columns <= groupCount)
    for (const columns of columnOptions) {
      const rowCount = Math.ceil(groupCount / columns)
      const widthLimit = (region.width - 14 - columns * 3 - (columns - 1) * 6) / (4 * columns)
      const heightLimit = (contentHeight - rowCount + 1) * 3 / (4 * rowCount)
      const tileWidth = Math.floor(Math.min(widthLimit, heightLimit, 40) * 2) / 2
      // Use two desktop columns only when the free side space makes the tiles larger.
      if (tileWidth > best.tileWidth || (nextCompact && tileWidth === best.tileWidth && rowCount < best.rowCount)) {
        best = { tileWidth, columns, rowCount, region }
      }
    }
  }
  // Scrolling is a fallback only when the complete wall cannot fit at a readable minimum.
  const needsScroll = best.tileWidth < MIN_TILE_WIDTH
  const tileWidth = Math.max(MIN_TILE_WIDTH, best.tileWidth)
  const columns = needsScroll
    ? Math.max(1, Math.min(best.columns, Math.floor((best.region.width - 24 + 6) / (4 * tileWidth + 3 + 6))))
    : best.columns
  fit.value = {
    tileWidth,
    columns,
    width: Math.min(best.region.width, Math.max(112, columns * (tileWidth * 4 + 3) + (columns - 1) * 6 + 14 + (needsScroll ? 10 : 0))),
    height: best.region.height,
  }
  await nextTick()
  if (!needsScroll && scrollElement.value) {
    scrollElement.value.scrollTop = 0
    scrollElement.value.scrollLeft = 0
  }
  moveTo(position.value.x, position.value.y)
}

function startDrag(event: PointerEvent) {
  if (event.button !== 0) return
  drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, startX: position.value.x, startY: position.value.y }
  const handle = event.currentTarget as HTMLElement
  handle.setPointerCapture(event.pointerId)
}

function moveDrag(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.pointerId) return
  moveTo(drag.startX + event.clientX - drag.x, drag.startY + event.clientY - drag.y)
}

function endDrag(event: PointerEvent) {
  if (!drag || event.pointerId !== drag.pointerId) return
  drag = null
  const handle = event.currentTarget as HTMLElement
  if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId)
}

function moveWithKeyboard(event: KeyboardEvent) {
  const offsets: Record<string, [number, number]> = {
    ArrowLeft: [-12, 0], ArrowRight: [12, 0], ArrowUp: [0, -12], ArrowDown: [0, 12],
  }
  const offset = offsets[event.key]
  if (!offset) return
  event.preventDefault()
  moveTo(position.value.x + offset[0], position.value.y + offset[1])
}

watch(open, async (visible) => {
  drag = null
  await updateLayout()
  const focusTarget = visible ? handleElement.value : toggleElement.value
  focusTarget?.focus({ preventScroll: true })
})
watch([() => props.hintsEnabled, () => props.tiles.length], () => { void updateLayout() })
watch(() => props.roundIndex, () => {
  if (scrollElement.value) {
    scrollElement.value.scrollTop = 0
    scrollElement.value.scrollLeft = 0
  }
})

onMounted(() => {
  observer = new ResizeObserver(() => { void updateLayout() })
  if (layerElement.value) observer.observe(layerElement.value)
  if (props.stageElement) observer.observe(props.stageElement)
  const tools = props.stageElement?.parentElement?.querySelector('.replay-board-tools')
  if (tools) observer.observe(tools)
  window.addEventListener('resize', updateLayout)
  void updateLayout()
})
onBeforeUnmount(() => {
  observer?.disconnect()
  window.removeEventListener('resize', updateLayout)
  drag = null
})
</script>

<style>
.replay-independent-wall {
  position: absolute;
  z-index: 38;
  inset: 12px 12px 82px;
  pointer-events: none;
  font-family: system-ui, "Microsoft YaHei", sans-serif;
}
.replay-independent-wall button {
  border: 1px solid rgba(255, 255, 255, 0.35);
  border-radius: 3px;
  background: #252a2d;
  color: #f4f5f6;
  font: 600 11px/1.2 system-ui, "Microsoft YaHei", sans-serif;
  cursor: pointer;
}
.replay-independent-wall .replay-independent-wall__toggle {
  display: block;
  min-height: 32px;
  padding: 0 10px;
  background: #181b1d;
  pointer-events: auto;
}
.replay-independent-wall .replay-independent-wall__toggle.is-active {
  border-color: #6da5e8;
  background: rgba(42, 91, 148, 0.98);
}
.replay-independent-wall button:hover { border-color: #6da5e8; }
.replay-independent-wall__panel {
  position: absolute;
  top: 38px;
  left: 0;
  display: flex;
  flex-direction: column;
  max-width: 100%;
  overflow: hidden;
  border: 1px solid var(--replay-line);
  border-radius: 6px;
  background: var(--replay-panel);
  box-shadow: 0 5px 18px rgba(0, 0, 0, 0.3);
  box-sizing: border-box;
  pointer-events: auto;
}
.replay-independent-wall__header {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  height: 30px;
  gap: 3px;
  padding: 3px 4px;
  box-sizing: border-box;
  border-bottom: 1px solid var(--replay-line);
}
.replay-independent-wall .replay-independent-wall__handle {
  min-width: 0;
  min-height: 22px;
  flex: 1 1 auto;
  padding: 0;
  border-color: transparent;
  background: transparent;
  cursor: grab;
  touch-action: none;
  user-select: none;
  white-space: nowrap;
  text-align: left;
}
.replay-independent-wall .replay-independent-wall__handle:active { cursor: grabbing; }
.replay-independent-wall .replay-wall__legend { flex: 0 0 auto; gap: 3px; margin-left: 0; }
.replay-independent-wall .replay-wall__legend i { padding: 1px 3px; font-size: 10px; }
.replay-independent-wall__scroll {
  min-width: 0;
  min-height: 0;
  padding: 6px;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-width: thin;
  scrollbar-color: #60686c #24292c;
  touch-action: pan-y;
}
.replay-independent-wall__rows {
  display: grid;
  grid-template-columns: repeat(var(--wall-group-columns), max-content);
  justify-content: center;
  gap: 1px 6px;
}
.replay-independent-wall .replay-wall__row { gap: 1px; }
.replay-independent-wall .replay-wall__row > span {
  width: var(--wall-tile-width);
  height: calc(var(--wall-tile-width) * 4 / 3);
  flex-basis: var(--wall-tile-width);
  box-sizing: border-box;
}
.replay-independent-wall__empty { color: #aeb5b8; font-size: 11px; }
.replay-independent-wall.is-compact { inset: 8px 8px 82px; }
</style>
