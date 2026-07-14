<!-- 单张麻将牌：按全局牌面风格显示小助手 / 立体 SVG -->
<template>
  <div
    class="tile-chip"
    :class="[
      size,
      faceStyle,
      {
        highlighted,
        selected,
        disabled,
        'has-art': !!artSrc,
      },
    ]"
    :style="chipStyle"
    :title="fullName"
    @click="onClick"
  >
    <img
      v-if="artSrc"
      class="tile-art"
      :src="artSrc"
      :alt="fullName"
      draggable="false"
      @error="onArtError"
    />
    <template v-else>
      <span class="tile-glyph">{{ glyph }}</span>
      <span class="tile-text">{{ shortName }}</span>
    </template>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import {
  TILE_UNICODE,
  TILE_NAME,
  tileColor,
  tileSvgUrl,
  tileFacePngUrl,
} from '@/composables/useMahjongTiles'
import { useTileFaceStyle, TILE_FACE_STYLE } from '@/composables/useTileFaceStyle'

const props = defineProps({
  tileId: { type: Number, required: true },
  size: { type: String, default: 'md' },
  highlighted: { type: Boolean, default: false },
  selected: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false }
})

const emit = defineEmits(['click'])
const { tileFaceStyle } = useTileFaceStyle()

const artStage = ref(0)
watch([() => props.tileId, tileFaceStyle], () => { artStage.value = 0 })

const faceStyle = computed(() => tileFaceStyle.value)

const fullName = computed(() => TILE_NAME[props.tileId] || String(props.tileId))
const glyph = computed(() => TILE_UNICODE[props.tileId] || '?')
const shortName = computed(() => {
  const id = props.tileId
  if (id >= 11 && id <= 39) return String(id % 10)
  return TILE_NAME[id] || ''
})

const artCandidates = computed(() => {
  const list = []
  const svg = tileSvgUrl(props.tileId)
  const png = tileFacePngUrl(props.tileId)
  if (tileFaceStyle.value === TILE_FACE_STYLE.oblique) {
    if (svg) list.push(svg)
    if (png) list.push(png)
  } else {
    if (png) list.push(png)
    if (svg) list.push(svg)
  }
  return list
})

const artSrc = computed(() => artCandidates.value[artStage.value] || null)

const chipStyle = computed(() => ({
  '--tile-accent': tileColor(props.tileId)
}))

const onArtError = () => {
  if (artStage.value < artCandidates.value.length - 1) {
    artStage.value += 1
  } else {
    artStage.value = artCandidates.value.length
  }
}

const onClick = () => {
  if (!props.disabled) {
    emit('click', props.tileId)
  }
}
</script>

<style scoped>
.tile-chip {
  --tile-accent: #666;
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  border-radius: 0;
  cursor: pointer;
  user-select: none;
  font-family: 'Segoe UI Symbol', 'Microsoft YaHei', sans-serif;
  color: var(--tile-accent);
  transition: transform 0.12s ease, filter 0.12s ease;
  box-shadow: none;
  padding: 0;
  overflow: visible;
}

.tile-art {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: contain;
  pointer-events: none;
}

/* —— 小助手：直接显示原图，不加外框 —— */
.tile-chip.helper.has-art {
  background: transparent;
  border: none;
  box-shadow: none;
  padding: 0;
}
.tile-chip.helper.sm { width: 26px; height: 36px; }
.tile-chip.helper.md { width: 36px; height: 50px; }
.tile-chip.helper.lg { width: 46px; height: 64px; }

/* —— 立体 SVG：自带牌体 —— */
.tile-chip.oblique.has-art {
  background: transparent;
  border: none;
  box-shadow: none;
  padding: 0;
}
.tile-chip.oblique.sm { width: 34px; height: 42px; }
.tile-chip.oblique.md { width: 46px; height: 56px; }
.tile-chip.oblique.lg { width: 58px; height: 72px; }

/* Unicode 回退 */
.tile-chip:not(.has-art) {
  background: linear-gradient(180deg, #fdfdf6, #f3efd9);
  border: 2px solid var(--tile-accent);
  border-radius: 8px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.18);
}
.tile-chip.sm:not(.has-art) { width: 36px; height: 50px; padding: 4px 0; }
.tile-chip.md:not(.has-art) { width: 48px; height: 66px; padding: 6px 0; }
.tile-chip.lg:not(.has-art) { width: 60px; height: 84px; padding: 8px 0; }
.tile-chip.sm .tile-glyph { font-size: 24px; line-height: 1; }
.tile-chip.sm .tile-text { font-size: 11px; line-height: 1.2; }
.tile-chip.md .tile-glyph { font-size: 32px; line-height: 1; }
.tile-chip.md .tile-text { font-size: 13px; line-height: 1.3; margin-top: 2px; }
.tile-chip.lg .tile-glyph { font-size: 40px; line-height: 1; }
.tile-chip.lg .tile-text { font-size: 14px; line-height: 1.3; margin-top: 4px; }

.tile-chip:hover:not(.disabled) {
  transform: translateY(-2px);
  filter: drop-shadow(0 3px 5px rgba(0, 0, 0, 0.2));
}

.tile-chip.highlighted {
  outline: 2px solid #ffd04b;
  outline-offset: 1px;
  border-radius: 2px;
}

.tile-chip.selected {
  outline: 2px solid #3a7afe;
  outline-offset: 1px;
  border-radius: 2px;
}

.tile-chip.disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
