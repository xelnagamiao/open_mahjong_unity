<template>
  <span class="guobiao-fan-tile" role="img" :aria-label="label" :title="label">
    <img class="guobiao-fan-tile__background" :src="assets.background" alt="" aria-hidden="true" draggable="false">
    <img v-if="assets.face" class="guobiao-fan-tile__face" :src="assets.face" alt="" aria-hidden="true" draggable="false">
  </span>
</template>

<script setup>
import { computed } from 'vue'
import { TILE_NAME, tileIdToNotation } from '@/composables/useMahjongTiles'
import { guobiaoFanTileAssets } from '@/utils/guobiaoFanTileAssets'
import { tr } from '@/i18n'

const props = defineProps({
  tileId: { type: Number, required: true },
  faceDown: { type: Boolean, default: false },
})

const assets = computed(() => guobiaoFanTileAssets(props.tileId, props.faceDown, import.meta.env.BASE_URL))
const label = computed(() => props.faceDown ? tr('牌背') : tr(TILE_NAME[props.tileId] || tileIdToNotation(props.tileId)))
</script>

<style scoped>
.guobiao-fan-tile {
  position: relative;
  display: block;
  aspect-ratio: 272 / 389;
}
.guobiao-fan-tile__background,
.guobiao-fan-tile__face {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  pointer-events: none;
}
</style>
