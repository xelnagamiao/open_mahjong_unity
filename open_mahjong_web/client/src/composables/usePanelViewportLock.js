import { onMounted, onUnmounted } from 'vue'

const LOCK_CLASS = 'panel-viewport-lock'

/**
 * Lock document width to the device viewport while panel layouts are mounted,
 * so wide tables cannot expand the page and force browser zoom-out.
 */
export function usePanelViewportLock() {
  onMounted(() => {
    document.documentElement.classList.add(LOCK_CLASS)
    document.body.classList.add(LOCK_CLASS)
  })

  onUnmounted(() => {
    document.documentElement.classList.remove(LOCK_CLASS)
    document.body.classList.remove(LOCK_CLASS)
  })
}
