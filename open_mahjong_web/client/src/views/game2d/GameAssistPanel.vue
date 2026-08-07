<template>
  <div v-if="detailOnly" class="scene-appearance-panel assist-detail">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">鸣牌 / 和牌过滤</h3>
    </div>
    <button
      v-for="item in detailToggles"
      :key="item.key"
      type="button"
      class="assist-switch assist-switch--row"
      :class="{ 'is-on': item.value }"
      :aria-pressed="item.value"
      @click="item.toggle()"
    >
      <span class="assist-switch__label">{{ item.label }}</span>
      <span class="assist-switch__track" aria-hidden="true">
        <span class="assist-switch__thumb" />
      </span>
    </button>
    <p class="scene-appearance-panel__hint">
      不自摸/不抢杠仅在自动和牌开启时作为阻挡项生效。
    </p>
  </div>

  <div v-else-if="compact" class="assist-dock">
    <button
      v-for="item in mainToggles"
      :key="item.key"
      type="button"
      class="assist-dock__item"
      :class="{ 'is-on': item.value }"
      :aria-pressed="item.value"
      @click="item.toggle()"
    >
      <span>{{ item.shortLabel }}</span>
    </button>
    <button
      v-if="showTileSettings"
      type="button"
      class="assist-dock__item"
      :class="{ 'is-on': tileSettingsExpanded }"
      :aria-expanded="tileSettingsExpanded"
      @click="$emit('toggle-tile-settings')"
    >
      <span>牌张</span>
    </button>
    <button
      type="button"
      class="assist-dock__item"
      :class="{ 'is-on': expanded }"
      :aria-expanded="expanded"
      @click="$emit('toggle-expand')"
    >
      <span>展开</span>
    </button>
    <div v-if="$slots['tile-panel'] && tileSettingsExpanded" class="assist-dock__tile-panel">
      <slot name="tile-panel" />
    </div>
  </div>

  <div v-else class="assist-inline">
    <div v-if="showTileSettings" class="assist-inline__tile-column">
      <button
        type="button"
        class="assist-switch assist-switch--expand assist-inline__tile-settings"
        :class="{ 'is-on': tileSettingsExpanded }"
        :aria-expanded="tileSettingsExpanded"
        @click="$emit('toggle-tile-settings')"
      >
        <span class="assist-switch__label">牌张设置</span>
      </button>
      <div v-if="$slots['tile-panel']" class="assist-inline__tile-panel">
        <slot name="tile-panel" />
      </div>
    </div>

    <div class="assist-inline__operation-column">
      <button
        v-for="item in mainToggles"
        :key="item.key"
        type="button"
        class="assist-switch"
        :class="{ 'is-on': item.value }"
        :aria-pressed="item.value"
        @click="item.toggle()"
      >
        <span class="assist-switch__label">{{ item.label }}</span>
        <span class="assist-switch__track" aria-hidden="true">
          <span class="assist-switch__thumb" />
        </span>
      </button>
      <button
        type="button"
        class="assist-switch assist-switch--expand"
        :class="{ 'is-on': expanded }"
        :aria-expanded="expanded"
        @click="$emit('toggle-expand')"
      >
        <span class="assist-switch__label">展开</span>
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { withAutoPass, withMeldPassOption } from '@/game2d/lib/assistSettings'

const props = defineProps({
  settings: { type: Object, required: true },
  expanded: { type: Boolean, default: false },
  detailOnly: { type: Boolean, default: false },
  compact: { type: Boolean, default: false },
  showTileSettings: { type: Boolean, default: false },
  tileSettingsExpanded: { type: Boolean, default: false },
})

const emit = defineEmits(['update', 'toggle-expand', 'toggle-tile-settings'])

const mainToggles = computed(() => [
  {
    key: 'autoFlower',
    label: '自动补花',
    shortLabel: '补花',
    value: Boolean(props.settings.autoFlower),
    toggle: () => emit('update', { autoFlower: !props.settings.autoFlower }),
  },
  {
    key: 'autoDiscard',
    label: '自动摸切',
    shortLabel: '摸切',
    value: Boolean(props.settings.autoDiscard),
    toggle: () => emit('update', { autoDiscard: !props.settings.autoDiscard }),
  },
  {
    key: 'autoPass',
    label: '不吃碰杠',
    shortLabel: '过牌',
    value: Boolean(props.settings.autoPass),
    toggle: () => emit('update', withAutoPass(props.settings, !props.settings.autoPass)),
  },
  {
    key: 'autoWin',
    label: '自动和牌',
    shortLabel: '和牌',
    value: Boolean(props.settings.autoWin),
    toggle: () => emit('update', { autoWin: !props.settings.autoWin }),
  },
])

const detailToggles = computed(() => [
  {
    key: 'passChi',
    label: '不吃',
    value: Boolean(props.settings.passChi),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passChi', !props.settings.passChi)),
  },
  {
    key: 'passPeng',
    label: '不碰',
    value: Boolean(props.settings.passPeng),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passPeng', !props.settings.passPeng)),
  },
  {
    key: 'passMingGang',
    label: '不杠',
    value: Boolean(props.settings.passMingGang),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passMingGang', !props.settings.passMingGang)),
  },
  {
    key: 'noRon',
    label: '不点和',
    value: Boolean(props.settings.noRon),
    toggle: () => emit('update', { noRon: !props.settings.noRon }),
  },
  {
    key: 'noTsumo',
    label: '不自摸',
    value: Boolean(props.settings.noTsumo),
    toggle: () => emit('update', { noTsumo: !props.settings.noTsumo }),
  },
  {
    key: 'noRobKong',
    label: '不抢杠',
    value: Boolean(props.settings.noRobKong),
    toggle: () => emit('update', { noRobKong: !props.settings.noRobKong }),
  },
])
</script>

<style scoped>
.assist-inline {
  display: grid;
  grid-template-columns: max-content max-content;
  gap: 6px;
  align-items: start;
  justify-content: end;
}

.assist-inline__operation-column {
  display: grid;
  width: max-content;
  gap: 6px;
}

.assist-inline__operation-column > .assist-switch {
  width: 100%;
  justify-content: space-between;
}

.assist-inline__tile-settings {
  width: auto;
  min-width: 92px;
  justify-content: center;
}

.assist-inline__tile-column {
  position: relative;
  width: max-content;
}

.assist-inline__tile-panel {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  z-index: 32;
  width: min(420px, calc(100vw - 36px));
  max-height: min(70dvh, 560px);
  overflow-y: auto;
}

.assist-detail {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.assist-dock {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: flex-end;
  align-items: center;
  pointer-events: auto;
}

.assist-dock__item {
  flex: 0 0 auto;
  min-width: 48px;
  padding: 8px 10px;
  border: 1px solid rgba(96, 96, 96, 0.28);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.94);
  color: #1e2425;
  font-size: 13px;
  line-height: 1.2;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.12);
}

.assist-dock__item.is-on {
  background: rgba(18, 110, 130, 0.16);
  border-color: rgba(18, 110, 130, 0.55);
  color: #0e5666;
  font-weight: 600;
}

.assist-dock__item:hover { background: rgba(255, 255, 255, 0.99); }

.assist-dock__tile-panel {
  position: absolute;
  left: 0;
  right: 0;
  bottom: calc(100% + 6px);
  z-index: 32;
  width: min(420px, 100%);
  max-height: min(70dvh, 560px);
  overflow-y: auto;
}
</style>
