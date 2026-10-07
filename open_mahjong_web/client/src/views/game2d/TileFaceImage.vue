<template>
  <span class="tile-face-image" :style="{ '--face-scale': `${scale * 100}%` }">
    <img class="tile-face-image__art" :src="src" :alt="alt">
    <span v-if="label" class="tile-face-image__label" :style="{ color: tileFaceLabelColor(faceId) }" aria-hidden="true" data-no-translate>{{ label }}</span>
  </span>
</template>

<script setup>
import { computed, inject } from 'vue'
import { locale } from '@/i18n'
import { shouldShowTileLabels, tileFaceLabel, tileFaceLabelColor } from '@/game2d/lib/tileLabels'

const props = defineProps({
  src: { type: String, required: true },
  faceId: { type: Number, required: true },
  alt: { type: String, default: '' },
  scale: { type: Number, default: 0.84 },
})
const enabled = inject('game2d.tileLabelsEnabled', computed(() => shouldShowTileLabels('auto', locale.value)))
const label = computed(() => enabled.value ? tileFaceLabel(props.faceId) : '')
</script>

<style scoped>
.tile-face-image {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  container-type: inline-size;
}
.tile-face-image .tile-face-image__art {
  display: block;
  width: var(--face-scale);
  height: var(--face-scale);
  object-fit: contain;
}
.tile-face-image__label {
  position: absolute;
  top: 2%;
  right: 3%;
  font: 700 25cqw/1 Arial, sans-serif;
  text-shadow: -0.8cqw 0 #f7f7f0, 0.8cqw 0 #f7f7f0, 0 -0.8cqw #f7f7f0, 0 0.8cqw #f7f7f0;
  pointer-events: none;
}
</style>
