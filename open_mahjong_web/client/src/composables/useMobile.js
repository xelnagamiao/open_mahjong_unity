import { ref, onMounted, onUnmounted } from 'vue'

const MOBILE_QUERY = '(max-width: 768px)'

/**
 * Reactive match for mobile breakpoint (max-width: 768px).
 */
export function useMobile() {
  const isMobile = ref(false)

  function update() {
    if (typeof window === 'undefined') return
    isMobile.value = window.matchMedia(MOBILE_QUERY).matches
  }

  onMounted(() => {
    update()
    window.addEventListener('resize', update)
  })

  onUnmounted(() => {
    window.removeEventListener('resize', update)
  })

  return { isMobile }
}
