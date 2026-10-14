import { defineStore } from 'pinia'
import adminApi, { getAdminToken } from '@/api/adminClient'

export const useAdminNotificationsStore = defineStore('adminNotifications', {
  state: () => ({ counts: { event: 0, base: 0 }, requestVersion: 0 }),
  actions: {
    reset() {
      this.requestVersion += 1
      this.counts = { event: 0, base: 0 }
    },
    async refresh() {
      const token = getAdminToken()
      if (!token) { this.reset(); return }
      const version = ++this.requestVersion
      try {
        const response = await adminApi.get('/event-applications/pending-counts')
        if (version !== this.requestVersion || getAdminToken() !== token) return
        const data = response.data.data
        this.counts = { event: Number(data.event) || 0, base: Number(data.base) || 0 }
      } catch {
        // 暂时失败保留上一次已确认数量，下次刷新重试。
      }
    },
  },
})
