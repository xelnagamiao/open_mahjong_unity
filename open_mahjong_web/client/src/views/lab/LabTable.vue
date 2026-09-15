<template>
  <div
    class="lab-table mahjongGame replay-page"
    :class="{ 'is-black-tile-face': appearance.tileFaceTheme === 'black', 'is-empty': !hasSnapshot }"
    :style="{ background: appearance.backgroundColorTable }"
  >
    <div
      ref="stageElement"
      class="game-stage"
      :class="{ 'is-interactive': interactive }"
      @click.capture="onStageClick"
      @contextmenu.capture="onStageContext"
      @wheel.capture="onStageWheel"
    />
  </div>
</template>

<script>
import { MahjongScene } from '@/game2d/game/scene/MahjongScene'
import { GAME_SOUND_ASSETS, getPreloadedSoundUrl, preloadGame2dResources } from '@/game2d/game/resources'
import { loadStoredSceneAppearance, loadStoredVolume } from '@/game2d/lib/storage'
import { loadStoredSceneBackgroundImage } from '@/game2d/lib/sceneBackgroundImage'
import {
  buildUnitySceneSnapshot,
  emptyUnitySceneSnapshot,
  encodeUnityAction,
  hasUnitySnapshot,
  salasasaTileToMmcr,
  waitDataFromTips,
} from './labUnitySnapshot'

const MANUAL_ASSIST = {
  autoFlower: false,
  autoFlowerOnMatchStart: false,
  autoDiscard: false,
  autoWin: false,
  autoPass: false,
  passChi: false,
  passPeng: false,
  passMingGang: false,
  noRon: false,
  noTsumo: false,
  noRobKong: false,
  silentTiles: [],
  silentSkipTsumo: true,
  confirmDiscard: false,
}

function loadAppearance() {
  try {
    return loadStoredSceneAppearance()
  } catch {
    return { backgroundColorTable: '#097fa0', tileFaceTheme: 'regular' }
  }
}

