import { useRouter } from 'vue-router'
import { useAdminAuthStore } from '@/stores/adminAuth'

// Each visit has its own submitted filters. Re-query on return so edits in a
// detail view are reflected without persisting account records in the browser.
export function useAdminListState(section = 'list') {
  const router = useRouter()
  const history = router.options.history
  const path = router.currentRoute.value.fullPath
  const position = history.state.position
  const owner = useAdminAuthStore().userId
  const read = () => {
    const entry = history.state.adminLists
    return entry?.owner === owner && entry.path === path ? entry.sections : {}
  }
  function remember(filters) {
    if (history.state.position !== position || history.location !== path) return
    history.replace(path, {
      adminLists: { owner, path, sections: { ...read(), [section]: filters } },
    })
  }
  return { saved: read()[section] || {}, remember }
}

export function returnToAdminList(router, fallback) {
  const previous = router.options.history.state.back
  if (previous && router.resolve(previous).meta.requiresAdmin) router.back()
  else router.replace(fallback)
}
