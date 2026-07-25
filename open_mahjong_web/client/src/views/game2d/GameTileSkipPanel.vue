<template>
  <div class="scene-appearance-panel">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">牌张设置</h3>
      <button type="button" class="scene-appearance-panel__ghost-button" @click="$emit('clear-tiles')">清空</button>
    </div>

    <p class="scene-appearance-panel__hint">
      点选后：这些牌被打出时不再询问吃碰杠和；若下方开启，自摸这些牌也不询问。
    </p>

    <button
      type="button"
      class="assist-switch assist-switch--row"
      :class="{ 'is-on': settings.silentSkipTsumo }"
      :aria-pressed="settings.silentSkipTsumo"
      @click="$emit('update', { silentSkipTsumo: !settings.silentSkipTsumo })"
    >
      <span class="assist-switch__label">自摸也不询问</span>
      <span class="assist-switch__track" aria-hidden="true">
        <span class="assist-switch__thumb" />
      </span>
    </button>

    <div
      v-for="group in tileGroups"
      :key="group.label"
      class="silent-tile-group"
    >
      <div class="scene-appearance-panel__section-header">
        <span class="scene-appearance-panel__label">{{ group.label }}</span>
      </div>
      <div class="silent-tile-grid">
        <button
          v-for="tile in group.tiles"
          :key="tile"
          type="button"
          class="silent-tile-grid__tile"
          :class="{ 'is-active': isSelected(tile) }"
          :title="String(tile)"
          @click="toggleTile(tile)"
        >
          <img :src="tileSrc(tile)" alt="">
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { standardSilentTileChoices } from '@/game2d/lib/assistSettings'

const props = defineProps({
  settings: { type: Object, required: true },
  tileSrc: { type: Function, required: true },
})

const emit = defineEmits(['update', 'clear-tiles'])

const allTiles = standardSilentTileChoices()

const tileGroups = computed(() => [
  { label: '万', tiles: allTiles.filter((tile) => (tile & 0xe0) === 0x40) },
  { label: '饼', tiles: allTiles.filter((tile) => (tile & 0xe0) === 0x60) },
  { label: '条', tiles: allTiles.filter((tile) => (tile & 0xe0) === 0xc0) },
  { label: '字', tiles: allTiles.filter((tile) => (tile & 0xe0) === 0xa0) },
])

function isSelected(tile) {
  return (props.settings.silentTiles ?? []).includes(tile)
}

function toggleTile(tile) {
  const current = new Set(props.settings.silentTiles ?? [])
  if (current.has(tile)) current.delete(tile)
  else current.add(tile)
  emit('update', { silentTiles: [...current].sort((a, b) => a - b) })
}
</script>

<style scoped>
.silent-tile-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 8px;
}

.silent-tile-grid {
  display: grid;
  grid-template-columns: repeat(9, minmax(0, 1fr));
  gap: 4px;
}

.silent-tile-grid__tile {
  aspect-ratio: 3 / 4;
  padding: 2px;
  border: 1px solid rgba(96, 96, 96, 0.28);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.9);
  cursor: pointer;
}

.silent-tile-grid__tile img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: contain;
}

.silent-tile-grid__tile.is-active {
  border-color: rgba(24, 144, 255, 0.75);
  background: rgba(237, 244, 255, 0.98);
  box-shadow: inset 0 0 0 1px rgba(24, 144, 255, 0.35);
}
</style>