export default {
  name: 'LabTable',
  props: {
    components: { type: Object, default: () => ({}) },
    legal: { type: Object, default: () => ({}) },
    dump: { type: Object, default: () => ({}) },
    interactive: { type: Boolean, default: false },
    showOtherHands: { type: Boolean, default: false },
    atLastFrame: { type: Boolean, default: false },
    chongHint: { type: Boolean, default: false },
    waitingTiles: { type: Array, default: () => [] },
  },
  emits: ['action', 'board-step'],
  data() {
    return {
      appearance: loadAppearance(),
      sceneReady: false,
    }
  },
  computed: {
    hasSnapshot() {
      return hasUnitySnapshot(this.components)
    },
    snapshotKey() {
      return JSON.stringify({
        interactive: this.interactive,
        showOtherHands: this.showOtherHands,
        atLastFrame: this.atLastFrame,
        chongHint: this.chongHint,
        waitingTiles: this.waitingTiles,
        unity: this.components,
        legal: this.legal,
        dumpHands: ((this.dump && this.dump.players) || []).map((player) => [
          player.index,
          player.hand_tiles,
          player.has_draw_slot,
          player.last_drawn_tile,
        ]),
      })
    },
  },
  watch: {
    snapshotKey() {
      this.applyScene()
    },
  },
  async mounted() {
    await this.mountScene()
  },
  beforeUnmount() {
    this.destroyScene()
  },
  methods: {
    onStageClick(event) {
      if (this.interactive) return
      event.preventDefault()
      this.$emit('board-step', 1)
    },
    onStageContext(event) {
      event.preventDefault()
      if (this.interactive) return
      this.$emit('board-step', -1)
    },
    onStageWheel(event) {
      event.preventDefault()
      if (this.interactive || Math.abs(event.deltaY) < 1) return
      this.$emit('board-step', event.deltaY > 0 ? 1 : -1)
    },
    async mountScene() {
      if (this.scene) return
      await preloadGame2dResources()
      const scene = new MahjongScene((type, payload) => this.onSceneInput(type, payload))
      scene.setPresentationMode(this.interactive ? 'game' : 'replay')
      scene.setReplayRecordVersion(6)
      scene.setReplayMoqieHintEnabled(true)
      scene.setReplayWaitTips(null)
      scene.setVolume(loadStoredVolume())
      scene.setAppearance({ ...this.appearance, forcePassEnabled: true })
      scene.setAssistSettings(MANUAL_ASSIST)
      for (const sound of GAME_SOUND_ASSETS) {
        const audio = new Audio(getPreloadedSoundUrl(sound.file))
        audio.preload = 'auto'
        audio.load()
        scene.loadSound(sound.alias, audio)
      }
      this.scene = scene
      const host = this.$refs.stageElement
      if (!host) return
      const mounted = await scene.mount(host)
      if (!mounted || this.scene !== scene) return
      try {
        const background = await loadStoredSceneBackgroundImage()
        scene.setBackgroundImage(background?.dataUrl ?? null)
      } catch {
        scene.setBackgroundImage(null)
      }
      this.sceneReady = true
      await this.$nextTick()
      scene.forceResize()
      this.applyScene()
      requestAnimationFrame(() => {
        if (this.scene === scene) scene.forceResize()
      })
    },
    destroyScene() {
      this.sceneReady = false
      this.scene?.destroy()
      this.scene = null
    },
    applyScene() {
      const scene = this.scene
      if (!scene || !this.sceneReady) return
      scene.setPresentationMode(this.interactive ? 'game' : 'replay')
      scene.setAppearance({ ...this.appearance, forcePassEnabled: true })
      scene.setAssistSettings(MANUAL_ASSIST)
      const snapshot = buildUnitySceneSnapshot({
        components: this.components,
        legal: this.legal,
        dump: this.dump,
        interactive: this.interactive,
        showOtherHands: this.showOtherHands,
        atLastFrame: this.atLastFrame,
      }) || emptyUnitySceneSnapshot()
      scene.flushFromSnapshot(snapshot)
      const danger = new Map()
      if (this.chongHint) {
        const tiles = new Set((this.waitingTiles || []).map((tile) => salasasaTileToMmcr(Number(tile) || 0)).filter(Boolean))
        if (tiles.size) {
          for (const seat of snapshot.seats || []) {
            if (seat.seat_index !== snapshot.viewer.seat_index) danger.set(seat.seat_index, tiles)
          }
        }
      }
      scene.setReplayDangerTiles(danger.size ? danger : null)
      if (!this.interactive) {
        scene.setReplayWaitTips(waitDataFromTips(this.components.TipsSim))
      }
      scene.forceResize()
    },
    onSceneInput(type, payload) {
      if (type !== 'game.input' || !this.interactive) return
      const gsm = this.components.GsmSim || {}
      const selfIndex = Number(gsm.selfIndex || 0)
      const seatLegal = ((this.legal && this.legal.seats) || {})[String(selfIndex)] || {}
      const actions = seatLegal.client_actions || gsm.allowActionList || []
      const encoded = encodeUnityAction(payload, {
        claimTile: seatLegal.claim_tile ?? gsm.currentAskCutTileId ?? gsm.lastCutCardID,
        huSelf: actions.includes('hu_self')
          ? 'hu_self'
          : (actions.includes('hu_flower') ? 'hu_flower' : 'hu_self'),
        huClaim: actions.find((item) => String(item).startsWith('hu')) || 'hu',
      })
      if (!encoded) return
      this.$emit('action', encoded)
    },
  },
}
</script>

<style scoped>
.lab-table {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
.lab-table.is-empty {
  background: #097fa0;
}
.game-stage {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  width: 100%;
  height: 100%;
  overflow: hidden;
  cursor: default;
}
.game-stage.is-interactive {
  cursor: pointer;
}
.game-stage :deep(canvas) {
  display: block;
  width: 100%;
  height: 100%;
}
</style>
