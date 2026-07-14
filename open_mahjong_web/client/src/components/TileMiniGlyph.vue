<!-- 结果区小号牌面：跟随全局牌面风格 -->
<template>
  <span class="tile-mini" :class="faceStyle" :title="tip">
    <img
      v-if="artSrc"
      class="tile-mini-art"
      :src="artSrc"
      :alt="tip"
      draggable="false"
      @error="onArtError"
    />
    <template v-else>{{ glyph }}</template>
  </span>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import {
  TILE_UNICODE,
  TILE_NAME,
  tileIdToNotation,
  tileSvgUrl,
  tileFacePngUrl,
} from '@/composables/useMahjongTiles'
import { useTileFaceStyle, TILE_FACE_STYLE } from '@/composables/useTileFaceStyle'

const props = defineProps({
  tileId: { type: Number, required: true }
})

const { tileFaceStyle } = useTileFaceStyle()
const artFailed = ref(false)
watch([() => props.tileId, tileFaceStyle], () => { artFailed.value = false })

const faceStyle = computed(() => tileFaceStyle.value)
const tip = computed(() => TILE_NAME[props.tileId] || tileIdToNotation(props.tileId))
const glyph = computed(() => TILE_UNICODE[props.tileId] || tileIdToNotation(props.tileId))

const artSrc = computed(() => {
  if (artFailed.value) return null
  if (tileFaceStyle.value === TILE_FACE_STYLE.oblique) {
    return tileSvgUrl(props.tileId) || tileFacePngUrl(props.tileId)
  }
  return tileFacePngUrl(props.tileId) || tileSvgUrl(props.tileId)
})

const onArtError = () => {
  artFailed.value = true
}
</script>

<style scoped>
.tile-mini {
  font-size: 15px;
  line-height: 1;
  display: inline-flex;
  align-items: center;
  vertical-align: middle;
  margin: 0 1px;
  font-family: 'Segoe UI Symbol', 'Apple Color Emoji', 'Noto Sans Symbols 2', 'Microsoft YaHei', sans-serif;
}

.tile-mini-art {
  display: block;
  object-fit: contain;
}

.tile-mini.helper .tile-mini-art {
  width: 18px;
  height: 24px;
  background: transparent;
  border: none;
  border-radius: 0;
  padding: 0;
  box-sizing: border-box;
}

.tile-mini.oblique .tile-mini-art {
  width: 22px;
  height: 27px;
}
</style>
