<!-- 四槽副露：点选槽位后从牌盘录入；亦支持简写；点牌可删除 -->
<template>
  <div class="fulu-slots">
    <div
      v-for="(slot, idx) in slots"
      :key="idx"
      class="fulu-slot"
      :class="{ locked: !!slot.locked, active: !slot.locked && activeIdx === idx }"
      @click="onSlotClick(idx)"
    >
      <span class="slot-index">#{{ idx + 1 }}</span>
      <template v-if="slot.locked">
        <el-tag size="small" type="info" effect="plain" class="fulu-kind">{{ slot.locked.label }}</el-tag>
        <div class="fulu-tiles" @click.stop>
          <TileChip
            v-for="(tid, tIdx) in slot.locked.displayTiles"
            :key="tIdx"
            :tile-id="tid"
            size="sm"
            highlighted
            @click="onLockedTileClick(idx, tIdx)"
          />
        </div>
        <el-button type="danger" link size="small" class="fulu-clear" @click.stop="clearSlot(idx)">
          清除
        </el-button>
      </template>
      <template v-else>
        <el-input
          v-model="slot.input"
          :placeholder="hints[idx]"
          size="small"
          clearable
          class="fulu-input"
          @click.stop="activate(idx)"
          @focus="activate(idx)"
          @input="onSlotInput(idx)"
        />
        <div class="fulu-tiles draft" @click.stop>
          <TileChip
            v-for="(tid, tIdx) in draftTiles(idx)"
            :key="tIdx"
            :tile-id="tid"
            size="sm"
            @click="removeDraftTile(idx, tIdx)"
          />
        </div>
        <div class="fulu-options" @click.stop>
          <el-button
            v-for="(opt, oIdx) in slot.options"
            :key="oIdx"
            type="primary"
            plain
            size="small"
            @click="lockSlot(idx, opt)"
          >
            {{ opt.label }}
          </el-button>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup>
import TileChip from './TileChip.vue'
import { tryParseMeldTiles, FULU_SLOT_HINTS } from '@/composables/useFuluSlots'

const props = defineProps({
  slots: { type: Array, required: true },
  activeIdx: { type: Number, default: -1 },
  hints: { type: Array, default: () => FULU_SLOT_HINTS },
})

const emit = defineEmits(['activate', 'clear', 'input', 'lock', 'remove-draft', 'remove-locked'])

const draftTiles = (idx) => {
  const tiles = tryParseMeldTiles(props.slots[idx]?.input)
  return tiles || []
}

const onSlotClick = (idx) => {
  if (props.slots[idx].locked) return
  emit('activate', idx)
}

const activate = (idx) => emit('activate', idx)
const clearSlot = (idx) => emit('clear', idx)
const onSlotInput = (idx) => emit('input', idx)
const lockSlot = (idx, opt) => emit('lock', idx, opt)
const removeDraftTile = (idx, tIdx) => emit('remove-draft', idx, tIdx)
const onLockedTileClick = (idx, tIdx) => emit('remove-locked', idx, tIdx)
</script>

<style scoped>
.fulu-slots {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 6px;
}

.fulu-slot {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 4px;
  padding: 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
  min-height: 148px;
  min-width: 0;
  cursor: pointer;
  transition: border-color 0.12s ease, background 0.12s ease;
}

.fulu-slot.active {
  border-color: #409eff;
  border-style: solid;
  background: #f0f7ff;
}

.fulu-slot.locked {
  border-style: solid;
  background: #f0f9ff;
  cursor: default;
}

.slot-index {
  font-size: 10px;
  font-weight: 700;
  color: var(--omu-text-muted, #94a3b8);
  line-height: 1;
}

.fulu-kind {
  align-self: flex-start;
}

.fulu-input {
  width: 100%;
  flex-shrink: 0;
}

.fulu-tiles {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  align-content: flex-start;
  gap: 2px;
}

.fulu-tiles.draft {
  min-height: 40px;
  flex-shrink: 0;
}

.fulu-clear {
  align-self: center;
  padding: 0;
  height: auto;
}

.fulu-options {
  display: flex;
  flex-direction: column;
  justify-content: flex-start;
  gap: 3px;
  min-height: 56px;
  flex-shrink: 0;
}

.fulu-options :deep(.el-button) {
  width: 100%;
  margin: 0;
  padding: 4px 0;
}

@media (max-width: 640px) {
  .fulu-slots {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
