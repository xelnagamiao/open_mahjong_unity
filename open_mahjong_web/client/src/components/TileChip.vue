<!-- 单张麻将牌的可视化展示组件 -->
<template>
  <div
    class="tile-chip"
    :class="[size, { highlighted, selected, disabled }]"
    :style="chipStyle"
    @click="onClick"
  >
    <span class="tile-glyph">{{ glyph }}</span>
    <span class="tile-text">{{ shortName }}</span>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { TILE_UNICODE, TILE_NAME, tileColor } from '@/composables/useMahjongTiles'

const props = defineProps({
  tileId: { type: Number, required: true },
  size: { type: String, default: 'md' },
  highlighted: { type: Boolean, default: false },
  selected: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false }
})

const emit = defineEmits(['click'])

const glyph = computed(() => TILE_UNICODE[props.tileId] || '?')
const shortName = computed(() => {
  const name = TILE_NAME[props.tileId] || ''
  // 数牌取数字部分用于卡片底部标识，例如"五万"->"5"，字牌使用全名
  const id = props.tileId
  if (id >= 11 && id <= 39) return String(id % 10)
  return name
})

const chipStyle = computed(() => ({
  '--tile-accent': tileColor(props.tileId)
}))

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
  background: linear-gradient(180deg, #fdfdf6, #f3efd9);
  border: 2px solid var(--tile-accent);
  border-radius: 8px;
  cursor: pointer;
  user-select: none;
  font-family: 'Segoe UI Symbol', 'Microsoft YaHei', sans-serif;
  color: var(--tile-accent);
  transition: transform 0.12s ease, box-shadow 0.12s ease;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.18);
}

.tile-chip.sm {
  width: 36px;
  height: 50px;
  padding: 4px 0;
}
.tile-chip.sm .tile-glyph { font-size: 24px; line-height: 1; }
.tile-chip.sm .tile-text { font-size: 11px; line-height: 1.2; }

.tile-chip.md {
  width: 48px;
  height: 66px;
  padding: 6px 0;
}
.tile-chip.md .tile-glyph { font-size: 32px; line-height: 1; }
.tile-chip.md .tile-text { font-size: 13px; line-height: 1.3; margin-top: 2px; }

.tile-chip.lg {
  width: 60px;
  height: 84px;
  padding: 8px 0;
}
.tile-chip.lg .tile-glyph { font-size: 40px; line-height: 1; }
.tile-chip.lg .tile-text { font-size: 14px; line-height: 1.3; margin-top: 4px; }

.tile-chip:hover:not(.disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 12px rgba(0, 0, 0, 0.24);
}

.tile-chip.highlighted {
  background: linear-gradient(180deg, #fff8c1, #ffe98a);
  box-shadow: 0 0 0 3px #ffd04b, 0 4px 8px rgba(0, 0, 0, 0.24);
}

.tile-chip.selected {
  background: linear-gradient(180deg, #e3f1ff, #b9dcff);
  box-shadow: 0 0 0 3px #3a7afe, 0 4px 8px rgba(0, 0, 0, 0.24);
}

.tile-chip.disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
