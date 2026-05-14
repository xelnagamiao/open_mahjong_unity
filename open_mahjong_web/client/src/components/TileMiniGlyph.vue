<!-- 结果区用小号麻将 Unicode，缺字时回退为 m/p/s/z 简写 -->
<template>
  <span class="tile-mini" :title="tip">{{ glyph }}</span>
</template>

<script setup>
import { computed } from 'vue'
import { TILE_UNICODE, TILE_NAME, tileIdToNotation } from '@/composables/useMahjongTiles'

const props = defineProps({
  tileId: { type: Number, required: true }
})

const tip = computed(() => TILE_NAME[props.tileId] || tileIdToNotation(props.tileId))
const glyph = computed(() => TILE_UNICODE[props.tileId] || tileIdToNotation(props.tileId))
</script>

<style scoped>
.tile-mini {
  font-size: 15px;
  line-height: 1;
  display: inline-block;
  vertical-align: baseline;
  margin: 0 1px;
  font-family: 'Segoe UI Symbol', 'Apple Color Emoji', 'Noto Sans Symbols 2', 'Microsoft YaHei', sans-serif;
}
</style>
