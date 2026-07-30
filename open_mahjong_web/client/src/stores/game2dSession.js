import { defineStore } from 'pinia'
import { getPlayerToken } from '@/api/playerClient'
import { salasasaClient } from '@/game2d/salasasa/client'
import { preloadGame2dResources } from '@/game2d/game/resources'

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
      const websiteToken = getPlayerToken()
      if (!restorePromise || (websiteToken && !salasasaClient.loginInfo)) {
        this.restoring = true
        restorePromise = preloadGame2dResources()
          .then(() => salasasaClient.restore(websiteToken))
          .finally(() => {
            this.restoring = false
            this.syncFromClient()
          })
      }
      await restorePromise
    },
    async loginWithWebsiteToken(token) {
      await preloadGame2dResources()
      await salasasaClient.connectWithToken(token)
      this.syncFromClient()
    },
    async login(username, password) {
      await preloadGame2dResources()
      await salasasaClient.connect(username, password)
      this.syncFromClient()
    },
    async reconnect() {
      const token = getPlayerToken()
      if (!token) throw new Error('网站登录状态已失效，请重新登录')
      restorePromise = null
      await preloadGame2dResources()
      await salasasaClient.connectWithToken(token)
      this.syncFromClient()
    },
    disconnectForGameExit() {
      salasasaClient.disconnectForGameExit()
      restorePromise = null
      this.syncFromClient()
    },
    logout() {
      salasasaClient.logout()
      restorePromise = null
      this.syncFromClient()
    },
  },
})
