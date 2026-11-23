import { nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

// The admin panel scrolls its own main element, outside Vue Router's window
// scroll restoration. Rows may arrive after the destination has mounted.
export function useAdminScroll(content) {
  const route = useRoute()
  const history = useRouter().options.history
  const positions = new Map()
  let entry = history.state.position

  watch(() => route.fullPath, async (path, previous, onCleanup) => {
    const main = content.value?.parentElement
    if (!main) return
    if (main.scrollTop || main.scrollLeft) {
      positions.set(entry, { path: previous, top: main.scrollTop, left: main.scrollLeft })
    } else {
      positions.delete(entry)
    }
    entry = history.state.position
    const saved = positions.get(entry)
    const returning = saved?.path === path
    const controller = new AbortController()
    let observer
    function stop() {
      observer?.disconnect()
      controller.abort()
    }
    onCleanup(stop)
    await nextTick()
    if (controller.signal.aborted) return
    main.scrollTo(0, 0)
    if (!returning) return

    function restore() {
      main.scrollTo(saved.left, saved.top)
      if (Math.abs(main.scrollTop - saved.top) < 1 && Math.abs(main.scrollLeft - saved.left) < 1) stop()
    }
    for (const event of ['wheel', 'touchstart', 'pointerdown', 'keydown']) {
      main.addEventListener(event, stop, { signal: controller.signal, passive: true })
    }
    observer = new ResizeObserver(restore)
    observer.observe(content.value)
    restore()
  })
}
