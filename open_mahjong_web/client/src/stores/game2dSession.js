import { defineStore } from 'pinia'
import { ElMessage } from 'element-plus'
import { getPlayerToken } from '@/api/playerClient'
import { salasasaClient } from '@/game2d/salasasa/client'
import { preloadGame2dResources } from '@/game2d/game/resources'

let stateSubscribed = false
let matchSubscribed = false
let restorePromise = null

export const useGame2dSessionStore = defineStore('game2dSession', {
  state: () => ({
    status: salasasaClient.status,
    player: salasasaClient.loginInfo,
    rank: salasasaClient.rankData,
    restoring: true,
    joinedQueue: null,
    matchFound: false,
  }),
  actions: {
    syncFromClient() {
      const prevStatus = this.status
      this.status = salasasaClient.status
      this.player = salasasaClient.loginInfo
      this.rank = salasasaClient.rankData
      if (this.status !== 'online') {
        this.clearMatch()
        return
      }
      if (prevStatus !== 'online') {
        salasasaClient.send({ type: 'match/get_queue_status' })
      }
    },
    ensureSubscriptions() {
      if (!stateSubscribed) {
        salasasaClient.subscribeState(() => this.syncFromClient())
        stateSubscribed = true
      }
      if (!matchSubscribed) {
        salasasaClient.subscribe((message) => this.handleMatchMessage(message))
        matchSubscribed = true
      }
    },
    clearMatch() {
      this.joinedQueue = null
      this.matchFound = false
    },
    noteJoinAttempt(queueKey) {
      this.joinedQueue = queueKey
      this.matchFound = false
    },
    applyServerMatchState(message) {
      if (message.match_committed) {
        this.matchFound = true
        if (message.my_queue) this.joinedQueue = message.my_queue
        return
      }
      if (message.my_queue) {
        this.joinedQueue = message.my_queue
        this.matchFound = false
      }
    },
    handleMatchMessage(message) {
      if (message.type === 'match/queue_status') {
        this.applyServerMatchState(message)
        return
      }
      if (message.type === 'match/join_queue_done') {
        if (message.my_queue) this.joinedQueue = message.my_queue
        else if (message.success === false) this.joinedQueue = null
        if (message.success) ElMessage.success(message.message || '已加入匹配')
        else if (message.message) ElMessage.warning(message.message)
        return
      }
      if (message.type === 'match/leave_queue_done' && message.success) {
        this.clearMatch()
        ElMessage.success(message.message || '已取消匹配')
        return
      }
      if (message.type === 'match/match_found') {
        this.matchFound = true
        ElMessage.success(message.message || '匹配成功，即将开局')
        return
      }
      if (message.type === 'gamestate/guobiao/game_start') {
        this.clearMatch()
        void this.navigateToGameIfNeeded()
      }
    },
    async navigateToGameIfNeeded() {
      const { default: router } = await import('@/router')
      if (router.currentRoute.value.path !== '/2d/game') {
        router.push('/2d/game')
      }
    },
    async init() {
      this.ensureSubscriptions()
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
      this.ensureSubscriptions()
      await preloadGame2dResources()
      await salasasaClient.connectWithToken(token)
      this.syncFromClient()
    },
    async login(username, password) {
      this.ensureSubscriptions()
      await preloadGame2dResources()
      await salasasaClient.connect(username, password)
      this.syncFromClient()
    },
    async reconnect() {
      const token = getPlayerToken()
      if (!token) throw new Error('网站登录状态已失效，请重新登录')
      restorePromise = null
      this.ensureSubscriptions()
      await preloadGame2dResources()
      await salasasaClient.connectWithToken(token)
      this.syncFromClient()
    },
    disconnectForGameExit() {
      this.clearMatch()
      salasasaClient.disconnectForGameExit()
      restorePromise = null
      this.syncFromClient()
    },
    logout() {
      this.clearMatch()
      salasasaClient.logout()
      restorePromise = null
      this.syncFromClient()
    },
  },
})
