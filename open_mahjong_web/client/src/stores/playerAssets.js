import { defineStore } from 'pinia'
import playerApi from '@/api/playerClient'
import { usePlayerAuthStore } from './playerAuth'

export const usePlayerAssetsStore = defineStore('playerAssets', {
  state: () => ({ owner: null, epoch: 0, inventory: null, unread: 0, claimable: 0, pending: null }),
  actions: {
    syncOwner() {
      const user = usePlayerAuthStore().userId
      if (user !== this.owner) {
        this.owner = user; this.epoch++; this.inventory = null; this.unread = 0; this.claimable = 0; this.pending = null
        try { this.pending = JSON.parse(sessionStorage.getItem(`account-item-pending:${user}`) || 'null') } catch { /* unavailable */ }
      }
      return this.epoch
    },
    current(epoch) { return epoch === this.epoch && this.owner === usePlayerAuthStore().userId },
    setPending(value) {
      this.pending = value
      try {
        const key = `account-item-pending:${this.owner}`
        if (value) sessionStorage.setItem(key, JSON.stringify(value)); else sessionStorage.removeItem(key)
      } catch { /* request remains available in memory */ }
    },
    async refreshSummary() {
      const epoch = this.syncOwner()
      if (!this.owner) return
      const { data } = await playerApi.get('/mail/summary')
      if (this.current(epoch)) { this.unread = Number(data.data.unread_count); this.claimable = Number(data.data.claimable_count) }
    },
    async refreshInventory() {
      const epoch = this.syncOwner()
      if (!this.owner) return
      const { data } = await playerApi.get('/inventory')
      if (this.current(epoch)) this.applyInventory(data.data)
    },
    applyInventory(state) {
      if (!state || String(state.user_id) !== String(usePlayerAuthStore().userId)) return
      this.inventory = state
      const auth = usePlayerAuthStore()
      auth.renameCardCount = state.rename_card_count; auth.renameCardUsableCount = state.rename_card_usable_count
    },
  },
})
