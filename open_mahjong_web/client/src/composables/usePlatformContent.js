import { inject, onMounted, shallowRef, ref } from 'vue'
const content = shallowRef(null), error = ref('')
let pending
export async function refreshPlatformContent() {
  if (pending) return pending
  pending = (async () => {
    try {
      const response = await fetch('/api/platform/content', { cache: 'no-cache' })
      if (!response.ok) throw new Error('内容暂时无法加载')
      const data = await response.json()
      if (data.schemaVersion !== 1 || !Array.isArray(data.credits) || !Array.isArray(data.communities)) throw new Error('内容格式不正确')
      content.value = data; error.value = ''
    } catch (failure) { error.value = content.value ? '连接失败，已保留上次内容' : failure.message }
    finally { pending = null }
  })()
  return pending
}
export function usePlatformContent() {
  // Keep SSR content per render; never leak a previous build's content through the module cache.
  const initialContent = inject('platform-content', null)
  onMounted(refreshPlatformContent)
  return { content: initialContent ? shallowRef(initialContent) : content, error, refresh: refreshPlatformContent }
}
