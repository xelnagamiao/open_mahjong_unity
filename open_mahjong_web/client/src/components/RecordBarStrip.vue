<template>
  <div class="bar-strip" :class="{ compact, empty: !bars.length }">
    <button
      v-for="bar in bars"
      :key="bar.key"
      type="button"
      class="bar"
      :class="[`size-${bar.size}`, { downloaded: bar.fillRatio >= 1, interactive, partial: isPartial(bar) }]"
      :style="barStyle(bar)"
      :disabled="interactive && disabled"
      :title="barTitle(bar)"
      @click="onClick(bar)"
    >
      <span class="bar-fill" :style="fillStyle(bar)" />
    </button>
  </div>
</template>

<script setup>
import { BAR_UNIT_STYLES } from '../utils/recordBarUnits'

const props = defineProps({
  bars: { type: Array, default: () => [] },
  compact: { type: Boolean, default: false },
  interactive: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
})

const emit = defineEmits(['select'])

const isPartial = (bar) => {
  const ratio = Number(bar.fillRatio) || 0
  return ratio > 0 && ratio < 1
}

const barStyle = (bar) => {
  const spec = BAR_UNIT_STYLES[bar.size] || BAR_UNIT_STYLES[1]
  return {
    height: `${props.compact ? spec.compactHeight : spec.height}px`,
    width: `${props.compact ? spec.compactWidth : spec.width}px`,
    '--bar-color': spec.color,
  }
}

const fillStyle = (bar) => {
  const spec = BAR_UNIT_STYLES[bar.size] || BAR_UNIT_STYLES[1]
  const ratio = Math.max(0, Math.min(1, Number(bar.fillRatio) || 0))
  return {
    height: `${ratio * 100}%`,
    background: spec.color,
  }
}

const barTitle = (bar) => {
  if (props.compact) return `${bar.size}局`
  const have = Number(bar.downloadedCount) || 0
  const total = bar.ids?.length || bar.size
  if (have >= total && total > 0) return `${bar.size}局 · 已下载`
  return `${bar.size}局 · 已下载 ${have}/${total}，点击补下缺失`
}

const onClick = (bar) => {
  if (!props.interactive || props.disabled) return
  if (!bar?.missingIds?.length) return
  emit('select', bar)
}
</script>

<style scoped>
.bar-strip {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  align-content: flex-start;
  gap: 3px;
  min-height: 100px;
}
.bar-strip.compact {
  min-height: 22px;
  gap: 2px;
}
.bar-strip.empty {
  min-height: 0;
}
.bar {
  appearance: none;
  border: none;
  padding: 0;
  margin: 0;
  display: block;
  flex-shrink: 0;
  position: relative;
  overflow: hidden;
  background: #eef0f3;
  cursor: default;
}
.bar-fill {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  display: block;
  pointer-events: none;
}
.bar.interactive:not(:disabled) {
  cursor: pointer;
}
.bar.interactive:not(:disabled):hover {
  outline: 1px solid rgba(15, 23, 42, 0.25);
  outline-offset: 0;
}
.bar.interactive:not(:disabled):active {
  outline: 1px solid rgba(15, 23, 42, 0.4);
  transform: translateY(1px);
}
.bar:disabled {
  cursor: default;
}
</style>
