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
  showTileSettings: { type: Boolean, default: false },
  tileSettingsExpanded: { type: Boolean, default: false },
})

const emit = defineEmits(['update', 'toggle-expand', 'toggle-tile-settings'])

const mainToggles = computed(() => [
  {
    key: 'autoFlower',
    label: '自动补花',
    value: Boolean(props.settings.autoFlower),
    toggle: () => emit('update', { autoFlower: !props.settings.autoFlower }),
  },
  {
    key: 'autoDiscard',
    label: '自动摸切',
    value: Boolean(props.settings.autoDiscard),
    toggle: () => emit('update', { autoDiscard: !props.settings.autoDiscard }),
  },
  {
    key: 'autoPass',
    label: '不吃碰杠',
    value: Boolean(props.settings.autoPass),
    toggle: () => emit('update', withAutoPass(props.settings, !props.settings.autoPass)),
  },
  {
    key: 'autoWin',
    label: '自动和牌',
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

@media (max-width: 560px) and (orientation: portrait) {
  .assist-inline {
    width: 100%;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    align-items: start;
  }

  .assist-inline__tile-column,
  .assist-inline__operation-column {
    display: contents;
  }

  .assist-inline__operation-column > .assist-switch:nth-child(1) { order: 1; }
  .assist-inline__operation-column > .assist-switch:nth-child(2) { order: 2; }
  .assist-inline__operation-column > .assist-switch:nth-child(3) { order: 3; }
  .assist-inline__operation-column > .assist-switch:nth-child(4) { order: 4; }
  .assist-inline__tile-settings { order: 5; }
  .assist-inline__operation-column > .assist-switch:nth-child(5) { order: 6; }
}
</style>
