import { defineStore } from 'pinia'
import { salasasaClient } from '@/game2d/salasasa/client'

let stateSubscribed = false
let restorePromise = null

export const useGame2dSessionStore = defineStore('game2dSession', {
  state: () => ({
    status: salasasaClient.status,
    player: salasasaClient.loginInfo,
    rank: salasasaClient.rankData,
    restoring: true,
  }),
  actions: {
    syncFromClient() {
      this.status = salasasaClient.status
      this.player = salasasaClient.loginInfo
      this.rank = salasasaClient.rankData
    },
    async init() {
      if (!stateSubscribed) {
        salasasaClient.subscribeState(() => this.syncFromClient())
        stateSubscribed = true
      }
      if (!restorePromise) {
        this.restoring = true
        restorePromise = salasasaClient.restore()
          .finally(() => {
            this.restoring = false
            this.syncFromClient()
          })
      }
      await restorePromise
    },
    async login(username, password) {
      await salasasaClient.connect(username, password)
      this.syncFromClient()
    },
    logout() {
      salasasaClient.logout()
      this.syncFromClient()
    },
  },
})
