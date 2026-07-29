<template>
  <div class="scene-appearance-panel">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">牌张设置</h3>
      <div class="silent-tile-header-actions">
        <button
          type="button"
          class="assist-switch assist-switch--compact"
          :class="{ 'is-on': settings.silentSkipTsumo }"
          :aria-pressed="settings.silentSkipTsumo"
          @click="$emit('update', { silentSkipTsumo: !settings.silentSkipTsumo })"
        >
          <span class="assist-switch__label">自摸也不询问</span>
          <span class="assist-switch__track" aria-hidden="true">
            <span class="assist-switch__thumb" />
          </span>
        </button>
        <button
          type="button"
          class="scene-appearance-panel__ghost-button silent-tile-clear-button"
          @click="$emit('clear-tiles')"
        >
          清空
        </button>
      </div>
    </div>

    <p class="scene-appearance-panel__hint">
      点选后：这些牌被打出时不再询问吃碰杠和；若开启自摸也不询问，自摸这些牌则不会自动和牌。
    </p>

    <div
      v-for="(group, groupIndex) in tileGroups"
      :key="groupIndex"
      class="silent-tile-group"
    >
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

    <div class="silent-tile-batch">
      <span class="scene-appearance-panel__label">批量操作</span>
      <div class="silent-tile-batch__buttons">
        <button
          v-for="batch in tileBatches"
          :key="batch.label"
          type="button"
          class="scene-appearance-panel__button silent-tile-batch__button"
          :class="{ 'is-active': isBatchSelected(batch.tiles) }"
          @click="toggleBatch(batch.tiles)"
        >
          {{ batch.label }}
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

const tileBatches = computed(() => [
  { label: '全部', tiles: allTiles },
  tileGroups.value[0],
  tileGroups.value[2],
  tileGroups.value[1],
  tileGroups.value[3],
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

function isBatchSelected(tiles) {
  const current = new Set(props.settings.silentTiles ?? [])
  return tiles.length > 0 && tiles.every((tile) => current.has(tile))
}

function toggleBatch(tiles) {
  const current = new Set(props.settings.silentTiles ?? [])
  const enable = tiles.some((tile) => !current.has(tile))
  for (const tile of tiles) {
    if (enable) current.add(tile)
    else current.delete(tile)
  }
  emit('update', { silentTiles: [...current].sort((a, b) => a - b) })
}

</script>

<style scoped>
.silent-tile-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 2px;
}

.silent-tile-header-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
}

.silent-tile-clear-button {
  min-width: 76px;
  padding-inline: 16px;
}

.assist-switch--compact {
  min-height: 30px;
  padding: 4px 8px;
  border-radius: 10px;
  box-shadow: none;
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

.silent-tile-batch {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid rgba(96, 96, 96, 0.2);
}

.silent-tile-batch__buttons {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 5px;
}

.silent-tile-batch__button {
  padding: 6px 5px;
}

.silent-tile-batch__button.is-active {
  color: #0d66bd;
  border-color: rgba(24, 144, 255, 0.65);
  background: rgba(237, 244, 255, 0.98);
}
</style>
