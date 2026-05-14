<!-- 麻将牌选择面板：按花色分组展示所有牌，点击触发添加 -->
<template>
  <div class="tile-palette">
    <div class="palette-row">
      <span class="row-label">万</span>
      <div class="palette-tiles">
        <TileChip v-for="id in wanTiles" :key="id" :tile-id="id" :size="size" @click="onPick(id)" />
      </div>
    </div>
    <div class="palette-row">
      <span class="row-label">饼</span>
      <div class="palette-tiles">
        <TileChip v-for="id in bingTiles" :key="id" :tile-id="id" :size="size" @click="onPick(id)" />
      </div>
    </div>
    <div class="palette-row">
      <span class="row-label">条</span>
      <div class="palette-tiles">
        <TileChip v-for="id in tiaoTiles" :key="id" :tile-id="id" :size="size" @click="onPick(id)" />
      </div>
    </div>
    <div class="palette-row">
      <span class="row-label">风箭</span>
      <div class="palette-tiles">
        <TileChip v-for="id in zipaiTiles" :key="id" :tile-id="id" :size="size" @click="onPick(id)" />
      </div>
    </div>
    <div v-if="includeFlowers" class="palette-row">
      <span class="row-label">花</span>
      <div class="palette-tiles">
        <TileChip v-for="id in flowerTiles" :key="id" :tile-id="id" :size="size" @click="onPick(id)" />
      </div>
    </div>
  </div>
</template>

<script setup>
import TileChip from './TileChip.vue'

defineProps({
  size: { type: String, default: 'md' },
  includeFlowers: { type: Boolean, default: false }
})

const emit = defineEmits(['pick'])

const wanTiles = [11, 12, 13, 14, 15, 16, 17, 18, 19]
const bingTiles = [21, 22, 23, 24, 25, 26, 27, 28, 29]
const tiaoTiles = [31, 32, 33, 34, 35, 36, 37, 38, 39]
const zipaiTiles = [41, 42, 43, 44, 45, 46, 47]
const flowerTiles = [51, 52, 53, 54, 55, 56, 57, 58]

const onPick = (id) => emit('pick', id)
</script>

<style scoped>
.tile-palette {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
  min-width: 0;
  background: var(--omu-surface-soft, #f5f7fa);
  border: 1px dashed var(--omu-border, #ebeef5);
  border-radius: 12px;
}

.palette-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.row-label {
  width: 2.5em;
  flex-shrink: 0;
  font-weight: 600;
  font-size: 12px;
  color: var(--omu-text-soft, #606266);
}

.palette-tiles {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  gap: 4px;
  flex: 1;
  min-width: 0;
  overflow-x: auto;
  padding-bottom: 2px;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: thin;
}
</style>
