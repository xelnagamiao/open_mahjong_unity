import { onBeforeUnmount, ref } from 'vue'

/**
 * 通用可拖拽/收起侧栏。宽度与收起状态存 localStorage，按 key 区分。
 * side: 'left' 时向右拖拽把手会变宽；'right' 时向左拖拽会变宽。
 */
export function useResizablePanel({ key, defaultWidth = 240, min = 180, max = 420, side = 'left' } = {}) {
  const widthKey = `om-lineage-panel:${key}:w`
  const collapsedKey = `om-lineage-panel:${key}:c`
  const hasStorage = typeof localStorage !== 'undefined'

  function clamp(w) {
    return Math.min(max, Math.max(min, w))
  }

  function readStoredWidth() {
    if (!hasStorage) return defaultWidth
    const n = Number(localStorage.getItem(widthKey))
    return Number.isFinite(n) && n > 0 ? clamp(n) : defaultWidth
  }

  const width = ref(readStoredWidth())
  const collapsed = ref(hasStorage && localStorage.getItem(collapsedKey) === '1')

  let dragging = false
  let startX = 0
  let startW = 0

  function onMove(e) {
    if (!dragging) return
    const dx = e.clientX - startX
    const delta = side === 'left' ? dx : -dx
    width.value = clamp(startW + delta)
  }

  function onUp() {
    if (!dragging) return
    dragging = false
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    if (hasStorage) localStorage.setItem(widthKey, String(Math.round(width.value)))
  }

  function startDrag(e) {
    if (collapsed.value) return
    dragging = true
    startX = e.clientX
    startW = width.value
    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'
    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onUp)
    e.preventDefault()
  }

  function toggleCollapsed() {
    collapsed.value = !collapsed.value
    if (hasStorage) localStorage.setItem(collapsedKey, collapsed.value ? '1' : '0')
  }

  onBeforeUnmount(() => {
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  })

  return { width, collapsed, startDrag, toggleCollapsed }
}
